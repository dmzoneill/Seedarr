using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NLog;

namespace NzbDrone.Core.Indexers;

public class IndexerStatusService : IIndexerStatusService
{
    public const int DefaultFailureThreshold = 3;

    private readonly ConcurrentDictionary<int, IndexerStatus> _statuses = new();
    private readonly Logger _logger;
    private readonly Func<DateTime> _nowProvider;
    private readonly Func<double> _jitterRandomProvider;
    private readonly int _failureThreshold;

    public IndexerStatusService()
        : this(null, null, DefaultFailureThreshold)
    {
    }

    public IndexerStatusService(Func<DateTime> nowProvider)
        : this(nowProvider, null, DefaultFailureThreshold)
    {
    }

    public IndexerStatusService(Func<DateTime> nowProvider, Func<double> jitterRandomProvider)
        : this(nowProvider, jitterRandomProvider, DefaultFailureThreshold)
    {
    }

    public IndexerStatusService(Func<DateTime> nowProvider, Func<double> jitterRandomProvider, int failureThreshold)
    {
        _logger = LogManager.GetCurrentClassLogger();
        _nowProvider = nowProvider ?? (() => DateTime.UtcNow);
        _jitterRandomProvider = jitterRandomProvider;
        _failureThreshold = failureThreshold > 0 ? failureThreshold : DefaultFailureThreshold;
    }

    public void RecordSuccess(int indexerId)
    {
        if (_statuses.TryGetValue(indexerId, out var status))
        {
            lock (status)
            {
                status.ConsecutiveFailures = 0;
                status.DisabledTill = null;
                status.InitialFailure = null;
                status.LastFailureMessage = null;
                status.LastStatusCode = null;
            }
        }
    }

    public void RecordFailure(int indexerId, int? statusCode = null, string errorMessage = null, Exception ex = null, TimeSpan? retryAfter = null)
    {
        var status = _statuses.GetOrAdd(indexerId, id => new IndexerStatus { IndexerId = id });
        lock (status)
        {
            var now = _nowProvider();
            DecayFailuresIfExpired(status, now);

            if (!status.InitialFailure.HasValue)
            {
                status.InitialFailure = now;
            }

            status.MostRecentFailure = now;
            status.ConsecutiveFailures++;
            status.LastStatusCode = statusCode;
            status.LastFailureMessage = errorMessage ?? ex?.Message ?? "Unknown error";
            status.CurrentTime = now;

            if (!retryAfter.HasValue)
            {
                if (ex?.Data != null && ex.Data.Contains("RetryAfter") && ex.Data["RetryAfter"] is TimeSpan ta)
                {
                    retryAfter = ta;
                }
                else
                {
                    retryAfter = TryParseRetryAfter(status.LastFailureMessage);
                }
            }

            var backoff = CalculateBackoff(status.ConsecutiveFailures, statusCode, retryAfter, applyJitter: true);
            if (backoff > TimeSpan.Zero)
            {
                status.DisabledTill = now.Add(backoff);
                _logger.Warn(
                    "Indexer {0} failed ({1}). Consecutive failures: {2}. Disabled until {3}",
                    indexerId,
                    status.LastFailureMessage,
                    status.ConsecutiveFailures,
                    status.DisabledTill);
            }
            else
            {
                status.DisabledTill = null;
                _logger.Warn(
                    "Indexer {0} transient failure ({1}). Consecutive failures: {2} (threshold {3}). Not disabling.",
                    indexerId,
                    status.LastFailureMessage,
                    status.ConsecutiveFailures,
                    _failureThreshold);
            }
        }
    }

    public bool IsDisabled(int indexerId)
    {
        if (_statuses.TryGetValue(indexerId, out var status))
        {
            lock (status)
            {
                var now = _nowProvider();
                if (status.LastStatusCode == 401 || status.LastStatusCode == 403)
                {
                    return true;
                }

                if (status.DisabledTill.HasValue)
                {
                    if (status.DisabledTill.Value <= now)
                    {
                        // Automatic recovery: backoff window has elapsed; decay failure count
                        DecayFailuresIfExpired(status, now);
                        return false;
                    }

                    return true;
                }
            }
        }

        return false;
    }

    public bool IsAuthFailed(int indexerId)
    {
        if (_statuses.TryGetValue(indexerId, out var status))
        {
            lock (status)
            {
                var now = _nowProvider();
                DecayFailuresIfExpired(status, now);
                return (status.LastStatusCode == 401 || status.LastStatusCode == 403) &&
                       status.ConsecutiveFailures > 0;
            }
        }

        return false;
    }

    public bool IsRateLimited(int indexerId)
    {
        if (_statuses.TryGetValue(indexerId, out var status))
        {
            lock (status)
            {
                var now = _nowProvider();
                DecayFailuresIfExpired(status, now);
                return status.LastStatusCode == 429 &&
                       status.DisabledTill.HasValue &&
                       status.DisabledTill.Value > now;
            }
        }

        return false;
    }

