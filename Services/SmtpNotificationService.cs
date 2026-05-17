using System.Net;
using System.Net.Mail;

namespace RadiopaediaConnect.Services
{
    public class SmtpNotificationService : INotificationService
    {
        private readonly SettingsService _settingsService;
        private readonly ILogger<SmtpNotificationService> _logger;

        public SmtpNotificationService(SettingsService settingsService, ILogger<SmtpNotificationService> logger)
        {
            _settingsService = settingsService;
            _logger = logger;
        }

        public async Task SendAsync(string subject, string body, string? jobId = null)
        {
            try
            {
                var settings = await _settingsService.GetLocalSettingsAsync();

                if (string.IsNullOrWhiteSpace(settings.SmtpHost) ||
                    string.IsNullOrWhiteSpace(settings.SmtpFromAddress) ||
                    string.IsNullOrWhiteSpace(settings.NotificationRecipients))
                {
                    _logger.LogInformation("[Notifications] SMTP not configured, skipping notification.");
                    return;
                }

                var recipients = settings.NotificationRecipients
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(r => r.Contains('@'))
                    .ToList();

                if (recipients.Count == 0) return;

                int port = settings.SmtpPort ?? 587;
                bool useSsl = port == 587 || port == 465;

                using var smtp = new SmtpClient(settings.SmtpHost, port)
                {
                    EnableSsl = useSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                };

                if (!string.IsNullOrWhiteSpace(settings.SmtpUsername))
                {
                    smtp.Credentials = new NetworkCredential(settings.SmtpUsername, settings.SmtpPassword ?? "");
                }

                var fullBody = jobId != null
                    ? $"{body}\n\n---\nJob ID: {jobId}\nTimestamp: {DateTime.UtcNow:u} UTC"
                    : $"{body}\n\n---\nTimestamp: {DateTime.UtcNow:u} UTC";

                using var message = new MailMessage
                {
                    From = new MailAddress(settings.SmtpFromAddress, "RadiopaediaConnect"),
                    Subject = $"[RadiopaediaConnect] {subject}",
                    Body = fullBody,
                    IsBodyHtml = false,
                };

                foreach (var recipient in recipients)
                    message.To.Add(recipient);

                await smtp.SendMailAsync(message);
                _logger.LogInformation("[Notifications] Email sent: {Subject} -> {Count} recipient(s)", subject, recipients.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Notifications] Failed to send email: {Subject}", subject);
                // Never re-throw — notification failures must not break the pipeline
            }
        }
    }
}
