using System.Text.Json.Serialization;

namespace RadiopaediaConnect.Models
{
    /// <summary>
    /// One entry from GET /api/v1/cases (the caller's own cases).
    /// See: https://radiopaedia.org/api-documentation#listing-cases
    /// </summary>
    public class RadiopaediaCaseSummary
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("author_id")]
        public long AuthorId { get; set; }

        /// <summary>"draft", "pending_review" or "published".</summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        /// <summary>"public" or "unlisted".</summary>
        [JsonPropertyName("visibility")]
        public string? Visibility { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Values we store in DraftCases.RemoteStatus. The first three mirror the API's
    /// own status values; "deleted" is ours, meaning the case ID we hold was not in
    /// the user's case listing, so it no longer exists on Radiopaedia.
    /// </summary>
    public static class RadiopaediaCaseStatus
    {
        public const string Draft = "draft";
        public const string PendingReview = "pending_review";
        public const string Published = "published";
        public const string Deleted = "deleted";

        /// <summary>Imaging can only be added to a case that is still a draft.</summary>
        public static bool AcceptsNewImaging(string? status) =>
            string.Equals(status, Draft, StringComparison.OrdinalIgnoreCase);

        /// <summary>Human-readable phrase for error messages and notifications.</summary>
        public static string Describe(string? status) => status switch
        {
            Draft => "a draft",
            PendingReview => "awaiting editorial review",
            Published => "published",
            Deleted => "no longer present on Radiopaedia",
            null or "" => "of unknown status",
            _ => $"in state '{status}'"
        };
    }

    /// <summary>The remote state of one local case, as of the last reconciliation.</summary>
    public class RemoteCaseState
    {
        public Guid CaseId { get; set; }
        public string? RadiopaediaCaseId { get; set; }
        public string? RemoteStatus { get; set; }
        public string? RemoteVisibility { get; set; }
        public DateTime? RemoteCheckedAt { get; set; }
    }

    /// <summary>Summary of a reconciliation run, returned to the UI.</summary>
    public class CaseReconciliationResult
    {
        public int RemoteCaseCount { get; set; }
        public int LocalCasesChecked { get; set; }
        public int DraftCount { get; set; }
        public int PendingReviewCount { get; set; }
        public int PublishedCount { get; set; }
        public int DeletedCount { get; set; }
        public DateTime CheckedAtUtc { get; set; }
    }
}
