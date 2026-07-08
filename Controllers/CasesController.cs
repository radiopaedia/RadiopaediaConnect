using Microsoft.AspNetCore.Mvc;
using RadiopaediaConnect.Data;
using RadiopaediaConnect.Models;
using System.Security.Claims;
using RadiopaediaConnect.Services;
using RadiopaediaConnect.Logging;

namespace RadiopaediaConnect.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CasesController : AdminControllerBase
    {
        private readonly DicomRepository _repository;
        private readonly AppLogsRepository _logsRepository;
        private readonly ILogger<CasesController> _logger;
        private readonly CaseProcessorService _caseProcessor;
        private readonly AdminSessionService _sessionService;
        private readonly RadiopaediaApiClient _radiopaediaApiClient;

        public CasesController(DicomRepository repository, AppLogsRepository logsRepository, ILogger<CasesController> logger, CaseProcessorService caseProcessor, AdminSessionService sessionService, RadiopaediaApiClient radiopaediaApiClient)
        {
            _repository = repository;
            _logsRepository = logsRepository;
            _logger = logger;
            _caseProcessor = caseProcessor;
            _sessionService = sessionService;
            _radiopaediaApiClient = radiopaediaApiClient;
        }

        /// <summary>
        /// Get all cases for the authenticated user
        /// </summary>
        [HttpGet("my-cases")]
        public async Task<IActionResult> GetMyCases()
        {
            var username = User.FindFirst("urn:radiopaedia:username")?.Value;

            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized("User session invalid. Please log in again.");
            }

            try
            {
                var cases = await _repository.GetUserCasesAsync(username);
                return Ok(cases);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve cases for user {Username}", username);
                return StatusCode(500, "Failed to retrieve cases.");
            }
        }

        /// <summary>
        /// Get detailed case information including studies and series
        /// </summary>
        [HttpGet("{caseId:guid}")]
        public async Task<IActionResult> GetCaseDetail(Guid caseId)
        {
            var username = User.FindFirst("urn:radiopaedia:username")?.Value;

            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized("User session invalid. Please log in again.");
            }

            try
            {
                var caseDetail = await _repository.GetCaseDetailAsync(caseId, username);

                if (caseDetail == null)
                {
                    return NotFound("Case not found.");
                }

                return Ok(caseDetail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve case detail for {CaseId}", caseId);
                return StatusCode(500, "Failed to retrieve case details.");
            }
        }

        /// <summary>
        /// Check if a patient already has existing cases in the system.
        /// Returns list of cases for the given patient ID.
        /// </summary>
        [HttpGet("check-patient/{patientId}")]
        public async Task<IActionResult> CheckPatientCases(string patientId)
        {
            if (string.IsNullOrEmpty(User.FindFirst("urn:radiopaedia:username")?.Value))
            {
                return Unauthorized("User session invalid. Please log in again.");
            }

            if (string.IsNullOrWhiteSpace(patientId))
            {
                return BadRequest("Patient ID is required.");
            }

            try
            {
                var cases = await _repository.GetCasesByPatientIdAsync(patientId);
                return Ok(new
                {
                    patientId,
                    caseCount = cases.Count(),
                    cases = cases
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check patient cases for {PatientId}", patientId);
                return StatusCode(500, "Failed to check patient cases.");
            }
        }

        // Admin-session guarded (not Radiopaedia login) — admins may not be Radiopaedia users.
        [HttpGet("all-cases")]
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public async Task<IActionResult> GetAllCases()
        {
            if (!AuthorizeAdmin(_sessionService)) return Unauthorized(new { message = "Invalid admin session." });

            try
            {
                var cases = await _repository.GetAllCasesAsync();
                return Ok(cases);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve all cases");
                return StatusCode(500, "Failed to retrieve cases.");
            }
        }

        // Admin-session guarded (not Radiopaedia login) — admins may not be Radiopaedia users.
        [HttpGet("{caseId:guid}/admin")]
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public async Task<IActionResult> GetCaseDetailAdmin(Guid caseId)
        {
            if (!AuthorizeAdmin(_sessionService)) return Unauthorized(new { message = "Invalid admin session." });

            try
            {
                var caseDetail = await _repository.GetCaseDetailAdminAsync(caseId);
                if (caseDetail == null) return NotFound("Case not found.");
                return Ok(caseDetail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve case detail for {CaseId}", caseId);
                return StatusCode(500, "Failed to retrieve case details.");
            }
        }

        [HttpGet("{caseId:guid}/logs")]
        public async Task<IActionResult> GetCaseLogs(
            Guid caseId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100)
        {
            var username = User.FindFirst("urn:radiopaedia:username")?.Value;
            if (string.IsNullOrEmpty(username))
                return Unauthorized("User session invalid. Please log in again.");

            // Verify the case belongs to this user before returning its logs
            var caseDetail = await _repository.GetCaseDetailAsync(caseId, username);
            if (caseDetail == null)
                return NotFound("Case not found.");

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 1;
            if (pageSize > 500) pageSize = 500;

            var (items, total) = await _logsRepository.QueryByCaseAsync(caseId, page, pageSize);

            return Ok(new
            {
                items = items.Select(l => new
                {
                    l.Id,
                    l.TimestampUtc,
                    l.Level,
                    l.Category,
                    l.Message,
                    l.Exception,
                    l.JobId,
                }),
                totalCount = total,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(total / (double)pageSize),
            });
        }

        [HttpGet("{caseId:guid}/originals")]
        public async Task<IActionResult> GetCaseOriginals(Guid caseId)
        {
            var username = User.FindFirst("urn:radiopaedia:username")?.Value;
            if (string.IsNullOrEmpty(username))
                return Unauthorized("User session invalid. Please log in again.");

            var caseDetail = await _repository.GetCaseDetailAsync(caseId, username);
            if (caseDetail == null)
                return NotFound("Case not found.");

            if (string.IsNullOrEmpty(caseDetail.RadiopaediaCaseId))
                return NotFound("Case has not been published to Radiopaedia yet.");

            try
            {
                var originals = await _radiopaediaApiClient.GetCaseOriginalsAsync(caseDetail.RadiopaediaCaseId, username);
                if (originals == null)
                    return StatusCode(502, "Failed to retrieve originals from Radiopaedia.");

                return Ok(originals);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve originals for case {CaseId}", caseId);
                return StatusCode(500, "Failed to retrieve case originals.");
            }
        }

        [HttpPost("submit")]
        public async Task<IActionResult> SubmitCase([FromBody] SubmitCaseDto request)
        {
            var username = User.FindFirst("urn:radiopaedia:username")?.Value;

            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized("User session invalid. Please log in again.");
            }

            if (request.Studies.Count == 0)
                return BadRequest("Case must contain at least one study.");

            try
            {
                _logger.LogInformation($"Received Case Submission from {username}: '{request.Title}'");

                var caseId = await _repository.SaveDraftCaseAsync(request, username);

                return Ok(new
                {
                    Success = true,
                    CaseId = caseId,
                    Message = "Case queued for processing."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to submit case.");
                return StatusCode(500, "Internal server error during case submission.");
            }
        }
    }
}