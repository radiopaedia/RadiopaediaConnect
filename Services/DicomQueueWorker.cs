using Microsoft.Extensions.Options;
using RadiopaediaConnect.Data;
using RadiopaediaConnect.Models;
using RadiopaediaConnect.Services.Dicom;
using System.Runtime.InteropServices;
using FellowOakDicom;

namespace RadiopaediaConnect.Services
{
    public class DicomQueueWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DicomSettings _settings;
        private readonly ILogger<DicomQueueWorker> _logger;

        private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(2);
        private readonly TimeSpan _purgeInterval = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _retentionPeriod = TimeSpan.FromMinutes(30);

        private DateTime _lastPurgeTime = DateTime.MinValue;

        public DicomQueueWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<DicomSettings> settings,
            ILogger<DicomQueueWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _settings = settings.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[QueueWorker] Service Started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (DateTime.UtcNow - _lastPurgeTime > _purgeInterval)
                    {
                        await RunPurgeCycleAsync();
                        _lastPurgeTime = DateTime.UtcNow;
                    }

                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var repository = scope.ServiceProvider.GetRequiredService<DicomRepository>();

                        int activeJobs = await repository.GetActiveJobCountAsync();
                        if (activeJobs >= _settings.MaxConcurrentDownloads)
                        {
                            await Task.Delay(_pollInterval, stoppingToken);
                            continue;
                        }

                        var job = await repository.ClaimNextJobAsync();
                        if (job == null)
                        {
                            await Task.Delay(_pollInterval, stoppingToken);
                            continue;
                        }

                        _ = Task.Run(() => ProcessJobWrapperAsync(job), stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[QueueWorker] Loop Error");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }

        private async Task ProcessJobWrapperAsync(DicomJob job)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var repository = scope.ServiceProvider.GetRequiredService<DicomRepository>();

                    if (job.Type == JobType.Upload)
                    {
                        var processor = scope.ServiceProvider.GetRequiredService<CaseProcessorService>();
                        await ProcessUploadJobAsync(repository, processor, job);
                    }
                    else
                    {
                        var dicomScu = scope.ServiceProvider.GetRequiredService<DicomScu>();
                        await ProcessRetrievalJobAsync(repository, dicomScu, job);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[QueueWorker] Fatal error in job wrapper for {job.Id}");
            }
        }

        private async Task ProcessUploadJobAsync(DicomRepository repository, CaseProcessorService processor, DicomJob job)
        {
            _logger.LogInformation($"[Upload Worker] Processing Upload Job {job.Id} for Case {job.ResourceId}");

            if (!Guid.TryParse(job.ResourceId, out var caseId))
            {
                await repository.CompleteJobAsync(job.Id, false, $"Invalid ResourceId (Case GUID): {job.ResourceId}");
                return;
            }

            try
            {
                await processor.ProcessCaseAsync(caseId);

                await repository.CompleteJobAsync(job.Id, true, "Upload Successful");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[Upload Worker] Failed to process case {caseId}");
                await repository.CompleteJobAsync(job.Id, false, ex.Message);
            }
        }

