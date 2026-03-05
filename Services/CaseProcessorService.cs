using FellowOakDicom;
using FellowOakDicom.Imaging;
using RadiopaediaConnect.Data;
using RadiopaediaConnect.Models;
using RadiopaediaConnect.Services.Dicom;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO.Compression;

namespace RadiopaediaConnect.Services
{
    public class CaseProcessorService
    {
        private readonly DicomScu _dicomScu;
        private readonly DicomRepository _repository;
        private readonly RadiopaediaApiClient _apiClient;
        private readonly ILogger<CaseProcessorService> _logger;

        public CaseProcessorService(
            DicomScu dicomScu,
            DicomRepository repository,
            RadiopaediaApiClient apiClient,
            ILogger<CaseProcessorService> logger)
        {
            _dicomScu = dicomScu;
            _repository = repository;
            _apiClient = apiClient;
            _logger = logger;

            var processingRoot = _repository.GetProcessingRoot();
            if (!Directory.Exists(processingRoot)) Directory.CreateDirectory(processingRoot);
        }

        public async Task<string> PrepareSeriesAsync(string studyUid, SubmitCaseSeriesDto seriesDto, string remoteNodeName)
        {
            _logger.LogInformation($"[Processor] Preparing Series: {seriesDto.SeriesInstanceUid}");

            string dicomPath = _repository.GetSeriesStoragePath(studyUid, seriesDto.SeriesInstanceUid);
            string outputPath = Path.Combine(_repository.GetProcessingRoot(), seriesDto.SeriesInstanceUid);

            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
            Directory.CreateDirectory(outputPath);

            var existing = await _repository.GetSeriesAsync(seriesDto.SeriesInstanceUid);
            if (existing == null || !existing.IsRetrieved)
            {
                await _dicomScu.TriggerCMoveAsync(studyUid, seriesDto.SeriesInstanceUid, remoteNodeName);
                int attempts = 0;
                while (!Directory.Exists(dicomPath) || !Directory.EnumerateFiles(dicomPath, "*.dcm").Any())
                {
                    if (attempts >= 60) throw new Exception($"Timeout waiting for DICOM files at {dicomPath}");
                    await Task.Delay(1000);
                    attempts++;
                }
            }

            // Build expanded frame list using the shared helper (same algorithm as metadata endpoint)
            var dicomFiles = Directory.GetFiles(dicomPath, "*.dcm");
            var fileInfos = await DicomFrameExpander.ScanFilesAsync(dicomFiles);
            var expandedFrames = DicomFrameExpander.ExpandFrames(fileInfos);

            _logger.LogInformation($"[Processor] Series {seriesDto.SeriesInstanceUid}: {dicomFiles.Length} files, {expandedFrames.Count} total frames");

            // Apply Start/End/Step on the expanded frame list (1-based indices)
            int skipCount = Math.Max(0, seriesDto.Start - 1);
            int takeCount = Math.Max(0, seriesDto.End - seriesDto.Start + 1);

            var targetFrames = expandedFrames
                .Skip(skipCount)
                .Take(takeCount)
                .ToList();

            if (seriesDto.Step > 1)
                targetFrames = targetFrames.Where((x, i) => i % seriesDto.Step == 0).ToList();

            _logger.LogInformation($"[Processor] After selection (Start={seriesDto.Start}, End={seriesDto.End}, Step={seriesDto.Step}): {targetFrames.Count} frames to process");

            // Render each frame to PNG
            for (int i = 0; i < targetFrames.Count; i++)
            {
                var frame = targetFrames[i];
                string pngName = $"frame_{i:D4}.png";
                string fullOutputPath = Path.Combine(outputPath, pngName);
                await ProcessDicomFrameWithImageSharpAsync(frame.FilePath, frame.FrameIndex, fullOutputPath, seriesDto.Redactions);
            }

            return outputPath;
        }

        /// <summary>
        /// Renders a specific frame from a DICOM file to PNG, applying redaction zones.
        /// Uses fo-dicom's DicomImage.RenderImage(frameIndex) which handles all transfer syntaxes
        /// including JPEG Lossless (via the registered ImageSharp manager + codecs).
        /// </summary>
        private async Task ProcessDicomFrameWithImageSharpAsync(
            string dicomFile, int frameIndex, string outputPath, List<RedactionZoneDto> redactions)
        {
            var file = await DicomFile.OpenAsync(dicomFile);
            var dicomImage = new DicomImage(file.Dataset);
            using (var image = dicomImage.RenderImage(frameIndex).AsSharpImage())
            {
                image.ProcessPixelRows(accessor =>
                {
                    if (redactions != null && redactions.Count > 0)
                    {
                        var black = Color.Black;
                        int w = image.Width;
                        int h = image.Height;
                        foreach (var zone in redactions)
                        {
                            int rx = (int)Math.Round(zone.X);
                            int ry = (int)Math.Round(zone.Y);
                            int rw = (int)Math.Round(zone.W);
                            int rh = (int)Math.Round(zone.H);
                            int startX = Math.Clamp(rx, 0, w - 1);
                            int startY = Math.Clamp(ry, 0, h - 1);
                            int endX = Math.Clamp(rx + rw, 0, w);
                            int endY = Math.Clamp(ry + rh, 0, h);
                            if (endX > startX && endY > startY)
                            {
                                for (int y = startY; y < endY; y++)
                                    accessor.GetRowSpan(y).Slice(startX, endX - startX).Fill(black);
                            }
                        }
                    }
                });
                await image.SaveAsPngAsync(outputPath);
            }
        }

