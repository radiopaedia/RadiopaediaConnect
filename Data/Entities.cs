using System;

namespace RadiopaediaConnect.Data
{
    public class UserToken
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime TokenExpiresAtUtc { get; set; }
        public DateTime LastUpdatedUtc { get; set; }
    }

    public class OAuthAuditLog
    {
        public int Id { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string EventType { get; set; }
        public string Username { get; set; }
        public string Severity { get; set; }
        public string Message { get; set; }
    }
}