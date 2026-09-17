using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Indexers;

public class IndexerStatus
{
    public int IndexerId { get; set; }
    public DateTime? InitialFailure { get; set; }
    public DateTime? MostRecentFailure { get; set; }
    public DateTime? DisabledTill { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string LastFailureMessage { get; set; }
    public int? LastStatusCode { get; set; }
    public bool IsDisabled => DisabledTill.HasValue && DisabledTill.Value > DateTime.UtcNow;
    public bool IsAuthFailure => (LastStatusCode == 401 || LastStatusCode == 403) && IsDisabled;
    public bool IsRateLimited => LastStatusCode == 429 && IsDisabled;
}

public interface IIndexerStatusService
{
    void RecordSuccess(int indexerId);
    void RecordFailure(int indexerId, int? statusCode = null, string errorMessage = null, Exception ex = null, TimeSpan? retryAfter = null);
    bool IsDisabled(int indexerId);
    bool IsAuthFailed(int indexerId);
    bool IsRateLimited(int indexerId);
    IndexerStatus GetStatus(int indexerId);
    IReadOnlyDictionary<int, IndexerStatus> GetAllStatuses();
    void Reset(int indexerId);
    void ResetAll();
    TimeSpan CalculateBackoff(int consecutiveFailures, int? statusCode = null, TimeSpan? retryAfter = null, bool applyJitter = true);
}
