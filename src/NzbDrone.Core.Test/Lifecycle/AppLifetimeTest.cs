using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Torrents;
using NzbDrone.Host;

namespace NzbDrone.Core.Test.Lifecycle;

[TestFixture]
public class AppLifetimeTest
{
    private IEventAggregator _eventAggregator;
    private ITorrentService _torrentService;
    private IConfigService _configService;
    private IDiskSpaceService _diskSpaceService;
    private IUpnpService _upnpService;
    private AppLifetime _subject;

    [SetUp]
    public void SetUp()
    {
        _eventAggregator = Substitute.For<IEventAggregator>();
        _torrentService = Substitute.For<ITorrentService>();
        _configService = Substitute.For<IConfigService>();
        _diskSpaceService = Substitute.For<IDiskSpaceService>();
        _upnpService = Substitute.For<IUpnpService>();

        _subject = new AppLifetime(
            _eventAggregator,
            null,
            _torrentService,
            _configService,
            _diskSpaceService,
            _upnpService);
    }

    [TearDown]
    public void TearDown()
    {
        _subject.Dispose();
    }

    [Test]
    public void StalledTorrents_should_publish_TorrentStalledEvent_once_when_entering_stall()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 0,
            Progress = 0.5,
            DateAdded = DateTime.UtcNow.AddMinutes(-10)
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        _subject.EvaluateWatchdogMetrics();

        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentStalledEvent>(e => e.Torrent.Id == 1 && e.StalledMinutes >= 5));
    }

    [Test]
    public void StalledTorrents_should_not_publish_duplicate_TorrentStalledEvent_on_consecutive_ticks()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 0,
            Progress = 0.5,
            DateAdded = DateTime.UtcNow.AddMinutes(-10)
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        _subject.EvaluateWatchdogMetrics();
        _subject.EvaluateWatchdogMetrics();

        _eventAggregator.Received(1).PublishEvent(Arg.Any<TorrentStalledEvent>());
    }

    [Test]
    public void StalledTorrents_should_publish_TorrentStallResolvedEvent_when_download_speed_resumes()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 0,
            Progress = 0.5,
            DateAdded = DateTime.UtcNow.AddMinutes(-10)
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _subject.EvaluateWatchdogMetrics();

        // Speed resumes
        torrent.DownloadSpeed = 50000;
        _subject.EvaluateWatchdogMetrics();

        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentStallResolvedEvent>(e => e.Torrent.Id == 1));

        // Subsequent tick with active speed should not re-emit resolved
        _subject.EvaluateWatchdogMetrics();
        _eventAggregator.Received(1).PublishEvent(Arg.Any<TorrentStallResolvedEvent>());
    }

    [Test]
    public void StalledTorrents_should_publish_TorrentStallResolvedEvent_when_torrent_finishes()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 0,
            Progress = 0.5,
            DateAdded = DateTime.UtcNow.AddMinutes(-10)
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _subject.EvaluateWatchdogMetrics();

        // Torrent completes
        torrent.Progress = 1.0;
        torrent.Status = TorrentStatus.Seeding;
        _subject.EvaluateWatchdogMetrics();

        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentStallResolvedEvent>(e => e.Torrent.Id == 1));
    }

    [Test]
    public void StalledTorrents_should_publish_TorrentStallResolvedEvent_when_torrent_is_removed()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 0,
            Progress = 0.5,
            DateAdded = DateTime.UtcNow.AddMinutes(-10)
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _subject.EvaluateWatchdogMetrics();

        // Torrent deleted / removed
        _torrentService.GetAll().Returns(new List<Torrent>());
        _subject.EvaluateWatchdogMetrics();

        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentStallResolvedEvent>(e => e.Torrent.Id == 1));
    }

    [Test]
    public void SpeedThreshold_should_publish_SpeedThresholdExceededEvent_once_on_threshold_crossing()
    {
        _configService.MaxDownloadSpeedKbps.Returns(100);
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 200 * 1024,
            Progress = 0.2
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        _subject.EvaluateWatchdogMetrics();
        _subject.EvaluateWatchdogMetrics();

        _eventAggregator.Received(1).PublishEvent(Arg.Is<SpeedThresholdExceededEvent>(e => e.DownloadSpeed == 200 * 1024));
    }

    [Test]
    public void SpeedThreshold_should_publish_SpeedThresholdDroppedEvent_when_traffic_drops_below_threshold()
    {
        _configService.MaxDownloadSpeedKbps.Returns(100);
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 200 * 1024,
            Progress = 0.2
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _subject.EvaluateWatchdogMetrics();

        // Speed drops below threshold
        torrent.DownloadSpeed = 50 * 1024;
        _subject.EvaluateWatchdogMetrics();

        _eventAggregator.Received(1).PublishEvent(Arg.Any<SpeedThresholdDroppedEvent>());

        // Subsequent tick below threshold should not re-emit
        _subject.EvaluateWatchdogMetrics();
        _eventAggregator.Received(1).PublishEvent(Arg.Any<SpeedThresholdDroppedEvent>());
    }

    [Test]
    public void PortForwarding_should_publish_PortForwardingFailedEvent_once_when_upnp_is_unavailable()
    {
        _configService.UpnpEnabled.Returns(true);
        _configService.ListeningPort.Returns(6881);
        _upnpService.IsAvailable.Returns(false);

        _subject.EvaluateWatchdogMetrics();
        _subject.EvaluateWatchdogMetrics();

        _eventAggregator.Received(1).PublishEvent(Arg.Is<PortForwardingFailedEvent>(e => e.Port == 6881));
    }

    [Test]
    public void PortForwarding_should_rearm_when_upnp_becomes_available_again()
    {
        _configService.UpnpEnabled.Returns(true);
        _configService.ListeningPort.Returns(6881);

        // Unavailable
        _upnpService.IsAvailable.Returns(false);
        _subject.EvaluateWatchdogMetrics();

        // Recovers
        _upnpService.IsAvailable.Returns(true);
        _subject.EvaluateWatchdogMetrics();

        // Fails again
        _upnpService.IsAvailable.Returns(false);
        _subject.EvaluateWatchdogMetrics();

        _eventAggregator.Received(2).PublishEvent(Arg.Is<PortForwardingFailedEvent>(e => e.Port == 6881));
    }
}
