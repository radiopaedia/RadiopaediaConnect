using Dapper;
using Microsoft.Data.Sqlite;
using RadiopaediaConnect.Models;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;

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

        private static readonly Regex _dicomUidRegex = new(@"^[0-9.]{1,64}$", RegexOptions.Compiled);

        public static string SanitizeDicomUid(string uid)
        {
            if (!_dicomUidRegex.IsMatch(uid))
                throw new ArgumentException($"Invalid DICOM UID: '{uid}'");
            return uid;
        }

        public string GetSeriesStoragePath(string studyUid, string seriesUid)
        {
            return Path.Combine(GetDicomRoot(), SanitizeDicomUid(studyUid), SanitizeDicomUid(seriesUid));
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
                    RadiopaediaStudyId = (string?)s.RadiopaediaStudyId,
                    Series = new List<SubmitCaseSeriesDto>()
                };

                var seriesRecords = await conn.QueryAsync("SELECT * FROM DraftCaseSeries WHERE DraftCaseStudyId = @StudyId", new { StudyId = (long)s.Id });

                foreach (var ser in seriesRecords)
                {
                    var redactions = string.IsNullOrEmpty((string)ser.RedactionsJson)
                        ? new List<RedactionZoneDto>()
                        : JsonSerializer.Deserialize<List<RedactionZoneDto>>((string)ser.RedactionsJson);

                    var sopUids = string.IsNullOrEmpty((string?)ser.SopInstanceUidsJson)
                        ? new List<string>()
                        : JsonSerializer.Deserialize<List<string>>((string)ser.SopInstanceUidsJson);

                    studyDto.Series.Add(new SubmitCaseSeriesDto
                    {
                        RowId = (long)ser.Id,
                        SeriesInstanceUid = ser.SeriesInstanceUid,
                        SeriesDescription = ser.SeriesDescription,
                        Modality = ser.Modality,
                        Start = (int)ser.StartFrame,
                        End = (int)ser.EndFrame,
                        Step = (int)ser.StepFrame,
                        Redactions = redactions ?? new List<RedactionZoneDto>(),
                        UploadMethod = (string?)ser.UploadMethod ?? "dicom",
                        SubSeriesKey = (string?)ser.SubSeriesKey,
                        SubSeriesLabel = (string?)ser.SubSeriesLabel,
                        SopInstanceUids = sopUids ?? new List<string>(),
                        IsUploaded = ser.UploadedAt != null
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
                        DiagnosticCertainty, CaseDiscussion, CreatedAt, Status,
                        PatientName, PatientId, PatientDob
                    ) VALUES (
                        @Id, @Username, @Title, @Presentation, @System, @Age, @Sex, 
                        @DiagnosticCertainty, @CaseDiscussion, @CreatedAt, @Status,
                        @PatientName, @PatientId, @PatientDob
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
                    Status = "Queued",
                    payload.PatientName,
                    payload.PatientId,
                    payload.PatientDob
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
                                StartFrame, EndFrame, StepFrame, RedactionsJson, UploadMethod,
                                SubSeriesKey, SubSeriesLabel, SopInstanceUidsJson
                            ) VALUES (
                                @StudyId, @SeriesUid, @Desc, @Mod,
                                @Start, @End, @Step, @Redactions, @UploadMethod,
                                @SubKey, @SubLabel, @SopUids
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
                            Redactions = JsonSerializer.Serialize(series.Redactions),
                            UploadMethod = series.UploadMethod,
                            SubKey = series.SubSeriesKey,
                            SubLabel = series.SubSeriesLabel,
                            SopUids = series.SopInstanceUids.Count > 0
                                ? JsonSerializer.Serialize(series.SopInstanceUids)
                                : null
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

        /// <summary>
        /// Appends studies/series to an existing draft case and re-queues it for upload.
        /// Studies matching an existing StudyInstanceUid on the case get their series added
        /// to that study row; new UIDs create new study rows. The processor skips anything
        /// already marked uploaded, so only the appended content is sent to Radiopaedia.
        /// </summary>
        public async Task AppendToDraftCaseAsync(Guid caseId, List<SubmitCaseStudyDto> studies)
        {
            using var conn = GetConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();

            try
            {
                // Refuse to queue a second upload for a case that already has one waiting or
                // running. Duplicate submissions arrive together when a user clicks the button
                // more than once, and the resulting jobs fight over the same processing folders.
                var activeUploads = await conn.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(1) FROM DicomJobs
                    WHERE Type = @Upload
                      AND ResourceId = @CaseId
                      AND Status IN (@Pending, @InProgress)",
                    new
                    {
                        Upload = JobType.Upload,
                        CaseId = caseId,
                        Pending = JobStatus.Pending,
                        InProgress = JobStatus.InProgress
                    }, trans);

                if (activeUploads > 0)
                    throw new DuplicateUploadJobException(caseId);

                foreach (var study in studies)
                {
                    var studyId = await conn.ExecuteScalarAsync<long?>(
                        @"SELECT Id FROM DraftCaseStudies
                          WHERE DraftCaseId = @CaseId AND StudyInstanceUid = @StudyUid",
                        new { CaseId = caseId, StudyUid = study.StudyInstanceUid }, trans);

                    if (studyId == null)
                    {
                        studyId = await conn.ExecuteScalarAsync<long>(@"
                            INSERT INTO DraftCaseStudies (
                                DraftCaseId, StudyInstanceUid, RemoteNodeName, Modality, Findings
                            ) VALUES (
                                @DraftCaseId, @StudyUid, @Node, @Mod, @Find
                            );
                            SELECT last_insert_rowid();",
                            new
                            {
                                DraftCaseId = caseId,
                                StudyUid = study.StudyInstanceUid,
                                Node = study.RemoteNodeName,
                                Mod = study.Modality,
                                Find = study.Findings
                            }, trans);
                    }

                    foreach (var series in study.Series)
                    {
                        await conn.ExecuteAsync(@"
                            INSERT INTO DraftCaseSeries (
                                DraftCaseStudyId, SeriesInstanceUid, SeriesDescription, Modality,
                                StartFrame, EndFrame, StepFrame, RedactionsJson, UploadMethod,
                                SubSeriesKey, SubSeriesLabel, SopInstanceUidsJson
                            ) VALUES (
                                @StudyId, @SeriesUid, @Desc, @Mod,
                                @Start, @End, @Step, @Redactions, @UploadMethod,
                                @SubKey, @SubLabel, @SopUids
                            )",
                            new
                            {
                                StudyId = studyId,
                                SeriesUid = series.SeriesInstanceUid,
                                Desc = series.SeriesDescription,
                                Mod = series.Modality,
                                Start = series.Start,
                                End = series.End,
                                Step = series.Step,
                                Redactions = JsonSerializer.Serialize(series.Redactions),
                                UploadMethod = series.UploadMethod,
                                SubKey = series.SubSeriesKey,
                                SubLabel = series.SubSeriesLabel,
                                SopUids = series.SopInstanceUids.Count > 0
                                    ? JsonSerializer.Serialize(series.SopInstanceUids)
                                    : null
                            }, trans);
                    }
                }

                await conn.ExecuteAsync(
                    "UPDATE DraftCases SET Status = 'Queued', ErrorMessage = NULL WHERE Id = @Id",
                    new { Id = caseId }, trans);

                var primaryStudy = studies.First();
                await conn.ExecuteAsync(@"
                    INSERT INTO DicomJobs (
                        Id, StudyInstanceUid, SeriesInstanceUid, RemoteAeTitle,
                        Type, Status, Priority, CreatedAt, ResourceId
                    ) VALUES (
                        @Id, @StudyUid, NULL, @RemoteNode,
                        @Type, @Status, @Priority, @CreatedAt, @ResourceId
                    )",
                    new
                    {
                        Id = Guid.NewGuid(),
                        StudyUid = primaryStudy.StudyInstanceUid,
                        RemoteNode = primaryStudy.RemoteNodeName ?? "UNKNOWN",
                        Type = JobType.Upload,
                        Status = JobStatus.Pending,
                        Priority = 5,
                        CreatedAt = DateTime.UtcNow,
                        ResourceId = caseId
                    }, trans);

                trans.Commit();
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        public async Task MarkSeriesUploadedAsync(long seriesRowId)
        {
            using var conn = GetConnection();
            await conn.ExecuteAsync(
                "UPDATE DraftCaseSeries SET UploadedAt = @Now WHERE Id = @Id",
                new { Id = seriesRowId, Now = DateTime.UtcNow });
        }

        public async Task<DraftCase?> GetDraftCaseAsync(Guid caseId)
        {
            using var conn = GetConnection();
            return await conn.QueryFirstOrDefaultAsync<DraftCase>(
                "SELECT * FROM DraftCases WHERE Id = @Id", new { Id = caseId });
        }

        public async Task<IEnumerable<CaseListItemDto>> GetUserCasesAsync(string username)
        {
            using var conn = GetConnection();
            var sql = @"
                SELECT
                    Id, Title, Presentation, Age, Sex, Status,
                    CreatedAt, RadiopaediaCaseId, ErrorMessage,
                    PatientName, PatientId, PatientDob,
                    RemoteStatus, RemoteVisibility, RemoteCheckedAt
                FROM DraftCases
                WHERE Username = @Username
                ORDER BY CreatedAt DESC";

            return await conn.QueryAsync<CaseListItemDto>(sql, new { Username = username });
        }

        public async Task<IEnumerable<AdminCaseListItemDto>> GetAllCasesAsync()
        {
            using var conn = GetConnection();
            var sql = @"
                SELECT
                    Id, Username, Title, Presentation, Age, Sex, Status,
                    CreatedAt, RadiopaediaCaseId, ErrorMessage,
                    PatientName, PatientId, PatientDob,
                    RemoteStatus, RemoteVisibility, RemoteCheckedAt
                FROM DraftCases
                ORDER BY CreatedAt DESC";

            return await conn.QueryAsync<AdminCaseListItemDto>(sql);
        }

        public async Task<CaseDetailDto?> GetCaseDetailAdminAsync(Guid caseId)
        {
            using var conn = GetConnection();

            var sqlCase = @"
                SELECT Id, Title, Presentation, System, Age, Sex, DiagnosticCertainty,
                       CaseDiscussion, Status, CreatedAt, RadiopaediaCaseId, ErrorMessage,
                       PatientName, PatientId, PatientDob,
                       RemoteStatus, RemoteVisibility, RemoteCheckedAt
                FROM DraftCases
                WHERE Id = @Id";

            var draft = await conn.QueryFirstOrDefaultAsync<CaseDetailDto>(sqlCase, new { Id = caseId });
            if (draft == null) return null;

            var sqlStudies = @"
                SELECT Id, StudyInstanceUid, RemoteNodeName, Modality, Findings, RadiopaediaStudyId
                FROM DraftCaseStudies
                WHERE DraftCaseId = @CaseId";

            var studies = await conn.QueryAsync(sqlStudies, new { CaseId = caseId });

            foreach (var study in studies)
            {
                var studyDto = new CaseDetailStudyDto
                {
                    Id = (long)study.Id,
                    StudyInstanceUid = study.StudyInstanceUid ?? string.Empty,
                    RemoteNodeName = study.RemoteNodeName,
                    Modality = study.Modality,
                    Findings = study.Findings,
                    RadiopaediaStudyId = study.RadiopaediaStudyId
                };

                var sqlSeries = @"
                    SELECT Id, SeriesInstanceUid, SeriesDescription, SubSeriesLabel, Modality,
                           StartFrame, EndFrame, StepFrame, RedactionsJson
                    FROM DraftCaseSeries
                    WHERE DraftCaseStudyId = @StudyId";

                var seriesRecords = await conn.QueryAsync(sqlSeries, new { StudyId = studyDto.Id });

                foreach (var series in seriesRecords)
                {
                    int start = (int)(series.StartFrame ?? 1);
                    int end = (int)(series.EndFrame ?? 1);
                    int step = (int)(series.StepFrame ?? 1);
                    step = step < 1 ? 1 : step;

                    int selectedCount = ((end - start) / step) + 1;

                    int redactionCount = 0;
                    string? redactionsJson = series.RedactionsJson;
                    if (!string.IsNullOrEmpty(redactionsJson))
                    {
                        try
                        {
                            var redactions = JsonSerializer.Deserialize<List<RedactionZoneDto>>(redactionsJson);
                            redactionCount = redactions?.Count ?? 0;
                        }
                        catch { }
                    }

                    studyDto.Series.Add(new CaseDetailSeriesDto
                    {
                        Id = (long)series.Id,
                        SeriesInstanceUid = series.SeriesInstanceUid ?? string.Empty,
                        SeriesDescription = series.SeriesDescription,
                        SubSeriesLabel = series.SubSeriesLabel,
                        Modality = series.Modality,
                        StartFrame = start,
                        EndFrame = end,
                        StepFrame = step,
                        SelectedFrameCount = selectedCount,
                        RedactionCount = redactionCount
                    });
                }

                draft.Studies.Add(studyDto);
            }

            return draft;
        }

        public async Task<IEnumerable<CaseListItemDto>> GetCasesByPatientIdAsync(string patientId)
        {
            if (string.IsNullOrWhiteSpace(patientId))
                return Enumerable.Empty<CaseListItemDto>();

            using var conn = GetConnection();
            var sql = @"
                SELECT 
                    Id, Title, Presentation, Age, Sex, Status, 
                    CreatedAt, RadiopaediaCaseId, ErrorMessage,
                    PatientName, PatientId, PatientDob
                FROM DraftCases 
                WHERE PatientId = @PatientId 
                ORDER BY CreatedAt DESC";

            return await conn.QueryAsync<CaseListItemDto>(sql, new { PatientId = patientId });
        }

        /// <summary>
        /// Every case belonging to a user that has made it as far as having a Radiopaedia ID.
        /// These are the rows worth reconciling against the user's case listing.
        /// </summary>
        public async Task<IEnumerable<RemoteCaseState>> GetUploadedCaseIdsAsync(string username)
        {
            using var conn = GetConnection();
            var sql = @"
                SELECT Id AS CaseId, RadiopaediaCaseId, RemoteStatus, RemoteVisibility, RemoteCheckedAt
                FROM DraftCases
                WHERE Username = @Username
                  AND RadiopaediaCaseId IS NOT NULL
                  AND TRIM(RadiopaediaCaseId) <> ''";

            return await conn.QueryAsync<RemoteCaseState>(sql, new { Username = username });
        }

        /// <summary>
        /// Records what the Radiopaedia case listing said about a case. A null status means
        /// the case was not in the listing at all, which we store as "deleted".
        /// </summary>
        public async Task UpdateRemoteCaseStateAsync(
            Guid caseId, string? remoteStatus, string? remoteVisibility, DateTime checkedAtUtc)
        {
            using var conn = GetConnection();
            var sql = @"
                UPDATE DraftCases
                SET RemoteStatus = @RemoteStatus,
                    RemoteVisibility = @RemoteVisibility,
                    RemoteCheckedAt = @CheckedAt
                WHERE Id = @Id";

            await conn.ExecuteAsync(sql, new
            {
                Id = caseId,
                RemoteStatus = remoteStatus,
                RemoteVisibility = remoteVisibility,
                CheckedAt = checkedAtUtc
            });
        }

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

        public async Task UpdateStudyRadiopaediaIdAsync(Guid caseId, string studyInstanceUid, string radiopaediaStudyId)
        {
            using var conn = GetConnection();
            await conn.ExecuteAsync(
                @"UPDATE DraftCaseStudies SET RadiopaediaStudyId = @RadiopaediaStudyId
                  WHERE DraftCaseId = @CaseId AND StudyInstanceUid = @StudyUid",
                new { CaseId = caseId, StudyUid = studyInstanceUid, RadiopaediaStudyId = radiopaediaStudyId });
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
                // An Upload job is left pending while another Upload job for the same case is
                // still running. Two jobs for one case share the same processing folders and
                // would delete each other's staged files mid-upload, which has already caused
                // a series to be uploaded with only part of its frames.
                var sqlFind = @"
                    SELECT * FROM DicomJobs AS j
                    WHERE j.Status = @Pending
                      AND (
                          j.Type <> @Upload
                          OR NOT EXISTS (
                              SELECT 1 FROM DicomJobs AS running
                              WHERE running.Status = @InProgress
                                AND running.Type = @Upload
                                AND running.ResourceId = j.ResourceId
                          )
                      )
                    ORDER BY j.Priority ASC, j.CreatedAt ASC
                    LIMIT 1";

                var job = await conn.QueryFirstOrDefaultAsync<DicomJob>(
                    sqlFind,
                    new { Pending = JobStatus.Pending, InProgress = JobStatus.InProgress, Upload = JobType.Upload },
                    trans);

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

        public async Task<CaseDetailDto?> GetCaseDetailAsync(Guid caseId, string username)
        {
            using var conn = GetConnection();

            // Get the main case record with username validation
            var sqlCase = @"
                SELECT Id, Title, Presentation, System, Age, Sex, DiagnosticCertainty,
                       CaseDiscussion, Status, CreatedAt, RadiopaediaCaseId, ErrorMessage,
                       PatientName, PatientId, PatientDob,
                       RemoteStatus, RemoteVisibility, RemoteCheckedAt
                FROM DraftCases
                WHERE Id = @Id AND Username = @Username";

            var draft = await conn.QueryFirstOrDefaultAsync<CaseDetailDto>(sqlCase, new { Id = caseId, Username = username });
            if (draft == null) return null;

            // Get studies for this case
            var sqlStudies = @"
                SELECT Id, StudyInstanceUid, RemoteNodeName, Modality, Findings
                FROM DraftCaseStudies 
                WHERE DraftCaseId = @CaseId";

            var studies = await conn.QueryAsync(sqlStudies, new { CaseId = caseId });

            foreach (var study in studies)
            {
                var studyDto = new CaseDetailStudyDto
                {
                    Id = (long)study.Id,
                    StudyInstanceUid = study.StudyInstanceUid ?? string.Empty,
                    RemoteNodeName = study.RemoteNodeName,
                    Modality = study.Modality,
                    Findings = study.Findings,
                    RadiopaediaStudyId = study.RadiopaediaStudyId
                };

                // Get series for this study
                var sqlSeries = @"
                    SELECT Id, SeriesInstanceUid, SeriesDescription, SubSeriesLabel, Modality,
                           StartFrame, EndFrame, StepFrame, RedactionsJson
                    FROM DraftCaseSeries
                    WHERE DraftCaseStudyId = @StudyId";

                var seriesRecords = await conn.QueryAsync(sqlSeries, new { StudyId = studyDto.Id });

                foreach (var series in seriesRecords)
                {
                    int start = (int)(series.StartFrame ?? 1);
                    int end = (int)(series.EndFrame ?? 1);
                    int step = (int)(series.StepFrame ?? 1);
                    step = step < 1 ? 1 : step;

                    int selectedCount = ((end - start) / step) + 1;

                    int redactionCount = 0;
                    string? redactionsJson = series.RedactionsJson;
                    if (!string.IsNullOrEmpty(redactionsJson))
                    {
                        try
                        {
                            var redactions = JsonSerializer.Deserialize<List<RedactionZoneDto>>(redactionsJson);
                            redactionCount = redactions?.Count ?? 0;
                        }
                        catch
                        {
                            // Ignore JSON parse errors
                        }
                    }

                    studyDto.Series.Add(new CaseDetailSeriesDto
                    {
                        Id = (long)series.Id,
                        SeriesInstanceUid = series.SeriesInstanceUid ?? string.Empty,
                        SeriesDescription = series.SeriesDescription,
                        SubSeriesLabel = series.SubSeriesLabel,
                        Modality = series.Modality,
                        StartFrame = start,
                        EndFrame = end,
                        StepFrame = step,
                        SelectedFrameCount = selectedCount,
                        RedactionCount = redactionCount
                    });
                }

                draft.Studies.Add(studyDto);
            }

            return draft;
        }
    }

    /// <summary>
    /// Thrown when an upload is queued for a case that already has one pending or running.
    /// The caller should tell the user to wait rather than treating this as a fault.
    /// </summary>
    public class DuplicateUploadJobException : Exception
    {
        public Guid CaseId { get; }

        public DuplicateUploadJobException(Guid caseId)
            : base($"Case {caseId} already has an upload queued or in progress.")
        {
            CaseId = caseId;
        }
    }
}