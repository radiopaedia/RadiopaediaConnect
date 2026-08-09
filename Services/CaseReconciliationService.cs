using System.Collections.Concurrent;
using RadiopaediaConnect.Data;
using RadiopaediaConnect.Models;

namespace RadiopaediaConnect.Services
{
    /// <summary>
    /// Keeps our local case records in step with Radiopaedia by reading the user's own
    /// case listing (GET /api/v1/cases).
    ///
    /// Two things come out of that listing:
    ///   1. whether a case we uploaded still exists (a case the user deleted on
    ///      Radiopaedia disappears from the listing but stays in our database), and
    ///   2. whether it is still a draft, which is the only state that accepts new imaging.
    ///
    /// The listing is cached briefly per user so the append checks in the controller and
    /// the pipeline do not each re-page the whole thing seconds apart.
    /// </summary>
    public class CaseReconciliationService
    {
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DicomRepository _repository;
        private readonly ILogger<CaseReconciliationService> _logger;

        private readonly ConcurrentDictionary<string, CachedListing> _cache =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fetchLocks =
            new(StringComparer.OrdinalIgnoreCase);

        public CaseReconciliationService(
            IServiceScopeFactory scopeFactory,
            DicomRepository repository,
            ILogger<CaseReconciliationService> logger)
        {
            _scopeFactory = scopeFactory;
            _repository = repository;
            _logger = logger;
        }

        /// <summary>
        /// Fetches the user's cases from Radiopaedia and writes the result back onto every
        /// local case that has a Radiopaedia ID.
        /// </summary>
        public async Task<CaseReconciliationResult> ReconcileUserAsync(
            string username, CancellationToken cancellationToken = default)
        {
            var listing = await GetListingAsync(username, forceRefresh: true, cancellationToken);
            var localCases = (await _repository.GetUploadedCaseIdsAsync(username)).ToList();
            var checkedAt = DateTime.UtcNow;

            var result = new CaseReconciliationResult
            {
                RemoteCaseCount = listing.Count,
                LocalCasesChecked = localCases.Count,
                CheckedAtUtc = checkedAt
            };

            foreach (var local in localCases)
            {
                listing.TryGetValue(local.RadiopaediaCaseId!, out var remote);

                var status = remote?.Status ?? RadiopaediaCaseStatus.Deleted;
                var visibility = remote?.Visibility;

                switch (status)
                {
                    case RadiopaediaCaseStatus.Draft: result.DraftCount++; break;
                    case RadiopaediaCaseStatus.PendingReview: result.PendingReviewCount++; break;
                    case RadiopaediaCaseStatus.Published: result.PublishedCount++; break;
                    case RadiopaediaCaseStatus.Deleted: result.DeletedCount++; break;
                }

                bool changed = !string.Equals(local.RemoteStatus, status, StringComparison.OrdinalIgnoreCase);
                if (changed && local.RemoteStatus != null)
                {
                    _logger.LogInformation(
                        "[RECONCILE] Case {CaseId} (Radiopaedia {RCaseId}) moved from '{Old}' to '{New}'",
                        local.CaseId, local.RadiopaediaCaseId, local.RemoteStatus, status);
                }

                await _repository.UpdateRemoteCaseStateAsync(local.CaseId, status, visibility, checkedAt);
            }

            _logger.LogInformation(
                "[RECONCILE] {Username}: {Local} local case(s) checked against {Remote} remote case(s) " +
                "({Draft} draft, {Review} in review, {Published} published, {Deleted} deleted)",
                username, result.LocalCasesChecked, result.RemoteCaseCount,
                result.DraftCount, result.PendingReviewCount, result.PublishedCount, result.DeletedCount);

            return result;
        }

