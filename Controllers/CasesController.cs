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
        private readonly CaseReconciliationService _reconciliation;

        public CasesController(DicomRepository repository, AppLogsRepository logsRepository, ILogger<CasesController> logger, CaseProcessorService caseProcessor, AdminSessionService sessionService, RadiopaediaApiClient radiopaediaApiClient, CaseReconciliationService reconciliation)
        {
            _repository = repository;
            _logsRepository = logsRepository;
            _logger = logger;
            _caseProcessor = caseProcessor;
            _sessionService = sessionService;
            _radiopaediaApiClient = radiopaediaApiClient;
            _reconciliation = reconciliation;
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

        /// <summary>
        /// Reconciles this user's uploaded cases against their Radiopaedia case listing,
        /// recording which are still drafts, which have been published and which no longer
        /// exist. Returns the refreshed case list so the caller does not need a second call.
        /// </summary>
        [HttpPost("reconcile")]
        public async Task<IActionResult> ReconcileCases(CancellationToken cancellationToken)
        {
            var username = User.FindFirst("urn:radiopaedia:username")?.Value;

            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized("User session invalid. Please log in again.");
            }

            try
            {
                var summary = await _reconciliation.ReconcileUserAsync(username, cancellationToken);
                var cases = await _repository.GetUserCasesAsync(username);

                return Ok(new { summary, cases });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Reconciliation failed for {Username}", username);
                return StatusCode(502, new { message = "Could not reach Radiopaedia to check your cases." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconciliation failed for {Username}", username);
                return StatusCode(500, new { message = "Failed to reconcile cases with Radiopaedia." });
            }
        }

        /// <summary>
        /// The live Radiopaedia status of a single case, used to decide whether the
        /// "add imaging" flow can be entered.
        /// </summary>
        [HttpGet("{caseId:guid}/remote-status")]
        public async Task<IActionResult> GetRemoteStatus(Guid caseId, CancellationToken cancellationToken)
        {
            var username = User.FindFirst("urn:radiopaedia:username")?.Value;

            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized("User session invalid. Please log in again.");
            }

            var draft = await _repository.GetDraftCaseAsync(caseId);
            if (draft == null || !string.Equals(draft.Username, username, StringComparison.OrdinalIgnoreCase))
                return NotFound("Case not found.");

            if (string.IsNullOrEmpty(draft.RadiopaediaCaseId))
                return Ok(new { remoteStatus = (string?)null, acceptsNewImaging = false });

            try
            {
                var status = await _reconciliation.GetRemoteStatusAsync(
                    username, draft.RadiopaediaCaseId, caseId, forceRefresh: false,
                    cancellationToken: cancellationToken);

                return Ok(new
                {
                    remoteStatus = status,
                    acceptsNewImaging = RadiopaediaCaseStatus.AcceptsNewImaging(status),
                    radiopaediaCaseId = draft.RadiopaediaCaseId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read Radiopaedia status for case {CaseId}", caseId);
                return StatusCode(502, new { message = "Could not check the case status with Radiopaedia." });
            }
        }

        /// <summary>
        /// Append studies/series to an existing, already-uploaded case.
        /// Studies matching an existing StudyInstanceUid on the case have their series
        /// added to that study on Radiopaedia; new UIDs become new studies.
        /// </summary>
        [HttpPost("{caseId:guid}/append")]
        public async Task<IActionResult> AppendToCase(Guid caseId, [FromBody] AppendCaseDto request)
        {
            var username = User.FindFirst("urn:radiopaedia:username")?.Value;

            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized("User session invalid. Please log in again.");
            }

            if (request.Studies.Count == 0 || request.Studies.Any(s => s.Series.Count == 0))
                return BadRequest("Append request must contain at least one study, each with at least one series.");

            var draft = await _repository.GetDraftCaseAsync(caseId);
            if (draft == null || !string.Equals(draft.Username, username, StringComparison.OrdinalIgnoreCase))
                return NotFound("Case not found.");

            if (draft.Status != "Completed" || string.IsNullOrEmpty(draft.RadiopaediaCaseId))
                return BadRequest("Studies can only be added to a case that has completed uploading to Radiopaedia.");

            // Radiopaedia rejects new imaging once a case leaves draft, so check with them
            // before queueing anything. The pipeline repeats this check before it uploads.
            try
            {
                await _reconciliation.EnsureAcceptsNewImagingAsync(
                    username, draft.RadiopaediaCaseId, caseId, forceRefresh: true);
            }
            catch (CaseNotEditableException ex)
            {
                _logger.LogInformation("Append to case {CaseId} refused: {Message}", caseId, ex.Message);
                return Conflict(new { message = ex.Message, remoteStatus = ex.RemoteStatus });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not verify the Radiopaedia status of case {CaseId}", caseId);
                return StatusCode(502, new
                {
                    message = "Could not check the case status with Radiopaedia. Please try again shortly."
                });
            }

            try
            {
                _logger.LogInformation("Received append of {StudyCount} study(ies) to case {CaseId} from {Username}",
                    request.Studies.Count, caseId, username);

                await _repository.AppendToDraftCaseAsync(caseId, request.Studies);

                return Ok(new
                {
                    Success = true,
                    CaseId = caseId,
                    Message = "Additional studies queued for upload."
                });
            }
            catch (DuplicateUploadJobException)
            {
                // Expected when a submission is sent more than once, not a fault.
                _logger.LogInformation("Append to case {CaseId} refused: an upload is already queued.", caseId);
                return Conflict(new
                {
                    message = "This case already has an upload in progress. Wait for it to finish before adding more studies."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to append to case {CaseId}.", caseId);
                return StatusCode(500, "Internal server error while appending to case.");
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