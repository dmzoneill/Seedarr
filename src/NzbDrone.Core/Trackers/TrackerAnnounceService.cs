using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Simulation.ClientBehavior.Profiles;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers.Metrics;
using NzbDrone.Core.Trackers.MultiTracker;

namespace NzbDrone.Core.Trackers;

public interface ITrackerAnnounceService
{
    List<TrackerAnnounceResult> AnnounceTorrent(Torrent torrent, bool force = false, AnnounceEvent eventType = AnnounceEvent.None);
    TrackerAnnounceResult AnnounceTracker(Torrent torrent, TrackerEntry entry, bool force = false, AnnounceEvent eventType = AnnounceEvent.None);
    TrackerScrapeResponse ScrapeTorrent(Torrent torrent);
}

public class TrackerAnnounceResult
{
    public int TrackerId { get; set; }
    public string Url { get; set; }
    public bool Success { get; set; }
    public int Seeders { get; set; }
    public int Leechers { get; set; }
    public int PeersDiscovered { get; set; }
    public int AnnounceInterval { get; set; }
    public long ResponseTimeMs { get; set; }
    public string FailureReason { get; set; }
    public string WarningMessage { get; set; }
    public long LastAnnouncedUploaded { get; set; }
    public int RetryAfterSeconds { get; set; }
}

