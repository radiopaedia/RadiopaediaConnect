using Dapper;
using Microsoft.Data.Sqlite;
using System.Data;

namespace RadiopaediaConnect.Data
{
    public class SettingsRepository
    {
        private readonly string _connectionString;

        public SettingsRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        private IDbConnection GetConnection() => new SqliteConnection(_connectionString);

        // ─── Admin Password ────────────────────────────────────────────

        public async Task<bool> IsPasswordSetAsync()
        {
            using var conn = GetConnection();
            var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AdminConfig WHERE Id = 1");
            return count > 0;
        }

        public async Task SetPasswordAsync(string bcryptHash)
        {
            using var conn = GetConnection();
            var exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AdminConfig WHERE Id = 1");

            if (exists > 0)
            {
                await conn.ExecuteAsync(
                    "UPDATE AdminConfig SET PasswordHash = @Hash, CreatedAtUtc = @Now WHERE Id = 1",
                    new { Hash = bcryptHash, Now = DateTime.UtcNow.ToString("o") });
            }
            else
            {
                await conn.ExecuteAsync(
                    "INSERT INTO AdminConfig (Id, PasswordHash, CreatedAtUtc) VALUES (1, @Hash, @Now)",
                    new { Hash = bcryptHash, Now = DateTime.UtcNow.ToString("o") });
            }
        }

        public async Task<string?> GetPasswordHashAsync()
        {
            using var conn = GetConnection();
            return await conn.ExecuteScalarAsync<string?>("SELECT PasswordHash FROM AdminConfig WHERE Id = 1");
        }

        // ─── Local Settings ────────────────────────────────────────────

        public async Task<LocalSettingsEntity> GetLocalSettingsAsync()
        {
            using var conn = GetConnection();
            var settings = await conn.QueryFirstOrDefaultAsync<LocalSettingsEntity>(
                "SELECT * FROM LocalSettings WHERE Id = 1");

            return settings ?? new LocalSettingsEntity();
        }

        /// <summary>
        /// Synchronous overload for use in non-async contexts (e.g. IPostConfigureOptions).
        /// Dapper supports sync calls natively so there is no deadlock risk.
        /// </summary>
        public LocalSettingsEntity GetLocalSettings()
        {
            using var conn = GetConnection();
            var settings = conn.QueryFirstOrDefault<LocalSettingsEntity>(
                "SELECT * FROM LocalSettings WHERE Id = 1");

            return settings ?? new LocalSettingsEntity();
        }

        public async Task SaveLocalSettingsAsync(LocalSettingsEntity settings)
        {
            using var conn = GetConnection();
            var exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LocalSettings WHERE Id = 1");

            if (exists > 0)
            {
                await conn.ExecuteAsync(@"
                    UPDATE LocalSettings SET
                        StorageScpAeTitle = @StorageScpAeTitle,
                        MaxConcurrentDownloads = @MaxConcurrentDownloads,
                        RadiopaediaClientId = @RadiopaediaClientId,
                        RadiopaediaClientSecret = @RadiopaediaClientSecret,
                        SmtpHost = @SmtpHost,
                        SmtpPort = @SmtpPort,
                        SmtpUsername = @SmtpUsername,
                        SmtpPassword = @SmtpPassword,
                        SmtpFromAddress = @SmtpFromAddress,
                        NotificationRecipients = @NotificationRecipients,
                        UpdatedAtUtc = @UpdatedAtUtc
                    WHERE Id = 1", settings);
            }
            else
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO LocalSettings
                        (Id, StorageScpAeTitle, MaxConcurrentDownloads,
                         RadiopaediaClientId, RadiopaediaClientSecret,
                         SmtpHost, SmtpPort, SmtpUsername, SmtpPassword,
                         SmtpFromAddress, NotificationRecipients, UpdatedAtUtc)
                    VALUES
                        (1, @StorageScpAeTitle, @MaxConcurrentDownloads,
                         @RadiopaediaClientId, @RadiopaediaClientSecret,
                         @SmtpHost, @SmtpPort, @SmtpUsername, @SmtpPassword,
                         @SmtpFromAddress, @NotificationRecipients, @UpdatedAtUtc)", settings);
            }
        }

        // ─── Remote Nodes ──────────────────────────────────────────────

        public async Task<List<RemoteNodeEntity>> GetRemoteNodesAsync()
        {
            using var conn = GetConnection();
            var nodes = await conn.QueryAsync<RemoteNodeEntity>(
                "SELECT * FROM RemoteNodes ORDER BY SortOrder, Id");
            return nodes.ToList();
        }

        public async Task<RemoteNodeEntity?> GetRemoteNodeAsync(int id)
        {
            using var conn = GetConnection();
            return await conn.QueryFirstOrDefaultAsync<RemoteNodeEntity>(
                "SELECT * FROM RemoteNodes WHERE Id = @Id", new { Id = id });
        }

        public async Task<int> AddRemoteNodeAsync(RemoteNodeEntity node)
        {
            using var conn = GetConnection();
            var id = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO RemoteNodes (Name, AeTitle, Host, Port, CallingAe, SortOrder)
                VALUES (@Name, @AeTitle, @Host, @Port, @CallingAe, @SortOrder);
                SELECT last_insert_rowid();", node);
            return id;
        }

        public async Task UpdateRemoteNodeAsync(RemoteNodeEntity node)
        {
            using var conn = GetConnection();
            await conn.ExecuteAsync(@"
                UPDATE RemoteNodes SET
                    Name = @Name,
                    AeTitle = @AeTitle,
                    Host = @Host,
                    Port = @Port,
                    CallingAe = @CallingAe,
                    SortOrder = @SortOrder
                WHERE Id = @Id", node);
        }

        public async Task DeleteRemoteNodeAsync(int id)
        {
            using var conn = GetConnection();
            await conn.ExecuteAsync("DELETE FROM RemoteNodes WHERE Id = @Id", new { Id = id });
        }

        public async Task ReorderRemoteNodesAsync(List<int> orderedIds)
        {
            using var conn = GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            for (int i = 0; i < orderedIds.Count; i++)
            {
                await conn.ExecuteAsync(
                    "UPDATE RemoteNodes SET SortOrder = @Order WHERE Id = @Id",
                    new { Order = i, Id = orderedIds[i] },
                    tx);
            }

            tx.Commit();
        }
    }

    // ─── Entities ──────────────────────────────────────────────────────

    public class LocalSettingsEntity
    {
        public int Id { get; set; } = 1;
        public string StorageScpAeTitle { get; set; } = "RCONNECT_SCP";
        public int MaxConcurrentDownloads { get; set; } = 5;
        public string? RadiopaediaClientId { get; set; }
        public string? RadiopaediaClientSecret { get; set; }
        public string? SmtpHost { get; set; }
        public int? SmtpPort { get; set; }
        public string? SmtpUsername { get; set; }
        public string? SmtpPassword { get; set; }
        public string? SmtpFromAddress { get; set; }
        public string? NotificationRecipients { get; set; }
        public string? UpdatedAtUtc { get; set; }
    }

    public class RemoteNodeEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string AeTitle { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 104;
        public string CallingAe { get; set; } = "RCONNECT_SCU";
        public int SortOrder { get; set; }
    }
}