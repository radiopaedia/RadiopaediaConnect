using FellowOakDicom;
using FellowOakDicom.Imaging;
using RadiopaediaConnect.Data;
using RadiopaediaConnect.Models;
using RadiopaediaConnect.Services.Dicom;
using DicomAnonymizer = RadiopaediaConnect.Services.Dicom.DicomAnonymizer;
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
        private readonly DicomAnonymizer _anonymizer;
        private readonly INotificationService _notificationService;
        private readonly CaseReconciliationService _reconciliation;
        private readonly ILogger<CaseProcessorService> _logger;

        public CaseProcessorService(
            DicomScu dicomScu,
            DicomRepository repository,
            RadiopaediaApiClient apiClient,
            DicomAnonymizer anonymizer,
            INotificationService notificationService,
            CaseReconciliationService reconciliation,
            ILogger<CaseProcessorService> logger)
        {
            _dicomScu = dicomScu;
            _repository = repository;
            _apiClient = apiClient;
            _anonymizer = anonymizer;
            _notificationService = notificationService;
            _reconciliation = reconciliation;
            _logger = logger;

            var processingRoot = _repository.GetProcessingRoot();
            if (!Directory.Exists(processingRoot)) Directory.CreateDirectory(processingRoot);
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // Public entry point
        // ──────────────────────────────────────────────────────────────────────────────────

        public async Task ProcessCaseAsync(Guid caseId)
        {
            _logger.LogInformation("[PIPELINE] Processing Case {CaseId}", caseId);
            await _repository.UpdateCaseStatusAsync(caseId, "Processing");

            string? rCaseId = null;

            // Every scratch folder for this run lives under the case, so two cases holding the
            // same series can never stage into the same place.
            string caseWorkRoot = Path.Combine(_repository.GetProcessingRoot(), caseId.ToString("N"));

            try
            {
                var fullCase = await _repository.GetFullDraftCaseAsync(caseId);
                if (fullCase == null) throw new Exception($"DraftCase {caseId} not found in database.");

                var draftEntity = await _repository.GetDraftCaseAsync(caseId);
                string username = draftEntity?.Username ?? "unknown_user";

                // Reuse the Radiopaedia case when it already exists (append / retry);
                // otherwise create it. Same pattern per study and per series below, so
                // re-running a case only uploads what hasn't been uploaded yet.
                if (!string.IsNullOrEmpty(draftEntity?.RadiopaediaCaseId))
                {
                    rCaseId = draftEntity.RadiopaediaCaseId;

                    // Radiopaedia only accepts new imaging while a case is a draft, and the
                    // case may have been published or deleted since this job was queued. The
                    // controller checks too, but this is the last point before we upload.
                    await _reconciliation.EnsureAcceptsNewImagingAsync(
                        username, rCaseId, caseId, forceRefresh: true);

                    _logger.LogInformation("[PIPELINE] Appending to existing Radiopaedia Case {RCaseId}", rCaseId);
                    await _repository.UpdateCaseStatusAsync(caseId, "Processing", rCaseId);
                }
                else
                {
                    rCaseId = await _apiClient.CreateCaseAsync(draftEntity, username);
                    _logger.LogInformation("[PIPELINE] Case Created! Radiopaedia ID: {RCaseId}", rCaseId);
                    await _repository.UpdateCaseStatusAsync(caseId, "Processing", rCaseId);

                    // The new case will not be in the cached listing yet.
                    _reconciliation.InvalidateCache(username);
                    await _repository.UpdateRemoteCaseStateAsync(
                        caseId, RadiopaediaCaseStatus.Draft, null, DateTime.UtcNow);
                }

                foreach (var study in fullCase.Studies)
                {
                    var pendingSeries = study.Series.Where(s => !s.IsUploaded).ToList();
                    if (pendingSeries.Count == 0)
                    {
                        _logger.LogInformation("[PIPELINE] Study {StudyUid}: all series already uploaded — skipping.",
                            study.StudyInstanceUid);
                        continue;
                    }

                    string rStudyId;
                    if (!string.IsNullOrEmpty(study.RadiopaediaStudyId))
                    {
                        rStudyId = study.RadiopaediaStudyId;
                        _logger.LogInformation("[PIPELINE] Study {StudyUid} already exists on Radiopaedia (ID: {RStudyId})",
                            study.StudyInstanceUid, rStudyId);
                    }
                    else
                    {
                        string rawModality = study.Series[0].Modality;
                        string radiopaediaModality = MapToRadiopaediaModality(rawModality);
                        _logger.LogInformation("[PIPELINE] Processing Study: {StudyUid} → {Modality}",
                            study.StudyInstanceUid, radiopaediaModality);

                        var studyPayload = new SubmitCaseStudyDto
                        {
                            Modality = radiopaediaModality,
                            Findings = study.Findings
                        };

                        rStudyId = await _apiClient.CreateStudyAsync(rCaseId, studyPayload, username);
                        _logger.LogInformation("[PIPELINE] Study Created! Radiopaedia ID: {RStudyId}", rStudyId);
                        await _repository.UpdateStudyRadiopaediaIdAsync(caseId, study.StudyInstanceUid, rStudyId);
                    }

                    foreach (var series in pendingSeries)
                    {
                        _logger.LogInformation("[PIPELINE] Processing Series {SeriesUid} (method: {Method})",
                            series.LogName, series.UploadMethod);

                        // Ensure DICOM files are present (C-MOVE if needed)
                        string dicomPath = await EnsureSeriesRetrievedAsync(
                            study.StudyInstanceUid, series.SeriesInstanceUid, study.RemoteNodeName);

                        // Now that the files are on disk we can see what the series actually
                        // holds, which the picker could only guess at before retrieval.
                        var parts = await ExpandSeriesForUploadAsync(dicomPath, series);

                        foreach (var part in parts)
                        {
                            if (part.RequestsDicom)
                            {
                                await ProcessDicomSeriesAsync(
                                    rCaseId, rStudyId, dicomPath, part, username, caseWorkRoot);
                            }
                            else
                            {
                                await ProcessPngSeriesAsync(
                                    rCaseId, rStudyId, dicomPath, part, username, caseWorkRoot);
                            }
                        }

                        // Non-exception completion (including empty-selection skips) counts
                        // as done — prevents duplicate uploads if the case is re-queued.
                        await _repository.MarkSeriesUploadedAsync(series.RowId);
                    }
                }

                await _apiClient.MarkUploadFinishedAsync(rCaseId, username);
                await _repository.UpdateCaseStatusAsync(caseId, "Completed", rCaseId);
                _logger.LogInformation("[PIPELINE] SUCCESS! Case {RCaseId} completed.", rCaseId);
            }
            catch (CaseNotEditableException ex)
            {
                // Expected outcome, not a fault: the case was published or removed on
                // Radiopaedia after this upload was queued. Fail the case with a message the
                // user can act on and skip the stack-trace notification.
                _logger.LogWarning("[PIPELINE] Case {CaseId} cannot accept imaging: {Message}", caseId, ex.Message);
                await _repository.UpdateCaseStatusAsync(caseId, "Failed", rCaseId, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PIPELINE] FAILED! Case {CaseId}: {Message}", caseId, ex.Message);
                await _repository.UpdateCaseStatusAsync(caseId, "Failed", rCaseId, ex.Message);
                await _notificationService.SendAsync(
                    $"Pipeline failed: Case {caseId}",
                    $"Case ID: {caseId}\nRadiopaedia Case ID: {rCaseId ?? "not created"}\nError: {ex.Message}\n\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                // Scratch files are worthless once the run ends, either way.
                try
                {
                    if (Directory.Exists(caseWorkRoot)) Directory.Delete(caseWorkRoot, true);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning("[PIPELINE] Could not clear {Path}: {Message}",
                        caseWorkRoot, cleanupEx.Message);
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // Shared: retrieve DICOM files from PACS if not already present
        // ──────────────────────────────────────────────────────────────────────────────────

        private async Task<string> EnsureSeriesRetrievedAsync(
            string studyUid, string seriesUid, string remoteNodeName)
        {
            string dicomPath = _repository.GetSeriesStoragePath(studyUid, seriesUid);
            var existing = await _repository.GetSeriesAsync(seriesUid);

            if (existing == null || !existing.IsRetrieved)
            {
                await _dicomScu.TriggerCMoveAsync(studyUid, seriesUid, remoteNodeName);
                int attempts = 0;
                while (!Directory.Exists(dicomPath) || !Directory.EnumerateFiles(dicomPath, "*.dcm").Any())
                {
                    if (attempts >= 60)
                        throw new Exception($"Timeout waiting for DICOM files at {dicomPath}");
                    if (attempts > 0 && attempts % 10 == 0)
                        _logger.LogInformation("[PIPELINE] Waiting for DICOM files at {Path}, attempt {A}/60",
                            dicomPath, attempts);
                    await Task.Delay(1000);
                    attempts++;
                }
            }

            return dicomPath;
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // Shared: split a series that turns out to hold several acquisitions
        // ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Expands one selected series into the series that should actually be uploaded.
        ///
        /// Some PACS store several independent acquisitions under a single SeriesInstanceUID
        /// (biplane angio is the usual case), and uploading those as one series stitches
        /// unrelated runs into a single stack. The picker offers the split up front, but only
        /// for series the user previewed first, so this is the backstop for everything else and
        /// it runs against the retrieved files so it cannot be fooled.
        ///
        /// For angiography the split is decided per SOP instance by the plane that produced it,
        /// which also catches biplane runs stored as single-frame spot images. Elsewhere a series
        /// holding one multiframe instance is left alone: that single file is a complete
        /// acquisition and uploads as-is.
        /// </summary>
        private async Task<List<SubmitCaseSeriesDto>> ExpandSeriesForUploadAsync(
            string dicomPath, SubmitCaseSeriesDto series)
        {
            var asIs = new List<SubmitCaseSeriesDto> { series };

            // Already split in the picker — the user's grouping wins
            if (series.IsSubSeries) return asIs;

            var fileInfos = await DicomFrameExpander.ScanFilesAsync(
                Directory.GetFiles(dicomPath, "*.dcm"));

            var subs = DicomFrameExpander.BuildSubSeries(fileInfos);
            if (subs.Count < 2) return asIs;

            _logger.LogWarning(
                "[PIPELINE] Series {SeriesUid} holds {Count} independent acquisitions under one " +
                "SeriesInstanceUID, uploading them as separate series instead of one " +
                "stitched stack.", series.SeriesInstanceUid, subs.Count);

            var parts = new List<SubmitCaseSeriesDto>();

            foreach (var sub in subs)
            {
                // The user's frame window counts positions across the whole series, so it has
                // to be renumbered for each part.
                var window = DicomFrameExpander.MapWindowToSubSeries(
                    fileInfos, sub, series.Start, series.End, series.Step);

                if (window == null)
                {
                    _logger.LogInformation(
                        "[PIPELINE]   part \"{Label}\": outside the selected frame range — skipping.",
                        sub.Label);
                    continue;
                }

                parts.Add(new SubmitCaseSeriesDto
                {
                    SeriesInstanceUid = series.SeriesInstanceUid,
                    SeriesDescription = series.SeriesDescription,
                    Modality = series.Modality,
                    SubSeriesKey = sub.Key,
                    SubSeriesLabel = sub.Label,
                    SopInstanceUids = sub.SopInstanceUids,
                    Start = window.Value.Start,
                    End = window.Value.End,
                    Step = window.Value.Step,
                    Redactions = series.Redactions,
                    UploadMethod = series.UploadMethod,
                    RowId = series.RowId,
                    IsUploaded = series.IsUploaded
                });

                _logger.LogInformation(
                    "[PIPELINE]   part \"{Label}\": {Frames} frame(s), window {Start}-{End} step {Step}",
                    sub.Label, sub.FrameCount, window.Value.Start, window.Value.End, window.Value.Step);
            }

            // Every part fell outside the selection — fall back rather than upload nothing
            return parts.Count > 0 ? parts : asIs;
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // Path A: Native DICOM upload via S3
        // ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Uploads a series as native DICOM. Multiframe instances are broken into one instance
        /// per frame first: Radiopaedia builds a stack of one image per uploaded file and does
        /// not expand multiframe files, so a cine run sent whole shows only its first frame.
        /// A run whose frames cannot be lifted out falls back to the PNG path.
        /// </summary>
        private async Task ProcessDicomSeriesAsync(
            string rCaseId, string rStudyId,
            string dicomPath, SubmitCaseSeriesDto series, string username, string caseWorkRoot)
        {
            // Narrow to this series' instances first: when the user split a source series in the
            // picker, only the selected part is uploaded and the frame window below applies to
            // that part alone.
            var fileInfos = DicomFrameExpander.FilterToSubSeries(
                await DicomFrameExpander.ScanFilesAsync(Directory.GetFiles(dicomPath, "*.dcm")),
                series.SopInstanceUids);

            var selectedFrames = DicomFrameExpander.ApplyWindow(
                DicomFrameExpander.ExpandFrames(fileInfos), series.Start, series.End, series.Step);

            if (selectedFrames.Count == 0)
            {
                _logger.LogWarning("[PIPELINE] Series {SeriesUid}: no frames selected after culling, skipping.",
                    series.LogName);
                return;
            }

            // Only the instances the selection actually reaches matter here: a window that
            // lands entirely on single-frame images needs no splitting even if the part also
            // holds a cine run.
            var selectedPaths = selectedFrames
                .Select(f => f.FilePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool hasMultiframe = fileInfos.Any(f => f.NumberOfFrames > 1 && selectedPaths.Contains(f.FilePath));

            // ── Anonymise: SHA-512 UIDs + allowlist copy, then upload ────────────────────
            // UIDs are replaced using Radiopaedia's own deterministic hashing algorithm
            // (SHA-512 → first two 32-bit signed words → "1.2.826.0.1.3680043.10.341.512.W0.W1")
            // so their server-side validator accepts the files.
            string anonDir = Path.Combine(caseWorkRoot, series.StorageKey, "anon");
            if (Directory.Exists(anonDir)) Directory.Delete(anonDir, true);

            // Stage the selected frames into a temp folder so AnonymizeSeriesAsync can glob *.dcm
            var uidMap = new DicomUidMap();
            var tempInputDir = Path.Combine(caseWorkRoot, series.StorageKey, "selected");
            if (Directory.Exists(tempInputDir)) Directory.Delete(tempInputDir, true);
            Directory.CreateDirectory(tempInputDir);

            // How many files the upload is expected to carry. Checked against the anonymiser's
            // output below so a series can never go up with some of its frames missing.
            int stagedCount;

            if (hasMultiframe)
            {
                // Radiopaedia counts one uploaded file as one image in the stack and never
                // expands a multiframe instance, so a cine run has to be broken into one
                // instance per frame or only the first frame of each file is ever seen.
                try
                {
                    stagedCount = (await DicomFrameSplitter.StageFramesAsync(selectedFrames, tempInputDir)).Count;
                    _logger.LogInformation(
                        "[PIPELINE] Series {SeriesUid}: split {Frames} frame(s) out of {Files} source instance(s)",
                        series.LogName, selectedFrames.Count, selectedPaths.Count);
                }
                catch (FrameSplitNotSupportedException ex)
                {
                    // Expected for the odd acquisition, not a fault. Nothing is lost by
                    // rendering instead, so the run still goes up in full.
                    _logger.LogWarning(
                        "[PIPELINE] Series {SeriesUid}: {Reason}. Falling back from DICOM to PNG upload.",
                        series.LogName, ex.Message);
                    if (Directory.Exists(tempInputDir)) Directory.Delete(tempInputDir, true);
                    await ProcessPngSeriesAsync(rCaseId, rStudyId, dicomPath, series, username, caseWorkRoot);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[PIPELINE] Series {SeriesUid}: splitting the multiframe instances failed. " +
                        "Falling back from DICOM to PNG upload.", series.LogName);
                    if (Directory.Exists(tempInputDir)) Directory.Delete(tempInputDir, true);
                    await ProcessPngSeriesAsync(rCaseId, rStudyId, dicomPath, series, username, caseWorkRoot);
                    return;
                }
            }
            else
            {
                // Single-frame instances: one .dcm is one frame, so the selected frames name
                // the files to send and the originals go up untouched.
                foreach (var src in selectedPaths)
                    File.Copy(src, Path.Combine(tempInputDir, Path.GetFileName(src)), overwrite: true);

                stagedCount = selectedPaths.Count;
            }

            // SeriesUidSeed is non-null only for a split series — it keeps the parts from being
            // merged back into one series by Radiopaedia's UID-based grouping.
            var anonPaths = await _anonymizer.AnonymizeSeriesAsync(
                tempInputDir, anonDir, uidMap, series.SeriesUidSeed);
            if (Directory.Exists(tempInputDir)) Directory.Delete(tempInputDir, true);

            try
            {
                // The anonymiser skips files it cannot read and carries on, so a short result
                // means frames went missing between staging and anonymisation. Uploading anyway
                // would put a partial series on Radiopaedia and report it as a success.
                if (anonPaths.Count != stagedCount)
                    throw new Exception(
                        $"Anonymisation produced {anonPaths.Count} file(s) for Series {series.LogName} " +
                        $"but {stagedCount} were staged. Refusing to upload a partial series; check the " +
                        $"[Anon] Skipped warnings above.");

                _logger.LogInformation("[PIPELINE] Uploading {Count} anonymised DICOM file(s) for Series {SeriesUid}",
                    anonPaths.Count, series.LogName);
                await _apiClient.UploadDicomSeriesAsync(rCaseId, rStudyId, anonPaths, username);
                _logger.LogInformation("[PIPELINE] DICOM upload complete for Series {SeriesUid}", series.LogName);
            }
            finally
            {
                if (Directory.Exists(anonDir)) Directory.Delete(anonDir, true);
            }
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // Path B: PNG render → ZIP → legacy endpoint
        // ──────────────────────────────────────────────────────────────────────────────────

        private async Task ProcessPngSeriesAsync(
            string rCaseId, string rStudyId,
            string dicomPath, SubmitCaseSeriesDto series, string username, string caseWorkRoot)
        {
            string outputPath = Path.Combine(caseWorkRoot, series.StorageKey, "png");
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
            Directory.CreateDirectory(outputPath);

            // Build expanded frame list, narrowed to this part when the series was split
            var fileInfos = DicomFrameExpander.FilterToSubSeries(
                await DicomFrameExpander.ScanFilesAsync(Directory.GetFiles(dicomPath, "*.dcm")),
                series.SopInstanceUids);
            var expandedFrames = DicomFrameExpander.ExpandFrames(fileInfos);

            _logger.LogInformation("[PIPELINE] Series {SeriesUid}: {Files} files, {Frames} total frames",
                series.LogName, fileInfos.Count, expandedFrames.Count);

            // Apply Start/End/Step
            var targetFrames = DicomFrameExpander.ApplyWindow(
                expandedFrames, series.Start, series.End, series.Step);

            if (targetFrames.Count == 0)
            {
                _logger.LogWarning("[PIPELINE] Series {SeriesUid}: 0 frames after filtering — skipping.",
                    series.LogName);
                return;
            }

            _logger.LogInformation("[PIPELINE] After selection (Start={S}, End={E}, Step={St}): {Count} frames",
                series.Start, series.End, series.Step, targetFrames.Count);

            // Render each frame to PNG
            for (int i = 0; i < targetFrames.Count; i++)
            {
                var frame = targetFrames[i];
                string pngName = $"frame_{i:D4}.png";
                await ProcessDicomFrameWithImageSharpAsync(
                    frame.FilePath, frame.FrameIndex,
                    Path.Combine(outputPath, pngName),
                    series.Redactions);
            }

            _logger.LogInformation("[PIPELINE] Series {SeriesUid}: rendered {Count} PNG frame(s)",
                series.LogName, targetFrames.Count);

            if (Directory.GetFiles(outputPath, "*.png").Length == 0)
            {
                _logger.LogWarning("[PIPELINE] No PNGs generated for {SeriesUid} — skipping.", series.LogName);
                if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
                return;
            }

            string zipPath = Path.Combine(caseWorkRoot, $"{series.StorageKey}.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);

            _logger.LogInformation("[PIPELINE] Creating ZIP for Series {SeriesUid}", series.LogName);
            ZipFile.CreateFromDirectory(outputPath, zipPath, CompressionLevel.Fastest, false);

            _logger.LogInformation("[PIPELINE] Uploading ZIP for Series {SeriesUid} → Study {RStudyId}",
                series.LogName, rStudyId);
            await _apiClient.UploadStudyZipAsync(rCaseId, rStudyId, zipPath, username);

            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // PNG rendering (unchanged)
        // ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Kept for the legacy PNG path and for the preview pipeline.
        /// Renders a single DICOM frame to a PNG file, applying optional redaction zones.
        /// </summary>
        public async Task<string> PrepareSeriesAsync(
            string studyUid, SubmitCaseSeriesDto seriesDto, string remoteNodeName)
        {
            _logger.LogInformation("[Processor] Preparing Series: {SeriesUid}", seriesDto.SeriesInstanceUid);

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
                    if (attempts >= 60)
                        throw new Exception($"Timeout waiting for DICOM files at {dicomPath}");
                    if (attempts > 0 && attempts % 10 == 0)
                        _logger.LogInformation("[PIPELINE] Waiting for DICOM files at {Path}, attempt {A}/60",
                            dicomPath, attempts);
                    await Task.Delay(1000);
                    attempts++;
                }
            }

            var dicomFiles = Directory.GetFiles(dicomPath, "*.dcm");
            var fileInfos = await DicomFrameExpander.ScanFilesAsync(dicomFiles);
            var expandedFrames = DicomFrameExpander.ExpandFrames(fileInfos);

            _logger.LogInformation("[Processor] Series {SeriesUid}: {Files} files, {Frames} total frames",
                seriesDto.SeriesInstanceUid, dicomFiles.Length, expandedFrames.Count);

            var targetFrames = DicomFrameExpander.ApplyWindow(
                expandedFrames, seriesDto.Start, seriesDto.End, seriesDto.Step);

            for (int i = 0; i < targetFrames.Count; i++)
            {
                var frame = targetFrames[i];
                string pngName = $"frame_{i:D4}.png";
                await ProcessDicomFrameWithImageSharpAsync(
                    frame.FilePath, frame.FrameIndex,
                    Path.Combine(outputPath, pngName),
                    seriesDto.Redactions);
            }

            _logger.LogInformation("[PIPELINE] Series {SeriesUid}: rendered {Count} PNG frame(s)",
                seriesDto.SeriesInstanceUid, targetFrames.Count);

            return outputPath;
        }

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

        // ──────────────────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────────────────

        private string MapToRadiopaediaModality(string dicomModality)
        {
            if (string.IsNullOrWhiteSpace(dicomModality)) return "X-ray";

            var parts = dicomModality.Split(new[] { ',', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var primary = parts.Length > 0 ? parts[0].Trim().ToUpper() : "UNKNOWN";

            return primary switch
            {
                "CT"   => "CT",
                "MR"   => "MRI",
                "US"   => "Ultrasound",
                "IVUS" => "Ultrasound",
                "NM"   => "Nuclear medicine",
                "PT"   => "Nuclear medicine",
                "ST"   => "Nuclear medicine",
                "CR"   => "X-ray",
                "DX"   => "X-ray",
                "RG"   => "X-ray",
                "IO"   => "X-ray",
                "PX"   => "X-ray",
                "MG"   => "Mammography",
                "RF"   => "Fluoroscopy",
                "XA"   => "DSA (angiography)",
                "SC"   => "X-ray",
                "OT"   => "X-ray",
                _      => "X-ray"
            };
        }
    }
}
