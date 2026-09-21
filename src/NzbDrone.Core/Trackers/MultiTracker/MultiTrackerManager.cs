using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Trackers.MultiTracker;

public interface IMultiTrackerManager
{
    TrackerAnnounceResponse Announce(TrackerAnnounceRequest request, List<List<string>> announceList);
    TrackerAnnounceResponse Announce(TrackerAnnounceRequest request, List<List<string>> announceList, bool isPrivate);
    TrackerScrapeResponse Scrape(string infoHash, List<List<string>> announceList);
    TrackerScrapeResponse Scrape(TrackerEntry entry, string infoHash);
}

public class MultiTrackerManager : IMultiTrackerManager
{
    private readonly ITrackerProvider _httpTracker;
    private readonly ITrackerProvider _udpTracker;
    private readonly IConfigService _configService;
    private readonly IVpnKillSwitchService _vpnKillSwitchService;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, TrackerFailureState> _failureStates = new();
    private readonly ConcurrentDictionary<string, TrackerPerformanceState> _performanceStates = new();

    public MultiTrackerManager(
        IEnumerable<ITrackerProvider> trackerProviders,
        IConfigService configService,
        ITrackerProviderFactory trackerProviderFactory = null,
        IVpnKillSwitchService vpnKillSwitchService = null)
    {
        var providers = trackerProviders?.ToList() ?? new List<ITrackerProvider>();
        _httpTracker = providers.FirstOrDefault(p => p.Name == "HTTP");
        _udpTracker = providers.FirstOrDefault(p => p.Name == "UDP");

        if ((_httpTracker == null || _udpTracker == null) && trackerProviderFactory != null)
        {
            var factoryProviders = trackerProviderFactory.GetAvailableProviders();
            _httpTracker ??= factoryProviders.FirstOrDefault(p => p.Name == "HTTP");
            _udpTracker ??= factoryProviders.FirstOrDefault(p => p.Name == "UDP");
        }

        _configService = configService;
        _vpnKillSwitchService = vpnKillSwitchService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public static void ShuffleTier<T>(IList<T> list, Random random = null)
    {
        if (list == null || list.Count <= 1)
        {
            return;
        }

        var rng = random ?? Random.Shared;
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public static void ShuffleTiers(List<List<string>> announceList, Random random = null)
    {
        if (announceList == null)
        {
            return;
        }

        foreach (var tier in announceList)
        {
            ShuffleTier(tier, random);
        }
    }

    public TrackerAnnounceResponse Announce(TrackerAnnounceRequest request, List<List<string>> announceList)
    {
        return Announce(request, announceList, request?.IsPrivate ?? false);
    }

    public TrackerAnnounceResponse Announce(TrackerAnnounceRequest request, List<List<string>> announceList, bool isPrivate)
    {
        if (_vpnKillSwitchService?.IsFailClosedActive == true)
        {
            _logger.Debug("VPN kill switch active; deferring tracker announce for {0}", request?.InfoHash);
            return new TrackerAnnounceResponse { Success = false, FailureReason = "VPN outage: announce deferred" };
        }

        if (request != null)
        {
            request.IsPrivate = isPrivate;
        }

        if (announceList == null || announceList.Count == 0 || announceList.All(t => t == null || t.Count == 0))
        {
            return new TrackerAnnounceResponse { Success = false, FailureReason = "No trackers available" };
        }

        // BEP 12: The order of trackers within each tier should be randomized when the torrent is started.
        if (request?.Event == AnnounceEvent.Started)
        {
            ShuffleTiers(announceList);
        }

        if (!_configService.MultiTrackerEnabled)
        {
            var firstTracker = announceList.FirstOrDefault(t => t != null && t.Count > 0)?.FirstOrDefault();
            if (firstTracker == null)
            {
                return new TrackerAnnounceResponse { Success = false, FailureReason = "No trackers available" };
            }

            return AnnounceToTracker(request, firstTracker).Response;
        }

        return ExecuteTrackerOperation(
            request.InfoHash,
            announceList,
            trackerUrl => AnnounceToTracker(request, trackerUrl),
            () => new TrackerAnnounceResponse { Success = false, FailureReason = "All trackers failed" },
            true,
            isPrivate);
    }

    public TrackerScrapeResponse Scrape(TrackerEntry entry, string infoHash)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Url))
        {
            return new TrackerScrapeResponse { Success = false, FailureReason = "Invalid tracker entry" };
        }

        if (_vpnKillSwitchService?.IsFailClosedActive == true)
        {
            _logger.Debug("VPN kill switch active; deferring tracker scrape for {0}", infoHash);
            return new TrackerScrapeResponse { Success = false, FailureReason = "VPN outage: scrape deferred" };
        }

