using Dapper;
using Microsoft.Data.Sqlite;

namespace RadiopaediaConnect.Data
{
    public static class SettingsDbInitializer
    {
        public static void Initialize(string connectionString)
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            conn.Execute("PRAGMA journal_mode = WAL;");

            var sql = @"
                CREATE TABLE IF NOT EXISTS AdminConfig (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    PasswordHash TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS LocalSettings (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    StorageScpAeTitle TEXT DEFAULT 'RCONNECT_SCP',
                    MaxConcurrentDownloads INTEGER DEFAULT 5,
                    RadiopaediaClientId TEXT,
                    RadiopaediaClientSecret TEXT,
                    SmtpHost TEXT,
                    SmtpPort INTEGER,
                    SmtpUsername TEXT,
                    SmtpPassword TEXT,
                    SmtpFromAddress TEXT,
                    NotificationRecipients TEXT,
                    UpdatedAtUtc TEXT
                );

                CREATE TABLE IF NOT EXISTS RemoteNodes (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    AeTitle TEXT NOT NULL,
                    Host TEXT NOT NULL,
                    Port INTEGER DEFAULT 104,
                    CallingAe TEXT DEFAULT 'RCONNECT_SCU',
                    SortOrder INTEGER DEFAULT 0
                );
            ";

            conn.Execute(sql);
        }
    }
}