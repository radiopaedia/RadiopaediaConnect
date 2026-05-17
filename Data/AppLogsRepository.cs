using Dapper;
using Microsoft.Data.Sqlite;

namespace RadiopaediaConnect.Data
{
    public class AppLogsRepository
    {
        private readonly string _connectionString;

        public AppLogsRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task InsertAsync(AppLogEntity entry)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.ExecuteAsync(
                @"INSERT INTO AppLogs (TimestampUtc, Level, Category, Message, Exception, JobId)
                  VALUES (@TimestampUtc, @Level, @Category, @Message, @Exception, @JobId)",
                entry);
        }

        public async Task<(List<AppLogEntity> Items, int TotalCount)> QueryAsync(
            DateTime? startDate, DateTime? endDate, string? level, int page, int pageSize)
        {
            var conditions = new List<string>();
            var parameters = new DynamicParameters();

            if (startDate.HasValue)
            {
                conditions.Add("TimestampUtc >= @Start");
                parameters.Add("Start", startDate.Value.ToString("o"));
            }
            if (endDate.HasValue)
            {
                // Include the full end date (to end of day)
                conditions.Add("TimestampUtc <= @End");
                parameters.Add("End", endDate.Value.Date.AddDays(1).AddTicks(-1).ToString("o"));
            }
            if (!string.IsNullOrWhiteSpace(level))
            {
                conditions.Add("Level = @Level");
                parameters.Add("Level", level);
            }

            var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
            var offset = (page - 1) * pageSize;

            using var conn = new SqliteConnection(_connectionString);

            var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM AppLogs {where}", parameters);

            parameters.Add("Limit", pageSize);
            parameters.Add("Offset", offset);

            var items = (await conn.QueryAsync<AppLogEntity>(
                $"SELECT * FROM AppLogs {where} ORDER BY Id DESC LIMIT @Limit OFFSET @Offset",
                parameters)).ToList();

            return (items, total);
        }

        public async Task PruneOldLogsAsync(int retentionDays = 30)
        {
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays).ToString("o");
            using var conn = new SqliteConnection(_connectionString);
            await conn.ExecuteAsync("DELETE FROM AppLogs WHERE TimestampUtc < @Cutoff", new { Cutoff = cutoff });
        }
    }
}