        return ScrapeTracker(infoHash, entry.Url).Response;
    }

    public TrackerScrapeResponse Scrape(string infoHash, List<List<string>> announceList)
    {
        if (_vpnKillSwitchService?.IsFailClosedActive == true)
        {
            _logger.Debug("VPN kill switch active; deferring tracker scrape for {0}", infoHash);
            return new TrackerScrapeResponse { Success = false, FailureReason = "VPN outage: scrape deferred" };
        }

        if (announceList == null || announceList.Count == 0 || announceList.All(t => t == null || t.Count == 0))
        {
            return new TrackerScrapeResponse { Success = false, FailureReason = "No trackers available" };
        }

        if (!_configService.MultiTrackerEnabled)
        {
            var firstTracker = announceList.FirstOrDefault(t => t != null && t.Count > 0)?.FirstOrDefault();
            if (firstTracker == null)
            {
                return new TrackerScrapeResponse { Success = false, FailureReason = "No trackers available" };
            }

            return ScrapeTracker(infoHash, firstTracker).Response;
        }

        return ExecuteTrackerOperation(
            infoHash,
            announceList,
            trackerUrl => ScrapeTracker(infoHash, trackerUrl),
            () => new TrackerScrapeResponse { Success = false, FailureReason = "All trackers failed" },
            false);
    }

    private TResponse ExecuteTrackerOperation<TResponse>(
        string infoHash,
        List<List<string>> announceList,
        Func<string, (TResponse Response, bool IsNetworkError)> operation,
        Func<TResponse> fallbackResponse,
        bool logBackoffSkip,
        bool isPrivate = false)
        where TResponse : class, ITrackerResponse
    {
        var announceToAllTiers = !isPrivate && _configService.AnnounceToAllTiers;
        var announceToAllInTier = !isPrivate && _configService.AnnounceToAllInTier;
        TResponse bestResponse = null;
        var failedResponses = new List<TResponse>();

        string primaryUrl = null;
        if (isPrivate)
        {
            primaryUrl = announceList.SelectMany(tier => tier).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
        }

        foreach (var tier in announceList)
        {
            var tierCopy = tier.ToList();
            var tierHasSuccess = false;

            foreach (var trackerUrl in tierCopy)
            {
                if (isPrivate && !IsAuthorizedPrivateTrackerDomain(primaryUrl, trackerUrl))
                {
                    _logger.Warn("Private torrent {0} skipping unauthorized tracker: {1}", infoHash ?? "unknown", trackerUrl);
                    continue;
                }

                if (IsTrackerBackedOff(infoHash, trackerUrl))
                {
                    if (logBackoffSkip)
                    {
                        _logger.Debug("Tracker {0} is in backoff, skipping", trackerUrl);
                    }

                    continue;
                }

                var (response, isNetworkError) = operation(trackerUrl);

                if (response != null && response.Success)
                {
                    tierHasSuccess = true;
                    ResetFailureState(infoHash, trackerUrl);

                    // BEP 12: In-Tier Promotion on Success
                    // "If a tracker in a tier succeeds, it is moved to the head of that tier for subsequent announces."
                    var currentIdx = tier.IndexOf(trackerUrl);
                    if (currentIdx > 0)
                    {
                        tier.RemoveAt(currentIdx);
                        tier.Insert(0, trackerUrl);
                    }

                    if (bestResponse == null)
                    {
                        bestResponse = response;
                        if (bestResponse is TrackerAnnounceResponse bestAnnounce)
                        {
                            foreach (var fr in failedResponses)
                            {
                                if (fr is TrackerAnnounceResponse fa && !bestAnnounce.TrackerResponses.Contains(fa))
                                {
                                    bestAnnounce.TrackerResponses.Add(fa);
                                }
                            }

                            if (!bestAnnounce.TrackerResponses.Contains(bestAnnounce))
                            {
                                bestAnnounce.TrackerResponses.Add(bestAnnounce);
                            }
                        }
                    }
                    else
                    {
                        // Deduplicate peers received across multiple tracker responses
                        if (bestResponse is TrackerAnnounceResponse targetAnnounce && response is TrackerAnnounceResponse srcAnnounce)
                        {
                            MergeAnnounceResponses(targetAnnounce, srcAnnounce);
                        }
                        else if (bestResponse is TrackerScrapeResponse targetScrape && response is TrackerScrapeResponse srcScrape)
                        {
                            targetScrape.Complete = Math.Max(targetScrape.Complete, srcScrape.Complete);
                            targetScrape.Incomplete = Math.Max(targetScrape.Incomplete, srcScrape.Incomplete);
                            targetScrape.Downloaded = Math.Max(targetScrape.Downloaded, srcScrape.Downloaded);
                        }
                    }

                    if (!announceToAllInTier)
                    {
                        break;
                    }
                }
                else
                {
                    RecordFailure(infoHash, trackerUrl, isNetworkError);
                    if (response != null)
                    {
                        if (bestResponse is TrackerAnnounceResponse bestAnnounce && response is TrackerAnnounceResponse srcAnnounce)
                        {
                            if (!bestAnnounce.TrackerResponses.Contains(srcAnnounce))
                            {
                                bestAnnounce.TrackerResponses.Add(srcAnnounce);
                            }
                        }
                        else
                        {
                            failedResponses.Add(response);
                        }
                    }
                }
            }

            if (tierHasSuccess && !announceToAllTiers)
            {
                return bestResponse;
            }
        }

        if (bestResponse != null)
        {
            return bestResponse;
        }

        var fallback = fallbackResponse();
        if (fallback is TrackerAnnounceResponse fbAnnounce)
        {
            foreach (var fr in failedResponses)
            {
                if (fr is TrackerAnnounceResponse fa && !fbAnnounce.TrackerResponses.Contains(fa))
                {
                    fbAnnounce.TrackerResponses.Add(fa);
                }
            }
        }

        return fallback;
    }

    private static void MergeAnnounceResponses(TrackerAnnounceResponse target, TrackerAnnounceResponse source)
    {
        if (target == null || source == null)
        {
            return;
        }

        if (!target.TrackerResponses.Contains(source))
        {
            target.TrackerResponses.Add(source);
        }

        target.Complete = Math.Max(target.Complete, source.Complete);
        target.Incomplete = Math.Max(target.Incomplete, source.Incomplete);

        if (source.Interval > 0 && (target.Interval <= 0 || source.Interval < target.Interval))
        {
            target.Interval = source.Interval;
        }

        if (source.MinInterval > 0 && (target.MinInterval <= 0 || source.MinInterval < target.MinInterval))
        {
            target.MinInterval = source.MinInterval;
        }

        if (source.Peers != null && source.Peers.Count > 0)
        {
            target.Peers ??= new List<TrackerPeer>();
            var existingKeys = new HashSet<string>(
                target.Peers.Select(p => $"{p.Ip}:{p.Port}"),
                StringComparer.OrdinalIgnoreCase);

            foreach (var peer in source.Peers)
            {
                if (peer != null && existingKeys.Add($"{peer.Ip}:{peer.Port}"))
                {
                    target.Peers.Add(peer);
                }
            }
        }
    }

    private (TrackerAnnounceResponse Response, bool IsNetworkError) AnnounceToTracker(TrackerAnnounceRequest request, string trackerUrl)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var trackerRequest = request.Clone(trackerUrl);
            var provider = GetProvider(trackerUrl);
            if (provider == null)
            {
                return (new TrackerAnnounceResponse { Success = false, FailureReason = "Unknown tracker protocol", TrackerUrl = trackerUrl }, true);
            }

            var response = provider.Announce(trackerRequest);
            sw.Stop();
            var responseTimeMs = sw.ElapsedMilliseconds;

            if (response != null)
            {
                response.TrackerUrl ??= trackerUrl;
                response.ResponseTimeMs = responseTimeMs;
                RecordResponseTime(request.InfoHash, trackerUrl, responseTimeMs);
            }

            if (response != null && !response.Success)
            {
                _logger.Warn("Tracker {0} failed: {1}", trackerUrl, response.FailureReason);
            }

            var isNetwork = IsNetworkError(response?.FailureReason);
            return (response, isNetwork);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.Warn(ex, "Tracker {0} error", trackerUrl);
            return (new TrackerAnnounceResponse { Success = false, FailureReason = ex.Message, TrackerUrl = trackerUrl, ResponseTimeMs = sw.ElapsedMilliseconds }, true);
        }
    }

    private (TrackerScrapeResponse Response, bool IsNetworkError) ScrapeTracker(string infoHash, string trackerUrl)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var provider = GetProvider(trackerUrl);
            if (provider == null)
            {
                return (new TrackerScrapeResponse { Success = false, FailureReason = "Unknown tracker protocol" }, true);
            }

            var response = provider.Scrape(infoHash, trackerUrl);
            sw.Stop();

            if (response != null)
            {
                RecordResponseTime(infoHash, trackerUrl, sw.ElapsedMilliseconds);
            }

            var isNetwork = IsNetworkError(response?.FailureReason);
            return (response, isNetwork);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.Warn(ex, "Scrape {0} error", trackerUrl);
            return (new TrackerScrapeResponse { Success = false, FailureReason = ex.Message }, true);
        }
    }

    private bool IsTrackerBackedOff(string trackerUrl)
    {
        return IsTrackerBackedOff(null, trackerUrl);
    }

    private bool IsTrackerBackedOff(string infoHash, string trackerUrl)
    {
        if (!_configService.MultiTrackerFailoverEnabled)
        {
            return false;
        }

        if (IsKeyBackedOff(trackerUrl))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(infoHash))
        {
            var torrentKey = GetTorrentKey(infoHash, trackerUrl);
            return IsKeyBackedOff(torrentKey);
        }

        return false;
    }

    private bool IsKeyBackedOff(string key)
    {
        if (!_failureStates.TryGetValue(key, out var state))
        {
            return false;
        }

        var maxFailures = _configService.FailoverMaxConsecutiveFailures > 0
            ? _configService.FailoverMaxConsecutiveFailures
            : 5;

        if (state.IsDisabled)
        {
            return true;
        }

        if (Volatile.Read(ref state.ConsecutiveFailures) < maxFailures)
        {
            return false;
        }

        return DateTime.UtcNow < state.BackoffUntil;
    }

    public bool IsTrackerDisabled(string trackerUrl)
    {
        return IsTrackerDisabled(null, trackerUrl);
    }

    public bool IsTrackerDisabled(string infoHash, string trackerUrl)
    {
        if (IsKeyDisabled(trackerUrl))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(infoHash))
        {
            return IsKeyDisabled(GetTorrentKey(infoHash, trackerUrl));
        }

        return false;
    }

    private bool IsKeyDisabled(string key)
    {
        if (!_failureStates.TryGetValue(key, out var state))
        {
            return false;
        }

        var maxFailures = _configService.FailoverMaxConsecutiveFailures > 0
            ? _configService.FailoverMaxConsecutiveFailures
            : 5;

        return state.IsDisabled || Volatile.Read(ref state.ConsecutiveFailures) >= maxFailures;
    }

    public double GetAverageResponseTime(string trackerUrl)
    {
        return _performanceStates.TryGetValue(trackerUrl, out var state) ? state.AverageResponseTimeMs : 0.0;
    }

    public double GetLastResponseTime(string trackerUrl)
    {
        return _performanceStates.TryGetValue(trackerUrl, out var state) ? state.LastResponseTimeMs : 0.0;
    }

    public static double CalculateResponseTimeEma(double responseTimeMs, double currentEma, double alpha = 0.2)
    {
        if (currentEma <= 0)
        {
            return responseTimeMs;
        }

        return (alpha * responseTimeMs) + ((1.0 - alpha) * currentEma);
    }

    private void RecordResponseTime(string infoHash, string trackerUrl, double responseTimeMs)
    {
        var state = _performanceStates.GetOrAdd(trackerUrl, _ => new TrackerPerformanceState());
        state.RecordResponseTime(responseTimeMs);

        if (!string.IsNullOrWhiteSpace(infoHash))
        {
            var torrentKey = GetTorrentKey(infoHash, trackerUrl);
            var torrentState = _performanceStates.GetOrAdd(torrentKey, _ => new TrackerPerformanceState());
            torrentState.RecordResponseTime(responseTimeMs);
        }
    }

    private void RecordFailure(string trackerUrl)
    {
        RecordFailure(null, trackerUrl, isNetworkError: true);
    }

    private void RecordFailure(string infoHash, string trackerUrl, bool isNetworkError = false)
    {
        if (!_configService.MultiTrackerFailoverEnabled)
        {
            return;
        }

        var key = (isNetworkError || string.IsNullOrWhiteSpace(infoHash))
            ? trackerUrl
            : GetTorrentKey(infoHash, trackerUrl);

        var state = _failureStates.GetOrAdd(key, _ => new TrackerFailureState());
        var failures = Interlocked.Increment(ref state.ConsecutiveFailures);

        var baseSeconds = _configService.FailoverBackoffBaseSeconds > 0
            ? _configService.FailoverBackoffBaseSeconds
            : 60;
        var maxBackoffSeconds = _configService.FailoverMaxBackoffSeconds > 0
            ? _configService.FailoverMaxBackoffSeconds
            : 3600;
        var maxFailures = _configService.FailoverMaxConsecutiveFailures > 0
            ? _configService.FailoverMaxConsecutiveFailures
            : 5;

        // Exponential backoff per tracker on failure (base 60s, max 3600s)
        var exponent = Math.Min(Math.Max(0, failures - 1), 10);
        var backoffSeconds = Math.Min(baseSeconds * Math.Pow(2, exponent), maxBackoffSeconds);
        state.BackoffUntil = DateTime.UtcNow.AddSeconds(backoffSeconds);

        // Auto-disable after 5 consecutive failures
        if (failures >= maxFailures)
        {
            state.IsDisabled = true;
            _logger.Warn(
                "Tracker {0} auto-disabled after {1} consecutive failures (key: {2})",
                trackerUrl,
                failures,
                key);
        }
        else
        {
            _logger.Warn(
                "Tracker {0} backed off for {1:F0}s after failure #{2} (key: {3})",
                trackerUrl,
                backoffSeconds,
                failures,
                key);
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
            .Where(kvp => !kvp.Value.IsDisabled && kvp.Value.BackoffUntil != DateTime.MinValue && kvp.Value.BackoffUntil < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in staleKeys)
        {
            _failureStates.TryRemove(key, out _);
        }
    }

    private void ResetFailureState(string trackerUrl)
    {
        ResetFailureState(null, trackerUrl);
    }

    private void ResetFailureState(string infoHash, string trackerUrl)
    {
        _failureStates.TryRemove(trackerUrl, out _);

        if (!string.IsNullOrWhiteSpace(infoHash))
        {
            _failureStates.TryRemove(GetTorrentKey(infoHash, trackerUrl), out _);
        }
    }

    private static string GetTorrentKey(string infoHash, string trackerUrl)
    {
        return $"{infoHash.ToLowerInvariant()}@{trackerUrl}";
    }

    private static bool IsNetworkError(string failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            return true;
        }

        var lower = failureReason.ToLowerInvariant();

        if (lower.Contains("torrent") ||
            lower.Contains("hash") ||
            lower.Contains("passkey") ||
            lower.Contains("unregistered") ||
            lower.Contains("not registered"))
        {
            return false;
        }

        if (lower.Contains("timeout") ||
            lower.Contains("timed out") ||
            lower.Contains("connection") ||
            lower.Contains("socket") ||
            lower.Contains("unreachable") ||
            lower.Contains("refused") ||
            lower.Contains("reset") ||
            lower.Contains("dns") ||
            lower.Contains("host") ||
            lower.Contains("network") ||
            lower.Contains("gateway") ||
            lower.Contains("unavailable") ||
            lower.Contains("protocol") ||
            lower == "error")
        {
            return true;
        }

        return false;
    }

    public static bool IsAuthorizedPrivateTrackerDomain(string primaryUrl, string trackerUrl)
    {
        if (string.IsNullOrWhiteSpace(primaryUrl) || string.IsNullOrWhiteSpace(trackerUrl))
        {
            return false;
        }

        if (string.Equals(primaryUrl, trackerUrl, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(primaryUrl, UriKind.Absolute, out var primaryUri) ||
            !Uri.TryCreate(trackerUrl, UriKind.Absolute, out var trackerUri))
        {
            return false;
        }

        var primaryHost = primaryUri.Host.ToLowerInvariant();
        var trackerHost = trackerUri.Host.ToLowerInvariant();

        if (primaryHost == trackerHost)
        {
            return true;
        }

        var primaryDomain = GetRootDomain(primaryHost);
        var trackerDomain = GetRootDomain(trackerHost);

        return !string.IsNullOrEmpty(primaryDomain) &&
            string.Equals(primaryDomain, trackerDomain, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRootDomain(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        var parts = host.Split('.');
        if (parts.Length >= 2)
        {
            return $"{parts[^2]}.{parts[^1]}";
        }

        return host;
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
        public int ConsecutiveFailures;
        public bool IsDisabled;
        private long _backoffUntilTicks;

        public DateTime BackoffUntil
        {
            get => new DateTime(Interlocked.Read(ref _backoffUntilTicks));
            set => Interlocked.Exchange(ref _backoffUntilTicks, value.Ticks);
        }
    }

    private class TrackerPerformanceState
    {
        public double AverageResponseTimeMs;
        public double LastResponseTimeMs;

        public void RecordResponseTime(double responseTimeMs, double alpha = 0.2)
        {
            LastResponseTimeMs = responseTimeMs;
            if (AverageResponseTimeMs <= 0)
            {
                AverageResponseTimeMs = responseTimeMs;
            }
            else
            {
                AverageResponseTimeMs = (alpha * responseTimeMs) + ((1.0 - alpha) * AverageResponseTimeMs);
            }
        }
    }
}
