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