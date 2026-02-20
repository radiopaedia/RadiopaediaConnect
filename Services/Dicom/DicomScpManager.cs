using FellowOakDicom;
using FellowOakDicom.Network;
using RadiopaediaConnect.Data;
using System.Text;

namespace RadiopaediaConnect.Services.Dicom
{
    /// <summary>
    /// Manages the lifecycle of the DICOM SCP server.
    /// Supports stopping and restarting (e.g. after AE Title changes).
    /// Port is always 104 (container users map it externally).
    /// </summary>
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
            {
                Directory.CreateDirectory(storagePath);
            }
        }

        /// <summary>
        /// The AE Title the SCP is currently configured with.
        /// </summary>
        public string CurrentAeTitle => _currentAeTitle;

        /// <summary>
        /// Start the SCP with the specified AE Title on port 104.
        /// </summary>
        public void Start(string aeTitle)
        {
            lock (_lock)
            {
                _currentAeTitle = aeTitle;

                _logger.LogInformation($"[DICOM] Starting SCP '{aeTitle}' on Port 104");
                _logger.LogInformation($"[DICOM] Storage Root: {_repository.GetStorageRoot()}");

                // Store the AE Title and repository so the service class can access them
                CStoreService.StaticRepository = _repository;
                CStoreService.ConfiguredAeTitle = aeTitle;

                _server = DicomServerFactory.Create<CStoreService>(104);
            }
        }

        /// <summary>
        /// Stop the SCP.
        /// </summary>
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

        /// <summary>
        /// Restart the SCP with a new AE Title (port stays at 104).
        /// </summary>
        public void Restart(string aeTitle)
        {
            _logger.LogInformation($"[DICOM] Restarting SCP with AE Title '{aeTitle}'...");
            Stop();

            // Brief pause to allow the port to be released
            Thread.Sleep(500);

            Start(aeTitle);
            _logger.LogInformation("[DICOM] SCP restarted.");
        }
    }

    /// <summary>
    /// DICOM C-STORE service provider. Handles incoming DICOM associations.
    /// Uses static fields as a bridge since DicomServerFactory instantiates
    /// this class internally.
    /// </summary>
    public class CStoreService : DicomService, IDicomServiceProvider, IDicomCStoreProvider, IDicomCEchoProvider
    {
        internal static DicomRepository? StaticRepository;
        internal static string ConfiguredAeTitle = "RCONNECT_SCP";

        public CStoreService(INetworkStream stream, Encoding fallbackEncoding, ILogger log, DicomServiceDependencies dependencies)
            : base(stream, fallbackEncoding, log, dependencies)
        {
        }

        public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
        {
            Logger.LogInformation($"[SCP] Incoming connection from {association.CallingAE}");
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
            var studyUid = dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "UNKNOWN");
            var seriesUid = dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, "UNKNOWN");
            var sopUid = dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, "UNKNOWN");

            var seriesFolder = StaticRepository.GetSeriesStoragePath(studyUid, seriesUid);

            try
            {
                if (!Directory.Exists(seriesFolder))
                {
                    Directory.CreateDirectory(seriesFolder);
                }

                var filePath = Path.Combine(seriesFolder, $"{sopUid}.dcm");
                await request.File.SaveAsync(filePath);

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