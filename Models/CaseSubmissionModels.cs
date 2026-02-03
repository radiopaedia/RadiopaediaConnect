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
    /// DTO for returning case information to the frontend
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
}