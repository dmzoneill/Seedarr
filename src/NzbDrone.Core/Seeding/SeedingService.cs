using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Seeding;

public interface ISeedingService
{
    bool Start(int torrentId);
    bool Stop(int torrentId);
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

    public bool Start(int torrentId)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            _logger.Warn("Cannot start seeding: torrent {0} not found", torrentId);
            return false;
        }

        torrent.Resume();
        torrent.Active = true;
        torrent.ForceStart = true;
        torrent.LastActive = DateTime.UtcNow;
        _torrentService.Update(torrent);

        _logger.Info("Started seeding: {0}", torrent.Name);
        _eventAggregator.PublishEvent(new SeedingStartedEvent(torrentId));
        return true;
    }

    public bool Stop(int torrentId)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return false;
        }

        if (torrent.Status == TorrentStatus.Stopped)
        {
            return true;
        }

        torrent.Stop();
        torrent.ForceStart = false;
        _torrentService.Update(torrent);

        _logger.Info("Stopped seeding: {0}", torrent.Name);
        _eventAggregator.PublishEvent(new SeedingStoppedEvent(torrentId));
        return true;
    }

    public void StartAll()
    {
        var torrents = _torrentService.GetAll()
            .Where(t => t.Status == TorrentStatus.Stopped ||
                        t.Status == TorrentStatus.Queued ||
                        t.Status == TorrentStatus.Paused);

        foreach (var torrent in torrents)
        {
            torrent.Resume();
            torrent.Active = true;
            torrent.ForceStart = true;
            torrent.LastActive = DateTime.UtcNow;
            _torrentService.Update(torrent);

            _logger.Info("Started seeding: {0}", torrent.Name);
            _eventAggregator.PublishEvent(new SeedingStartedEvent(torrent.Id));
        }

        _logger.Info("Started seeding all torrents");
    }

    public void StopAll()
    {
        var torrents = _torrentService.GetAll()
            .Where(t => t.Status == TorrentStatus.Seeding ||
                        t.Status == TorrentStatus.Downloading ||
                        t.Active);

        foreach (var torrent in torrents)
        {
            torrent.Stop();
            torrent.ForceStart = false;
            _torrentService.Update(torrent);

            _logger.Info("Stopped seeding: {0}", torrent.Name);
            _eventAggregator.PublishEvent(new SeedingStoppedEvent(torrent.Id));
        }

        _logger.Info("Stopped seeding all torrents");
    }

    public SeedingStats GetStats()
    {
        var all = _torrentService.GetAll();
        var active = all.Where(t => t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Downloading).ToList();

        return new SeedingStats
        {
            ActiveTorrents = active.Count,
            TotalUploaded = all.Sum(t => t.Uploaded),
            TotalDownloaded = all.Sum(t => t.Downloaded),
            AverageRatio = active.Count > 0 ? active.Average(t => t.Ratio) : 0
        };
    }
}
