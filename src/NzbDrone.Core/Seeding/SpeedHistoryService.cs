using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Seeding;

public class SpeedSnapshot
{
    public DateTime Timestamp { get; set; }
    public long UploadSpeed { get; set; }
    public long DownloadSpeed { get; set; }
    public int ActiveTorrents { get; set; }
    public int TotalPeers { get; set; }
    public double AverageRatio { get; set; }
    public long TotalUploaded { get; set; }
    public long TotalDownloaded { get; set; }
}

public class TorrentSpeedSnapshot
{
    public DateTime Timestamp { get; set; }
    public long UploadSpeed { get; set; }
    public long DownloadSpeed { get; set; }
}

public interface ISpeedHistoryService
{
    List<SpeedSnapshot> GetHistory();
    List<TorrentSpeedSnapshot> GetTorrentHistory(int torrentId);
}

public class SpeedHistoryService : BackgroundService, ISpeedHistoryService
{
    private const int MaxSnapshots = 300;

    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(5);

    private readonly ITorrentService _torrentService;
    private readonly Logger _logger;
    private readonly LinkedList<SpeedSnapshot> _snapshots = new LinkedList<SpeedSnapshot>();
    private readonly Dictionary<int, LinkedList<TorrentSpeedSnapshot>> _torrentSnapshots = new Dictionary<int, LinkedList<TorrentSpeedSnapshot>>();
    private readonly Dictionary<int, long> _prevTorrentUploaded = new Dictionary<int, long>();
    private readonly Dictionary<int, long> _prevTorrentDownloaded = new Dictionary<int, long>();
    private readonly HashSet<int> _prevActiveTorrentIds = new HashSet<int>();
    private readonly Dictionary<int, int> _inactiveSnapshotCounts = new Dictionary<int, int>();
    private readonly object _lock = new object();

    private long _prevUploaded;
    private long _prevDownloaded;
    private DateTime _prevTime;
    private bool _hasPrev;

    public SpeedHistoryService(ITorrentService torrentService)
    {
        _torrentService = torrentService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("Speed history service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                RecordSnapshot();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Speed history snapshot error");
            }

