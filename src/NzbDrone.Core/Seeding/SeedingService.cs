using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Seeding;

public interface ISeedingService
{
    void Start(int torrentId);
    void Stop(int torrentId);
    void StartAll();
    void StopAll();
    SeedingStats GetStats();
}

public class SeedingStats
{
    public int ActiveTorrents { get; set; }
    public long TotalUploaded { get; set; }
    public long TotalDownloaded { get; set; }
    public double AverageRatio { get; set; }
}

public class SeedingService : ISeedingService
{
    private readonly ITorrentService _torrentService;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;

    public SeedingService(ITorrentService torrentService, IEventAggregator eventAggregator)
    {
        _torrentService = torrentService;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Start(int torrentId)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            _logger.Warn("Cannot start seeding: torrent {0} not found", torrentId);
            return;
        }

        torrent.Status = TorrentStatus.Seeding;
        torrent.LastActive = DateTime.UtcNow;
        _torrentService.Update(torrent);

        _logger.Info("Started seeding: {0}", torrent.Name);
        _eventAggregator.PublishEvent(new SeedingStartedEvent(torrentId));
    }

    public void Stop(int torrentId)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return;
        }

        torrent.Status = TorrentStatus.Stopped;
        _torrentService.Update(torrent);

        _logger.Info("Stopped seeding: {0}", torrent.Name);
        _eventAggregator.PublishEvent(new SeedingStoppedEvent(torrentId));
    }

    public void StartAll()
    {
        var torrents = _torrentService.GetAll()
            .Where(t => t.Status == TorrentStatus.Stopped || t.Status == TorrentStatus.Queued);

        foreach (var torrent in torrents)
        {
            torrent.Status = TorrentStatus.Seeding;
            torrent.LastActive = DateTime.UtcNow;
            _torrentService.Update(torrent);
        }

        _logger.Info("Started seeding all torrents");
    }

    public void StopAll()
    {
        var torrents = _torrentService.GetAll()
            .Where(t => t.Status == TorrentStatus.Seeding);

        foreach (var torrent in torrents)
        {
            torrent.Status = TorrentStatus.Stopped;
            _torrentService.Update(torrent);
        }

        _logger.Info("Stopped seeding all torrents");
    }

    public SeedingStats GetStats()
    {
        var all = _torrentService.GetAll();
        var active = all.Where(t => t.Status == TorrentStatus.Seeding).ToList();

        return new SeedingStats
        {
            ActiveTorrents = active.Count,
            TotalUploaded = all.Sum(t => t.Uploaded),
            TotalDownloaded = all.Sum(t => t.Downloaded),
            AverageRatio = active.Count > 0 ? active.Average(t => t.Ratio) : 0
        };
    }
}
