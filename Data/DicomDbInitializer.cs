using Dapper;
using Microsoft.Data.Sqlite;

namespace RadiopaediaConnect.Data
{
    public static class DicomDbInitializer
    {
        public static void Initialize(string connectionString)
        {
            SqlMapper.AddTypeHandler(new SqliteGuidTypeHandler());
            SqlMapper.AddTypeHandler(new SqliteDateTimeHandler());
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            conn.Execute("PRAGMA journal_mode = WAL;");

            var createTablesSql = @"
                CREATE TABLE IF NOT EXISTS DicomSeries (
                    SeriesInstanceUid TEXT PRIMARY KEY,
                    StudyInstanceUid TEXT NOT NULL,
                    Modality TEXT,
                    SeriesDescription TEXT,
                    NumberOfInstances INTEGER DEFAULT 0,
                    IsRetrieved INTEGER DEFAULT 0,
                    StoragePath TEXT,
                    LastAccessedAt TEXT NOT NULL,
                    RetrievedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS DicomJobs (
                    Id TEXT PRIMARY KEY,
                    StudyInstanceUid TEXT NOT NULL,
                    SeriesInstanceUid TEXT,
                    RemoteAeTitle TEXT,
                    Type INTEGER NOT NULL,
                    Status INTEGER NOT NULL,
                    Priority INTEGER DEFAULT 10,
                    CreatedAt TEXT NOT NULL,
                    StartedAt TEXT,
                    CompletedAt TEXT,
                    ErrorMessage TEXT,
                    RetryCount INTEGER DEFAULT 0,
                    ResourceId TEXT
                );

                CREATE INDEX IF NOT EXISTS IX_DicomJobs_Status_Priority ON DicomJobs(Status, Priority);
                CREATE INDEX IF NOT EXISTS IX_DicomSeries_StudyUid ON DicomSeries(StudyInstanceUid);
                CREATE INDEX IF NOT EXISTS IX_DicomSeries_IsRetrieved ON DicomSeries(IsRetrieved);
            ";

            conn.Execute(createTablesSql);

            var createCaseTablesSql = @"
                CREATE TABLE IF NOT EXISTS DraftCases (
                    Id TEXT PRIMARY KEY,
                    Username TEXT NOT NULL,
                    Title TEXT,
                    Presentation TEXT,
                    System INTEGER,
                    Age TEXT,
                    Sex TEXT,
                    DiagnosticCertainty INTEGER,
                    CaseDiscussion TEXT,
                    CreatedAt TEXT NOT NULL,
                    Status TEXT DEFAULT 'Queued'
                );

                CREATE TABLE IF NOT EXISTS DraftCaseStudies (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DraftCaseId TEXT NOT NULL,
                    StudyInstanceUid TEXT NOT NULL,
                    RemoteNodeName TEXT,
                    Modality TEXT,
                    Findings TEXT,
                    FOREIGN KEY(DraftCaseId) REFERENCES DraftCases(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS DraftCaseSeries (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DraftCaseStudyId INTEGER NOT NULL,
                    SeriesInstanceUid TEXT NOT NULL,
                    SeriesDescription TEXT,
                    Modality TEXT,
                    StartFrame INTEGER,
                    EndFrame INTEGER,
                    StepFrame INTEGER,
                    RedactionsJson TEXT,
                    FOREIGN KEY(DraftCaseStudyId) REFERENCES DraftCaseStudies(Id) ON DELETE CASCADE
                );
            ";

            conn.Execute(createCaseTablesSql);

            EnsureColumnExists(conn, "DicomJobs", "RemoteAeTitle", "TEXT");
            EnsureColumnExists(conn, "DicomJobs", "ResourceId", "TEXT");
            EnsureColumnExists(conn, "DraftCases", "Username", "TEXT DEFAULT 'Unknown'");
            EnsureColumnExists(conn, "DraftCases", "RadiopaediaCaseId", "TEXT");
            EnsureColumnExists(conn, "DraftCases", "ErrorMessage", "TEXT");

            // Patient demographics columns
            EnsureColumnExists(conn, "DraftCases", "PatientName", "TEXT");
            EnsureColumnExists(conn, "DraftCases", "PatientId", "TEXT");
            EnsureColumnExists(conn, "DraftCases", "PatientDob", "TEXT");

            // Remote (Radiopaedia-side) state of the case, refreshed by reconciling against
            // GET /api/v1/cases. RemoteStatus is "draft", "pending_review", "published" or
            // "deleted" (our own value for a case ID that is no longer in the user's listing).
            EnsureColumnExists(conn, "DraftCases", "RemoteStatus", "TEXT");
            EnsureColumnExists(conn, "DraftCases", "RemoteVisibility", "TEXT");
            EnsureColumnExists(conn, "DraftCases", "RemoteCheckedAt", "TEXT");

            // Upload method per series: "dicom" (native DICOM via S3) or "png" (rendered ZIP)
            EnsureColumnExists(conn, "DraftCaseSeries", "UploadMethod", "TEXT NOT NULL DEFAULT 'dicom'");

            // Radiopaedia study ID assigned after upload, used to cross-reference originals API
            EnsureColumnExists(conn, "DraftCaseStudies", "RadiopaediaStudyId", "TEXT");

            // Per-series upload completion marker. Lets the processor distinguish
            // already-uploaded series from newly appended ones when a case is re-queued.
            EnsureColumnExists(conn, "DraftCaseSeries", "UploadedAt", "TEXT");

            // Sub-series split: several independent acquisitions can share one SeriesInstanceUID
            // (biplane angio). When the user splits them in the picker each part gets its own row,
            // identified by SubSeriesKey and carrying the SOP Instance UIDs it owns. NULL/empty
            // means "the whole series" — the behaviour for every series that was never split.
            EnsureColumnExists(conn, "DraftCaseSeries", "SubSeriesKey", "TEXT");
            EnsureColumnExists(conn, "DraftCaseSeries", "SubSeriesLabel", "TEXT");
            EnsureColumnExists(conn, "DraftCaseSeries", "SopInstanceUidsJson", "TEXT");

            // Backfill: series belonging to cases completed before this column existed
            // were all uploaded, so mark them to prevent re-upload on append.
            conn.Execute(@"
                UPDATE DraftCaseSeries SET UploadedAt = datetime('now')
                WHERE UploadedAt IS NULL
                  AND DraftCaseStudyId IN (
                      SELECT s.Id FROM DraftCaseStudies s
                      JOIN DraftCases c ON c.Id = s.DraftCaseId
                      WHERE c.Status = 'Completed')");

            var createAppLogsSql = @"
                CREATE TABLE IF NOT EXISTS AppLogs (
                    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    TimestampUtc TEXT    NOT NULL,
                    Level        TEXT    NOT NULL,
                    Category     TEXT    NOT NULL,
                    Message      TEXT    NOT NULL,
                    Exception    TEXT,
                    JobId        TEXT
                );
                CREATE INDEX IF NOT EXISTS IX_AppLogs_TimestampUtc ON AppLogs(TimestampUtc);
                CREATE INDEX IF NOT EXISTS IX_AppLogs_Level        ON AppLogs(Level);
            ";

            conn.Execute(createAppLogsSql);
            EnsureColumnExists(conn, "AppLogs", "CaseId", "TEXT");

            FailOrphanedWork(conn);
        }

        /// <summary>
        /// Marks any job still flagged as running at startup as failed, and any case still
        /// flagged as processing along with it. Nothing survives a process restart, so both
        /// are always leftovers. Left alone the job would hold a slot in the concurrency
        /// budget forever, and now that a running upload blocks further uploads for the same
        /// case, one leftover would stop that case being uploaded again.
        ///
        /// They are failed rather than requeued so a half-finished upload is never silently
        /// repeated: the case shows as failed and the user decides whether to retry it. The
        /// case row matters as much as the job row here: retry only offers itself on a failed
        /// case, so a case left sitting in "Processing" would be stranded with no way back.
        /// </summary>
        private static void FailOrphanedWork(SqliteConnection conn)
        {
            const string jobSql = @"
                UPDATE DicomJobs
                SET Status = @Failed,
                    ErrorMessage = COALESCE(ErrorMessage, 'Interrupted by a service restart.'),
                    CompletedAt = @Now
                WHERE Status = @InProgress";

            int jobs = conn.Execute(jobSql, new
            {
                Failed = JobStatus.Failed,
                InProgress = JobStatus.InProgress,
                Now = DateTime.UtcNow
            });
            if (jobs > 0)
                Console.WriteLine($"[DB] Failed {jobs} job(s) left running by a previous process.");

            // "Queued" is left alone: that job is still pending and will be picked up normally.
            const string caseSql = @"
                UPDATE DraftCases
                SET Status = 'Failed',
                    ErrorMessage = COALESCE(
                        ErrorMessage,
                        'Interrupted by a service restart. Retry the upload to resume it.')
                WHERE Status = 'Processing'";

            int cases = conn.Execute(caseSql);
            if (cases > 0)
                Console.WriteLine($"[DB] Failed {cases} case(s) left processing by a previous process.");
        }

        private static void EnsureColumnExists(SqliteConnection conn, string tableName, string columnName, string columnDef)
        {
            var checkColumnSql = $"PRAGMA table_info({tableName});";
            var columns = conn.Query(checkColumnSql);
            var exists = columns.Any(c => c.name.ToString().Equals(columnName, StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                var alterSql = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDef};";
                conn.Execute(alterSql);
            }
        }
    }
}