using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
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
    private readonly ConcurrentDictionary<int, bool> _completedTorrents = new();
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
        IVpnKillSwitchService vpnKillSwitchService = null)
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

    public List<TrackerAnnounceResult> AnnounceTorrent(Torrent torrent, bool force = false, AnnounceEvent eventType = AnnounceEvent.None)
    {
        var results = new List<TrackerAnnounceResult>();
        if (torrent == null || string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
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

        foreach (var entry in enabledTrackers)
        {
            if (torrent.IsPrivate && !MultiTrackerManager.IsAuthorizedPrivateTrackerDomain(primaryUrl, entry.Url))
            {
                _logger.Warn("Private torrent {0} skipping unauthorized tracker: {1}", torrent.Name ?? torrent.InfoHash, entry.Url);
                continue;
            }

            var isFirstAnnounce = entry.TotalAnnounces == 0 || !entry.LastAnnounce.HasValue;
            if (!force && !isFirstAnnounce && entry.NextAnnounce.HasValue && entry.NextAnnounce.Value > DateTime.UtcNow)
            {
                continue;
            }

            var result = ExecuteAnnounce(torrent, entry, isFirstAnnounce, eventType);
            results.Add(result);

            if (torrent.Status == TorrentStatus.Paused && !string.IsNullOrWhiteSpace(entry.WarningMessage))
            {
                break;
            }

            if (torrent.IsPrivate && result.Success)
            {
                break;
            }
        }

        return results;
    }

    public TrackerAnnounceResult AnnounceTracker(Torrent torrent, TrackerEntry entry, bool force = false, AnnounceEvent eventType = AnnounceEvent.None)
    {
        if (torrent == null || entry == null || string.IsNullOrWhiteSpace(entry.Url))
        {
            return new TrackerAnnounceResult { Success = false, FailureReason = "Invalid torrent or tracker" };
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

        return ExecuteAnnounce(torrent, entry, isFirstAnnounce, eventType);
    }

    public TrackerScrapeResponse ScrapeTorrent(Torrent torrent)
    {
        if (torrent == null || string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            return new TrackerScrapeResponse { Success = false, FailureReason = "Invalid torrent" };
        }

        if (IsVpnBlocked(torrent))
        {
            _logger.Debug("VPN is down or torrent {0} is VPN-paused; deferring tracker scrape.", torrent.Name ?? torrent.InfoHash);
            return new TrackerScrapeResponse { Success = false, FailureReason = "VPN outage: scrape deferred" };
        }

        var trackerEntries = _trackerEntryService.GetByTorrentId(torrent.Id);
        var announceList = trackerEntries
            .Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.Url))
            .Select(t => new List<string> { t.Url })
            .ToList();

        if (announceList.Count == 0 && !string.IsNullOrEmpty(torrent.TrackerUrl))
        {
            announceList.Add(new List<string> { torrent.TrackerUrl });
        }

        return _multiTracker.Scrape(torrent.InfoHash, announceList);
    }

    private TrackerAnnounceResult ExecuteAnnounce(Torrent torrent, TrackerEntry entry, bool isFirstAnnounce, AnnounceEvent eventType = AnnounceEvent.None)
    {
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

        var uploadedBytes = Math.Max(torrent.Uploaded, entry.LastAnnouncedUploaded);

        _eventLogService.Info(
            torrent.Id,
            "Tracker",
            $"Announcing to tracker: {entry.Url} (event: {eventName}, uploaded: {uploadedBytes:N0} bytes, left: {left:N0} bytes)");

        var session = (_clientBehaviorSimulator != null && !_configService.AnonymousMode)
            ? _clientBehaviorSimulator.GetOrCreateSession(torrent.InfoHash, torrent.IsPrivate)
            : null;
        var profile = session?.Profile ?? ((_clientBehaviorSimulator != null && !_configService.AnonymousMode)
            ? _clientBehaviorSimulator.GetProfileForTorrent(torrent.InfoHash, torrent.IsPrivate)
            : null);

        if (profile == null)
        {
            var primary = _configService.PrimaryClient?.Trim().ToLowerInvariant() ?? "";
            profile = primary switch
            {
                "transmission" => new TransmissionProfile(),
                "deluge" => new DelugeProfile(),
                "utorrent" => new UTorrentProfile(),
                "biglybt" => new BiglyBTProfile(),
                _ => new QBittorrentProfile()
            };
        }

        var peerId = session?.PeerId ?? profile.GeneratePeerId();
        var userAgent = !_configService.ClientBehaviorEngineEnabled && !string.IsNullOrWhiteSpace(_configService.BitTorrentUserAgent)
            ? _configService.BitTorrentUserAgent
            : (session?.Profile?.UserAgent ?? profile.UserAgent ?? _configService.BitTorrentUserAgent);
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
            entry.Status = TrackerStatus.Failed;
            entry.ConsecutiveFailures++;
            entry.ErrorMessage = response.FailureReason;
            entry.LastErrorTime = DateTime.UtcNow;
            var backoffSeconds = Math.Min(1800, 60 * Math.Pow(2, Math.Min(5, entry.ConsecutiveFailures)));
            entry.NextAnnounce = DateTime.UtcNow.AddSeconds(backoffSeconds);
            _trackerEntryService.Update(entry);

            _eventLogService.Warn(
                torrent.Id,
                "Tracker",
                $"Tracker announce failed: {entry.Url} -> {response.FailureReason ?? "Unreachable"} (failure #{entry.ConsecutiveFailures}, next retry in {(int)backoffSeconds}s)");

            _eventAggregator?.PublishEvent(new TrackerUnreachableEvent(torrent, entry.Url, response.FailureReason ?? "Unreachable"));
            _eventAggregator?.PublishEvent(new TrackerStatusChangedEvent(torrent, entry, TrackerStatus.Announcing, TrackerStatus.Failed));
        }

        _eventAggregator?.PublishEvent(new TrackerAnnounceEvent(torrent, entry.Url, response.Complete, response.Incomplete, response.Peers?.Count ?? 0, sw.ElapsedMilliseconds, response.Success, response.FailureReason, entry.Id, entry.Status));

        return result;
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
            if (cancellationToken.IsCancellationRequested || _isVpnDown)
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
