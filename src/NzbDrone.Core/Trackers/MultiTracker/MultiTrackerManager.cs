using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        if (!_configService.MultiTrackerEnabled)
        {
            var firstTracker = announceList.FirstOrDefault()?.FirstOrDefault();
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

        if (!_configService.MultiTrackerEnabled)
        {
            var firstTracker = announceList.FirstOrDefault()?.FirstOrDefault();
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

        string primaryUrl = null;
        if (isPrivate)
        {
            primaryUrl = announceList.SelectMany(tier => tier).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
        }

        foreach (var tier in announceList)
        {
            foreach (var trackerUrl in tier)
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
                    ResetFailureState(infoHash, trackerUrl);

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
                    RecordFailure(infoHash, trackerUrl, isNetworkError);
                }
            }

            if (bestResponse != null && !announceToAllTiers)
            {
                return bestResponse;
            }
        }

        return bestResponse ?? fallbackResponse();
    }

    private (TrackerAnnounceResponse Response, bool IsNetworkError) AnnounceToTracker(TrackerAnnounceRequest request, string trackerUrl)
    {
        try
        {
            var trackerRequest = request.Clone(trackerUrl);
            var provider = GetProvider(trackerUrl);
            if (provider == null)
            {
                return (new TrackerAnnounceResponse { Success = false, FailureReason = "Unknown tracker protocol" }, true);
            }

            var response = provider.Announce(trackerRequest);
            if (!response.Success)
            {
                _logger.Warn("Tracker {0} failed: {1}", trackerUrl, response.FailureReason);
            }

            var isNetwork = IsNetworkError(response?.FailureReason);
            return (response, isNetwork);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Tracker {0} error", trackerUrl);
            return (new TrackerAnnounceResponse { Success = false, FailureReason = ex.Message }, true);
        }
    }

    private (TrackerScrapeResponse Response, bool IsNetworkError) ScrapeTracker(string infoHash, string trackerUrl)
    {
        try
        {
            var provider = GetProvider(trackerUrl);
            if (provider == null)
            {
                return (new TrackerScrapeResponse { Success = false, FailureReason = "Unknown tracker protocol" }, true);
            }

            var response = provider.Scrape(infoHash, trackerUrl);
            var isNetwork = IsNetworkError(response?.FailureReason);
            return (response, isNetwork);
        }
        catch (Exception ex)
        {
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

        if (Volatile.Read(ref state.ConsecutiveFailures) < _configService.FailoverMaxConsecutiveFailures)
        {
            return false;
        }

        return DateTime.UtcNow < state.BackoffUntil;
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

        var maxFailures = _configService.FailoverMaxConsecutiveFailures;
        if (failures >= maxFailures)
        {
            var baseSeconds = _configService.FailoverBackoffBaseSeconds;
            var maxBackoffSeconds = _configService.FailoverMaxBackoffSeconds;
            var exponent = Math.Min(failures - maxFailures, 10);
            var backoffSeconds = Math.Min(baseSeconds * Math.Pow(2, exponent), maxBackoffSeconds);
            state.BackoffUntil = DateTime.UtcNow.AddSeconds(backoffSeconds);
            _logger.Warn(
                "Tracker {0} disabled for {1:F0}s after {2} consecutive failures (key: {3})",
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
            .Where(kvp => kvp.Value.BackoffUntil != DateTime.MinValue && kvp.Value.BackoffUntil < now)
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
        private long _backoffUntilTicks;

        public DateTime BackoffUntil
        {
            get => new DateTime(Interlocked.Read(ref _backoffUntilTicks));
            set => Interlocked.Exchange(ref _backoffUntilTicks, value.Ticks);
        }
    }
}
