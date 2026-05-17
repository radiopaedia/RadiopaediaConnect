namespace RadiopaediaConnect.Data
{
    public class AppLogEntity
    {
        public long   Id           { get; set; }
        public string TimestampUtc { get; set; } = string.Empty;
        public string Level        { get; set; } = string.Empty;
        public string Category     { get; set; } = string.Empty;
        public string Message      { get; set; } = string.Empty;
        public string? Exception   { get; set; }
        public string? JobId       { get; set; }
    }
}
