using System.Text.Json.Serialization;

namespace RadiopaediaConnect.Models
{
    public class SubmitCaseDto
    {
        public string Title { get; set; } = string.Empty;
        public string Presentation { get; set; } = string.Empty;

        public int System { get; set; }
        public int DiagnosticCertainty { get; set; }

        public string Age { get; set; } = string.Empty;
        public string Sex { get; set; } = string.Empty;

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
    }

    /// <summary>
    /// DTO for returning case information to the frontend list view
    /// </summary>
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
    }

    #region Case Detail DTOs

    /// <summary>
    /// DTO for detailed case view including studies and series
    /// </summary>
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
        public List<CaseDetailStudyDto> Studies { get; set; } = new();
    }

    /// <summary>
    /// Study information within a case detail view
    /// </summary>
    public class CaseDetailStudyDto
    {
        public long Id { get; set; }
        public string StudyInstanceUid { get; set; } = string.Empty;
        public string? RemoteNodeName { get; set; }
        public string? Modality { get; set; }
        public string? Findings { get; set; }
        public List<CaseDetailSeriesDto> Series { get; set; } = new();
    }

    /// <summary>
    /// Series information within a study detail view
    /// </summary>
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

    #endregion
}