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
            var timeDelta = _hasPrev ? (now - _prevTime).TotalSeconds : 0;
            if (timeDelta <= 0)
            {
                timeDelta = 0.001;
            }

            if (_hasPrev)
            {
                long uploadBytesDelta = 0;
                long downloadBytesDelta = 0;

                foreach (var torrent in all)
                {
                    if (_prevTorrentUploaded.TryGetValue(torrent.Id, out var prevUp))
                    {
                        if (torrent.Uploaded >= prevUp)
                        {
                            uploadBytesDelta += torrent.Uploaded - prevUp;
                        }
                    }

                    if (_prevTorrentDownloaded.TryGetValue(torrent.Id, out var prevDown))
                    {
                        if (torrent.Downloaded >= prevDown)
                        {
                            downloadBytesDelta += torrent.Downloaded - prevDown;
                        }
                    }
                }

                uploadSpeed = (long)Math.Max(0, uploadBytesDelta / timeDelta);
                downloadSpeed = (long)Math.Max(0, downloadBytesDelta / timeDelta);
            }

            _prevUploaded = totalUploaded;
            _prevDownloaded = totalDownloaded;
            _prevTime = now;
            var wasPrev = _hasPrev;
            _hasPrev = true;

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

            foreach (var torrent in all)
            {
                if (!_torrentSnapshots.TryGetValue(torrent.Id, out var list))
                {
                    list = new LinkedList<TorrentSpeedSnapshot>();
                    _torrentSnapshots[torrent.Id] = list;
                }

                var isActive = torrent.Status == TorrentStatus.Seeding || torrent.Status == TorrentStatus.Downloading;
                long torrentUpSpeed = 0;
                long torrentDlSpeed = 0;

                if (isActive && wasPrev && timeDelta > 0)
                {
                    if (_prevTorrentUploaded.TryGetValue(torrent.Id, out var prevUp) && torrent.Uploaded >= prevUp)
                    {
                        torrentUpSpeed = Math.Max(0, (long)((torrent.Uploaded - prevUp) / timeDelta));
                    }

                    if (_prevTorrentDownloaded.TryGetValue(torrent.Id, out var prevDl) && torrent.Downloaded >= prevDl)
                    {
                        torrentDlSpeed = Math.Max(0, (long)((torrent.Downloaded - prevDl) / timeDelta));
                    }
                }

                _prevTorrentUploaded[torrent.Id] = torrent.Uploaded;
                _prevTorrentDownloaded[torrent.Id] = torrent.Downloaded;

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

            var existingIds = new HashSet<int>(all.Select(t => t.Id));
            var deletedIds = _torrentSnapshots.Keys.Where(id => !existingIds.Contains(id)).ToList();
            foreach (var id in deletedIds)
            {
                _torrentSnapshots.Remove(id);
                _prevTorrentUploaded.Remove(id);
                _prevTorrentDownloaded.Remove(id);
            }

            var orphanPrevIds = _prevTorrentUploaded.Keys.Where(id => !existingIds.Contains(id)).ToList();
            foreach (var id in orphanPrevIds)
            {
                _prevTorrentUploaded.Remove(id);
                _prevTorrentDownloaded.Remove(id);
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
