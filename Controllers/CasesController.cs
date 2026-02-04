using Microsoft.AspNetCore.Mvc;
using RadiopaediaConnect.Data;
using RadiopaediaConnect.Models;
using System.Security.Claims;
using RadiopaediaConnect.Services;

namespace RadiopaediaConnect.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CasesController : ControllerBase
    {
        private readonly DicomRepository _repository;
        private readonly ILogger<CasesController> _logger;
        private readonly CaseProcessorService _caseProcessor;

        public CasesController(DicomRepository repository, ILogger<CasesController> logger, CaseProcessorService caseProcessor)
        {
            _repository = repository;
            _logger = logger;
            _caseProcessor = caseProcessor;
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