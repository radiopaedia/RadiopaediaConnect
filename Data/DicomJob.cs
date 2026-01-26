using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RadiopaediaConnect.Data
{
    public enum JobType
    {
        Preview = 0,
        Upload = 1,
        Purge = 2
    }

    public enum JobStatus
    {
        Pending = 0,
        InProgress = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4
    }

    public class DicomJob
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public string StudyInstanceUid { get; set; } = string.Empty;
        public string? SeriesInstanceUid { get; set; }

        public string RemoteAeTitle { get; set; } = string.Empty;

        [Required]
        public JobType Type { get; set; }

        [Required]
        public JobStatus Status { get; set; } = JobStatus.Pending;

        public int Priority { get; set; } = 10;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public string? ErrorMessage { get; set; }
        public int RetryCount { get; set; } = 0;
        public string? ResourceId { get; set; }
    }
}