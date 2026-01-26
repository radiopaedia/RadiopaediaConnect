namespace RadiopaediaConnect.Models
{
    public class DicomSettings
    {
        public const string SectionName = "Dicom";
        public int MaxConcurrentDownloads { get; set; } = 5;

        public ScpSettings Scp { get; set; } = new();
        public List<RemoteNode> RemoteNodes { get; set; } = new();
    }

    public class ScpSettings
    {
        public string AeTitle { get; set; } = "RCONNECT_SCP";
        public int Port { get; set; } = 104;
        public string? StoragePath { get; set; }
    }

    public class RemoteNode
    {
        public string Name { get; set; } = string.Empty;
        public string AeTitle { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 104;
        public string CallingAe { get; set; } = "RCONNECT_SCU";
    }
}