    public IndexerStatus GetStatus(int indexerId)
    {
        if (_statuses.TryGetValue(indexerId, out var status))
        {
            lock (status)
            {
                var now = _nowProvider();
                DecayFailuresIfExpired(status, now);
                return new IndexerStatus
                {
                    IndexerId = status.IndexerId,
                    InitialFailure = status.InitialFailure,
                    MostRecentFailure = status.MostRecentFailure,
                    DisabledTill = status.DisabledTill,
                    ConsecutiveFailures = status.ConsecutiveFailures,
                    LastFailureMessage = status.LastFailureMessage,
                    LastStatusCode = status.LastStatusCode,
                    CurrentTime = now
                };
            }
        }

        return new IndexerStatus { IndexerId = indexerId, CurrentTime = _nowProvider() };
    }

    public IReadOnlyDictionary<int, IndexerStatus> GetAllStatuses()
    {
        var dict = new Dictionary<int, IndexerStatus>();
        var now = _nowProvider();
        foreach (var kvp in _statuses)
        {
            lock (kvp.Value)
            {
                DecayFailuresIfExpired(kvp.Value, now);
                dict[kvp.Key] = new IndexerStatus
                {
                    IndexerId = kvp.Value.IndexerId,
                    InitialFailure = kvp.Value.InitialFailure,
                    MostRecentFailure = kvp.Value.MostRecentFailure,
                    DisabledTill = kvp.Value.DisabledTill,
                    ConsecutiveFailures = kvp.Value.ConsecutiveFailures,
                    LastFailureMessage = kvp.Value.LastFailureMessage,
                    LastStatusCode = kvp.Value.LastStatusCode,
                    CurrentTime = now
                };
            }
        }

        return dict;
    }

    public void Reset(int indexerId)
    {
        if (_statuses.TryGetValue(indexerId, out var status))
        {
            lock (status)
            {
                status.ConsecutiveFailures = 0;
                status.DisabledTill = null;
                status.InitialFailure = null;
                status.LastFailureMessage = null;
                status.LastStatusCode = null;
            }
        }
    }

    public void ResetAll()
    {
        _statuses.Clear();
    }

    public TimeSpan CalculateBackoff(int consecutiveFailures, int? statusCode = null, TimeSpan? retryAfter = null, bool applyJitter = true)
    {
        if (consecutiveFailures <= 0)
        {
            return TimeSpan.Zero;
        }

        TimeSpan baseBackoff;

        if (statusCode == 401 || statusCode == 403)
        {
            baseBackoff = TimeSpan.FromHours(24);
        }
        else if (statusCode == 429)
        {
            if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
            {
                baseBackoff = retryAfter.Value;
            }
            else
            {
                var hours = Math.Min(24, Math.Pow(2, consecutiveFailures - 1));
                baseBackoff = TimeSpan.FromHours(hours);
            }
        }
        else
        {
            if (consecutiveFailures < _failureThreshold)
            {
                return TimeSpan.Zero;
            }

            var effectiveFailures = consecutiveFailures - _failureThreshold + 1;
            baseBackoff = effectiveFailures switch
            {
                1 => TimeSpan.FromMinutes(5),
                2 => TimeSpan.FromMinutes(15),
                3 => TimeSpan.FromMinutes(30),
                4 => TimeSpan.FromHours(1),
                5 => TimeSpan.FromHours(2),
                6 => TimeSpan.FromHours(4),
                7 => TimeSpan.FromHours(8),
                _ => TimeSpan.FromHours(24),
            };
        }

        if (baseBackoff <= TimeSpan.Zero || !applyJitter)
        {
            return baseBackoff;
        }

        var randomValue = _jitterRandomProvider != null ? _jitterRandomProvider() : Random.Shared.NextDouble();
        var jitterFactor = 0.85 + (randomValue * 0.30);
        return TimeSpan.FromMilliseconds(baseBackoff.TotalMilliseconds * jitterFactor);
    }

    private static void DecayFailuresIfExpired(IndexerStatus status, DateTime now)
    {
        if (status.LastStatusCode == 401 || status.LastStatusCode == 403)
        {
            return;
        }

        if (status.DisabledTill.HasValue && status.DisabledTill.Value <= now)
        {
            status.DisabledTill = null;
            if (status.ConsecutiveFailures > 0)
            {
                status.ConsecutiveFailures--;
                if (status.ConsecutiveFailures == 0)
                {
                    status.InitialFailure = null;
                    status.LastFailureMessage = null;
                    status.LastStatusCode = null;
                }
            }
        }
    }

    private static TimeSpan? TryParseRetryAfter(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var match = Regex.Match(
            message,
            @"(?:retry[-_ ]?after[:= ]*|retry in )(\d+)",
            RegexOptions.IgnoreCase);

        if (match.Success && int.TryParse(match.Groups[1].Value, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }
}
