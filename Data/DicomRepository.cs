using Dapper;
using Microsoft.Data.Sqlite;
using RadiopaediaConnect.Models;
using System.Data;
using System.Text.Json;

namespace RadiopaediaConnect.Data
{
    public class DicomRepository
    {
        private readonly string _connectionString;

        public DicomRepository(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            _connectionString = connectionString;
        }

        private IDbConnection GetConnection() => new SqliteConnection(_connectionString);

        public string GetStorageRoot()
        {
            string dataBase = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
                ? @"C:\data"
                : "/data";

            var envPath = Environment.GetEnvironmentVariable("RCONNECT_DATA_PATH");
            if (!string.IsNullOrEmpty(envPath)) dataBase = envPath;

            return dataBase;
        }

        public string GetDicomRoot() => Path.Combine(GetStorageRoot(), "dicom");

        public string GetProcessingRoot() => Path.Combine(GetStorageRoot(), "processing");

        public string GetSeriesStoragePath(string studyUid, string seriesUid)
        {
            return Path.Combine(GetDicomRoot(), studyUid, seriesUid);
        }

        public async Task<SubmitCaseDto?> GetFullDraftCaseAsync(Guid caseId)
        {
            using var conn = GetConnection();
            var sqlCase = "SELECT * FROM DraftCases WHERE Id = @Id";
            var draft = await conn.QueryFirstOrDefaultAsync<DraftCase>(sqlCase, new { Id = caseId });

            if (draft == null) return null;

            var result = new SubmitCaseDto
            {
                Title = draft.Title,
                Presentation = draft.Presentation,
                System = draft.System,
                DiagnosticCertainty = draft.DiagnosticCertainty,
                Age = draft.Age,
                Sex = draft.Sex,
                CaseDiscussion = draft.CaseDiscussion,
                Studies = new List<SubmitCaseStudyDto>()
            };

            var studies = await conn.QueryAsync("SELECT * FROM DraftCaseStudies WHERE DraftCaseId = @Id", new { Id = caseId });

            foreach (var s in studies)
            {
                var studyDto = new SubmitCaseStudyDto
                {
                    StudyInstanceUid = s.StudyInstanceUid,
                    RemoteNodeName = s.RemoteNodeName,
                    Modality = s.Modality,
                    Findings = s.Findings,
                    Series = new List<SubmitCaseSeriesDto>()
                };

                var seriesRecords = await conn.QueryAsync("SELECT * FROM DraftCaseSeries WHERE DraftCaseStudyId = @StudyId", new { StudyId = (long)s.Id });

                foreach (var ser in seriesRecords)
                {
                    var redactions = string.IsNullOrEmpty((string)ser.RedactionsJson)
                        ? new List<RedactionZoneDto>()
                        : JsonSerializer.Deserialize<List<RedactionZoneDto>>((string)ser.RedactionsJson);

                    studyDto.Series.Add(new SubmitCaseSeriesDto
                    {
                        SeriesInstanceUid = ser.SeriesInstanceUid,
                        SeriesDescription = ser.SeriesDescription,
                        Modality = ser.Modality,
                        Start = (int)ser.StartFrame,
                        End = (int)ser.EndFrame,
                        Step = (int)ser.StepFrame,
                        Redactions = redactions ?? new List<RedactionZoneDto>()
                    });
                }
                result.Studies.Add(studyDto);
            }

            return result;
        }

