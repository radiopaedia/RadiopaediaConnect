using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RadiopaediaConnect.Data;
using RadiopaediaConnect.Models;
using RadiopaediaConnect.Services.Dicom;

namespace RadiopaediaConnect.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DicomController : ControllerBase
    {
        private readonly DicomRepository _repository;
        private readonly DicomScu _dicomScu;
        private readonly DicomSettings _settings;
        private readonly ILogger<DicomController> _logger;

        public DicomController(
            DicomRepository repository,
            DicomScu dicomScu,
            IOptions<DicomSettings> settings,
            ILogger<DicomController> logger)
        {
            _repository = repository;
            _dicomScu = dicomScu;
            _settings = settings.Value;
            _logger = logger;
        }

        [HttpGet("nodes")]
        public IActionResult GetNodes()
        {
            var nodes = _settings.RemoteNodes.Select(n => new
            {
                n.Name,
                n.AeTitle,
                n.Host,
                Label = $"{n.Name} ({n.AeTitle}@{n.Host})"
            });
            return Ok(nodes);
        }

        [HttpPost("studies")]
        public async Task<IActionResult> SearchStudies([FromBody] DicomSearchCriteria criteria)
        {
            if (string.IsNullOrEmpty(criteria.RemoteNodeName))
            {
                var defaultNode = _settings.RemoteNodes.FirstOrDefault();
                if (defaultNode == null) return BadRequest("No DICOM nodes configured.");
                criteria.RemoteNodeName = defaultNode.Name;
            }

            _logger.LogInformation($"Searching Studies on {criteria.RemoteNodeName} (POST)");

            var results = await _dicomScu.FindStudiesAsync(criteria);
            return Ok(results);
        }

        [HttpGet("series")]
        public async Task<IActionResult> SearchSeries([FromQuery] string studyUid, [FromQuery] string nodeName)
        {
            if (string.IsNullOrEmpty(studyUid) || string.IsNullOrEmpty(nodeName))
                return BadRequest("StudyUID and NodeName are required.");

            _logger.LogInformation($"Searching Series on {nodeName} for Study {studyUid}");

            var results = await _dicomScu.FindSeriesAsync(studyUid, nodeName);
            return Ok(results);
        }

        [HttpPost("preview")]
        public async Task<IActionResult> PreviewSeries([FromBody] PreviewRequest request)
        {
            if (string.IsNullOrEmpty(request.StudyInstanceUid) || string.IsNullOrEmpty(request.SeriesInstanceUid))
                return BadRequest("Study and Series UIDs are required for preview.");

            var targetAeTitle = request.RemoteAeTitle;
            if (string.IsNullOrEmpty(targetAeTitle))
            {
                var defaultNode = _settings.RemoteNodes.FirstOrDefault();
                if (defaultNode == null) return StatusCode(500, "No Remote DICOM Nodes configured.");
                targetAeTitle = defaultNode.AeTitle;
            }

            var existing = await _repository.GetSeriesAsync(request.SeriesInstanceUid);
            if (existing != null && existing.IsRetrieved)
            {
                if (Directory.Exists(existing.StoragePath) && Directory.EnumerateFiles(existing.StoragePath, "*.dcm").Any())
                {
                    return Ok(new PreviewResponse
                    {
                        Status = "Ready",
                        Message = "Series ready for viewing."
                    });
                }
            }

            bool isAlreadyQueued = await _repository.IsJobPendingOrRunningAsync(request.StudyInstanceUid, request.SeriesInstanceUid);
            if (isAlreadyQueued)
            {
                return Ok(new PreviewResponse
                {
                    Status = "Queued",
                    Message = "Preview download in progress."
                });
            }

            var job = new DicomJob
            {
                Id = Guid.NewGuid(),
                StudyInstanceUid = request.StudyInstanceUid,
                SeriesInstanceUid = request.SeriesInstanceUid,
                RemoteAeTitle = targetAeTitle,
                Type = JobType.Preview,
                Priority = 0,
                Status = JobStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            await _repository.EnqueueJobAsync(job);

            return Ok(new PreviewResponse
            {
                JobId = job.Id,
                Status = "Queued",
                Message = "Preview requested."
            });
        }

        [HttpGet("status/{jobId}")]
        public async Task<IActionResult> GetStatus(Guid jobId)
        {
            var job = await _repository.GetJobAsync(jobId);
            if (job == null) return NotFound("Job not found.");

            return Ok(new
            {
                job.Id,
                Status = job.Status.ToString(),
                job.ErrorMessage
            });
        }

        public class PreviewRequest
        {
            public string StudyInstanceUid { get; set; } = string.Empty;
            public string SeriesInstanceUid { get; set; } = string.Empty;
            public string? RemoteAeTitle { get; set; }
        }

        public class PreviewResponse
        {
            public Guid? JobId { get; set; }
            public string Status { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
        }
    }
}