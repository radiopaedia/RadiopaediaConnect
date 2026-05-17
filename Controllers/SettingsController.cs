using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RadiopaediaConnect.Data;
using RadiopaediaConnect.Extensions;
using RadiopaediaConnect.Services;
using RadiopaediaConnect.Services.Dicom;

namespace RadiopaediaConnect.Controllers
{
    [ApiController]
    [Route("api/settings")]
    public class SettingsController : ControllerBase
    {
        private readonly SettingsRepository _repository;
        private readonly SettingsService _settingsService;
        private readonly OAuthCredentialsCache _oauthCache;
        private readonly IOptionsMonitor<OAuthOptions> _oauthOptions;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(
            SettingsRepository repository,
            SettingsService settingsService,
            OAuthCredentialsCache oauthCache,
            IOptionsMonitor<OAuthOptions> oauthOptions,
            ILogger<SettingsController> logger)
        {
            _repository = repository;
            _settingsService = settingsService;
            _oauthCache = oauthCache;
            _oauthOptions = oauthOptions;
            _logger = logger;
        }

        /// <summary>
        /// Returns the current setup status: whether the admin password is set
        /// and whether all required settings are configured.
        /// </summary>
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var isPasswordSet = await _repository.IsPasswordSetAsync();
            var validation = await _settingsService.ValidateAsync();

            return Ok(new
            {
                isPasswordSet,
                isConfigured = validation.IsValid,
                issues = validation.Issues
            });
        }

        /// <summary>
        /// First-run password creation. Only works when no password has been set yet.
        /// </summary>
        [HttpPost("password/setup")]
        public async Task<IActionResult> SetupPassword([FromBody] SetPasswordRequest request)
        {
            var isAlreadySet = await _repository.IsPasswordSetAsync();
            if (isAlreadySet)
            {
                return Conflict(new { message = "Admin password has already been configured." });
            }

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            {
                return BadRequest(new { message = "Password must be at least 6 characters." });
            }

            var hash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            await _repository.SetPasswordAsync(hash);

            _logger.LogInformation("[Settings] Admin password set for the first time.");
            return Ok(new { message = "Admin password configured successfully." });
        }

        /// <summary>
        /// Verify the admin password. Returns 200 if correct, 401 if not.
        /// </summary>
        [HttpPost("password/verify")]
        public async Task<IActionResult> VerifyPassword([FromBody] VerifyPasswordRequest request)
        {
            if (!await VerifyAdminPasswordAsync(request.Password))
            {
                return Unauthorized(new { message = "Invalid admin password." });
            }

            return Ok(new { message = "Password verified." });
        }

        /// <summary>
        /// Change the admin password. Requires the current password.
        /// </summary>
        [HttpPost("password/change")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            if (!await VerifyAdminPasswordAsync(request.CurrentPassword))
            {
                return Unauthorized(new { message = "Current password is incorrect." });
            }

            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            {
                return BadRequest(new { message = "New password must be at least 6 characters." });
            }

            var hash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            await _repository.SetPasswordAsync(hash);

            _logger.LogInformation("[Settings] Admin password changed.");
            return Ok(new { message = "Password changed successfully." });
        }

        /// <summary>
        /// Get local settings (SCP, Radiopaedia, etc).
        /// </summary>
        [HttpGet("local")]
        public async Task<IActionResult> GetLocalSettings()
        {
            if (!await AuthorizeAdminAsync()) return Unauthorized(new { message = "Invalid admin password." });

            var settings = await _repository.GetLocalSettingsAsync();
            return Ok(new
            {
                storageScpAeTitle = settings.StorageScpAeTitle,
                maxConcurrentDownloads = settings.MaxConcurrentDownloads,
                radiopaediaClientId = settings.RadiopaediaClientId ?? "",
                radiopaediaClientSecret = settings.RadiopaediaClientSecret ?? "",
                smtpHost = settings.SmtpHost ?? "",
                smtpPort = settings.SmtpPort,
                smtpUsername = settings.SmtpUsername ?? "",
                smtpPassword = settings.SmtpPassword ?? "",
                smtpFromAddress = settings.SmtpFromAddress ?? "",
                notificationRecipients = settings.NotificationRecipients ?? ""
            });
        }

