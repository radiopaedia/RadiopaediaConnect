using Microsoft.AspNetCore.Mvc;
using RadiopaediaConnect.Data;
using RadiopaediaConnect.Services;

namespace RadiopaediaConnect.Controllers
{
    [ApiController]
    [Route("api/logs")]
    public class LogsController : AdminControllerBase
    {
        private readonly AppLogsRepository _logsRepository;
        private readonly AdminSessionService _sessionService;

        public LogsController(AppLogsRepository logsRepository, AdminSessionService sessionService)
        {
            _logsRepository = logsRepository;
            _sessionService = sessionService;
        }

        [HttpGet]
        public async Task<IActionResult> GetLogs(
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] string? level,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100)
        {
            if (!AuthorizeAdmin(_sessionService)) return Unauthorized(new { message = "Invalid admin session." });

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 1;
            if (pageSize > 500) pageSize = 500;

            var (items, total) = await _logsRepository.QueryAsync(startDate, endDate, level, page, pageSize);

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

        [HttpDelete]
        public async Task<IActionResult> PruneLogs([FromQuery] int retentionDays = 30)
        {
            if (!AuthorizeAdmin(_sessionService)) return Unauthorized(new { message = "Invalid admin session." });
            await _logsRepository.PruneOldLogsAsync(retentionDays);
            return Ok(new { message = $"Logs older than {retentionDays} days deleted." });
        }
    }
}