            await Task.Delay(SnapshotInterval, stoppingToken);
        }
    }

    private void RecordSnapshot()
    {
        RecordSnapshotAt(DateTime.UtcNow);
    }

    private void RecordSnapshotAt(DateTime now)
    {
        var all = _torrentService.GetAll();
        var active = all.Where(t => t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Downloading).ToList();

        var totalUploaded = all.Sum(t => t.Uploaded);
        var totalDownloaded = all.Sum(t => t.Downloaded);

        long uploadSpeed = 0;
        long downloadSpeed = 0;

        lock (_lock)
        {
            var wasPrev = _hasPrev;
            var timeDelta = wasPrev ? (now - _prevTime).TotalSeconds : 0;
            if (timeDelta <= 0)
            {
                timeDelta = 0.001;
            }

            long uploadBytesDelta = 0;
            long downloadBytesDelta = 0;
            var currentActiveIds = new HashSet<int>(active.Select(t => t.Id));

            foreach (var torrent in all)
            {
                if (!_torrentSnapshots.TryGetValue(torrent.Id, out var list))
                {
                    list = new LinkedList<TorrentSpeedSnapshot>();
                    _torrentSnapshots[torrent.Id] = list;
                }

                var isActive = torrent.Status == TorrentStatus.Seeding || torrent.Status == TorrentStatus.Downloading;
                var wasActive = _prevActiveTorrentIds.Contains(torrent.Id);
                var isDelayedResume = isActive && (!wasActive && (timeDelta > SnapshotInterval.TotalSeconds * 1.5 || (_inactiveSnapshotCounts.TryGetValue(torrent.Id, out var count) && count > 1)));

                long torrentUpSpeed = 0;
                long torrentDlSpeed = 0;

                if (!isActive)
                {
                    _inactiveSnapshotCounts[torrent.Id] = _inactiveSnapshotCounts.GetValueOrDefault(torrent.Id, 0) + 1;
                    _prevTorrentUploaded[torrent.Id] = torrent.Uploaded;
                    _prevTorrentDownloaded[torrent.Id] = torrent.Downloaded;
                }
                else if (!_prevTorrentUploaded.ContainsKey(torrent.Id) || isDelayedResume || !wasPrev)
                {
                    // Newly tracked or resumed after delay: re-initialize baseline without byte delta spikes
                    _prevTorrentUploaded[torrent.Id] = torrent.Uploaded;
                    _prevTorrentDownloaded[torrent.Id] = torrent.Downloaded;
                    _inactiveSnapshotCounts[torrent.Id] = 0;
                }
                else
                {
                    var prevUp = _prevTorrentUploaded[torrent.Id];
                    var prevDl = _prevTorrentDownloaded[torrent.Id];

                    if (torrent.Uploaded >= prevUp)
                    {
                        var deltaUp = torrent.Uploaded - prevUp;
                        uploadBytesDelta += deltaUp;
                        torrentUpSpeed = (long)Math.Max(0, deltaUp / timeDelta);
                    }

                    if (torrent.Downloaded >= prevDl)
                    {
                        var deltaDl = torrent.Downloaded - prevDl;
                        downloadBytesDelta += deltaDl;
                        torrentDlSpeed = (long)Math.Max(0, deltaDl / timeDelta);
                    }

                    _prevTorrentUploaded[torrent.Id] = torrent.Uploaded;
                    _prevTorrentDownloaded[torrent.Id] = torrent.Downloaded;
                    _inactiveSnapshotCounts[torrent.Id] = 0;
                }

                list.AddLast(new TorrentSpeedSnapshot
                {
                    Timestamp = now,
                    UploadSpeed = torrentUpSpeed,
                    DownloadSpeed = torrentDlSpeed
                });

                while (list.Count > MaxSnapshots)
                {
                    list.RemoveFirst();
                }
            }

            if (wasPrev && timeDelta > 0)
            {
                uploadSpeed = (long)Math.Max(0, uploadBytesDelta / timeDelta);
                downloadSpeed = (long)Math.Max(0, downloadBytesDelta / timeDelta);
            }

            _prevUploaded = totalUploaded;
            _prevDownloaded = totalDownloaded;
            _prevTime = now;
            _hasPrev = true;

            _prevActiveTorrentIds.Clear();
            foreach (var id in currentActiveIds)
            {
                _prevActiveTorrentIds.Add(id);
            }

            var totalPeers = all.Sum(t => t.Seeders + t.Leechers);
            var avgRatio = active.Count > 0 ? active.Average(t => t.Ratio) : 0;

            var snapshot = new SpeedSnapshot
            {
                Timestamp = now,
                UploadSpeed = uploadSpeed,
                DownloadSpeed = downloadSpeed,
                ActiveTorrents = active.Count,
                TotalPeers = totalPeers,
                AverageRatio = Math.Round(avgRatio, 3),
                TotalUploaded = totalUploaded,
                TotalDownloaded = totalDownloaded
            };

            _snapshots.AddLast(snapshot);
            while (_snapshots.Count > MaxSnapshots)
            {
                _snapshots.RemoveFirst();
            }

            var existingIds = new HashSet<int>(all.Select(t => t.Id));
            var deletedIds = _torrentSnapshots.Keys.Where(id => !existingIds.Contains(id)).ToList();
            foreach (var id in deletedIds)
            {
                _torrentSnapshots.Remove(id);
                _prevTorrentUploaded.Remove(id);
                _prevTorrentDownloaded.Remove(id);
                _inactiveSnapshotCounts.Remove(id);
                _prevActiveTorrentIds.Remove(id);
            }

            var orphanPrevIds = _prevTorrentUploaded.Keys.Where(id => !existingIds.Contains(id)).ToList();
            foreach (var id in orphanPrevIds)
            {
                _prevTorrentUploaded.Remove(id);
                _prevTorrentDownloaded.Remove(id);
                _inactiveSnapshotCounts.Remove(id);
                _prevActiveTorrentIds.Remove(id);
            }
        }
    }

    public List<SpeedSnapshot> GetHistory()
    {
        lock (_lock)
        {
            return _snapshots.ToList();
        }
    }

    public List<TorrentSpeedSnapshot> GetTorrentHistory(int torrentId)
    {
        lock (_lock)
        {
            if (_torrentSnapshots.TryGetValue(torrentId, out var list))
            {
                return list.ToList();
            }

            return new List<TorrentSpeedSnapshot>();
        }
    }
}
