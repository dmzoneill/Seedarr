using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
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
    private IFastResumeService _fastResumeService;
    private ITrackerAnnounceService _trackerAnnounceService;
    private IConnectionManager _connectionManager;
    private IPieceStorage _pieceStorage;
    private IMainDatabase _mainDatabase;
    private IPeerServer _peerServer;
    private AppLifetime _subject;

    [SetUp]
    public void SetUp()
    {
        _eventAggregator = Substitute.For<IEventAggregator>();
        _torrentService = Substitute.For<ITorrentService>();
        _configService = Substitute.For<IConfigService>();
        _diskSpaceService = Substitute.For<IDiskSpaceService>();
        _upnpService = Substitute.For<IUpnpService>();
        _fastResumeService = Substitute.For<IFastResumeService>();
        _trackerAnnounceService = Substitute.For<ITrackerAnnounceService>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _pieceStorage = Substitute.For<IPieceStorage>();
        _mainDatabase = Substitute.For<IMainDatabase>();
        _peerServer = Substitute.For<IPeerServer>();

        _subject = new AppLifetime(
            _eventAggregator,
            null,
            _torrentService,
            _configService,
            _diskSpaceService,
            _upnpService,
            _fastResumeService,
            _trackerAnnounceService,
            _connectionManager,
            _pieceStorage,
            _mainDatabase,
            _peerServer);
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

    [Test]
    public async Task StopAsync_should_orchestrate_complete_phased_shutdown_sequence()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Status = TorrentStatus.Downloading,
            Active = true,
            Uploaded = 5000,
            Downloaded = 10000
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        await _subject.StopAsync(CancellationToken.None);

        // Phase 1: Halt inbound traffic and send event=stopped announce
        _peerServer.Received(1).StopListening();
        _trackerAnnounceService.Received(1).AnnounceTorrent(
            Arg.Is<Torrent>(t => t.Id == 1),
            force: true,
            eventType: AnnounceEvent.Stopped);

        // Phase 2: State and buffer serialization
        _pieceStorage.Received(1).Flush();
        _fastResumeService.Received(1).SaveAll();
        _torrentService.Received(1).UpdateMany(Arg.Is<List<Torrent>>(list => list.Contains(torrent)));

        // Phase 3: Connection teardown and database checkpoint
        await _connectionManager.Received(1).DisconnectAllAsync();
        _mainDatabase.Received(1).Optimize();
        _mainDatabase.Received(1).Checkpoint(WalCheckpointMode.Truncate);
    }

    [Test]
    public async Task StopAsync_should_invoke_FastResumeService_SaveAll()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());

        await _subject.StopAsync(CancellationToken.None);

        _fastResumeService.Received(1).SaveAll();
    }

    [Test]
    public async Task StopAsync_should_announce_stopped_only_for_active_torrents()
    {
        var activeTorrent = new Torrent
        {
            Id = 1,
            InfoHash = "1111111111111111111111111111111111111111",
            Status = TorrentStatus.Seeding,
            Active = true
        };
        var stoppedTorrent = new Torrent
        {
            Id = 2,
            InfoHash = "2222222222222222222222222222222222222222",
            Status = TorrentStatus.Stopped,
            Active = false
        };

        _torrentService.GetAll().Returns(new List<Torrent> { activeTorrent, stoppedTorrent });

        await _subject.StopAsync(CancellationToken.None);

        _trackerAnnounceService.Received(1).AnnounceTorrent(
            Arg.Is<Torrent>(t => t.Id == 1),
            force: true,
            eventType: AnnounceEvent.Stopped);
        _trackerAnnounceService.DidNotReceive().AnnounceTorrent(
            Arg.Is<Torrent>(t => t.Id == 2),
            Arg.Any<bool>(),
            Arg.Any<AnnounceEvent>());
    }

    [Test]
    public async Task StopAsync_should_disconnect_peer_connections_gracefully()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());

        await _subject.StopAsync(CancellationToken.None);

        await _connectionManager.Received(1).DisconnectAllAsync();
    }

    [Test]
    public async Task StopAsync_should_continue_shutdown_when_tracker_announces_fail()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Status = TorrentStatus.Downloading,
            Active = true
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _trackerAnnounceService.When(x => x.AnnounceTorrent(Arg.Any<Torrent>(), Arg.Any<bool>(), Arg.Any<AnnounceEvent>()))
            .Do(_ => throw new InvalidOperationException("Tracker unreachable"));

        Assert.DoesNotThrowAsync(async () => await _subject.StopAsync(CancellationToken.None));

        _fastResumeService.Received(1).SaveAll();
        _pieceStorage.Received(1).Flush();
        await _connectionManager.Received(1).DisconnectAllAsync();
        _mainDatabase.Received(1).Checkpoint(WalCheckpointMode.Truncate);
    }

    [Test]
    public async Task StartAsync_should_invoke_FastResumeService_LoadAll()
    {
        await _subject.StartAsync(CancellationToken.None);

        _fastResumeService.Received(1).LoadAll();
    }

    [Test]
    public async Task StopAsync_should_continue_checkpoint_when_optimize_fails()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());
        _mainDatabase.When(x => x.Optimize()).Do(_ => throw new InvalidOperationException("Optimize failed"));

        Assert.DoesNotThrowAsync(async () => await _subject.StopAsync(CancellationToken.None));

        _mainDatabase.Received(1).Checkpoint(WalCheckpointMode.Truncate);
    }

    [Test]
    public async Task StopAsync_should_publish_ApplicationShutdownRequested()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());

        await _subject.StopAsync(CancellationToken.None);

        _eventAggregator.Received(1).PublishEvent(Arg.Any<ApplicationShutdownRequested>());
    }

    [Test]
    public async Task StopAsync_should_checkpoint_wal_via_maintenance_service_when_main_database_is_null()
    {
        var maintenanceService = Substitute.For<IDatabaseMaintenanceService>();
        var subject = new AppLifetime(
            _eventAggregator,
            null,
            _torrentService,
            _configService,
            _diskSpaceService,
            _upnpService,
            _fastResumeService,
            _trackerAnnounceService,
            _connectionManager,
            _pieceStorage,
            null,
            _peerServer,
            maintenanceService);

        _torrentService.GetAll().Returns(new List<Torrent>());

        await subject.StopAsync(CancellationToken.None);

        maintenanceService.Received(1).CheckpointWal(WalCheckpointMode.Truncate);
    }
}
