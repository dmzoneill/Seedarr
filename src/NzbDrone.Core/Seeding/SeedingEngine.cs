using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Seeding.Distribution;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Seeding;

public class SeedingEngine : BackgroundService
{
    private readonly ITorrentService _torrentService;
    private readonly ISpeedDistributionManager _distributionManager;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    public SeedingEngine(
        ITorrentService torrentService,
        ISpeedDistributionManager distributionManager,
        IEventAggregator eventAggregator)
    {
        _torrentService = torrentService;
        _distributionManager = distributionManager;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("Seeding engine started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Seeding engine tick error");
            }

            await Task.Delay(TickInterval, stoppingToken);
        }
    }

    private void Tick()
    {
        var torrents = _torrentService.GetAll()
            .Where(t => t.Status == TorrentStatus.Seeding)
            .ToList();

        if (torrents.Count == 0)
        {
            return;
        }

        var speeds = _distributionManager.DistributeSpeeds(torrents.Count);

        for (var i = 0; i < torrents.Count; i++)
        {
            var torrent = torrents[i];
            var bytesPerSecond = speeds[i];
            var bytesThisTick = (long)(bytesPerSecond * TickInterval.TotalSeconds);

            torrent.Uploaded += bytesThisTick;
            torrent.Ratio = torrent.TotalSize > 0
                ? Math.Round((double)torrent.Uploaded / torrent.TotalSize, 3)
                : 0;

            _torrentService.Update(torrent);
        }

        _eventAggregator.PublishEvent(new SeedingTickEvent(torrents.Count));
    }
}