        public async Task<Guid> SaveDraftCaseAsync(SubmitCaseDto payload, string username)
        {
            using var conn = GetConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();

            try
            {
                var caseId = Guid.NewGuid();

                var sqlCase = @"
                    INSERT INTO DraftCases (
                        Id, Username, Title, Presentation, System, Age, Sex, 
                        DiagnosticCertainty, CaseDiscussion, CreatedAt, Status
                    ) VALUES (
                        @Id, @Username, @Title, @Presentation, @System, @Age, @Sex, 
                        @DiagnosticCertainty, @CaseDiscussion, @CreatedAt, @Status
                    )";

                await conn.ExecuteAsync(sqlCase, new
                {
                    Id = caseId,
                    Username = username,
                    payload.Title,
                    payload.Presentation,
                    payload.System,
                    payload.Age,
                    payload.Sex,
                    payload.DiagnosticCertainty,
                    payload.CaseDiscussion,
                    CreatedAt = DateTime.UtcNow,
                    Status = "Queued"
                }, trans);

                foreach (var study in payload.Studies)
                {
                    var sqlStudy = @"
                        INSERT INTO DraftCaseStudies (
                            DraftCaseId, StudyInstanceUid, RemoteNodeName, Modality, Findings
                        ) VALUES (
                            @DraftCaseId, @StudyUid, @Node, @Mod, @Find
                        );
                        SELECT last_insert_rowid();";

                    var studyId = await conn.ExecuteScalarAsync<long>(sqlStudy, new
                    {
                        DraftCaseId = caseId,
                        StudyUid = study.StudyInstanceUid,
                        Node = study.RemoteNodeName,
                        Mod = study.Modality,
                        Find = study.Findings
                    }, trans);

                    foreach (var series in study.Series)
                    {
                        var sqlSeries = @"
                            INSERT INTO DraftCaseSeries (
                                DraftCaseStudyId, SeriesInstanceUid, SeriesDescription, Modality,
                                StartFrame, EndFrame, StepFrame, RedactionsJson
                            ) VALUES (
                                @StudyId, @SeriesUid, @Desc, @Mod,
                                @Start, @End, @Step, @Redactions
                            )";

                        await conn.ExecuteAsync(sqlSeries, new
                        {
                            StudyId = studyId,
                            SeriesUid = series.SeriesInstanceUid,
                            Desc = series.SeriesDescription,
                            Mod = series.Modality,
                            Start = series.Start,
                            End = series.End,
                            Step = series.Step,
                            Redactions = JsonSerializer.Serialize(series.Redactions)
                        }, trans);
                    }
                }

                var primaryStudy = payload.Studies.FirstOrDefault();
                var jobSql = @"
                    INSERT INTO DicomJobs (
                        Id, StudyInstanceUid, SeriesInstanceUid, RemoteAeTitle, 
                        Type, Status, Priority, CreatedAt, ResourceId
                    ) VALUES (
                        @Id, @StudyUid, NULL, @RemoteNode, 
                        @Type, @Status, @Priority, @CreatedAt, @ResourceId
                    )";

                await conn.ExecuteAsync(jobSql, new
                {
                    Id = Guid.NewGuid(),
                    StudyUid = primaryStudy?.StudyInstanceUid ?? "UNKNOWN",
                    RemoteNode = primaryStudy?.RemoteNodeName ?? "UNKNOWN",
                    Type = JobType.Upload,
                    Status = JobStatus.Pending,
                    Priority = 5,
                    CreatedAt = DateTime.UtcNow,
                    ResourceId = caseId
                }, trans);

                trans.Commit();
                return caseId;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        public async Task<DraftCase?> GetDraftCaseAsync(Guid caseId)
        {
            using var conn = GetConnection();
            return await conn.QueryFirstOrDefaultAsync<DraftCase>(
                "SELECT * FROM DraftCases WHERE Id = @Id", new { Id = caseId });
        }

        /// <summary>
        /// Get all cases for a specific user, ordered by creation date descending
        /// </summary>
        public async Task<IEnumerable<CaseListItemDto>> GetUserCasesAsync(string username)
        {
            using var conn = GetConnection();
            var sql = @"
                SELECT 
                    Id, Title, Presentation, Age, Sex, Status, 
                    CreatedAt, RadiopaediaCaseId, ErrorMessage
                FROM DraftCases 
                WHERE Username = @Username 
                ORDER BY CreatedAt DESC";

            return await conn.QueryAsync<CaseListItemDto>(sql, new { Username = username });
        }

        /// <summary>
        /// Update the case status after processing
        /// </summary>
        public async Task UpdateCaseStatusAsync(Guid caseId, string status, string? radiopaediaCaseId = null, string? errorMessage = null)
        {
            using var conn = GetConnection();
            var sql = @"
                UPDATE DraftCases 
                SET Status = @Status, 
                    RadiopaediaCaseId = COALESCE(@RadiopaediaCaseId, RadiopaediaCaseId),
                    ErrorMessage = @ErrorMessage
                WHERE Id = @Id";

            await conn.ExecuteAsync(sql, new
            {
                Id = caseId,
                Status = status,
                RadiopaediaCaseId = radiopaediaCaseId,
                ErrorMessage = errorMessage
            });
        }

        public async Task<Guid> EnqueueJobAsync(DicomJob job)
        {
            var sql = @"
                INSERT INTO DicomJobs (
                    Id, StudyInstanceUid, SeriesInstanceUid, RemoteAeTitle, Type, Status, 
                    Priority, CreatedAt, RetryCount, ErrorMessage
                ) VALUES (
                    @Id, @StudyInstanceUid, @SeriesInstanceUid, @RemoteAeTitle, @Type, @Status, 
                    @Priority, @CreatedAt, @RetryCount, @ErrorMessage
                )";

            using var conn = GetConnection();
            await conn.ExecuteAsync(sql, job);
            return job.Id;
        }

        public async Task<DicomJob?> ClaimNextJobAsync()
        {
            using var conn = GetConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();

            try
            {
                var sqlFind = @"
                    SELECT * FROM DicomJobs 
                    WHERE Status = @Pending 
                    ORDER BY Priority ASC, CreatedAt ASC 
                    LIMIT 1";

                var job = await conn.QueryFirstOrDefaultAsync<DicomJob>(sqlFind, new { Pending = JobStatus.Pending }, trans);

                if (job != null)
                {
                    var sqlUpdate = @"
                        UPDATE DicomJobs 
                        SET Status = @InProgress, StartedAt = @Now 
                        WHERE Id = @Id";

                    await conn.ExecuteAsync(sqlUpdate, new { Id = job.Id, InProgress = JobStatus.InProgress, Now = DateTime.UtcNow }, trans);
                    job.Status = JobStatus.InProgress;
                }

                trans.Commit();
                return job;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        public async Task<bool> IsJobPendingOrRunningAsync(string studyUid, string? seriesUid)
        {
            var sql = @"
                SELECT COUNT(1) FROM DicomJobs 
                WHERE StudyInstanceUid = @StudyUid 
                  AND (SeriesInstanceUid = @SeriesUid OR SeriesInstanceUid IS NULL)
                  AND Status IN (@Pending, @InProgress)";

            using var conn = GetConnection();
            var count = await conn.ExecuteScalarAsync<int>(sql, new
            {
                StudyUid = studyUid,
                SeriesUid = seriesUid,
                Pending = JobStatus.Pending,
                InProgress = JobStatus.InProgress
            });
            return count > 0;
        }

        public async Task<int> GetActiveJobCountAsync()
        {
            using var conn = GetConnection();
            return await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM DicomJobs WHERE Status = 1");
        }

        public async Task CompleteJobAsync(Guid jobId, bool success, string? error = null)
        {
            var sql = @"
                UPDATE DicomJobs 
                SET Status = @Status, ErrorMessage = @Error, CompletedAt = @Now
                WHERE Id = @Id";

            using var conn = GetConnection();
            await conn.ExecuteAsync(sql, new
            {
                Id = jobId,
                Status = success ? JobStatus.Completed : JobStatus.Failed,
                Error = error,
                Now = DateTime.UtcNow
            });
        }

        public async Task<DicomSeries?> GetSeriesAsync(string seriesUid)
        {
            using var conn = GetConnection();
            return await conn.QueryFirstOrDefaultAsync<DicomSeries>(
                "SELECT * FROM DicomSeries WHERE SeriesInstanceUid = @Uid", new { Uid = seriesUid });
        }

        public async Task MarkSeriesAsRetrievedAsync(DicomSeries series)
        {
            var sql = @"
                INSERT INTO DicomSeries (
                    SeriesInstanceUid, StudyInstanceUid, Modality, SeriesDescription, 
                    NumberOfInstances, IsRetrieved, StoragePath, LastAccessedAt, RetrievedAt
                ) VALUES (
                    @SeriesInstanceUid, @StudyInstanceUid, @Modality, @SeriesDescription,
                    @NumberOfInstances, 1, @StoragePath, @LastAccessedAt, @RetrievedAt
                )
                ON CONFLICT(SeriesInstanceUid) DO UPDATE SET
                    IsRetrieved = 1, StoragePath = excluded.StoragePath,
                    LastAccessedAt = excluded.LastAccessedAt, RetrievedAt = excluded.RetrievedAt";

            using var conn = GetConnection();
            await conn.ExecuteAsync(sql, series);
        }

        public async Task<IEnumerable<DicomSeries>> GetExpiredSeriesAsync(TimeSpan retentionPeriod)
        {
            var cutoff = DateTime.UtcNow.Subtract(retentionPeriod);
            using var conn = GetConnection();
            return await conn.QueryAsync<DicomSeries>(
                "SELECT * FROM DicomSeries WHERE IsRetrieved = 1 AND LastAccessedAt < @Cutoff",
                new { Cutoff = cutoff });
        }

        public async Task DeleteSeriesAsync(string seriesUid)
        {
            using var conn = GetConnection();
            await conn.ExecuteAsync("DELETE FROM DicomSeries WHERE SeriesInstanceUid = @Uid", new { Uid = seriesUid });
        }

        public async Task<DicomJob?> GetJobAsync(Guid jobId)
        {
            using var conn = GetConnection();
            return await conn.QueryFirstOrDefaultAsync<DicomJob>(
                "SELECT * FROM DicomJobs WHERE Id = @Id",
                new { Id = jobId });
        }

        public async Task DeleteOldJobsAsync(TimeSpan retentionPeriod)
        {
            var cutoff = DateTime.UtcNow.Subtract(retentionPeriod);
            var sql = @"
                DELETE FROM DicomJobs 
                WHERE Status IN (@Completed, @Failed, @Cancelled) 
                  AND (CompletedAt < @Cutoff OR (CompletedAt IS NULL AND CreatedAt < @Cutoff))";

            using var conn = GetConnection();
            await conn.ExecuteAsync(sql, new
            {
                Cutoff = cutoff,
                Completed = JobStatus.Completed,
                Failed = JobStatus.Failed,
                Cancelled = JobStatus.Cancelled
            });
        }
    }
}