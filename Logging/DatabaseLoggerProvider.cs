using Microsoft.Extensions.Logging;
using RadiopaediaConnect.Data;
using System.Threading.Channels;

namespace RadiopaediaConnect.Logging
{
    public class DatabaseLoggerProvider : ILoggerProvider
    {
        private readonly Channel<AppLogEntity> _channel;
        private readonly Task _drainTask;
        private readonly CancellationTokenSource _cts = new();
        private readonly AppLogsRepository _repository;

        // Category prefixes that qualify for Information-level capture
        private static readonly string[] AllowedInfoPrefixes =
        [
            "RadiopaediaConnect.Services",
            "RadiopaediaConnect.Controllers",
            "RadiopaediaConnect.Data",
            "RadiopaediaConnect.Extensions",
        ];

        // Category prefixes that are always excluded (even for Warning/Error)
        private static readonly string[] BlockedPrefixes =
        [
            "Microsoft.",
            "System.",
            "FellowOakDicom.",
        ];

        public DatabaseLoggerProvider(AppLogsRepository repository)
        {
            _repository = repository;
            _channel = Channel.CreateBounded<AppLogEntity>(new BoundedChannelOptions(500)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
            _drainTask = Task.Run(DrainAsync);
        }

        public ILogger CreateLogger(string categoryName) =>
            new DatabaseLogger(categoryName, _channel, this);

        internal bool IsEnabled(string categoryName, LogLevel logLevel)
        {
            if (logLevel < LogLevel.Information) return false;

            foreach (var blocked in BlockedPrefixes)
                if (categoryName.StartsWith(blocked, StringComparison.Ordinal)) return false;

            if (logLevel >= LogLevel.Warning) return true;

            if (logLevel == LogLevel.Information)
                foreach (var prefix in AllowedInfoPrefixes)
                    if (categoryName.StartsWith(prefix, StringComparison.Ordinal)) return true;

            return false;
        }

        internal static string ExtractCategory(string message)
        {
            if (message.StartsWith("[PIPELINE]") || message.StartsWith("[Processor]") ||
                message.StartsWith("[Upload Worker]") || message.StartsWith("[Anon]"))
                return "Pipeline";
            if (message.StartsWith("[QueueWorker]") || message.StartsWith("[Purge]"))
                return "Pipeline";
            if (message.StartsWith("[API]") || message.StartsWith("[API-DEBUG]"))
                return "API";
            if (message.StartsWith("[C-MOVE]") || message.StartsWith("[C-FIND]") ||
                message.StartsWith("[DICOM]"))
                return "DICOM";
            if (message.StartsWith("[SCP]"))
                return "SCP";
            if (message.StartsWith("[Settings]"))
                return "Settings";
            if (message.StartsWith("[OAuth]"))
                return "Auth";
            return "General";
        }

        private static int _drainErrorCount = 0;

        private async Task DrainAsync()
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                try
                {
                    await _repository.InsertAsync(entry).ConfigureAwait(false);
                    _drainErrorCount = 0;
                }
                catch (Exception ex)
                {
                    // Print to stderr so schema/column errors are visible during development
                    // without crashing the drain loop or creating a logging recursion.
                    var count = System.Threading.Interlocked.Increment(ref _drainErrorCount);
                    if (count <= 3)
                        Console.Error.WriteLine($"[DatabaseLogger] INSERT failed ({count}): {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            _channel.Writer.TryComplete();
            _cts.CancelAfter(TimeSpan.FromSeconds(3));
            try { _drainTask.Wait(TimeSpan.FromSeconds(5)); } catch { }
            _cts.Dispose();
        }
    }

    internal sealed class DatabaseLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly Channel<AppLogEntity> _channel;
        private readonly DatabaseLoggerProvider _provider;

        public DatabaseLogger(string categoryName, Channel<AppLogEntity> channel, DatabaseLoggerProvider provider)
        {
            _categoryName = categoryName;
            _channel = channel;
            _provider = provider;
        }

        public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(_categoryName, logLevel);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception == null) return;

            var entry = new AppLogEntity
            {
                TimestampUtc = DateTime.UtcNow.ToString("o"),
                Level = logLevel.ToString(),
                Category = DatabaseLoggerProvider.ExtractCategory(message ?? string.Empty),
                Message = message ?? exception?.Message ?? string.Empty,
                Exception = exception?.ToString(),
                JobId = JobLogContext.CurrentJobId,
                CaseId = JobLogContext.CurrentCaseId,
            };

            _channel.Writer.TryWrite(entry);
        }
    }
}
