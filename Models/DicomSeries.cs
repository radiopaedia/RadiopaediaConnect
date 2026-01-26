using System.ComponentModel.DataAnnotations;

namespace RadiopaediaConnect.Data
{
    public class DicomSeries
    {
        [Key]
        public string SeriesInstanceUid { get; set; } = string.Empty;

        [Required]
        public string StudyInstanceUid { get; set; } = string.Empty;

        public string Modality { get; set; } = string.Empty;
        public string? SeriesDescription { get; set; }
        public int NumberOfInstances { get; set; }
        public bool IsRetrieved { get; set; }
        public string? StoragePath { get; set; }
        public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

        public DateTime RetrievedAt { get; set; }
    }
}