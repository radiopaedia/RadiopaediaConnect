using RadiopaediaConnect.Data;
using RadiopaediaConnect.Models;

namespace RadiopaediaConnect.Services
{
    /// <summary>
    /// Provides application settings from the database.
    /// Reads from DB on first access, then holds in memory until
    /// InvalidateCache() is called (which happens after every save
    /// in SettingsController). This is the same pattern as IOptions -
    /// load once, hold forever, refresh on change.
    /// </summary>
    public class SettingsService
    {
        private readonly SettingsRepository _repository;
        private readonly ILogger<SettingsService> _logger;

        private LocalSettingsEntity? _cachedSettings;
        private List<RemoteNodeEntity>? _cachedNodes;

        public SettingsService(SettingsRepository repository, ILogger<SettingsService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        /// <summary>
        /// Clear the in-memory copy so the next read goes to the DB.
        /// Called by SettingsController after any write operation.
        /// </summary>
        public void InvalidateCache()
        {
            _cachedSettings = null;
            _cachedNodes = null;
        }

        private async Task EnsureLoadedAsync()
        {
            if (_cachedSettings != null && _cachedNodes != null)
                return;

            _cachedSettings = await _repository.GetLocalSettingsAsync();
            _cachedNodes = await _repository.GetRemoteNodesAsync();
        }

        public async Task<LocalSettingsEntity> GetLocalSettingsAsync()
        {
            await EnsureLoadedAsync();
            return _cachedSettings!;
        }

        public async Task<List<RemoteNodeEntity>> GetRemoteNodesAsync()
        {
            await EnsureLoadedAsync();
            return _cachedNodes!;
        }

        /// <summary>
        /// Build a DicomSettings object from the current DB state.
        /// Used by DicomScu, DicomQueueWorker, DicomController, etc.
        /// </summary>
        public async Task<DicomSettings> GetDicomSettingsAsync()
        {
            await EnsureLoadedAsync();

            var settings = _cachedSettings!;
            var nodes = _cachedNodes!;

            return new DicomSettings
            {
                MaxConcurrentDownloads = settings.MaxConcurrentDownloads,
                Scp = new ScpSettings
                {
                    AeTitle = settings.StorageScpAeTitle ?? "RCONNECT_SCP",
                    Port = 104
                },
                RemoteNodes = nodes.Select(n => new RemoteNode
                {
                    Name = n.Name,
                    AeTitle = n.AeTitle,
                    Host = n.Host,
                    Port = n.Port,
                    CallingAe = n.CallingAe
                }).ToList()
            };
        }

        /// <summary>
        /// Get Radiopaedia OAuth credentials from the DB.
        /// </summary>
        public async Task<(string? ClientId, string? ClientSecret)> GetRadiopaediaCredentialsAsync()
        {
            await EnsureLoadedAsync();
            return (_cachedSettings!.RadiopaediaClientId, _cachedSettings!.RadiopaediaClientSecret);
        }

        /// <summary>
        /// Validates that all required settings are configured.
        /// Returns a list of validation issues (empty = valid).
        /// </summary>
        public async Task<SettingsValidationResult> ValidateAsync()
        {
            await EnsureLoadedAsync();
            var issues = new List<string>();

            var settings = _cachedSettings!;
            var nodes = _cachedNodes!;

            if (string.IsNullOrWhiteSpace(settings.StorageScpAeTitle))
                issues.Add("Storage SCP AE Title is not set.");

            if (string.IsNullOrWhiteSpace(settings.RadiopaediaClientId))
                issues.Add("Radiopaedia Client ID is not set.");

            if (string.IsNullOrWhiteSpace(settings.RadiopaediaClientSecret))
                issues.Add("Radiopaedia Client Secret is not set.");

            if (nodes.Count == 0)
                issues.Add("No remote DICOM nodes configured.");

            return new SettingsValidationResult
            {
                IsValid = issues.Count == 0,
                Issues = issues
            };
        }
    }

    public class SettingsValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Issues { get; set; } = new();
    }
}