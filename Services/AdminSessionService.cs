using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace RadiopaediaConnect.Services
{
    public class AdminSessionService
    {
        private static readonly TimeSpan SessionDuration = TimeSpan.FromHours(8);
        private readonly ConcurrentDictionary<string, DateTime> _sessions = new();

        public string CreateSession()
        {
            PurgeExpired();
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            _sessions[token] = DateTime.UtcNow.Add(SessionDuration);
            return token;
        }

        public bool ValidateSession(string? token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (!_sessions.TryGetValue(token, out var expiry)) return false;
            if (DateTime.UtcNow > expiry) { _sessions.TryRemove(token, out _); return false; }
            return true;
        }

        public void InvalidateSession(string? token)
        {
            if (!string.IsNullOrEmpty(token))
                _sessions.TryRemove(token, out _);
        }

        private void PurgeExpired()
        {
            var now = DateTime.UtcNow;
            foreach (var key in _sessions.Keys.ToList())
                if (_sessions.TryGetValue(key, out var exp) && now > exp)
                    _sessions.TryRemove(key, out _);
        }
    }
}
