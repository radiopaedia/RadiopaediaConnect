using Microsoft.AspNetCore.Mvc;
using RadiopaediaConnect.Data;

namespace RadiopaediaConnect.Controllers
{
    [ApiController]
    [Route("api/logs")]
    public class LogsController : ControllerBase
    {
        private readonly AppLogsRepository _logsRepository;
        private readonly SettingsRepository _settingsRepository;

        public LogsController(AppLogsRepository logsRepository, SettingsRepository settingsRepository)
        {
            _logsRepository = logsRepository;
            _settingsRepository = settingsRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetLogs(
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] string? level,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100)
        {
            if (!await AuthorizeAdminAsync()) return Unauthorized(new { message = "Invalid admin password." });

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
            if (!await AuthorizeAdminAsync()) return Unauthorized(new { message = "Invalid admin password." });
            await _logsRepository.PruneOldLogsAsync(retentionDays);
            return Ok(new { message = $"Logs older than {retentionDays} days deleted." });
        }

        private async Task<bool> AuthorizeAdminAsync()
        {
            var password = Request.Headers["X-Admin-Password"].FirstOrDefault();
            if (string.IsNullOrEmpty(password)) return false;
            var hash = await _settingsRepository.GetPasswordHashAsync();
            if (string.IsNullOrEmpty(hash)) return false;
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
    }
}
