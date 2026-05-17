using FellowOakDicom;
using FellowOakDicom.Network;
using RadiopaediaConnect.Data;
using System.Text;

namespace RadiopaediaConnect.Services.Dicom
{
    public class DicomScp
    {
        private readonly IConfiguration _configuration;
        private IDicomServer? _server;
        private readonly DicomRepository _repository;

        // Static bridge to allow the nested Service class to access the Repository
        private static DicomRepository? _staticRepository;

        public DicomScp(IConfiguration configuration, DicomRepository repository)
        {
            _configuration = configuration;
            _repository = repository;
            _staticRepository = repository;

            var storagePath = _repository.GetStorageRoot();
            if (!Directory.Exists(storagePath))
            {
                Directory.CreateDirectory(storagePath);
            }
        }

        public void Start()
        {
            var port = _configuration.GetValue<int>("Dicom:Scp:Port", 104);
            var aeTitle = _configuration.GetValue<string>("Dicom:Scp:AeTitle", "RCONNECT_SCP");

            Console.WriteLine($"[DICOM] Starting SCP '{aeTitle}' on Port {port}");
            Console.WriteLine($"[DICOM] Storage Root: {_repository.GetStorageRoot()}");

            _server = DicomServerFactory.Create<CStoreService>(port);
        }

        public void Stop()
        {
            if (_server != null)
            {
                Console.WriteLine("[DICOM] Stopping SCP...");
                _server.Stop();
                _server.Dispose();
                _server = null;
            }
        }

        public class CStoreService : DicomService, IDicomServiceProvider, IDicomCStoreProvider, IDicomCEchoProvider
        {
            public CStoreService(INetworkStream stream, Encoding fallbackEncoding, ILogger log, DicomServiceDependencies dependencies)
                : base(stream, fallbackEncoding, log, dependencies)
            {
            }

            public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
            {
                Logger.LogInformation($"[SCP] Incoming connection from {association.CallingAE}");
                foreach (var pc in association.PresentationContexts)
                {
                    if (pc.AbstractSyntax == DicomUID.Verification || pc.AbstractSyntax.StorageCategory != DicomStorageCategory.None)
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
                if (_staticRepository == null)
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

                var seriesFolder = _staticRepository.GetSeriesStoragePath(studyUid, seriesUid);

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
}