public class TrackerAnnounceService : ITrackerAnnounceService,
    IHandle<SeedingStoppedEvent>,
    IHandle<TorrentStatusChangedEvent>,
    IHandle<TorrentPausedEvent>,
    IHandle<TorrentDeletedEvent>,
    IHandle<TorrentFinishedEvent>,
    IHandle<TorrentDownloadCompletedEvent>,
    IHandle<TorrentHashCheckCompletedEvent>,
    IHandle<ApplicationStartedEvent>,
    IHandle<VpnKillSwitchTriggeredEvent>,
    IHandle<VpnInterfaceRestoredEvent>,
    IHandle<VpnRestoredEvent>,
    IDisposable
{
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly IMultiTrackerManager _multiTracker;
    private readonly IPeerDiscoveryService _peerDiscovery;
    private readonly ITorrentEventLogService _eventLogService;
    private readonly IConfigService _configService;
    private readonly ITrackerMetricService _trackerMetricService;
    private readonly IEventAggregator _eventAggregator;
    private readonly ITorrentService _torrentService;
    private readonly IClientBehaviorSimulator _clientBehaviorSimulator;
    private readonly IVpnKillSwitchService _vpnKillSwitchService;
    private readonly IClientProfileFactory _clientProfileFactory;
    private readonly ConcurrentDictionary<int, bool> _completedTorrents = new();
    private readonly ConcurrentDictionary<int, List<List<string>>> _torrentTierOrder = new();
    private readonly ConcurrentQueue<Torrent> _staggeredQueue = new();
    private readonly object _staggeredLock = new();
    private readonly Logger _logger;
    private CancellationTokenSource _staggeredCts;
    private DateTime _lastVpnRestore = DateTime.MinValue;
    private bool _isVpnDown;

    public int PendingStaggeredAnnounces => _staggeredQueue.Count;
    public bool IsStaggeredAnnounceScheduled => _staggeredCts != null && !_staggeredCts.IsCancellationRequested;
    internal Func<TimeSpan, CancellationToken, Task> DelayAsync = Task.Delay;
    internal Func<double, int, double> JitterCalculator { get; set; } = (interval, minInterval) => CalculateJitteredInterval(interval, minInterval);

    public TrackerAnnounceService(
        ITrackerEntryService trackerEntryService,
        IMultiTrackerManager multiTracker,
        IPeerDiscoveryService peerDiscovery,
        ITorrentEventLogService eventLogService,
        IConfigService configService,
        ITrackerMetricService trackerMetricService = null,
        IEventAggregator eventAggregator = null,
        ITorrentService torrentService = null,
        IClientBehaviorSimulator clientBehaviorSimulator = null,
        IVpnKillSwitchService vpnKillSwitchService = null,
        IClientProfileFactory clientProfileFactory = null)
    {
        _trackerEntryService = trackerEntryService;
        _multiTracker = multiTracker;
        _peerDiscovery = peerDiscovery;
        _eventLogService = eventLogService;
        _configService = configService;
        _trackerMetricService = trackerMetricService;
        _eventAggregator = eventAggregator;
        _torrentService = torrentService;
        _clientBehaviorSimulator = clientBehaviorSimulator;
        _vpnKillSwitchService = vpnKillSwitchService;
        _clientProfileFactory = clientProfileFactory;
        _logger = LogManager.GetCurrentClassLogger();

        if (_torrentService != null)
        {
            try
            {
                var existingTorrents = _torrentService.GetAll();
                if (existingTorrents != null)
                {
                    foreach (var t in existingTorrents)
                    {
                        if (t.Progress >= 1.0 || t.Status == TorrentStatus.Seeding || (t.TotalSize > 0 && t.Downloaded >= t.TotalSize))
                        {
                            _completedTorrents.TryAdd(t.Id, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to load initial completed torrents");
            }
        }
    }

    private static bool IsRateLimited(TrackerEntry entry, bool isFirstAnnounce, out int minIntervalSeconds, out int retryAfter)
    {
        if (IsLoopbackOrMockTracker(entry?.Url))
        {
            minIntervalSeconds = 0;
            retryAfter = 0;
            return false;
        }

        minIntervalSeconds = entry.MinAnnounceInterval > 0
            ? entry.MinAnnounceInterval
            : Math.Min(entry.AnnounceInterval > 0 ? entry.AnnounceInterval / 2 : 60, 60);

        var earliestAllowed = (entry.LastAnnounce ?? DateTime.MinValue).AddSeconds(minIntervalSeconds);
        if (!isFirstAnnounce && DateTime.UtcNow < earliestAllowed)
        {
            retryAfter = Math.Max(1, (int)Math.Ceiling((earliestAllowed - DateTime.UtcNow).TotalSeconds));
            return true;
        }

        retryAfter = 0;
        return false;
    }

    public List<TrackerAnnounceResult> AnnounceTorrent(Torrent torrent, bool force = false, AnnounceEvent eventType = AnnounceEvent.None)
    {
        var results = new List<TrackerAnnounceResult>();
        if (torrent == null || string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            return results;
        }

        if (_vpnKillSwitchService?.IsFailClosedActive == true)
        {
            _logger.Debug("VPN kill switch fail-closed is active; halting tracker announce for torrent {0}.", torrent.Name ?? torrent.InfoHash);
            return results;
        }

        if (!force && torrent.Status == TorrentStatus.Paused && eventType != AnnounceEvent.Stopped)
        {
            return results;
        }

        if (IsVpnBlocked(torrent))
        {
            _logger.Debug("VPN is down or torrent {0} is VPN-paused; deferring tracker announce.", torrent.Name ?? torrent.InfoHash);
            return results;
        }

        var trackerEntries = _trackerEntryService.GetByTorrentId(torrent.Id);
        if (trackerEntries.Count == 0 && !string.IsNullOrEmpty(torrent.TrackerUrl))
        {
            var entry = new TrackerEntry
            {
                TorrentId = torrent.Id,
                Url = torrent.TrackerUrl,
                Tier = 1,
                Status = TrackerStatus.Unknown,
                Enabled = true
            };
            if (eventType != AnnounceEvent.Stopped)
            {
                entry = _trackerEntryService.Add(entry);
            }

            trackerEntries = new List<TrackerEntry> { entry };
        }

        var enabledTrackers = trackerEntries.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.Url)).ToList();
        if (enabledTrackers.Count == 0)
        {
            return results;
        }

        if (torrent.IsPrivate)
        {
            enabledTrackers = enabledTrackers.OrderBy(t => t.Tier).ToList();
        }

        var primaryUrl = torrent.IsPrivate ? enabledTrackers.FirstOrDefault()?.Url : null;

        if (torrent.IsPrivate)
        {
            enabledTrackers = enabledTrackers
                .Where(entry =>
                {
                    if (!MultiTrackerManager.IsAuthorizedPrivateTrackerDomain(primaryUrl, entry.Url))
                    {
                        _logger.Warn("Private torrent {0} skipping unauthorized tracker: {1}", torrent.Name ?? torrent.InfoHash, entry.Url);
                        return false;
                    }
                    return true;
                })
                .ToList();

            if (enabledTrackers.Count == 0)
            {
                return results;
            }
        }

        var candidateTrackers = new List<TrackerEntry>();
        foreach (var entry in enabledTrackers)
        {
            var isFirst = entry.TotalAnnounces == 0 || !entry.LastAnnounce.HasValue;
            if (!force && !isFirst && entry.NextAnnounce.HasValue && entry.NextAnnounce.Value > DateTime.UtcNow)
            {
                continue;
            }

            if (IsRateLimited(entry, isFirst, out var minIntervalSeconds, out var retryAfter))
            {
                results.Add(new TrackerAnnounceResult
                {
                    TrackerId = entry.Id,
                    Url = entry.Url,
                    Success = false,
                    FailureReason = $"Rate limited: minimum announce interval of {minIntervalSeconds}s not elapsed (retry in {retryAfter}s)",
                    RetryAfterSeconds = retryAfter
                });
                continue;
            }

            candidateTrackers.Add(entry);
        }

        if (candidateTrackers.Count == 0)
        {
            return results;
        }

        // BEP 12: Group candidate trackers by Tier into tiered structure
        var tieredEntries = candidateTrackers
            .GroupBy(t => t.Tier)
            .OrderBy(g => g.Key)
            .Select(g => g.ToList())
            .ToList();

        var isStopped = torrent.Status == TorrentStatus.Stopped || torrent.Status == TorrentStatus.Paused || eventType == AnnounceEvent.Stopped;
        var isFirstAnnounceOverall = candidateTrackers.All(t => t.TotalAnnounces == 0 || !t.LastAnnounce.HasValue);

        AnnounceEvent announceEvent;
        if (eventType != AnnounceEvent.None)
        {
            announceEvent = eventType;
        }
        else if (isStopped)
        {
            announceEvent = AnnounceEvent.Stopped;
        }
        else if (isFirstAnnounceOverall)
        {
            announceEvent = AnnounceEvent.Started;
        }
        else
        {
            announceEvent = AnnounceEvent.None;
        }

        if (!_torrentTierOrder.ContainsKey(torrent.Id))
        {
            _torrentTierOrder[torrent.Id] = tieredEntries
                .Select(tier => tier.Select(e => e.Url).ToList())
                .ToList();
        }
        else if (_torrentTierOrder.TryGetValue(torrent.Id, out var cachedTiers))
        {
            for (var i = 0; i < tieredEntries.Count; i++)
            {
                if (i < cachedTiers.Count)
                {
                    var cachedUrls = cachedTiers[i];
                    tieredEntries[i] = tieredEntries[i]
                        .OrderBy(e =>
                        {
                            var idx = cachedUrls.IndexOf(e.Url);
                            return idx >= 0 ? idx : int.MaxValue;
                        })
                        .ToList();
                }
            }
        }

        var announceList = tieredEntries
            .Select(tier => tier.Select(e => e.Url).ToList())
            .ToList();

        var left = announceEvent == AnnounceEvent.Completed ? 0 : Math.Max(0, torrent.TotalSize - torrent.Downloaded);
        var eventName = announceEvent != AnnounceEvent.None ? announceEvent.ToString().ToLowerInvariant() : "regular";
        var primaryTrackerUrl = primaryUrl ?? announceList.FirstOrDefault()?.FirstOrDefault();

        var isSimulated = torrent.IsSimulated || (_configService != null && _configService.SimulationModeEnabled);
        var isLoopback = IsLoopbackOrMockTracker(primaryTrackerUrl);

        long effectiveUploaded;
        if (isSimulated && !isLoopback)
        {
            effectiveUploaded = torrent.RealUploaded;
            if (torrent.Uploaded > torrent.RealUploaded)
            {
                _logger.Debug(
                    "Simulated torrent {0}: suppressing synthetic upload bytes ({1:N0} bytes) from external tracker {2}; reporting genuine wire bytes ({3:N0} bytes)",
                    torrent.Name ?? torrent.InfoHash,
                    torrent.Uploaded,
                    primaryTrackerUrl,
                    effectiveUploaded);
            }
        }
        else
        {
            effectiveUploaded = torrent.Uploaded;
        }

        var maxAnnouncedUploaded = candidateTrackers.Select(e => e.LastAnnouncedUploaded).DefaultIfEmpty(0).Max();
        var uploadedBytes = Math.Max(effectiveUploaded, maxAnnouncedUploaded);

        IClientProfile profile = null;
        TorrentClientSession session = null;

        if (!string.IsNullOrWhiteSpace(torrent.ClientProfile))
        {
            profile = ResolveProfileByName(torrent.ClientProfile);
        }

        if (profile == null && _clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled && !_configService.AnonymousMode)
        {
            session = _clientBehaviorSimulator.GetOrCreateSession(torrent.InfoHash, torrent.IsPrivate);
            profile = session?.Profile ?? _clientBehaviorSimulator.GetProfileForTorrent(torrent.InfoHash, torrent.IsPrivate);
        }

        if (profile == null && !string.IsNullOrWhiteSpace(_configService.BitTorrentUserAgent))
        {
            profile = DetectProfileFromUserAgent(_configService.BitTorrentUserAgent);
        }

        if (profile == null && _clientBehaviorSimulator != null && !_configService.AnonymousMode)
        {
            session = _clientBehaviorSimulator.GetOrCreateSession(torrent.InfoHash, torrent.IsPrivate);
            profile = session?.Profile ?? _clientBehaviorSimulator.GetProfileForTorrent(torrent.InfoHash, torrent.IsPrivate);
        }

        if (profile == null)
        {
            profile = ResolveProfileByName(_configService.PrimaryClient) ?? new QBittorrentProfile();
        }

        var peerId = session?.PeerId ?? profile.GeneratePeerId();
        var userAgent = session?.Profile?.UserAgent ?? profile.UserAgent ?? (!string.IsNullOrWhiteSpace(_configService.BitTorrentUserAgent) ? _configService.BitTorrentUserAgent : "qBittorrent/4.4.2");
        var announceKey = session?.AnnounceKey ?? RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue).ToString("X8", CultureInfo.InvariantCulture);

        var request = new TrackerAnnounceRequest
        {
            InfoHash = torrent.InfoHash,
            PeerId = peerId,
            UserAgent = userAgent,
            Key = announceKey,
            Port = _configService.ListeningPort,
            Uploaded = uploadedBytes,
            Downloaded = torrent.Downloaded,
            Left = left,
            Event = announceEvent,
            TrackerUrl = primaryTrackerUrl,
            Compact = true,
            NumWant = isStopped ? 0 : 50,
            IsPrivate = torrent.IsPrivate,
            ClientProfile = profile,
            LastAnnouncedUploaded = maxAnnouncedUploaded
        };

        foreach (var entry in candidateTrackers)
        {
            var previousStatus = entry.Status;
            entry.Status = TrackerStatus.Announcing;
            _trackerEntryService.Update(entry);
            _eventAggregator?.PublishEvent(new TrackerStatusChangedEvent(torrent, entry, previousStatus, TrackerStatus.Announcing));
            _eventLogService.Info(
                torrent.Id,
                "Tracker",
                $"Announcing to tracker: {entry.Url} (event: {eventName}, uploaded: {uploadedBytes:N0} bytes, left: {left:N0} bytes)");
        }

        TrackerAnnounceResponse response;
        var sw = Stopwatch.StartNew();
        try
        {
            response = _multiTracker.Announce(request, announceList);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception announcing for torrent {0}", torrent.Name ?? torrent.InfoHash);
            response = new TrackerAnnounceResponse
            {
                Success = false,
                FailureReason = ex.Message
            };
        }
        finally
        {
            sw.Stop();
        }

        var responsesToProcess = (response?.TrackerResponses != null && response.TrackerResponses.Count > 0)
            ? response.TrackerResponses
            : candidateTrackers.Select(e => new TrackerAnnounceResponse
            {
                Success = response?.Success ?? false,
                Complete = response?.Complete ?? 0,
                Incomplete = response?.Incomplete ?? 0,
                Interval = response?.Interval ?? 0,
                MinInterval = response?.MinInterval ?? 0,
                Peers = response?.Peers ?? new List<TrackerPeer>(),
                FailureReason = response?.FailureReason,
                WarningMessage = response?.WarningMessage,
                TrackerUrl = e.Url,
                ResponseTimeMs = sw.ElapsedMilliseconds
            }).ToList();

        foreach (var tr in responsesToProcess)
        {
            var entry = candidateTrackers.FirstOrDefault(e => string.Equals(e.Url, tr.TrackerUrl, StringComparison.OrdinalIgnoreCase))
                ?? candidateTrackers.FirstOrDefault();

            if (entry == null)
            {
                continue;
            }

            var respTime = tr.ResponseTimeMs > 0 ? tr.ResponseTimeMs : sw.ElapsedMilliseconds;

            _trackerMetricService?.RecordAnnounce(
                entry.Url,
                torrent.Id,
                uploadedBytes,
                torrent.Downloaded,
                left,
                respTime,
                tr.Success,
                tr.Complete,
                tr.Incomplete,
                tr.Peers?.Count ?? 0,
                tr.FailureReason);

            entry.TotalAnnounces++;
            entry.LastResponseTime = respTime;
            entry.AverageResponseTime = MultiTrackerManager.CalculateResponseTimeEma(respTime, entry.AverageResponseTime);

            var result = new TrackerAnnounceResult
            {
                TrackerId = entry.Id,
                Url = entry.Url,
                Success = tr.Success,
                ResponseTimeMs = respTime,
                Seeders = tr.Complete,
                Leechers = tr.Incomplete,
                PeersDiscovered = tr.Peers?.Count ?? 0,
                FailureReason = tr.FailureReason,
                WarningMessage = tr.WarningMessage,
                LastAnnouncedUploaded = entry.LastAnnouncedUploaded
            };

            if (HasWarningOrAntiCheat(tr, out var warningText))
            {
                var oldStatus = torrent.Status;
                torrent.Status = TorrentStatus.Paused;
                torrent.ErrorMessage = $"Circuit breaker tripped: {warningText}";
                _torrentService?.Update(torrent);

                entry.Status = TrackerStatus.Disabled;
                entry.Enabled = false;
                entry.NextAnnounce = null;
                entry.WarningMessage = warningText;
                entry.ErrorMessage = $"Circuit breaker tripped: {warningText}";
                entry.LastErrorTime = DateTime.UtcNow;
                entry.LastAnnounce = DateTime.UtcNow;
                entry.LastAnnouncedUploaded = request.Uploaded;
                _trackerEntryService.Update(entry);

                _logger.Error("Circuit breaker tripped for torrent {0} ({1}) on tracker {2}: {3}", torrent.Name, torrent.InfoHash, entry.Url, warningText);
                _eventLogService.Error(
                    torrent.Id,
                    "Tracker",
                    $"CRITICAL: Tracker safety circuit breaker tripped for {entry.Url}: {warningText}. Pausing torrent and suspending automated announces.");

                _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, TorrentStatus.Paused, $"Circuit breaker tripped: {warningText}"));
                _eventAggregator?.PublishEvent(new HealthIssueEvent(torrent, "TrackerSafety", $"Circuit breaker tripped on {entry.Url}: {warningText}", isResolved: false));
                _eventAggregator?.PublishEvent(new TrackerWarningEvent(torrent, entry.Url, warningText));
                _eventAggregator?.PublishEvent(new TrackerStatusChangedEvent(torrent, entry, TrackerStatus.Announcing, TrackerStatus.Disabled));

                result.Success = false;
                result.FailureReason = $"Circuit breaker tripped: {warningText}";
                result.WarningMessage = warningText;
                result.LastAnnouncedUploaded = request.Uploaded;
                results.Add(result);
                break;
            }
            else if (tr.Success)
            {
                entry.Status = TrackerStatus.Working;
                entry.Seeders = tr.Complete;
                entry.Leechers = tr.Incomplete;
                entry.LastAnnounce = DateTime.UtcNow;
                entry.LastAnnouncedUploaded = request.Uploaded;
                var interval = tr.Interval > 0 ? tr.Interval : (_configService.AnnounceIntervalSeconds > 0 ? _configService.AnnounceIntervalSeconds : 1800);
                entry.AnnounceInterval = interval;
                entry.MinAnnounceInterval = tr.MinInterval > 0 ? tr.MinInterval : 900;
                var jitteredInterval = JitterCalculator != null
                    ? JitterCalculator(interval, tr.MinInterval)
                    : CalculateJitteredInterval(interval, tr.MinInterval);
                entry.NextAnnounce = DateTime.UtcNow.AddSeconds(jitteredInterval);
                entry.SuccessfulAnnounces++;
                entry.ConsecutiveFailures = 0;
                entry.ErrorMessage = null;
                entry.WarningMessage = null;
                _trackerEntryService.Update(entry);

                var allEntries = _trackerEntryService?.GetByTorrentId(torrent.Id);
                if (allEntries != null && allEntries.Count > 0)
                {
                    torrent.Seeders = Math.Max(tr.Complete, allEntries.Where(e => e.Enabled).Select(e => e.Seeders).DefaultIfEmpty(0).Max());
                    torrent.Leechers = Math.Max(tr.Incomplete, allEntries.Where(e => e.Enabled).Select(e => e.Leechers).DefaultIfEmpty(0).Max());
                }
                else
                {
                    torrent.Seeders = tr.Complete;
                    torrent.Leechers = tr.Incomplete;
                }

                _torrentService?.Update(torrent);
                _eventAggregator?.PublishEvent(new TrackerStatusChangedEvent(torrent, entry, TrackerStatus.Announcing, TrackerStatus.Working));

                result.AnnounceInterval = interval;
                result.LastAnnouncedUploaded = request.Uploaded;

                _eventLogService.Info(
                    torrent.Id,
                    "Tracker",
                    $"Tracker announce succeeded: {entry.Url} -> Seeders: {tr.Complete}, Leechers: {tr.Incomplete}, Peers: {tr.Peers?.Count ?? 0}, Interval: {interval}s ({respTime}ms)");

                if (tr.Peers != null && tr.Peers.Count > 0)
                {
                    _peerDiscovery.AddPeers(torrent.InfoHash, tr.Peers, "tracker");
                    var peerSample = string.Join(", ", tr.Peers.Take(5).Select(p => $"{p.Ip}:{p.Port}"));
                    _eventLogService.Info(
                        torrent.Id,
                        "Peers",
                        $"Discovered {tr.Peers.Count} peer candidate(s) from {entry.Url} ({peerSample}{(tr.Peers.Count > 5 ? ", ..." : "")})");
                }

                results.Add(result);

                PromoteTrackerInTorrentTierOrder(torrent.Id, entry.Tier, entry.Url);
            }
            else
            {
                entry.ConsecutiveFailures++;
                entry.ErrorMessage = tr.FailureReason;
                entry.LastErrorTime = DateTime.UtcNow;

                var maxFailures = _configService?.FailoverMaxConsecutiveFailures > 0
                    ? _configService.FailoverMaxConsecutiveFailures
                    : 5;

                if (entry.ConsecutiveFailures >= maxFailures)
                {
                    entry.Status = TrackerStatus.Disabled;
                    entry.Enabled = false;
                    _logger.Warn("Tracker {0} auto-disabled after {1} consecutive failures", entry.Url, entry.ConsecutiveFailures);
                }
                else
                {
                    entry.Status = TrackerStatus.Failed;
                }

                var baseSeconds = _configService?.FailoverBackoffBaseSeconds > 0
                    ? _configService.FailoverBackoffBaseSeconds
                    : 60;
                var maxBackoff = _configService?.FailoverMaxBackoffSeconds > 0
                    ? _configService.FailoverMaxBackoffSeconds
                    : 3600;
                var exponent = Math.Min(Math.Max(0, entry.ConsecutiveFailures - 1), 10);
                var backoffSeconds = Math.Min(baseSeconds * Math.Pow(2, exponent), maxBackoff);
                entry.NextAnnounce = DateTime.UtcNow.AddSeconds(backoffSeconds);
                _trackerEntryService.Update(entry);

                _eventLogService.Warn(
                    torrent.Id,
                    "Tracker",
                    $"Tracker announce failed: {entry.Url} -> {tr.FailureReason ?? "Unreachable"} (failure #{entry.ConsecutiveFailures}, next retry in {(int)backoffSeconds}s)");

                _eventAggregator?.PublishEvent(new TrackerUnreachableEvent(torrent, entry.Url, tr.FailureReason ?? "Unreachable"));
                _eventAggregator?.PublishEvent(new TrackerStatusChangedEvent(torrent, entry, TrackerStatus.Announcing, entry.Status));

                results.Add(result);
            }

            _eventAggregator?.PublishEvent(new TrackerAnnounceEvent(torrent, entry.Url, tr.Complete, tr.Incomplete, tr.Peers?.Count ?? 0, respTime, tr.Success, tr.FailureReason, entry.Id, entry.Status));
        }

        var announcedUrls = new HashSet<string>(results.Select(r => r.Url), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in candidateTrackers)
        {
            if (!announcedUrls.Contains(entry.Url) && entry.Status == TrackerStatus.Announcing)
            {
                entry.Status = TrackerStatus.Unknown;
                _trackerEntryService.Update(entry);
            }
        }

        return results;
    }

    private void PromoteTrackerInTorrentTierOrder(int torrentId, int tier, string trackerUrl)
    {
        if (_torrentTierOrder.TryGetValue(torrentId, out var tiers))
        {
            foreach (var t in tiers)
            {
                var idx = t.IndexOf(trackerUrl);
                if (idx > 0)
                {
                    t.RemoveAt(idx);
                    t.Insert(0, trackerUrl);
                    break;
                }
            }
        }
    }

    public TrackerAnnounceResult AnnounceTracker(Torrent torrent, TrackerEntry entry, bool force = false, AnnounceEvent eventType = AnnounceEvent.None)
    {
        if (torrent == null || entry == null || string.IsNullOrWhiteSpace(entry.Url))
        {
            return new TrackerAnnounceResult { Success = false, FailureReason = "Invalid torrent or tracker" };
        }

        if (_vpnKillSwitchService?.IsFailClosedActive == true)
        {
            _logger.Debug("VPN kill switch fail-closed is active; halting tracker announce for {0}.", entry.Url);
            return new TrackerAnnounceResult { Success = false, FailureReason = "VPN outage: announce deferred" };
        }

        if (!force && !entry.Enabled)
        {
            return new TrackerAnnounceResult { Success = false, FailureReason = "Tracker is disabled" };
        }

        if (!force && torrent.Status == TorrentStatus.Paused && eventType != AnnounceEvent.Stopped)
        {
            return new TrackerAnnounceResult { Success = false, FailureReason = "Torrent is paused" };
        }

        if (IsVpnBlocked(torrent))
        {
            _logger.Debug("VPN is down or torrent {0} is VPN-paused; deferring tracker announce for {1}.", torrent.Name ?? torrent.InfoHash, entry.Url);
            return new TrackerAnnounceResult { Success = false, FailureReason = "VPN outage: announce deferred" };
        }

        var isFirstAnnounce = entry.TotalAnnounces == 0 || !entry.LastAnnounce.HasValue;
        if (!force && !isFirstAnnounce && entry.NextAnnounce.HasValue && entry.NextAnnounce.Value > DateTime.UtcNow)
        {
            return new TrackerAnnounceResult { Success = false, FailureReason = "Tracker not due for announce yet" };
        }

        if (IsRateLimited(entry, isFirstAnnounce, out var minIntervalSeconds, out var retryAfter))
        {
            return new TrackerAnnounceResult
            {
                TrackerId = entry.Id,
                Url = entry.Url,
                Success = false,
                FailureReason = $"Rate limited: minimum announce interval of {minIntervalSeconds}s not elapsed (retry in {retryAfter}s)",
                RetryAfterSeconds = retryAfter
            };
        }

        return ExecuteAnnounce(torrent, entry, isFirstAnnounce, eventType);
    }

    public TrackerScrapeResponse ScrapeTorrent(Torrent torrent)
    {
        if (torrent == null || string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            return new TrackerScrapeResponse { Success = false, FailureReason = "Invalid torrent" };
        }

        if (_vpnKillSwitchService?.IsFailClosedActive == true)
        {
            _logger.Debug("VPN kill switch fail-closed is active; halting tracker scrape for torrent {0}.", torrent.Name ?? torrent.InfoHash);
            return new TrackerScrapeResponse { Success = false, FailureReason = "VPN outage: scrape deferred" };
        }

        if (IsVpnBlocked(torrent))
        {
            _logger.Debug("VPN is down or torrent {0} is VPN-paused; deferring tracker scrape.", torrent.Name ?? torrent.InfoHash);
            return new TrackerScrapeResponse { Success = false, FailureReason = "VPN outage: scrape deferred" };
        }

        var trackerEntries = _trackerEntryService.GetByTorrentId(torrent.Id);
        var announceList = trackerEntries
            .Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.Url))
            .GroupBy(t => t.Tier)
            .OrderBy(g => g.Key)
            .Select(g => g.Select(t => t.Url).ToList())
            .ToList();

        if (announceList.Count == 0 && !string.IsNullOrEmpty(torrent.TrackerUrl))
        {
            announceList.Add(new List<string> { torrent.TrackerUrl });
        }

        return _multiTracker.Scrape(torrent.InfoHash, announceList);
    }

    private TrackerAnnounceResult ExecuteAnnounce(Torrent torrent, TrackerEntry entry, bool isFirstAnnounce, AnnounceEvent eventType = AnnounceEvent.None)
    {
        if (_vpnKillSwitchService?.IsFailClosedActive == true)
        {
            _logger.Debug("VPN kill switch fail-closed is active; halting tracker announce execution for {0}.", entry?.Url);
            return new TrackerAnnounceResult { Success = false, FailureReason = "VPN outage: announce deferred" };
        }

        var isStopped = torrent.Status == TorrentStatus.Stopped || torrent.Status == TorrentStatus.Paused || eventType == AnnounceEvent.Stopped;

        AnnounceEvent announceEvent;
        if (eventType != AnnounceEvent.None)
        {
            announceEvent = eventType;
        }
        else if (isStopped)
        {
            announceEvent = AnnounceEvent.Stopped;
        }
        else if (isFirstAnnounce)
        {
            announceEvent = AnnounceEvent.Started;
        }
        else
        {
            announceEvent = AnnounceEvent.None;
        }

        var left = announceEvent == AnnounceEvent.Completed ? 0 : Math.Max(0, torrent.TotalSize - torrent.Downloaded);
        var eventName = announceEvent != AnnounceEvent.None ? announceEvent.ToString().ToLowerInvariant() : "regular";

        var isSimulated = torrent.IsSimulated || (_configService != null && _configService.SimulationModeEnabled);
        var isLoopback = IsLoopbackOrMockTracker(entry.Url);

        long effectiveUploaded;
        if (isSimulated && !isLoopback)
        {
            effectiveUploaded = torrent.RealUploaded;
            if (torrent.Uploaded > torrent.RealUploaded)
            {
                _logger.Debug(
                    "Simulated torrent {0}: suppressing synthetic upload bytes ({1:N0} bytes) from external tracker {2}; reporting genuine wire bytes ({3:N0} bytes)",
                    torrent.Name ?? torrent.InfoHash,
                    torrent.Uploaded,
                    entry.Url,
                    effectiveUploaded);
            }
        }
        else
        {
            effectiveUploaded = torrent.Uploaded;
        }

        var uploadedBytes = Math.Max(effectiveUploaded, entry.LastAnnouncedUploaded);

        _eventLogService.Info(
            torrent.Id,
            "Tracker",
            $"Announcing to tracker: {entry.Url} (event: {eventName}, uploaded: {uploadedBytes:N0} bytes, left: {left:N0} bytes)");

        IClientProfile profile = null;
        TorrentClientSession session = null;

        if (!string.IsNullOrWhiteSpace(torrent.ClientProfile))
        {
            profile = ResolveProfileByName(torrent.ClientProfile);
        }

        if (profile == null && _clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled && !_configService.AnonymousMode)
        {
            session = _clientBehaviorSimulator.GetOrCreateSession(torrent.InfoHash, torrent.IsPrivate);
            profile = session?.Profile ?? _clientBehaviorSimulator.GetProfileForTorrent(torrent.InfoHash, torrent.IsPrivate);
        }

        if (profile == null && !string.IsNullOrWhiteSpace(_configService.BitTorrentUserAgent))
        {
            profile = DetectProfileFromUserAgent(_configService.BitTorrentUserAgent);
        }

        if (profile == null && _clientBehaviorSimulator != null && !_configService.AnonymousMode)
        {
            session = _clientBehaviorSimulator.GetOrCreateSession(torrent.InfoHash, torrent.IsPrivate);
            profile = session?.Profile ?? _clientBehaviorSimulator.GetProfileForTorrent(torrent.InfoHash, torrent.IsPrivate);
        }

        if (profile == null)
        {
            profile = ResolveProfileByName(_configService.PrimaryClient) ?? new QBittorrentProfile();
        }

        var peerId = session?.PeerId ?? profile.GeneratePeerId();
        var userAgent = session?.Profile?.UserAgent ?? profile.UserAgent ?? (!string.IsNullOrWhiteSpace(_configService.BitTorrentUserAgent) ? _configService.BitTorrentUserAgent : "qBittorrent/4.4.2");
        var announceKey = session?.AnnounceKey ?? RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue).ToString("X8", CultureInfo.InvariantCulture);

        var request = new TrackerAnnounceRequest
        {
            InfoHash = torrent.InfoHash,
            PeerId = peerId,
            UserAgent = userAgent,
            Key = announceKey,
            Port = _configService.ListeningPort,
            Uploaded = uploadedBytes,
            Downloaded = torrent.Downloaded,
            Left = left,
            Event = announceEvent,
            TrackerUrl = entry.Url,
            Compact = true,
            NumWant = isStopped ? 0 : 50,
            IsPrivate = torrent.IsPrivate,
            ClientProfile = profile,
            LastAnnouncedUploaded = entry.LastAnnouncedUploaded
        };

        var announceList = new List<List<string>>
        {
            new() { entry.Url }
        };

        var previousStatus = entry.Status;
        entry.Status = TrackerStatus.Announcing;
        _trackerEntryService.Update(entry);
        _eventAggregator?.PublishEvent(new TrackerStatusChangedEvent(torrent, entry, previousStatus, TrackerStatus.Announcing));

        TrackerAnnounceResponse response;
        var sw = Stopwatch.StartNew();
        try
        {
            response = _multiTracker.Announce(request, announceList);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception announcing to {0}", entry.Url);
            response = new TrackerAnnounceResponse
            {
                Success = false,
                FailureReason = ex.Message
            };
        }
        finally
        {
            sw.Stop();
        }

        _trackerMetricService?.RecordAnnounce(
            entry.Url,
            torrent.Id,
            uploadedBytes,
            torrent.Downloaded,
            left,
            sw.ElapsedMilliseconds,
            response.Success,
            response.Complete,
            response.Incomplete,
            response.Peers?.Count ?? 0,
            response.FailureReason);

        entry.TotalAnnounces++;
        entry.LastResponseTime = sw.ElapsedMilliseconds;
        entry.AverageResponseTime = MultiTrackerManager.CalculateResponseTimeEma(sw.ElapsedMilliseconds, entry.AverageResponseTime);

        var result = new TrackerAnnounceResult
        {
            TrackerId = entry.Id,
            Url = entry.Url,
            Success = response.Success,
            ResponseTimeMs = sw.ElapsedMilliseconds,
            Seeders = response.Complete,
            Leechers = response.Incomplete,
            PeersDiscovered = response.Peers?.Count ?? 0,
            FailureReason = response.FailureReason,
            WarningMessage = response.WarningMessage,
            LastAnnouncedUploaded = entry.LastAnnouncedUploaded
        };

        if (HasWarningOrAntiCheat(response, out var warningText))
        {
            var oldStatus = torrent.Status;
            torrent.Status = TorrentStatus.Paused;
            torrent.ErrorMessage = $"Circuit breaker tripped: {warningText}";
            _torrentService?.Update(torrent);

            entry.Status = TrackerStatus.Disabled;
            entry.Enabled = false;
            entry.NextAnnounce = null;
            entry.WarningMessage = warningText;
            entry.ErrorMessage = $"Circuit breaker tripped: {warningText}";
            entry.LastErrorTime = DateTime.UtcNow;
            entry.LastAnnounce = DateTime.UtcNow;
            entry.LastAnnouncedUploaded = request.Uploaded;
            _trackerEntryService.Update(entry);

            _logger.Error("Circuit breaker tripped for torrent {0} ({1}) on tracker {2}: {3}", torrent.Name, torrent.InfoHash, entry.Url, warningText);
            _eventLogService.Error(
                torrent.Id,
                "Tracker",
                $"CRITICAL: Tracker safety circuit breaker tripped for {entry.Url}: {warningText}. Pausing torrent and suspending automated announces.");

            _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, TorrentStatus.Paused, $"Circuit breaker tripped: {warningText}"));
            _eventAggregator?.PublishEvent(new HealthIssueEvent(torrent, "TrackerSafety", $"Circuit breaker tripped on {entry.Url}: {warningText}", isResolved: false));
            _eventAggregator?.PublishEvent(new TrackerWarningEvent(torrent, entry.Url, warningText));
            _eventAggregator?.PublishEvent(new TrackerStatusChangedEvent(torrent, entry, TrackerStatus.Announcing, TrackerStatus.Disabled));

            result.Success = false;
            result.FailureReason = $"Circuit breaker tripped: {warningText}";
            result.WarningMessage = warningText;
            result.LastAnnouncedUploaded = request.Uploaded;
        }
        else if (response.Success)
        {
            entry.Status = TrackerStatus.Working;
            entry.Seeders = response.Complete;
            entry.Leechers = response.Incomplete;
            entry.LastAnnounce = DateTime.UtcNow;
            entry.LastAnnouncedUploaded = request.Uploaded;
            var interval = response.Interval > 0 ? response.Interval : (_configService.AnnounceIntervalSeconds > 0 ? _configService.AnnounceIntervalSeconds : 1800);
            entry.AnnounceInterval = interval;
            entry.MinAnnounceInterval = response.MinInterval > 0 ? response.MinInterval : 900;
            var jitteredInterval = JitterCalculator != null
                ? JitterCalculator(interval, response.MinInterval)
                : CalculateJitteredInterval(interval, response.MinInterval);
            entry.NextAnnounce = DateTime.UtcNow.AddSeconds(jitteredInterval);
            entry.SuccessfulAnnounces++;
            entry.ConsecutiveFailures = 0;
            entry.ErrorMessage = null;
            entry.WarningMessage = null;
            _trackerEntryService.Update(entry);
            var allEntries = _trackerEntryService?.GetByTorrentId(torrent.Id);
            if (allEntries != null && allEntries.Count > 0)
            {
                torrent.Seeders = Math.Max(response.Complete, allEntries.Where(e => e.Enabled).Select(e => e.Seeders).DefaultIfEmpty(0).Max());
                torrent.Leechers = Math.Max(response.Incomplete, allEntries.Where(e => e.Enabled).Select(e => e.Leechers).DefaultIfEmpty(0).Max());
            }
            else
            {
                torrent.Seeders = response.Complete;
                torrent.Leechers = response.Incomplete;
            }

            _torrentService?.Update(torrent);
            _eventAggregator?.PublishEvent(new TrackerStatusChangedEvent(torrent, entry, TrackerStatus.Announcing, TrackerStatus.Working));

            result.AnnounceInterval = interval;
            result.LastAnnouncedUploaded = request.Uploaded;

            _eventLogService.Info(
                torrent.Id,
                "Tracker",
                $"Tracker announce succeeded: {entry.Url} -> Seeders: {response.Complete}, Leechers: {response.Incomplete}, Peers: {response.Peers?.Count ?? 0}, Interval: {interval}s ({sw.ElapsedMilliseconds}ms)");

            if (response.Peers != null && response.Peers.Count > 0)
            {
                _peerDiscovery.AddPeers(torrent.InfoHash, response.Peers, "tracker");
                var peerSample = string.Join(", ", response.Peers.Take(5).Select(p => $"{p.Ip}:{p.Port}"));
                _eventLogService.Info(
                    torrent.Id,
                    "Peers",
                    $"Discovered {response.Peers.Count} peer candidate(s) from {entry.Url} ({peerSample}{(response.Peers.Count > 5 ? ", ..." : "")})");
            }
        }
        else
        {
            entry.ConsecutiveFailures++;
            entry.ErrorMessage = response.FailureReason;
            entry.LastErrorTime = DateTime.UtcNow;

            var maxFailures = _configService?.FailoverMaxConsecutiveFailures > 0
                ? _configService.FailoverMaxConsecutiveFailures
                : 5;

            if (entry.ConsecutiveFailures >= maxFailures)
            {
                entry.Status = TrackerStatus.Disabled;
                entry.Enabled = false;
                _logger.Warn("Tracker {0} auto-disabled after {1} consecutive failures", entry.Url, entry.ConsecutiveFailures);
            }
            else
            {
                entry.Status = TrackerStatus.Failed;
            }

            var baseSeconds = _configService?.FailoverBackoffBaseSeconds > 0
                ? _configService.FailoverBackoffBaseSeconds
                : 60;
            var maxBackoff = _configService?.FailoverMaxBackoffSeconds > 0
                ? _configService.FailoverMaxBackoffSeconds
                : 3600;
            var exponent = Math.Min(Math.Max(0, entry.ConsecutiveFailures - 1), 10);
            var backoffSeconds = Math.Min(baseSeconds * Math.Pow(2, exponent), maxBackoff);
            entry.NextAnnounce = DateTime.UtcNow.AddSeconds(backoffSeconds);
            _trackerEntryService.Update(entry);

            _eventLogService.Warn(
                torrent.Id,
                "Tracker",
                $"Tracker announce failed: {entry.Url} -> {response.FailureReason ?? "Unreachable"} (failure #{entry.ConsecutiveFailures}, next retry in {(int)backoffSeconds}s)");

            _eventAggregator?.PublishEvent(new TrackerUnreachableEvent(torrent, entry.Url, response.FailureReason ?? "Unreachable"));
            _eventAggregator?.PublishEvent(new TrackerStatusChangedEvent(torrent, entry, TrackerStatus.Announcing, entry.Status));
        }

        _eventAggregator?.PublishEvent(new TrackerAnnounceEvent(torrent, entry.Url, response.Complete, response.Incomplete, response.Peers?.Count ?? 0, sw.ElapsedMilliseconds, response.Success, response.FailureReason, entry.Id, entry.Status));

        return result;
    }

    public static bool IsLoopbackOrMockTracker(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var host = uri.Host;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip))
            {
                return true;
            }

            if (uri.Scheme.StartsWith("mock", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private IClientProfile ResolveProfileByName(string clientName)
    {
        if (string.IsNullOrWhiteSpace(clientName))
        {
            return null;
        }

        var name = clientName.Trim().ToLowerInvariant();

        if (_clientProfileFactory != null)
        {
            try
            {
                var available = _clientProfileFactory.GetAvailableProviders();
                if (available != null)
                {
                    var match = available.FirstOrDefault(p =>
                        p.Name.Equals(clientName, StringComparison.OrdinalIgnoreCase) ||
                        p.GetType().Name.StartsWith(clientName, StringComparison.OrdinalIgnoreCase) ||
                        p.Name.ToLowerInvariant().Contains(name));
                    if (match != null)
                    {
                        return match;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to get available providers from ClientProfileFactory");
            }
        }

        return name switch
        {
            var s when s.Contains("transmission") => new TransmissionProfile(),
            var s when s.Contains("deluge") => new DelugeProfile(),
            var s when s.Contains("utorrent") || s.Contains("µtorrent") => new UTorrentProfile(),
            var s when s.Contains("biglybt") => new BiglyBTProfile(),
            var s when s.Contains("qbittorrent") => new QBittorrentProfile(),
            _ => null
        };
    }

    private IClientProfile DetectProfileFromUserAgent(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var ua = userAgent.ToLowerInvariant();
        if (ua.Contains("transmission"))
        {
            return ResolveProfileByName("Transmission") ?? new TransmissionProfile();
        }

        if (ua.Contains("deluge"))
        {
            return ResolveProfileByName("Deluge") ?? new DelugeProfile();
        }

        if (ua.Contains("utorrent") || ua.Contains("µtorrent"))
        {
            return ResolveProfileByName("uTorrent") ?? new UTorrentProfile();
        }

        if (ua.Contains("biglybt"))
        {
            return ResolveProfileByName("BiglyBT") ?? new BiglyBTProfile();
        }

        if (ua.Contains("qbittorrent"))
        {
            return ResolveProfileByName("qBittorrent") ?? new QBittorrentProfile();
        }

        return null;
    }

    public static double CalculateJitteredInterval(double interval, int minInterval = 0, double? jitterFraction = null)
    {
        if (interval <= 0)
        {
            return 1.0;
        }

        // Pseudo-random jitter between -5% (-0.05) and +5% (+0.05)
        var fraction = jitterFraction ?? ((Random.Shared.NextDouble() * 0.10) - 0.05);

        // Clamp fraction to [-0.05, 0.05]
        fraction = Math.Clamp(fraction, -0.05, 0.05);

        var jittered = interval * (1.0 + fraction);

        // Clamped so interval never drops below min_interval if provided by tracker
        if (minInterval > 0 && jittered < minInterval)
        {
            jittered = minInterval;
        }

        // Ensure interval is positive and NextAnnounce is strictly in the future
        return Math.Max(1.0, jittered);
    }

    public static bool HasWarningOrAntiCheat(TrackerAnnounceResponse response, out string warningText)
    {
        if (response != null)
        {
            if (!string.IsNullOrWhiteSpace(response.WarningMessage))
            {
                warningText = response.WarningMessage;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(response.FailureReason) && IsAntiCheatWarning(response.FailureReason))
            {
                warningText = response.FailureReason;
                return true;
            }
        }

        warningText = null;
        return false;
    }

    private static bool IsAntiCheatWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var text = message.ToLowerInvariant();
        return text.Contains("unrealistic") ||
            text.Contains("anti-cheat") ||
            text.Contains("anticheat") ||
            text.Contains("speed throttled") ||
            text.Contains("throttled") ||
            text.Contains("ratio review") ||
            text.Contains("flagged") ||
            text.Contains("banned") ||
            text.Contains("blacklisted") ||
            text.Contains("cheat");
    }

    public void Handle(TorrentFinishedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        var torrent = message.Torrent;
        if (!_completedTorrents.TryAdd(torrent.Id, true))
        {
            return;
        }

        try
        {
            AnnounceTorrent(torrent, force: true, eventType: AnnounceEvent.Completed);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to send completed tracker announce on finished event for torrent {0}", torrent.Id);
        }
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        if (message?.Torrent != null)
        {
            Handle(new TorrentFinishedEvent(message.Torrent));
        }
    }

    public void Handle(TorrentHashCheckCompletedEvent message)
    {
        if (message?.Torrent != null)
        {
            var t = message.Torrent;
            if (t.Progress >= 1.0 || t.Status == TorrentStatus.Seeding || (t.TotalSize > 0 && t.Downloaded >= t.TotalSize))
            {
                _completedTorrents.TryAdd(t.Id, true);
            }
        }
    }

    public void Handle(ApplicationStartedEvent message)
    {
        if (_torrentService == null)
        {
            return;
        }

        try
        {
            var existingTorrents = _torrentService.GetAll();
            if (existingTorrents != null)
            {
                foreach (var t in existingTorrents)
                {
                    if (t.Progress >= 1.0 || t.Status == TorrentStatus.Seeding || (t.TotalSize > 0 && t.Downloaded >= t.TotalSize))
                    {
                        _completedTorrents.TryAdd(t.Id, true);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to load initial completed torrents on ApplicationStartedEvent");
        }
    }

    public void Handle(TorrentStatusChangedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        if (message.NewStatus == TorrentStatus.Stopped || message.NewStatus == TorrentStatus.Paused)
        {
            try
            {
                AnnounceTorrent(message.Torrent, force: true, eventType: AnnounceEvent.Stopped);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to send stopped tracker announce on status change for torrent {0}", message.Torrent.Id);
            }
        }
    }

    public void Handle(TorrentPausedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        try
        {
            AnnounceTorrent(message.Torrent, force: true, eventType: AnnounceEvent.Stopped);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to send stopped tracker announce on paused event for torrent {0}", message.Torrent.Id);
        }
    }

    public void Handle(TorrentDeletedEvent message)
    {
        var torrent = message?.Torrent;
        if (torrent == null && message?.TorrentId > 0)
        {
            try
            {
                torrent = _torrentService?.Get(message.TorrentId);
            }
            catch
            {
                // Best effort
            }
        }

        if (torrent == null)
        {
            return;
        }

        _completedTorrents.TryRemove(torrent.Id, out _);
        _torrentTierOrder.TryRemove(torrent.Id, out _);

        try
        {
            AnnounceTorrent(torrent, force: true, eventType: AnnounceEvent.Stopped);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to send stopped tracker announce on deleted event for torrent {0}", torrent.Id);
        }
    }

    public void Handle(SeedingStoppedEvent message)
    {
        _ = HandleStoppedEventAsync(message);
    }

    public async Task HandleStoppedEventAsync(SeedingStoppedEvent message)
    {
        if (message == null || message.TorrentId <= 0)
        {
            return;
        }

        await Task.Run(() =>
        {
            try
            {
                var torrent = _torrentService?.Get(message.TorrentId);
                if (torrent != null)
                {
                    AnnounceTorrent(torrent, force: true, eventType: AnnounceEvent.Stopped);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to send stopped tracker announce for torrent {0}", message.TorrentId);
            }
        }).ConfigureAwait(false);
    }

    private bool IsVpnBlocked(Torrent torrent)
    {
        if (torrent != null && torrent.IsVpnPaused)
        {
            return true;
        }

        if (_isVpnDown)
        {
            return true;
        }

        if (_vpnKillSwitchService != null && _vpnKillSwitchService.IsFailClosedActive)
        {
            return true;
        }

        return false;
    }

    public void Handle(VpnKillSwitchTriggeredEvent message)
    {
        _isVpnDown = true;
        lock (_staggeredLock)
        {
            _staggeredCts?.Cancel();
            while (_staggeredQueue.TryDequeue(out _))
            {
            }
        }
    }

    public void Handle(VpnInterfaceRestoredEvent message)
    {
        OnVpnRestored();
    }

    public void Handle(VpnRestoredEvent message)
    {
        OnVpnRestored();
    }

    private void OnVpnRestored()
    {
        _isVpnDown = false;

        if (_vpnKillSwitchService?.IsFailClosedActive == true)
        {
            _logger.Debug("VPN restored signal received but fail-closed is active; deferring staggered announces.");
            return;
        }

        lock (_staggeredLock)
        {
            if (DateTime.UtcNow - _lastVpnRestore < TimeSpan.FromSeconds(2))
            {
                return;
            }

            _lastVpnRestore = DateTime.UtcNow;
        }

        if (_torrentService == null)
        {
            return;
        }

        var activeTorrents = _torrentService.GetAll()
            .Where(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding)
            .ToList();

        if (activeTorrents.Count == 0)
        {
            return;
        }

        ScheduleStaggeredAnnounces(activeTorrents);
    }

    public void ScheduleStaggeredAnnounces(IEnumerable<Torrent> torrents)
    {
        if (_vpnKillSwitchService?.IsFailClosedActive == true)
        {
            return;
        }

        lock (_staggeredLock)
        {
            _staggeredCts?.Cancel();
            _staggeredCts?.Dispose();
            _staggeredCts = new CancellationTokenSource();
            var token = _staggeredCts.Token;

            while (_staggeredQueue.TryDequeue(out _))
            {
            }

            var list = torrents.ToList();
            foreach (var torrent in list)
            {
                _staggeredQueue.Enqueue(torrent);
            }

            _logger.Info("VPN restored: scheduling staggered announces for {0} torrents across rate-limited window.", list.Count);

            _ = ProcessStaggeredAnnouncesAsync(token);
        }
    }

    private async Task ProcessStaggeredAnnouncesAsync(CancellationToken cancellationToken)
    {
        var count = _staggeredQueue.Count;
        if (count == 0)
        {
            return;
        }

        var totalWindowMs = Math.Clamp(count * 1000, 15000, 30000);
        var intervalMs = Math.Max(250, totalWindowMs / count);
        var random = new Random();

        while (_staggeredQueue.TryDequeue(out var torrent))
        {
            if (cancellationToken.IsCancellationRequested || _isVpnDown || _vpnKillSwitchService?.IsFailClosedActive == true)
            {
                break;
            }

            try
            {
                if (!torrent.IsVpnPaused && (torrent.Status == TorrentStatus.Downloading || torrent.Status == TorrentStatus.Seeding))
                {
                    AnnounceTorrent(torrent, force: true);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed staggered announce for torrent {0}", torrent.Name);
            }

            if (_staggeredQueue.IsEmpty)
            {
                break;
            }

            var jitter = random.Next(-intervalMs / 4, Math.Max(1, intervalMs / 4));
            var delayMs = Math.Max(50, intervalMs + jitter);

            try
            {
                await DelayAsync(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        lock (_staggeredLock)
        {
            _staggeredCts?.Cancel();
            _staggeredCts?.Dispose();
            _staggeredCts = null;
        }
    }
}
