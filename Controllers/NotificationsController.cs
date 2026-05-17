using Microsoft.AspNetCore.Mvc;
using RadiopaediaConnect.Data;
using RadiopaediaConnect.Services;

namespace RadiopaediaConnect.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationService _notificationService;
        private readonly SettingsRepository _settingsRepository;

        public NotificationsController(INotificationService notificationService, SettingsRepository settingsRepository)
        {
            _notificationService = notificationService;
            _settingsRepository = settingsRepository;
        }

        [HttpPost("test")]
        public async Task<IActionResult> SendTestEmail([FromBody] TestEmailRequest? request)
        {
            if (!await AuthorizeAdminAsync()) return Unauthorized(new { message = "Invalid admin password." });

            var subject = request?.Subject ?? "RadiopaediaConnect Test";
            var body = request?.Body ?? "This is a test notification from RadiopaediaConnect. If you received this, your SMTP configuration is working correctly.";

            try
            {
                await _notificationService.SendAsync(subject, body);
                return Ok(new { message = "Test email sent successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Failed to send test email: {ex.Message}" });
            }
        }

        private async Task<bool> AuthorizeAdminAsync()
        {
            var password = Request.Headers["X-Admin-Password"].FirstOrDefault();
            if (string.IsNullOrEmpty(password)) return false;
            var hash = await _settingsRepository.GetPasswordHashAsync();
            if (string.IsNullOrEmpty(hash)) return false;
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }

        public class TestEmailRequest
        {
            public string? Subject { get; set; }
            public string? Body    { get; set; }
        }
    }
}