        /// <summary>
        /// Save local settings.
        /// </summary>
        [HttpPut("local")]
        public async Task<IActionResult> SaveLocalSettings([FromBody] SaveLocalSettingsRequest request)
        {
            if (!await AuthorizeAdminAsync()) return Unauthorized(new { message = "Invalid admin password." });

            var previousSettings = await _repository.GetLocalSettingsAsync();
            bool scpChanged = previousSettings.StorageScpAeTitle != request.StorageScpAeTitle;

            var entity = new LocalSettingsEntity
            {
                StorageScpAeTitle = request.StorageScpAeTitle,
                MaxConcurrentDownloads = request.MaxConcurrentDownloads,
                RadiopaediaClientId = request.RadiopaediaClientId,
                RadiopaediaClientSecret = request.RadiopaediaClientSecret,
                SmtpHost = request.SmtpHost,
                SmtpPort = request.SmtpPort,
                SmtpUsername = request.SmtpUsername,
                SmtpPassword = request.SmtpPassword,
                SmtpFromAddress = request.SmtpFromAddress,
                NotificationRecipients = request.NotificationRecipients,
                UpdatedAtUtc = DateTime.UtcNow.ToString("o")
            };

            await _repository.SaveLocalSettingsAsync(entity);
            _settingsService.InvalidateCache();

            // refresh credentials cache so next login uses updated values
            _oauthCache.Refresh();
            var liveOptions = _oauthOptions.Get("Radiopaedia");
            if (!string.IsNullOrEmpty(request.RadiopaediaClientId))
                liveOptions.ClientId = request.RadiopaediaClientId;
            if (!string.IsNullOrEmpty(request.RadiopaediaClientSecret))
                liveOptions.ClientSecret = request.RadiopaediaClientSecret;

            _logger.LogInformation("[Settings] Local settings saved.");

            // Signal that the SCP needs to be restarted if AE Title changed
            return Ok(new
            {
                message = "Settings saved.",
                scpRestartRequired = scpChanged
            });
        }

        [HttpGet("nodes")]
        public async Task<IActionResult> GetRemoteNodes()
        {
            if (!await AuthorizeAdminAsync()) return Unauthorized(new { message = "Invalid admin password." });

            var nodes = await _repository.GetRemoteNodesAsync();
            return Ok(nodes.Select(n => new
            {
                n.Id,
                n.Name,
                n.AeTitle,
                n.Host,
                n.Port,
                n.CallingAe,
                n.SortOrder
            }));
        }

        [HttpPost("nodes")]
        public async Task<IActionResult> AddRemoteNode([FromBody] SaveRemoteNodeRequest request)
        {
            if (!await AuthorizeAdminAsync()) return Unauthorized(new { message = "Invalid admin password." });

            var entity = new RemoteNodeEntity
            {
                Name = request.Name,
                AeTitle = request.AeTitle,
                Host = request.Host,
                Port = request.Port,
                CallingAe = request.CallingAe,
                SortOrder = request.SortOrder
            };

            var id = await _repository.AddRemoteNodeAsync(entity);
            _settingsService.InvalidateCache();

            _logger.LogInformation($"[Settings] Remote node '{request.Name}' added (Id: {id}).");
            return Ok(new { id, message = "Node added." });
        }

        [HttpPut("nodes/{id}")]
        public async Task<IActionResult> UpdateRemoteNode(int id, [FromBody] SaveRemoteNodeRequest request)
        {
            if (!await AuthorizeAdminAsync()) return Unauthorized(new { message = "Invalid admin password." });

            var existing = await _repository.GetRemoteNodeAsync(id);
            if (existing == null) return NotFound(new { message = "Node not found." });

            existing.Name = request.Name;
            existing.AeTitle = request.AeTitle;
            existing.Host = request.Host;
            existing.Port = request.Port;
            existing.CallingAe = request.CallingAe;
            existing.SortOrder = request.SortOrder;

            await _repository.UpdateRemoteNodeAsync(existing);
            _settingsService.InvalidateCache();

            _logger.LogInformation($"[Settings] Remote node '{request.Name}' updated (Id: {id}).");
            return Ok(new { message = "Node updated." });
        }

        [HttpDelete("nodes/{id}")]
        public async Task<IActionResult> DeleteRemoteNode(int id)
        {
            if (!await AuthorizeAdminAsync()) return Unauthorized(new { message = "Invalid admin password." });

            var existing = await _repository.GetRemoteNodeAsync(id);
            if (existing == null) return NotFound(new { message = "Node not found." });

            await _repository.DeleteRemoteNodeAsync(id);
            _settingsService.InvalidateCache();

            _logger.LogInformation($"[Settings] Remote node '{existing.Name}' deleted (Id: {id}).");
            return Ok(new { message = "Node deleted." });
        }

        [HttpPost("nodes/reorder")]
        public async Task<IActionResult> ReorderNodes([FromBody] ReorderNodesRequest request)
        {
            if (!await AuthorizeAdminAsync()) return Unauthorized(new { message = "Invalid admin password." });

            await _repository.ReorderRemoteNodesAsync(request.OrderedIds);
            _settingsService.InvalidateCache();

            return Ok(new { message = "Nodes reordered." });
        }

        /// <summary>
        /// C-ECHO test for a remote node. Can use an existing node ID or ad-hoc parameters.
        /// </summary>
        [HttpPost("nodes/echo")]
        public async Task<IActionResult> EchoNode([FromBody] EchoRequest request)
        {
            if (!await AuthorizeAdminAsync()) return Unauthorized(new { message = "Invalid admin password." });

            var localSettings = await _repository.GetLocalSettingsAsync();
            var callingAe = request.CallingAe ?? localSettings.StorageScpAeTitle ?? "RCONNECT_SCP";

            try
            {
                var client = DicomClientFactory.Create(
                    request.Host, request.Port, false, callingAe, request.AeTitle);

                client.ClientOptions.AssociationRequestTimeoutInMs = 5000;

                var echoRequest = new DicomCEchoRequest();
                bool success = false;
                string message = "No response received.";

                echoRequest.OnResponseReceived = (req, response) =>
                {
                    success = response.Status == DicomStatus.Success;
                    message = success ? "C-ECHO successful." : $"C-ECHO status: {response.Status}";
                };

                await client.AddRequestAsync(echoRequest);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await client.SendAsync(cts.Token);

                return Ok(new { success, message });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[C-ECHO] Failed for {request.AeTitle}@{request.Host}:{request.Port} - {ex.Message}");
                return Ok(new { success = false, message = $"Connection failed: {ex.Message}" });
            }
        }

