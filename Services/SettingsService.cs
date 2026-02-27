using RadiopaediaConnect.Data;
using RadiopaediaConnect.Models;

namespace RadiopaediaConnect.Services
{
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

        public async Task<(string? ClientId, string? ClientSecret)> GetRadiopaediaCredentialsAsync()
        {
            await EnsureLoadedAsync();
            return (_cachedSettings!.RadiopaediaClientId, _cachedSettings!.RadiopaediaClientSecret);
        }

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