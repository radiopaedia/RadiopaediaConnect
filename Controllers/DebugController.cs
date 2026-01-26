using Microsoft.AspNetCore.Mvc;
using RadiopaediaConnect.Services.Dicom;

namespace RadiopaediaConnect.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DebugController : ControllerBase
    {
        private readonly DicomScu _dicomScu;

        public DebugController(DicomScu dicomScu)
        {
            _dicomScu = dicomScu;
        }

        [HttpPost("test-cmove")]
        public async Task<IActionResult> TestCMove([FromBody] TestMoveRequest request)
        {
            if (string.IsNullOrEmpty(request.RemoteNode))
            {
                request.RemoteNode = "SANTESRV1";
            }

            var success = await _dicomScu.TriggerCMoveAsync(
                request.StudyUid,
                request.SeriesUid ?? "",
                request.RemoteNode
            );

            if (success)
            {
                return Ok(new { Message = "C-MOVE initiated successfully. Check server logs and /data folder for files." });
            }
            else
            {
                return BadRequest(new { Message = "C-MOVE failed. Check console logs." });
            }
        }
    }

    public class TestMoveRequest
    {
        public string StudyUid { get; set; } = string.Empty;
        public string SeriesUid { get; set; } = string.Empty;
        public string RemoteNode { get; set; } = "SANTESRV1";
    }
}