        /// <summary>
        /// Restart the DICOM SCP service. Called after AE Title changes.
        /// </summary>
        [HttpPost("scp/restart")]
        public async Task<IActionResult> RestartScp([FromServices] DicomScpManager scpManager)
        {
            if (!await AuthorizeAdminAsync()) return Unauthorized(new { message = "Invalid admin password." });

            try
            {
                var settings = await _settingsService.GetDicomSettingsAsync();
                var allowedAeTitles = settings.RemoteNodes.Select(n => n.AeTitle).Where(ae => !string.IsNullOrWhiteSpace(ae));
                scpManager.Restart(settings.Scp.AeTitle, allowedAeTitles);
                return Ok(new { message = "SCP restarted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Settings] Failed to restart SCP.");
                return StatusCode(500, new { message = $"Failed to restart SCP: {ex.Message}" });
            }
        }

        private async Task<bool> AuthorizeAdminAsync()
        {
            var password = Request.Headers["X-Admin-Password"].FirstOrDefault();
            if (string.IsNullOrEmpty(password)) return false;
            return await VerifyAdminPasswordAsync(password);
        }

        private async Task<bool> VerifyAdminPasswordAsync(string? password)
        {
            if (string.IsNullOrEmpty(password)) return false;

            var hash = await _repository.GetPasswordHashAsync();
            if (string.IsNullOrEmpty(hash)) return false;

            return BCrypt.Net.BCrypt.Verify(password, hash);
        }

        /// <summary>
        /// Password recovery via the Radiopaedia App Secret stored in local settings.
        /// If the provided secret matches, the admin password hash is cleared so the
        /// user can run through first-time setup again.
        /// </summary>
        [HttpPost("password/recover")]
        public async Task<IActionResult> RecoverPassword([FromBody] RecoverPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.AppSecret))
            {
                return BadRequest(new { message = "App secret is required." });
            }

            var settings = await _repository.GetLocalSettingsAsync();

            // Constant-time comparison prevents timing attacks
            var storedSecret = settings.RadiopaediaClientSecret ?? string.Empty;
            var secretsMatch = CryptographicEquals(storedSecret, request.AppSecret.Trim());

            if (!secretsMatch || string.IsNullOrEmpty(storedSecret))
            {
                _logger.LogWarning("[Settings] Failed password recovery attempt.");
                return Unauthorized(new { message = "App secret is incorrect." });
            }

            await _repository.ClearPasswordAsync();

            _logger.LogInformation("[Settings] Admin password cleared via app secret recovery.");
            return Ok(new { message = "Password cleared. Please set a new admin password." });
        }

        // Constant-time string comparison to avoid timing-based secret enumeration
        private static bool CryptographicEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            int result = 0;
            for (int i = 0; i < a.Length; i++)
                result |= a[i] ^ b[i];
            return result == 0;
        }

        public class RecoverPasswordRequest
        {
            public string AppSecret { get; set; } = string.Empty;
        }

        public class SetPasswordRequest
        {
            public string Password { get; set; } = string.Empty;
        }

        public class VerifyPasswordRequest
        {
            public string Password { get; set; } = string.Empty;
        }

        public class ChangePasswordRequest
        {
            public string CurrentPassword { get; set; } = string.Empty;
            public string NewPassword { get; set; } = string.Empty;
        }

        public class SaveLocalSettingsRequest
        {
            public string StorageScpAeTitle { get; set; } = "RCONNECT_SCP";
            public int MaxConcurrentDownloads { get; set; } = 5;
            public string? RadiopaediaClientId { get; set; }
            public string? RadiopaediaClientSecret { get; set; }
            public string? SmtpHost { get; set; }
            public int? SmtpPort { get; set; }
            public string? SmtpUsername { get; set; }
            public string? SmtpPassword { get; set; }
            public string? SmtpFromAddress { get; set; }
            public string? NotificationRecipients { get; set; }
        }

        public class SaveRemoteNodeRequest
        {
            public string Name { get; set; } = string.Empty;
            public string AeTitle { get; set; } = string.Empty;
            public string Host { get; set; } = string.Empty;
            public int Port { get; set; } = 104;
            public string CallingAe { get; set; } = "RCONNECT_SCU";
            public int SortOrder { get; set; }
        }

        public class ReorderNodesRequest
        {
            public List<int> OrderedIds { get; set; } = new();
        }

        public class EchoRequest
        {
            public string Host { get; set; } = string.Empty;
            public int Port { get; set; } = 104;
            public string AeTitle { get; set; } = string.Empty;
            public string? CallingAe { get; set; }
        }
    }
}