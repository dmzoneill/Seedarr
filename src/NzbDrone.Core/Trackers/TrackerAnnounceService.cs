using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        _eventLogService.Info(
            torrent.Id,
            "Tracker",
            $"Announcing to tracker: {entry.Url} (event: {eventName}, uploaded: {torrent.Uploaded:N0} bytes, left: {left:N0} bytes)");

        var session = (_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled && !_configService.AnonymousMode)
            ? _clientBehaviorSimulator.GetOrCreateSession(torrent.InfoHash, torrent.IsPrivate)
            : null;
        var profile = session?.Profile ?? ((_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled && !_configService.AnonymousMode)
            ? _clientBehaviorSimulator.GetProfileForTorrent(torrent.InfoHash, torrent.IsPrivate)
            : null);
        var peerId = session?.PeerId ?? profile?.GeneratePeerId() ?? "-SD1000-000000000000";
        var userAgent = profile?.UserAgent ?? _configService.BitTorrentUserAgent;

        var request = new TrackerAnnounceRequest
        {
            InfoHash = torrent.InfoHash,
            PeerId = peerId,
            UserAgent = userAgent,
            Key = session?.AnnounceKey,
            Port = _configService.ListeningPort,
            Uploaded = torrent.Uploaded,
            Downloaded = torrent.Downloaded,
            Left = left,
            Event = announceEvent,
            TrackerUrl = entry.Url,
            Compact = true,
            NumWant = isStopped ? 0 : 50,
            IsPrivate = torrent.IsPrivate
        };

        var announceList = new List<List<string>>
        {
            new() { entry.Url }
        };

        var sw = Stopwatch.StartNew();
        var response = _multiTracker.Announce(request, announceList);
        sw.Stop();

        _trackerMetricService?.RecordAnnounce(
            entry.Url,
            torrent.Id,
            torrent.Uploaded,
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
            FailureReason = response.FailureReason
        };

        if (response.Success)
        {
            entry.Status = TrackerStatus.Working;
            entry.Seeders = response.Complete;
            entry.Leechers = response.Incomplete;
            entry.LastAnnounce = DateTime.UtcNow;
            var interval = response.Interval > 0 ? response.Interval : (_configService.AnnounceIntervalSeconds > 0 ? _configService.AnnounceIntervalSeconds : 1800);
            entry.AnnounceInterval = interval;
            entry.MinAnnounceInterval = response.MinInterval > 0 ? response.MinInterval : 900;
            entry.NextAnnounce = DateTime.UtcNow.AddSeconds(interval);
            entry.SuccessfulAnnounces++;
            entry.ConsecutiveFailures = 0;
            entry.ErrorMessage = null;
            _trackerEntryService.Update(entry);

            result.AnnounceInterval = interval;

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
        }

        _eventAggregator?.PublishEvent(new TrackerAnnounceEvent(torrent, entry.Url, response.Complete, response.Incomplete, response.Peers?.Count ?? 0, sw.ElapsedMilliseconds, response.Success, response.FailureReason));

        return result;
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
