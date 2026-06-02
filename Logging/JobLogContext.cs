namespace RadiopaediaConnect.Logging
{
    public static class JobLogContext
    {
        private static readonly AsyncLocal<string?> _jobId = new();
        private static readonly AsyncLocal<string?> _caseId = new();

        public static string? CurrentJobId => _jobId.Value;
        public static string? CurrentCaseId => _caseId.Value;

        public static IDisposable Set(string jobId, string caseId)
        {
            var prevJob = _jobId.Value;
            var prevCase = _caseId.Value;
            _jobId.Value = jobId.ToLowerInvariant();
            _caseId.Value = caseId.ToLowerInvariant();
            return new Resetter(prevJob, prevCase);
        }

        private sealed class Resetter(string? prevJob, string? prevCase) : IDisposable
        {
            public void Dispose()
            {
                _jobId.Value = prevJob;
                _caseId.Value = prevCase;
            }
        }
    }
}
