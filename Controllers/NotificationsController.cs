using Microsoft.AspNetCore.Mvc;
using RadiopaediaConnect.Services;

namespace RadiopaediaConnect.Controllers
{
    // Anonymous by design: guarded by the admin session cookie, not the Radiopaedia login.
    [ApiController]
    [Route("api/notifications")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public class NotificationsController : AdminControllerBase
    {
        private readonly INotificationService _notificationService;
        private readonly AdminSessionService _sessionService;

        public NotificationsController(INotificationService notificationService, AdminSessionService sessionService)
        {
            _notificationService = notificationService;
            _sessionService = sessionService;
        }

        [HttpPost("test")]
        public async Task<IActionResult> SendTestEmail([FromBody] TestEmailRequest? request)
        {
            if (!AuthorizeAdmin(_sessionService)) return Unauthorized(new { message = "Invalid admin session." });

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

        public class TestEmailRequest
        {
            public string? Subject { get; set; }
            public string? Body    { get; set; }
        }
    }
}
