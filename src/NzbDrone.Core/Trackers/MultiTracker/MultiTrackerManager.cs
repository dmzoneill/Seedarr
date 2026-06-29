using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Trackers.MultiTracker;

public interface IMultiTrackerManager
{
    TrackerAnnounceResponse Announce(TrackerAnnounceRequest request, List<List<string>> announceList);
    TrackerScrapeResponse Scrape(string infoHash, List<List<string>> announceList);
}

public class MultiTrackerManager : IMultiTrackerManager
{
    private readonly ITrackerProvider _httpTracker;
    private readonly ITrackerProvider _udpTracker;
    private readonly IConfigService _configService;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, TrackerFailureState> _failureStates = new();

    public MultiTrackerManager(
        IEnumerable<ITrackerProvider> trackerProviders,
        IConfigService configService)
    {
        var providers = trackerProviders.ToList();
        _httpTracker = providers.FirstOrDefault(p => p.Name == "HTTP");
        _udpTracker = providers.FirstOrDefault(p => p.Name == "UDP");
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public TrackerAnnounceResponse Announce(TrackerAnnounceRequest request, List<List<string>> announceList)
    {
        if (!_configService.MultiTrackerEnabled)
        {
            var firstTracker = announceList.FirstOrDefault()?.FirstOrDefault();
            if (firstTracker == null)
            {
                return new TrackerAnnounceResponse { Success = false, FailureReason = "No trackers available" };
            }

            return AnnounceToTracker(request, firstTracker);
        }

        return ExecuteTrackerOperation(
            announceList,
            trackerUrl => AnnounceToTracker(request, trackerUrl),
            () => new TrackerAnnounceResponse { Success = false, FailureReason = "All trackers failed" },
            true);
    }

    public TrackerScrapeResponse Scrape(string infoHash, List<List<string>> announceList)
    {
        if (!_configService.MultiTrackerEnabled)
        {
            var firstTracker = announceList.FirstOrDefault()?.FirstOrDefault();
            if (firstTracker == null)
            {
                return new TrackerScrapeResponse { Success = false, FailureReason = "No trackers available" };
            }

            return ScrapeTracker(infoHash, firstTracker);
        }

        return ExecuteTrackerOperation(
            announceList,
            trackerUrl => ScrapeTracker(infoHash, trackerUrl),
            () => new TrackerScrapeResponse { Success = false, FailureReason = "All trackers failed" },
            false);
    }

    private TResponse ExecuteTrackerOperation<TResponse>(
        List<List<string>> announceList,
        Func<string, TResponse> operation,
        Func<TResponse> fallbackResponse,
        bool logBackoffSkip)
        where TResponse : class
    {
        var announceToAllTiers = _configService.AnnounceToAllTiers;
        var announceToAllInTier = _configService.AnnounceToAllInTier;
        TResponse bestResponse = null;

        foreach (var tier in announceList)
        {
            foreach (var trackerUrl in tier)
            {
                if (IsTrackerBackedOff(trackerUrl))
                {
                    if (logBackoffSkip)
                    {
                        _logger.Debug("Tracker {0} is in backoff, skipping", trackerUrl);
                    }

                    continue;
                }

                var response = operation(trackerUrl);
                dynamic dynResponse = response;

                if (dynResponse.Success)
                {
                    ResetFailureState(trackerUrl);

                    if (bestResponse == null)
                    {
                        bestResponse = response;
                    }

                    if (!announceToAllInTier)
                    {
                        break;
                    }
                }
                else
                {
                    RecordFailure(trackerUrl);
                }
            }

            if (bestResponse != null && !announceToAllTiers)
            {
                return bestResponse;
            }
        }

        return bestResponse ?? fallbackResponse();
    }

    private TrackerAnnounceResponse AnnounceToTracker(TrackerAnnounceRequest request, string trackerUrl)
    {
        try
        {
            request.TrackerUrl = trackerUrl;
            var provider = GetProvider(trackerUrl);
            if (provider == null)
            {
                return new TrackerAnnounceResponse { Success = false, FailureReason = "Unknown tracker protocol" };
            }

            var response = provider.Announce(request);
            if (!response.Success)
            {
                _logger.Warn("Tracker {0} failed: {1}", trackerUrl, response.FailureReason);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Tracker {0} error", trackerUrl);
            return new TrackerAnnounceResponse { Success = false, FailureReason = ex.Message };
        }
    }

    private TrackerScrapeResponse ScrapeTracker(string infoHash, string trackerUrl)
    {
        try
        {
            var provider = GetProvider(trackerUrl);
            if (provider == null)
            {
                return new TrackerScrapeResponse { Success = false, FailureReason = "Unknown tracker protocol" };
            }

            return provider.Scrape(infoHash, trackerUrl);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Scrape {0} error", trackerUrl);
            return new TrackerScrapeResponse { Success = false, FailureReason = ex.Message };
        }
    }

    private bool IsTrackerBackedOff(string trackerUrl)
    {
        if (!_configService.MultiTrackerFailoverEnabled)
        {
            return false;
        }

        if (!_failureStates.TryGetValue(trackerUrl, out var state))
        {
            return false;
        }

        if (state.ConsecutiveFailures < _configService.FailoverMaxConsecutiveFailures)
        {
            return false;
        }

        return DateTime.UtcNow < state.BackoffUntil;
    }

    private void RecordFailure(string trackerUrl)
    {
        if (!_configService.MultiTrackerFailoverEnabled)
        {
            return;
        }

        var state = _failureStates.GetOrAdd(trackerUrl, _ => new TrackerFailureState());
        state.ConsecutiveFailures++;

        var maxFailures = _configService.FailoverMaxConsecutiveFailures;
        if (state.ConsecutiveFailures >= maxFailures)
        {
            var baseSeconds = _configService.FailoverBackoffBaseSeconds;
            var maxBackoffSeconds = _configService.FailoverMaxBackoffSeconds;
            var exponent = Math.Min(state.ConsecutiveFailures - maxFailures, 10);
            var backoffSeconds = Math.Min(baseSeconds * Math.Pow(2, exponent), maxBackoffSeconds);
            state.BackoffUntil = DateTime.UtcNow.AddSeconds(backoffSeconds);
            _logger.Warn(
                "Tracker {0} disabled for {1:F0}s after {2} consecutive failures",
                trackerUrl,
                backoffSeconds,
                state.ConsecutiveFailures);
        }

        if (_failureStates.Count > 1000)
        {
            PurgeExpiredFailureStates();
        }
    }

    private void PurgeExpiredFailureStates()
    {
        var now = DateTime.UtcNow;
        var staleKeys = _failureStates
            .Where(kvp => kvp.Value.ConsecutiveFailures == 0 || kvp.Value.BackoffUntil < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in staleKeys)
        {
            _failureStates.TryRemove(key, out _);
        }
    }

    private void ResetFailureState(string trackerUrl)
    {
        _failureStates.TryRemove(trackerUrl, out _);
    }

    private ITrackerProvider GetProvider(string url)
    {
        if (url.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
        {
            return _udpTracker;
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return _httpTracker;
        }

        _logger.Warn("Unknown tracker protocol: {0}", url);
        return null;
    }

    private class TrackerFailureState
    {
        public int ConsecutiveFailures { get; set; }
        public DateTime BackoffUntil { get; set; }
    }
}