        private async Task ProcessRetrievalJobAsync(DicomRepository repository, DicomScu dicomScu, DicomJob job)
        {
            try
            {
                _logger.LogInformation($"[QueueWorker] Processing Retrieval {job.Id} (Study: {job.StudyInstanceUid})");

                var remoteNode = _settings.RemoteNodes
                    .FirstOrDefault(n => n.AeTitle.Equals(job.RemoteAeTitle, StringComparison.OrdinalIgnoreCase));

                if (remoteNode == null) throw new Exception($"Configured Remote Node '{job.RemoteAeTitle}' not found.");

                string storagePath = repository.GetSeriesStoragePath(job.StudyInstanceUid, job.SeriesInstanceUid ?? "");

                if (!string.IsNullOrEmpty(job.SeriesInstanceUid))
                {
                    var existingSeries = await repository.GetSeriesAsync(job.SeriesInstanceUid);
                    if (existingSeries != null && existingSeries.IsRetrieved &&
                        Directory.Exists(existingSeries.StoragePath) &&
                        Directory.EnumerateFiles(existingSeries.StoragePath, "*.dcm").Any())
                    {
                        await repository.CompleteJobAsync(job.Id, true, "Cached");
                        return;
                    }
                }

                bool success = await dicomScu.TriggerCMoveAsync(
                    job.StudyInstanceUid,
                    job.SeriesInstanceUid ?? "",
                    remoteNode.Name
                );

                if (success)
                {
                    if (Directory.Exists(storagePath) && Directory.EnumerateFiles(storagePath, "*.dcm").Any())
                    {
                        var seriesMetadata = await ExtractMetadataFromDiskAsync(job.SeriesInstanceUid, job.StudyInstanceUid, storagePath);
                        await repository.MarkSeriesAsRetrievedAsync(seriesMetadata);
                        await repository.CompleteJobAsync(job.Id, true);
                    }
                    else
                    {
                        await repository.CompleteJobAsync(job.Id, false, $"C-MOVE Success but no files found at {storagePath}");
                    }
                }
                else
                {
                    await repository.CompleteJobAsync(job.Id, false, "C-MOVE Rejected");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[QueueWorker] Job {job.Id} Failed");
                await repository.CompleteJobAsync(job.Id, false, ex.Message);
            }
        }

        private async Task<DicomSeries> ExtractMetadataFromDiskAsync(string seriesUid, string studyUid, string path)
        {
            var files = Directory.GetFiles(path, "*.dcm");
            if (files.Length == 0) throw new FileNotFoundException("No DICOM files found.");

            var file = await DicomFile.OpenAsync(files[0], FileReadOption.SkipLargeTags);

            return new DicomSeries
            {
                SeriesInstanceUid = seriesUid,
                StudyInstanceUid = studyUid,
                StoragePath = path,
                IsRetrieved = true,
                LastAccessedAt = DateTime.UtcNow,
                RetrievedAt = DateTime.UtcNow,
                Modality = file.Dataset.GetValueOrDefault(DicomTag.Modality, 0, "UNK"),
                SeriesDescription = file.Dataset.GetValueOrDefault(DicomTag.SeriesDescription, 0, "No Description"),
                NumberOfInstances = files.Length
            };
        }

        private async Task RunPurgeCycleAsync()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<DicomRepository>();
                var cutoff = DateTime.UtcNow.Subtract(_retentionPeriod);

                try
                {
                    var expiredSeries = await repository.GetExpiredSeriesAsync(_retentionPeriod);
                    foreach (var series in expiredSeries)
                    {
                        if (Directory.Exists(series.StoragePath)) Directory.Delete(series.StoragePath, true);
                        await repository.DeleteSeriesAsync(series.SeriesInstanceUid);
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, "DB Series Purge Error"); }

                try
                {
                    await repository.DeleteOldJobsAsync(_retentionPeriod);
                }
                catch (Exception ex) { _logger.LogError(ex, "DB Jobs Purge Error"); }

                try
                {
                    var dicomRoot = repository.GetDicomRoot();
                    if (Directory.Exists(dicomRoot))
                    {
                        var safetyCutoff = cutoff.Subtract(TimeSpan.FromMinutes(10));
                        foreach (var studyDir in Directory.GetDirectories(dicomRoot))
                        {
                            foreach (var seriesDir in Directory.GetDirectories(studyDir))
                            {
                                var dirInfo = new DirectoryInfo(seriesDir);
                                if (dirInfo.LastWriteTimeUtc < safetyCutoff)
                                {
                                    var seriesUid = dirInfo.Name;
                                    bool isJobActive = await repository.IsJobPendingOrRunningAsync(seriesUid, null);
                                    if (!isJobActive)
                                    {
                                        try
                                        {
                                            _logger.LogWarning($"[Purge] Sweeping orphan series: {seriesUid}");
                                            dirInfo.Delete(true);
                                            await repository.DeleteSeriesAsync(seriesUid);
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, "Disk Sweep (DICOM) Error"); }

                try
                {
                    var procRoot = repository.GetProcessingRoot();
                    if (Directory.Exists(procRoot))
                    {
                        foreach (var dir in Directory.GetDirectories(procRoot))
                        {
                            var info = new DirectoryInfo(dir);
                            if (info.CreationTimeUtc < cutoff && info.LastWriteTimeUtc < cutoff)
                            {
                                try
                                {
                                    info.Delete(true);
                                    _logger.LogInformation($"[Purge] Deleted stale processing folder: {info.Name}");
                                }
                                catch { }
                            }
                        }

                        foreach (var file in Directory.GetFiles(procRoot, "*.zip"))
                        {
                            var info = new FileInfo(file);
                            if (info.CreationTimeUtc < cutoff && info.LastWriteTimeUtc < cutoff)
                            {
                                try
                                {
                                    info.Delete();
                                    _logger.LogInformation($"[Purge] Deleted stale zip: {info.Name}");
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, "Disk Sweep (Processing) Error"); }
            }
        }
    }
}