using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers.Metrics;
using NzbDrone.Core.Trackers.MultiTracker;

namespace NzbDrone.Core.Trackers;

public interface ITrackerAnnounceService
{
    List<TrackerAnnounceResult> AnnounceTorrent(Torrent torrent, bool force = false);
    TrackerAnnounceResult AnnounceTracker(Torrent torrent, TrackerEntry entry, bool force = false);
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

public class TrackerAnnounceService : ITrackerAnnounceService
{
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly IMultiTrackerManager _multiTracker;
    private readonly IPeerDiscoveryService _peerDiscovery;
    private readonly ITorrentEventLogService _eventLogService;
    private readonly IConfigService _configService;
    private readonly ITrackerMetricService _trackerMetricService;
    private readonly Logger _logger;

    public TrackerAnnounceService(
        ITrackerEntryService trackerEntryService,
        IMultiTrackerManager multiTracker,
        IPeerDiscoveryService peerDiscovery,
        ITorrentEventLogService eventLogService,
        IConfigService configService,
        ITrackerMetricService trackerMetricService = null)
    {
        _trackerEntryService = trackerEntryService;
        _multiTracker = multiTracker;
        _peerDiscovery = peerDiscovery;
        _eventLogService = eventLogService;
        _configService = configService;
        _trackerMetricService = trackerMetricService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public List<TrackerAnnounceResult> AnnounceTorrent(Torrent torrent, bool force = false)
    {
        var results = new List<TrackerAnnounceResult>();
        if (torrent == null || string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
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
                Status = TrackerStatus.Working,
                Enabled = true
            };
            entry = _trackerEntryService.Add(entry);
            trackerEntries = new List<TrackerEntry> { entry };
        }

        var enabledTrackers = trackerEntries.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.Url)).ToList();
        if (enabledTrackers.Count == 0)
        {
            return results;
        }

        foreach (var entry in enabledTrackers)
        {
            var isFirstAnnounce = entry.TotalAnnounces == 0 || !entry.LastAnnounce.HasValue;
            if (!force && !isFirstAnnounce && entry.NextAnnounce.HasValue && entry.NextAnnounce.Value > DateTime.UtcNow)
            {
                continue;
            }

            var result = ExecuteAnnounce(torrent, entry, isFirstAnnounce);
            results.Add(result);
        }

        return results;
    }

    public TrackerAnnounceResult AnnounceTracker(Torrent torrent, TrackerEntry entry, bool force = false)
    {
        if (torrent == null || entry == null || string.IsNullOrWhiteSpace(entry.Url))
        {
            return new TrackerAnnounceResult { Success = false, FailureReason = "Invalid torrent or tracker" };
        }

        var isFirstAnnounce = entry.TotalAnnounces == 0 || !entry.LastAnnounce.HasValue;
        if (!force && !isFirstAnnounce && entry.NextAnnounce.HasValue && entry.NextAnnounce.Value > DateTime.UtcNow)
        {
            return new TrackerAnnounceResult { Success = false, FailureReason = "Tracker not due for announce yet" };
        }

        return ExecuteAnnounce(torrent, entry, isFirstAnnounce);
    }

    private TrackerAnnounceResult ExecuteAnnounce(Torrent torrent, TrackerEntry entry, bool isFirstAnnounce)
    {
        var eventName = isFirstAnnounce ? "started" : (torrent.Status == TorrentStatus.Stopped ? "stopped" : "regular");

        _eventLogService.Info(
            torrent.Id,
            "Tracker",
            $"Announcing to tracker: {entry.Url} (event: {eventName}, uploaded: {torrent.Uploaded:N0} bytes, left: {Math.Max(0, torrent.TotalSize - torrent.Downloaded):N0} bytes)");

        var request = new TrackerAnnounceRequest
        {
            InfoHash = torrent.InfoHash,
            PeerId = "-SD1000-000000000000",
            Port = _configService.ListeningPort,
            Uploaded = torrent.Uploaded,
            Downloaded = torrent.Downloaded,
            Left = Math.Max(0, torrent.TotalSize - torrent.Downloaded),
            Event = isFirstAnnounce ? "started" : null,
            TrackerUrl = entry.Url,
            Compact = true,
            NumWant = 50
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
            Math.Max(0, torrent.TotalSize - torrent.Downloaded),
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
        }

        return result;
    }
}
