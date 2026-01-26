using Dapper;
using Microsoft.Data.Sqlite;
using System.IO;

namespace RadiopaediaConnect.Data
{
    public static class DbInitializer
    {
        public static void Initialize(string connectionString)
        {
            SqlMapper.AddTypeHandler(new SqliteGuidTypeHandler());
            SqlMapper.AddTypeHandler(new SqliteDateTimeHandler());
            var builder = new SqliteConnectionStringBuilder(connectionString);
            var dbPath = builder.DataSource;

            if (!string.IsNullOrWhiteSpace(dbPath) && dbPath != ":memory:")
            {
                var directory = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }

            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            conn.Execute("PRAGMA journal_mode = WAL;");

            var sql = @"
                CREATE TABLE IF NOT EXISTS UserTokens (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL UNIQUE,
                    AccessToken TEXT NOT NULL,
                    RefreshToken TEXT NOT NULL,
                    TokenExpiresAtUtc TEXT NOT NULL,
                    LastUpdatedUtc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS OAuthAuditLogs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TimestampUtc TEXT NOT NULL,
                    EventType TEXT NOT NULL,
                    Username TEXT,
                    Severity TEXT DEFAULT 'Info',
                    Message TEXT
                );               
            ";

            conn.Execute(sql);
        }
    }
}