using System.Text.Json.Serialization;

namespace RadiopaediaConnect.Models
{
    public class SubmitCaseDto
    {
        public string Title { get; set; } = string.Empty;
        public string Presentation { get; set; } = string.Empty;

        public int System { get; set; }
        [JsonPropertyName("diagnostic_certainty")]
        public int DiagnosticCertainty { get; set; }        

        public string Age { get; set; } = string.Empty;
        public string Sex { get; set; } = string.Empty;

        // Patient demographics for display/search in My Cases
        public string PatientName { get; set; } = string.Empty;
        public string PatientId { get; set; } = string.Empty;
        public DateTime? PatientDob { get; set; }

        [JsonPropertyName("body")]
        public string CaseDiscussion { get; set; } = string.Empty;

        public List<SubmitCaseStudyDto> Studies { get; set; } = new();
    }

    public class SubmitCaseStudyDto
    {
        [JsonPropertyName("studyinstanceuid")]
        public string StudyInstanceUid { get; set; } = string.Empty;

        [JsonPropertyName("modality")]
        public string Modality { get; set; } = string.Empty;

        [JsonPropertyName("remoteNodeName")]
        public string RemoteNodeName { get; set; } = string.Empty;

        [JsonPropertyName("findings")]
        public string Findings { get; set; } = string.Empty;

        [JsonPropertyName("series")]
        public List<SubmitCaseSeriesDto> Series { get; set; } = new();
    }

    public class SubmitCaseSeriesDto
    {
        [JsonPropertyName("seriesinstanceuid")]
        public string SeriesInstanceUid { get; set; } = string.Empty;

        [JsonPropertyName("seriesdescription")]
        public string SeriesDescription { get; set; } = string.Empty;

        [JsonPropertyName("modality")]
        public string Modality { get; set; } = string.Empty;

        public int Start { get; set; }
        public int End { get; set; }
        public int Step { get; set; }

        [JsonPropertyName("redactions")]
        public List<RedactionZoneDto> Redactions { get; set; } = new();

        /// <summary>
        /// Requested upload method: "dicom" (native DICOM via S3) or "png" (rendered ZIP).
        /// Defaults to "dicom". The pipeline enforces fallback to "png" when redactions are
        /// present or when multiframe culling is detected at runtime.
        /// </summary>
        [JsonPropertyName("uploadMethod")]
        public string UploadMethod { get; set; } = "dicom";

        /// <summary>
        /// Returns true when native DICOM upload is eligible.
        /// Redactions always force PNG. Multiframe + culling is checked separately in
        /// the pipeline once the actual files are scanned.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool RequestsDicom => UploadMethod == "dicom" && !Redactions.Any();
    }

    public class RedactionZoneDto
    {
        [JsonPropertyName("x")]
        public double X { get; set; }
        [JsonPropertyName("y")]
        public double Y { get; set; }
        [JsonPropertyName("w")]
        public double W { get; set; }
        [JsonPropertyName("h")]
        public double H { get; set; }
    }

    public class DraftCase
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Title { get; set; }
        public string Presentation { get; set; }
        public int System { get; set; }
        public string Age { get; set; }
        public string Sex { get; set; }
        public int DiagnosticCertainty { get; set; }
        public string CaseDiscussion { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; }
        public string? RadiopaediaCaseId { get; set; }
        public string? ErrorMessage { get; set; }

        // Patient demographics
        public string? PatientName { get; set; }
        public string? PatientId { get; set; }
        public DateTime? PatientDob { get; set; }
    }

    public class CaseListItemDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Presentation { get; set; } = string.Empty;
        public string Age { get; set; } = string.Empty;
        public string Sex { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? RadiopaediaCaseId { get; set; }
        public string? ErrorMessage { get; set; }

        // Patient demographics for display/search
        public string? PatientName { get; set; }
        public string? PatientId { get; set; }
        public DateTime? PatientDob { get; set; }
    }

    public class CaseDetailDto
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public string? Presentation { get; set; }
        public int System { get; set; }
        public string? Age { get; set; }
        public string? Sex { get; set; }
        public int DiagnosticCertainty { get; set; }
        public string? CaseDiscussion { get; set; }
        public string? Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? RadiopaediaCaseId { get; set; }
        public string? ErrorMessage { get; set; }

        // Patient demographics
        public string? PatientName { get; set; }
        public string? PatientId { get; set; }
        public DateTime? PatientDob { get; set; }

        public List<CaseDetailStudyDto> Studies { get; set; } = new();
    }

    public class CaseDetailStudyDto
    {
        public long Id { get; set; }
        public string StudyInstanceUid { get; set; } = string.Empty;
        public string? RemoteNodeName { get; set; }
        public string? Modality { get; set; }
        public string? Findings { get; set; }
        public List<CaseDetailSeriesDto> Series { get; set; } = new();
    }

    public class CaseDetailSeriesDto
    {
        public long Id { get; set; }
        public string SeriesInstanceUid { get; set; } = string.Empty;
        public string? SeriesDescription { get; set; }
        public string? Modality { get; set; }
        public int StartFrame { get; set; }
        public int EndFrame { get; set; }
        public int StepFrame { get; set; }
        public int SelectedFrameCount { get; set; }
        public int RedactionCount { get; set; }
    }
}