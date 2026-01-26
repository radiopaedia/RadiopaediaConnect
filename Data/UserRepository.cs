using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Threading.Tasks;

namespace RadiopaediaConnect.Data
{
    public class UserRepository
    {
        private readonly string _connectionString;

        public UserRepository(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection");
        }

        private IDbConnection CreateConnection() => new SqliteConnection(_connectionString);

        public async Task UpsertTokensAsync(UserToken token)
        {
            using var conn = CreateConnection();
            var sql = @"
                INSERT INTO UserTokens (Username, AccessToken, RefreshToken, TokenExpiresAtUtc, LastUpdatedUtc) 
                VALUES (@Username, @AccessToken, @RefreshToken, @TokenExpiresAtUtc, @LastUpdatedUtc)
                ON CONFLICT(Username) DO UPDATE SET
                    AccessToken = @AccessToken,
                    RefreshToken = @RefreshToken,
                    TokenExpiresAtUtc = @TokenExpiresAtUtc,
                    LastUpdatedUtc = @LastUpdatedUtc;";

            await conn.ExecuteAsync(sql, token);
        }

        public async Task<UserToken> GetUserAsync(string username)
        {
            using var conn = CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<UserToken>(
                "SELECT * FROM UserTokens WHERE Username = @Username", new { Username = username });
        }

        public async Task LogEventAsync(string eventType, string username, string message, string severity = "Info")
        {
            using var conn = CreateConnection();
            await conn.ExecuteAsync(
                "INSERT INTO OAuthAuditLogs (TimestampUtc, EventType, Username, Severity, Message) VALUES (@TimestampUtc, @EventType, @Username, @Severity, @Message)",
                new { TimestampUtc = System.DateTime.UtcNow, EventType = eventType, Username = username, Severity = severity, Message = message });
        }
    }
}