        /// <summary>
        /// The current status of one Radiopaedia case, or "deleted" when it is no longer in
        /// the user's listing. The local record is updated with whatever we find, so a check
        /// made before an upload also keeps the My Cases view honest.
        /// </summary>
        public async Task<string> GetRemoteStatusAsync(
            string username,
            string radiopaediaCaseId,
            Guid? localCaseId = null,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var listing = await GetListingAsync(username, forceRefresh, cancellationToken);

            listing.TryGetValue(radiopaediaCaseId, out var remote);
            var status = remote?.Status ?? RadiopaediaCaseStatus.Deleted;

            if (localCaseId.HasValue)
            {
                await _repository.UpdateRemoteCaseStateAsync(
                    localCaseId.Value, status, remote?.Visibility, DateTime.UtcNow);
            }

            return status;
        }

        /// <summary>
        /// Throws when the case cannot accept new imaging. Called before queueing an append
        /// and again inside the pipeline, since the two can be minutes apart.
        /// </summary>
        public async Task EnsureAcceptsNewImagingAsync(
            string username,
            string radiopaediaCaseId,
            Guid? localCaseId = null,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var status = await GetRemoteStatusAsync(
                username, radiopaediaCaseId, localCaseId, forceRefresh, cancellationToken);

            if (RadiopaediaCaseStatus.AcceptsNewImaging(status)) return;

            throw new CaseNotEditableException(radiopaediaCaseId, status);
        }

        /// <summary>Drops the cached listing for a user, e.g. after creating a new case.</summary>
        public void InvalidateCache(string username) => _cache.TryRemove(username, out _);

        // ──────────────────────────────────────────────────────────────────────────────────
        // Listing cache
        // ──────────────────────────────────────────────────────────────────────────────────

        private async Task<IReadOnlyDictionary<string, RadiopaediaCaseSummary>> GetListingAsync(
            string username, bool forceRefresh, CancellationToken cancellationToken)
        {
            if (!forceRefresh &&
                _cache.TryGetValue(username, out var cached) &&
                DateTime.UtcNow - cached.FetchedAt < CacheLifetime)
            {
                return cached.CasesById;
            }

            var gate = _fetchLocks.GetOrAdd(username, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                // Another caller may have refreshed while we waited on the gate.
                if (_cache.TryGetValue(username, out cached) &&
                    DateTime.UtcNow - cached.FetchedAt < CacheLifetime)
                {
                    return cached.CasesById;
                }

                using var scope = _scopeFactory.CreateScope();
                var apiClient = scope.ServiceProvider.GetRequiredService<RadiopaediaApiClient>();

                var cases = await apiClient.ListCasesAsync(username, cancellationToken);

                // Radiopaedia case IDs are integers; we store them as strings everywhere.
                var byId = cases
                    .GroupBy(c => c.Id.ToString())
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

                _cache[username] = new CachedListing(byId, DateTime.UtcNow);
                return byId;
            }
            finally
            {
                gate.Release();
            }
        }

        private sealed record CachedListing(
            IReadOnlyDictionary<string, RadiopaediaCaseSummary> CasesById,
            DateTime FetchedAt);
    }

    /// <summary>
    /// Raised when imaging cannot be added to a case because it is no longer a draft
    /// (or no longer exists) on Radiopaedia.
    /// </summary>
    public class CaseNotEditableException : Exception
    {
        public string RadiopaediaCaseId { get; }
        public string RemoteStatus { get; }

        public CaseNotEditableException(string radiopaediaCaseId, string remoteStatus)
            : base(BuildMessage(radiopaediaCaseId, remoteStatus))
        {
            RadiopaediaCaseId = radiopaediaCaseId;
            RemoteStatus = remoteStatus;
        }

        private static string BuildMessage(string radiopaediaCaseId, string remoteStatus)
        {
            if (remoteStatus == RadiopaediaCaseStatus.Deleted)
                return $"Radiopaedia case {radiopaediaCaseId} no longer exists, so imaging cannot be added to it.";

            return $"Radiopaedia case {radiopaediaCaseId} is {RadiopaediaCaseStatus.Describe(remoteStatus)}. " +
                   "Imaging can only be added while a case is still a draft.";
        }
    }
}
