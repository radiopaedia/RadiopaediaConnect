using FellowOakDicom;
using FellowOakDicom.Network;
using RadiopaediaConnect.Data;
using System.Text;

namespace RadiopaediaConnect.Services.Dicom
{
    public class DicomScpManager
    {
        private readonly DicomRepository _repository;
        private readonly ILogger<DicomScpManager> _logger;
        private readonly object _lock = new();

        private IDicomServer? _server;
        private string _currentAeTitle = "RCONNECT_SCP";

        public DicomScpManager(DicomRepository repository, ILogger<DicomScpManager> logger)
        {
            _repository = repository;
            _logger = logger;

            var storagePath = _repository.GetStorageRoot();
            if (!Directory.Exists(storagePath))
                Directory.CreateDirectory(storagePath);

            var dicomRoot = _repository.GetDicomRoot();
            if (!Directory.Exists(dicomRoot))
                Directory.CreateDirectory(dicomRoot);
        }

        public string CurrentAeTitle => _currentAeTitle;

        public void Start(string aeTitle, IEnumerable<string>? allowedCallingAeTitles = null)
        {
            lock (_lock)
            {
                _currentAeTitle = aeTitle;

                _logger.LogInformation($"[DICOM] Starting SCP '{aeTitle}' on Port 104");
                _logger.LogInformation($"[DICOM] Storage Root: {_repository.GetStorageRoot()}");

                CStoreService.StaticRepository = _repository;
                CStoreService.ConfiguredAeTitle = aeTitle;
                CStoreService.AllowedCallingAeTitles = allowedCallingAeTitles is not null
                    ? new HashSet<string>(allowedCallingAeTitles, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (CStoreService.AllowedCallingAeTitles.Count == 0)
                    _logger.LogWarning("[DICOM] No allowed calling AE titles configured — accepting connections from any AE title.");
                else
                    _logger.LogInformation("[DICOM] Allowed calling AE titles: {AETitles}", string.Join(", ", CStoreService.AllowedCallingAeTitles));

                _server = DicomServerFactory.Create<CStoreService>(104);
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (_server != null)
                {
                    _logger.LogInformation("[DICOM] Stopping SCP...");
                    _server.Stop();
                    _server.Dispose();
                    _server = null;
                }
            }
        }

        public void Restart(string aeTitle, IEnumerable<string>? allowedCallingAeTitles = null)
        {
            _logger.LogInformation($"[DICOM] Restarting SCP with AE Title '{aeTitle}'...");
            Stop();

            // Brief pause to allow the port to be released
            Thread.Sleep(500);

            Start(aeTitle, allowedCallingAeTitles);
            _logger.LogInformation("[DICOM] SCP restarted.");
        }
    }

    public class CStoreService : DicomService, IDicomServiceProvider, IDicomCStoreProvider, IDicomCEchoProvider
    {
        internal static DicomRepository? StaticRepository;
        internal static string ConfiguredAeTitle = "RCONNECT_SCP";
        internal static HashSet<string> AllowedCallingAeTitles = new(StringComparer.OrdinalIgnoreCase);

        public CStoreService(INetworkStream stream, Encoding fallbackEncoding, ILogger log, DicomServiceDependencies dependencies)
            : base(stream, fallbackEncoding, log, dependencies)
        {
        }

        public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
        {
            var callingAe = association.CallingAE?.Trim() ?? "";

            if (AllowedCallingAeTitles.Count > 0 && !AllowedCallingAeTitles.Contains(callingAe))
            {
                Logger.LogWarning("[SCP] Rejected association from unknown AE title '{CallingAE}'", callingAe);
                return SendAssociationRejectAsync(
                    DicomRejectResult.Permanent,
                    DicomRejectSource.ServiceUser,
                    DicomRejectReason.CallingAENotRecognized);
            }

            Logger.LogInformation("[SCP] Accepted association from '{CallingAE}'", callingAe);
            foreach (var pc in association.PresentationContexts)
            {
                if (pc.AbstractSyntax == DicomUID.Verification ||
                    pc.AbstractSyntax.StorageCategory != DicomStorageCategory.None)
                {
                    pc.AcceptTransferSyntaxes(pc.GetTransferSyntaxes().ToArray());
                }
            }
            return SendAssociationAcceptAsync(association);
        }

        public Task OnReceiveAssociationReleaseRequestAsync() => SendAssociationReleaseResponseAsync();

        public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason) =>
            Logger.LogWarning($"[SCP] Aborted: {source} - {reason}");

        public void OnConnectionClosed(Exception exception) { }

        public Task<DicomCEchoResponse> OnCEchoRequestAsync(DicomCEchoRequest request) =>
            Task.FromResult(new DicomCEchoResponse(request, DicomStatus.Success));

        public async Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
        {
            if (StaticRepository == null)
            {
                Logger.LogError("[SCP] Repository bridge is null. Cannot determine storage path.");
                return new DicomCStoreResponse(request, DicomStatus.ProcessingFailure);
            }

            var dataset = request.File.Dataset;
            var studyUid = dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "");
            var seriesUid = dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, "");
            var sopUid = dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, "");

            try
            {
                studyUid = RadiopaediaConnect.Data.DicomRepository.SanitizeDicomUid(studyUid);
                seriesUid = RadiopaediaConnect.Data.DicomRepository.SanitizeDicomUid(seriesUid);
                sopUid = RadiopaediaConnect.Data.DicomRepository.SanitizeDicomUid(sopUid);
            }
            catch (ArgumentException ex)
            {
                Logger.LogWarning("[SCP] Rejected C-STORE with invalid UID: {Message}", ex.Message);
                return new DicomCStoreResponse(request, DicomStatus.InvalidAttributeValue);
            }

            var seriesFolder = StaticRepository.GetSeriesStoragePath(studyUid, seriesUid);

            try
            {
                if (!Directory.Exists(seriesFolder))
                {
                    Directory.CreateDirectory(seriesFolder);
                }

                var filePath = Path.Combine(seriesFolder, $"{sopUid}.dcm");
                await request.File.SaveAsync(filePath);
                Logger.LogInformation("[SCP] Stored DICOM: {SeriesUid}/{SopUid}.dcm", seriesUid, sopUid);

                return new DicomCStoreResponse(request, DicomStatus.Success);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SCP] Failed to save DICOM: {ex.Message}");
                return new DicomCStoreResponse(request, DicomStatus.ProcessingFailure);
            }
        }

        public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e)
        {
            Logger.LogError($"[SCP] C-STORE Exception: {e.Message}");
            return Task.CompletedTask;
        }
    }
}