        public async Task ProcessCaseAsync(Guid caseId)
        {
            _logger.LogInformation($"[PIPELINE] Processing Case {caseId}");

            // Update status to Processing
            await _repository.UpdateCaseStatusAsync(caseId, "Processing");

            string? rCaseId = null;

            try
            {
                var fullCase = await _repository.GetFullDraftCaseAsync(caseId);
                if (fullCase == null) throw new Exception($"DraftCase {caseId} not found in database.");

                var draftEntity = await _repository.GetDraftCaseAsync(caseId);
                string username = draftEntity?.Username ?? "unknown_user";

                rCaseId = await _apiClient.CreateCaseAsync(draftEntity, username);
                _logger.LogInformation($"[PIPELINE] Case Created! Radiopaedia ID: {rCaseId}");

                // Save Radiopaedia case ID immediately after creation
                await _repository.UpdateCaseStatusAsync(caseId, "Processing", rCaseId);

                foreach (var study in fullCase.Studies)
                {
                    string rawModality = study.Series[0].Modality;
                    string radiopaediaModality = MapToRadiopaediaModality(rawModality);
                    _logger.LogInformation($"[PIPELINE] Processing Study: {study.StudyInstanceUid} -> {radiopaediaModality}");

                    var studyPayload = new SubmitCaseStudyDto
                    {
                        Modality = radiopaediaModality,
                        Findings = study.Findings
                    };

                    string rStudyId = await _apiClient.CreateStudyAsync(rCaseId, studyPayload, username);
                    _logger.LogInformation($"[PIPELINE] Study Created! Radiopaedia ID: {rStudyId}");

                    foreach (var series in study.Series)
                    {
                        _logger.LogInformation($"[PIPELINE] Processing Series: {series.SeriesInstanceUid}");

                        string processedFolder = await PrepareSeriesAsync(study.StudyInstanceUid, series, study.RemoteNodeName);

                        if (Directory.GetFiles(processedFolder, "*.png").Length > 0)
                        {
                            string zipPath = Path.Combine(_repository.GetProcessingRoot(), $"{series.SeriesInstanceUid}.zip");
                            if (File.Exists(zipPath)) File.Delete(zipPath);

                            ZipFile.CreateFromDirectory(processedFolder, zipPath, CompressionLevel.Fastest, false);

                            _logger.LogInformation($"[PIPELINE] Uploading Series {series.SeriesInstanceUid} to Study {rStudyId}");
                            await _apiClient.UploadStudyZipAsync(rCaseId, rStudyId, zipPath, username);

                            if (File.Exists(zipPath)) File.Delete(zipPath);
                        }
                        else
                        {
                            _logger.LogWarning($"[PIPELINE] No PNGs generated for {series.SeriesInstanceUid}");
                        }

                        if (Directory.Exists(processedFolder)) Directory.Delete(processedFolder, true);
                    }
                }

                await _apiClient.MarkUploadFinishedAsync(rCaseId, username);

                // Update status to Completed with Radiopaedia case ID
                await _repository.UpdateCaseStatusAsync(caseId, "Completed", rCaseId);

                _logger.LogInformation($"[PIPELINE] SUCCESS! Case {rCaseId} completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[PIPELINE] FAILED! Case {caseId} error: {ex.Message}");

                // Update status to Failed with error message, preserve any existing Radiopaedia ID
                await _repository.UpdateCaseStatusAsync(caseId, "Failed", rCaseId, ex.Message);

                throw;
            }
        }

        private string MapToRadiopaediaModality(string dicomModality)
        {
            if (string.IsNullOrWhiteSpace(dicomModality)) return "X-ray";

            var parts = dicomModality.Split(new[] { ',', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var primary = parts.Length > 0 ? parts[0].Trim().ToUpper() : "UNKNOWN";

            return primary switch
            {
                "CT" => "CT",
                "MR" => "MRI",
                "US" => "Ultrasound",
                "IVUS" => "Ultrasound",
                "NM" => "Nuclear medicine",
                "PT" => "Nuclear medicine",
                "ST" => "Nuclear medicine",
                "CR" => "X-ray",
                "DX" => "X-ray",
                "RG" => "X-ray",
                "IO" => "X-ray",
                "PX" => "X-ray",
                "MG" => "Mammography",
                "RF" => "Fluoroscopy",
                "XA" => "DSA (angiography)",
                "SC" => "X-ray",
                "OT" => "X-ray",
                _ => "X-ray"
            };
        }
    }
}
