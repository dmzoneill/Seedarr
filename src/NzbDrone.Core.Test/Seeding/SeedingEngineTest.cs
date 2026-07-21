using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Seeding.Distribution;
using NzbDrone.Core.Seeding.Scheduling;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerServer;

namespace NzbDrone.Core.Test.Seeding;

[TestFixture]
public class SeedingEngineTest
{
    private ITorrentService _torrentService;
    private ISpeedDistributionManager _distributionManager;
    private ISpeedScheduler _speedScheduler;
    private IConfigService _configService;
    private IEventAggregator _eventAggregator;
    private IPeerDatabase _peerDatabase;
    private IConnectionManager _connectionManager;
    private SeedingEngine _engine;

    [SetUp]
    public void Setup()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _distributionManager = Substitute.For<ISpeedDistributionManager>();
        _speedScheduler = Substitute.For<ISpeedScheduler>();
        _configService = Substitute.For<IConfigService>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _peerDatabase = Substitute.For<IPeerDatabase>();
        _connectionManager = Substitute.For<IConnectionManager>();

        _configService.AutoStart.Returns(true);
        _configService.AlternativeSpeedEnabled.Returns(false);
        _configService.MaxUploadSpeedKbps.Returns(0);
        _configService.MaxDownloadSpeedKbps.Returns(0);
        _configService.AltUploadSpeedKbps.Returns(50);
        _configService.AltDownloadSpeedKbps.Returns(100);
        _configService.SpeedVariationMin.Returns(1.0);
        _configService.SpeedVariationMax.Returns(1.0);
        _configService.DownloadThresholdPercent.Returns(100);
        _configService.GlobalSeedRatioLimit.Returns(0.0);
        _configService.SeederUploadActivityProbability.Returns(1.0);
        _configService.UploadStoppedMinPercentage.Returns(0);
        _configService.UploadStoppedMaxPercentage.Returns(0);
        _configService.DownloadStoppedMinPercentage.Returns(0);
        _configService.DownloadStoppedMaxPercentage.Returns(0);

        _speedScheduler.GetCurrentLimits().Returns(new SpeedLimits
        {
            MaxUploadSpeed = 1_048_576,
            MaxDownloadSpeed = 1_048_576,
            IsScheduleActive = false
        });

        _peerDatabase.GetStats(Arg.Any<string>()).Returns(new ScrapeStats { Complete = 5, Incomplete = 3 });

        _engine = new SeedingEngine(
            _torrentService,
            _distributionManager,
            _speedScheduler,
            _configService,
            _eventAggregator,
            _peerDatabase,
            _connectionManager);
    }

    private void CallTick()
    {
        var method = typeof(SeedingEngine).GetMethod("Tick",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_engine, null);
    }

    [Test]
    public void Tick_should_deactivate_all_when_no_active_torrents()
    {
        var stopped = new Torrent { Id = 1, Status = TorrentStatus.Stopped, UploadSpeed = 100, Active = true };
        _torrentService.GetAll().Returns(new List<Torrent> { stopped });

        CallTick();

        Assert.That(stopped.UploadSpeed, Is.EqualTo(0));
        Assert.That(stopped.DownloadSpeed, Is.EqualTo(0));
        Assert.That(stopped.Active, Is.False);
        _torrentService.Received(1).Update(stopped);
    }

    [Test]
    public void Tick_should_not_update_inactive_torrent_with_zero_speeds()
    {
        var stopped = new Torrent { Id = 1, Status = TorrentStatus.Stopped, UploadSpeed = 0, DownloadSpeed = 0, Active = false };
        _torrentService.GetAll().Returns(new List<Torrent> { stopped });

        CallTick();

        _torrentService.DidNotReceive().Update(Arg.Any<Torrent>());
    }

    [Test]
    public void Tick_should_distribute_download_speeds_for_downloading_torrents()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            TotalSize = 1_000_000_000,
            Downloaded = 0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100_000 });

        CallTick();

        Assert.That(torrent.Downloaded, Is.GreaterThan(0));
    }

    [Test]
    public void Tick_should_distribute_upload_speeds_for_seeding_torrents()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000_000,
            Uploaded = 0,
            Downloaded = 1_000_000_000,
            Progress = 1.0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100_000 });

        CallTick();

        Assert.That(torrent.Uploaded, Is.GreaterThan(0));
    }

    [Test]
    public void Tick_should_switch_downloading_to_seeding_when_threshold_reached()
    {
        _configService.DownloadThresholdPercent.Returns(50);
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            TotalSize = 1000,
            Downloaded = 600,
            Progress = 0.6,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
    }

    [Test]
    public void Tick_should_switch_force_completed_to_seeding()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            TotalSize = 1000,
            Downloaded = 100,
            Progress = 0.1,
            ForceCompleted = true,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
    }

    [Test]
    public void Tick_should_stop_torrent_when_global_ratio_limit_reached()
    {
        _configService.GlobalSeedRatioLimit.Returns(2.0);
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Uploaded = 2500,
            Downloaded = 1000,
            Ratio = 2.5,
            Progress = 1.0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Stopped));
        Assert.That(torrent.UploadSpeed, Is.EqualTo(0));
        Assert.That(torrent.Active, Is.False);
    }

    [Test]
    public void Tick_should_not_stop_torrent_when_below_ratio_limit()
    {
        _configService.GlobalSeedRatioLimit.Returns(5.0);
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Uploaded = 500,
            Downloaded = 1000,
            Ratio = 0.5,
            Progress = 1.0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
    }

    [Test]
    public void Tick_should_not_check_ratio_when_limit_is_zero()
    {
        _configService.GlobalSeedRatioLimit.Returns(0.0);
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Uploaded = 100_000,
            Downloaded = 1000,
            Ratio = 100.0,
            Progress = 1.0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
    }

    [Test]
    public void Tick_should_use_alt_speeds_when_alternative_enabled()
    {
        _configService.AlternativeSpeedEnabled.Returns(true);
        _configService.AltUploadSpeedKbps.Returns(50);
        _configService.AltDownloadSpeedKbps.Returns(100);

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Uploaded = 0,
            Progress = 1.0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        _distributionManager.Received().DistributeUploadSpeeds(
            1,
            Arg.Is<long>(s => s <= 50 * 1024),
            Arg.Any<double[]>());
    }

    [Test]
    public void Tick_should_apply_per_torrent_upload_limit()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000_000,
            Uploaded = 0,
            Progress = 1.0,
            UploadLimit = 10,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 1_000_000 });

        CallTick();

        Assert.That(torrent.Uploaded, Is.LessThanOrEqualTo((10 * 1024 * 5) + 1));
    }

    [Test]
    public void Tick_should_apply_per_torrent_download_limit()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            TotalSize = 1_000_000_000,
            Downloaded = 0,
            DownloadLimit = 10,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 1_000_000 });

        CallTick();

        Assert.That(torrent.Downloaded, Is.LessThanOrEqualTo((10 * 1024 * 5) + 1));
    }

    [Test]
    public void Tick_should_boost_super_seeding_speed()
    {
        var normalTorrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000_000,
            Uploaded = 0,
            Progress = 1.0,
            SuperSeeding = false,
            InfoHash = "abc1"
        };
        var superTorrent = new Torrent
        {
            Id = 2,
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000_000,
            Uploaded = 0,
            Progress = 1.0,
            SuperSeeding = true,
            InfoHash = "abc2"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { normalTorrent, superTorrent });
        _distributionManager.DistributeUploadSpeeds(2, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100_000, 100_000 });

        CallTick();

        Assert.That(superTorrent.Uploaded, Is.GreaterThan(normalTorrent.Uploaded));
    }

    [Test]
    public void Tick_should_publish_tick_event()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Progress = 1.0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        _eventAggregator.Received(1).PublishEvent(Arg.Is<SeedingTickEvent>(e => e.ActiveTorrents == 1));
    }

    [Test]
    public void Tick_should_add_peer_for_active_torrents_with_infohash()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Progress = 1.0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        _peerDatabase.Received().AddPeer("abc123", "127.0.0.1", 6881, Arg.Any<string>());
    }

    [Test]
    public void Tick_should_not_add_peer_when_infohash_empty()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Progress = 1.0,
            InfoHash = ""
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        _peerDatabase.DidNotReceive().AddPeer(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>());
    }

    [Test]
    public void Tick_should_call_connection_manager_process_and_rotate()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Progress = 1.0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        _connectionManager.Received(1).ProcessDropouts();
        _connectionManager.Received(1).RotateConnections();
    }

    [Test]
    public void Tick_should_update_seeders_and_leechers_from_peer_database()
    {
        _peerDatabase.GetStats("abc123").Returns(new ScrapeStats { Complete = 10, Incomplete = 7 });

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Progress = 1.0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Seeders, Is.EqualTo(10));
        Assert.That(torrent.Leechers, Is.EqualTo(7));
    }

    [Test]
    public void Tick_should_set_threshold_if_zero()
    {
        _configService.DownloadThresholdPercent.Returns(30);

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Progress = 1.0,
            Threshold = 0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Threshold, Is.EqualTo(30));
    }

    [Test]
    public void Tick_should_not_overwrite_existing_threshold()
    {
        _configService.DownloadThresholdPercent.Returns(30);

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Progress = 1.0,
            Threshold = 50,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Threshold, Is.EqualTo(50));
    }

    [Test]
    public void Tick_should_use_per_torrent_threshold_over_global()
    {
        _configService.DownloadThresholdPercent.Returns(80);

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            TotalSize = 1000,
            Downloaded = 350,
            Progress = 0.35,
            Threshold = 30,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
    }

    [Test]
    public void Tick_should_not_switch_when_below_per_torrent_threshold()
    {
        _configService.DownloadThresholdPercent.Returns(10);

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            TotalSize = 1_000_000,
            Downloaded = 250_000,
            Progress = 0.25,
            Threshold = 50,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Downloading));
    }

    [Test]
    public void Tick_should_set_active_true_and_last_active()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Progress = 1.0,
            Active = false,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Active, Is.True);
        Assert.That(torrent.LastActive, Is.Not.Null);
    }

    [Test]
    public void Tick_should_calculate_eta_for_downloading_torrent()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            TotalSize = 10_000_000_000,
            Downloaded = 500_000,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 10_000 });

        CallTick();
        CallTick();

        Assert.That(torrent.Eta, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void Tick_should_set_eta_zero_for_seeding_torrent()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Downloaded = 1000,
            Progress = 1.0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Eta, Is.EqualTo(0));
    }

    [Test]
    public void Tick_should_set_availability()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Downloaded = 1000,
            Progress = 1.0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Availability, Is.EqualTo(1.0));
    }

    [Test]
    public void Tick_should_set_partial_availability()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            TotalSize = 1_000_000_000,
            Downloaded = 0,
            Progress = 0.5,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Availability, Is.LessThan(1.0));
    }

    [Test]
    public void Tick_should_calculate_ratio_during_seeding()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Uploaded = 500,
            Progress = 1.0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100_000 });

        CallTick();

        Assert.That(torrent.Ratio, Is.GreaterThan(0));
    }

    [Test]
    public void Tick_should_set_ratio_zero_when_total_size_zero()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 0,
            Uploaded = 500,
            Progress = 1.0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Ratio, Is.EqualTo(0));
    }

    [Test]
    public void Tick_should_deactivate_non_seeding_non_downloading_with_nonzero_speed()
    {
        var seeding = new Torrent { Id = 1, Status = TorrentStatus.Seeding, TotalSize = 1000, Progress = 1.0, InfoHash = "a" };
        var paused = new Torrent { Id = 2, Status = TorrentStatus.Paused, UploadSpeed = 50, Active = true };

        _torrentService.GetAll().Returns(new List<Torrent> { seeding, paused });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(paused.UploadSpeed, Is.EqualTo(0));
        Assert.That(paused.Active, Is.False);
    }

    [Test]
    public void Tick_should_respect_autostart_false_for_non_force_start()
    {
        _configService.AutoStart.Returns(false);

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Progress = 1.0,
            ForceStart = false,
            InfoHash = "abc"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        CallTick();

        _distributionManager.DidNotReceive().DistributeUploadSpeeds(
            Arg.Any<int>(), Arg.Any<long>(), Arg.Any<double[]>());
    }

    [Test]
    public void Tick_should_include_force_start_torrent_when_autostart_false()
    {
        _configService.AutoStart.Returns(false);

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Progress = 1.0,
            ForceStart = true,
            InfoHash = "abc"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        _distributionManager.Received(1).DistributeUploadSpeeds(
            1, Arg.Any<long>(), Arg.Any<double[]>());
    }

    [Test]
    public void Tick_should_use_config_upload_speed_min_with_scheduler()
    {
        _configService.MaxUploadSpeedKbps.Returns(50);
        _speedScheduler.GetCurrentLimits().Returns(new SpeedLimits
        {
            MaxUploadSpeed = 100 * 1024,
            MaxDownloadSpeed = 1_048_576,
            IsScheduleActive = true
        });

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Progress = 1.0,
            InfoHash = "abc"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        _distributionManager.Received().DistributeUploadSpeeds(
            1, Arg.Is<long>(s => s <= 50 * 1024), Arg.Any<double[]>());
    }

    [Test]
    public void Tick_should_calculate_session_uploaded()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Uploaded = 500,
            Progress = 1.0,
            InfoHash = "abc"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100_000 });

        CallTick();

        Assert.That(torrent.SessionUploaded, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void Tick_should_clean_stale_entries()
    {
        var torrent1 = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000_000,
            Uploaded = 0,
            Progress = 1.0,
            InfoHash = "abc"
        };
        var torrent2 = new Torrent
        {
            Id = 2,
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000_000,
            Uploaded = 0,
            Progress = 1.0,
            InfoHash = "def"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });
        _distributionManager.DistributeUploadSpeeds(2, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100, 100 });

        CallTick();

        _eventAggregator.Received(1).PublishEvent(Arg.Is<SeedingTickEvent>(e => e.ActiveTorrents == 2));

        _torrentService.GetAll().Returns(new List<Torrent> { torrent2 });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        _eventAggregator.Received(1).PublishEvent(Arg.Is<SeedingTickEvent>(e => e.ActiveTorrents == 1));
    }

    [Test]
    public void Tick_should_update_progress_for_downloading_torrent()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            TotalSize = 1_000_000,
            Downloaded = 0,
            Progress = 0,
            InfoHash = "abc"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100_000 });

        CallTick();

        Assert.That(torrent.Progress, Is.GreaterThan(0));
        Assert.That(torrent.Downloaded, Is.GreaterThan(0));
    }

    [Test]
    public void Tick_should_continue_download_during_seeding_when_incomplete()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000,
            Downloaded = 500_000,
            Progress = 0.5,
            InfoHash = "abc"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Downloaded, Is.GreaterThan(500_000));
    }

    [Test]
    public void Tick_should_not_download_more_during_seeding_when_force_completed()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000,
            Downloaded = 500_000,
            Progress = 0.5,
            ForceCompleted = true,
            InfoHash = "abc"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(torrent.Downloaded, Is.EqualTo(500_000));
    }

    [Test]
    public void Tick_should_handle_multiple_downloading_and_seeding()
    {
        var dl1 = new Torrent { Id = 1, Status = TorrentStatus.Downloading, TotalSize = 1_000_000_000, Downloaded = 0, InfoHash = "a" };
        var dl2 = new Torrent { Id = 2, Status = TorrentStatus.Downloading, TotalSize = 1_000_000_000, Downloaded = 0, InfoHash = "b" };
        var sd1 = new Torrent { Id = 3, Status = TorrentStatus.Seeding, TotalSize = 1000, Progress = 1.0, InfoHash = "c" };

        _torrentService.GetAll().Returns(new List<Torrent> { dl1, dl2, sd1 });
        _distributionManager.DistributeDownloadSpeeds(2, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 50_000, 50_000 });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        Assert.That(dl1.Downloaded, Is.GreaterThan(0));
        Assert.That(dl2.Downloaded, Is.GreaterThan(0));
    }

    [Test]
    public void Tick_should_set_progress_to_1_when_downloaded_exceeds_total()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            TotalSize = 100,
            Downloaded = 90,
            InfoHash = "abc"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100_000 });

        CallTick();

        Assert.That(torrent.Progress, Is.EqualTo(1.0));
    }

    [Test]
    public void GetPriorityWeight_should_return_2_for_high_priority()
    {
        var method = typeof(SeedingEngine).GetMethod("GetPriorityWeight",
            BindingFlags.NonPublic | BindingFlags.Static);
        var result = (double)method.Invoke(null, new object[] { 2 });

        Assert.That(result, Is.EqualTo(2.0));
    }

    [Test]
    public void GetPriorityWeight_should_return_half_for_low_priority()
    {
        var method = typeof(SeedingEngine).GetMethod("GetPriorityWeight",
            BindingFlags.NonPublic | BindingFlags.Static);
        var result = (double)method.Invoke(null, new object[] { 0 });

        Assert.That(result, Is.EqualTo(0.5));
    }

    [Test]
    public void GetPriorityWeight_should_return_1_for_normal_priority()
    {
        var method = typeof(SeedingEngine).GetMethod("GetPriorityWeight",
            BindingFlags.NonPublic | BindingFlags.Static);
        var result = (double)method.Invoke(null, new object[] { 1 });

        Assert.That(result, Is.EqualTo(1.0));
    }

    [Test]
    public void GetPriorityWeight_should_return_1_for_unknown_priority()
    {
        var method = typeof(SeedingEngine).GetMethod("GetPriorityWeight",
            BindingFlags.NonPublic | BindingFlags.Static);
        var result = (double)method.Invoke(null, new object[] { 99 });

        Assert.That(result, Is.EqualTo(1.0));
    }

    [Test]
    public void Tick_should_cap_download_speed_when_config_download_speed_set()
    {
        _configService.MaxDownloadSpeedKbps.Returns(50);
        _speedScheduler.GetCurrentLimits().Returns(new SpeedLimits
        {
            MaxUploadSpeed = 1_048_576,
            MaxDownloadSpeed = 200 * 1024,
            IsScheduleActive = true
        });

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            TotalSize = 1_000_000_000,
            Downloaded = 0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100_000 });

        CallTick();

        _distributionManager.Received().DistributeDownloadSpeeds(
            1, Arg.Is<long>(s => s <= 50 * 1024), Arg.Any<double[]>());
    }

    [Test]
    public void Tick_should_use_config_download_speed_when_scheduler_is_zero()
    {
        _configService.MaxDownloadSpeedKbps.Returns(100);
        _speedScheduler.GetCurrentLimits().Returns(new SpeedLimits
        {
            MaxUploadSpeed = 1_048_576,
            MaxDownloadSpeed = SpeedLimits.Unlimited,
            IsScheduleActive = false
        });

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            TotalSize = 1_000_000_000,
            Downloaded = 0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100_000 });

        CallTick();

        _distributionManager.Received().DistributeDownloadSpeeds(
            1, Arg.Is<long>(s => s == 100 * 1024), Arg.Any<double[]>());
    }

    [Test]
    public void Tick_should_use_min_of_config_and_scheduler_for_download_speed()
    {
        _configService.MaxDownloadSpeedKbps.Returns(80);
        _speedScheduler.GetCurrentLimits().Returns(new SpeedLimits
        {
            MaxUploadSpeed = 1_048_576,
            MaxDownloadSpeed = 120 * 1024,
            IsScheduleActive = true
        });

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            TotalSize = 1_000_000_000,
            Downloaded = 0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100_000 });

        CallTick();

        _distributionManager.Received().DistributeDownloadSpeeds(
            1, Arg.Is<long>(s => s == 80 * 1024), Arg.Any<double[]>());
    }

    [Test]
    public void Tick_should_use_min_of_config_and_scheduler_for_upload_speed()
    {
        _configService.MaxUploadSpeedKbps.Returns(60);
        _speedScheduler.GetCurrentLimits().Returns(new SpeedLimits
        {
            MaxUploadSpeed = 200 * 1024,
            MaxDownloadSpeed = 1_048_576,
            IsScheduleActive = true
        });

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Progress = 1.0,
            InfoHash = "abc"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        _distributionManager.Received().DistributeUploadSpeeds(
            1, Arg.Is<long>(s => s == 60 * 1024), Arg.Any<double[]>());
    }

    [Test]
    public void Tick_should_zero_upload_when_seeder_activity_probability_zero()
    {
        _configService.SeederUploadActivityProbability.Returns(0.0);

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000_000,
            Uploaded = 0,
            Progress = 1.0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100_000 });

        CallTick();

        Assert.That(torrent.Uploaded, Is.EqualTo(0));
    }

    [Test]
    public void Tick_should_zero_upload_for_all_torrents_when_seeder_inactive()
    {
        _configService.SeederUploadActivityProbability.Returns(0.0);

        var torrent1 = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000_000,
            Uploaded = 0,
            Progress = 1.0,
            InfoHash = "abc1"
        };
        var torrent2 = new Torrent
        {
            Id = 2,
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000_000,
            Uploaded = 0,
            Progress = 1.0,
            InfoHash = "abc2"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });
        _distributionManager.DistributeUploadSpeeds(2, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100_000, 100_000 });

        CallTick();

        Assert.That(torrent1.Uploaded, Is.EqualTo(0));
        Assert.That(torrent2.Uploaded, Is.EqualTo(0));
    }

    [Test]
    public void Tick_should_stop_some_uploads_when_upload_stopped_percentage_set()
    {
        _configService.UploadStoppedMinPercentage.Returns(50);
        _configService.UploadStoppedMaxPercentage.Returns(50);

        var torrents = new List<Torrent>();
        for (var i = 0; i < 10; i++)
        {
            torrents.Add(new Torrent
            {
                Id = i + 1,
                Status = TorrentStatus.Seeding,
                TotalSize = 1_000_000_000,
                Uploaded = 0,
                Progress = 1.0,
                InfoHash = $"hash{i}"
            });
        }

        _torrentService.GetAll().Returns(torrents);

        // Active torrents will be fewer than 10 due to stopped percentage
        _distributionManager.DistributeUploadSpeeds(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(callInfo =>
            {
                var count = callInfo.ArgAt<int>(0);
                var speeds = new long[count];
                for (var i = 0; i < count; i++)
                {
                    speeds[i] = 100_000;
                }

                return speeds;
            });

        CallTick();

        var zeroUploadCount = torrents.Count(t => t.Uploaded == 0);
        Assert.That(zeroUploadCount, Is.GreaterThan(0), "Some torrents should have zero upload due to stopped percentage");
        Assert.That(zeroUploadCount, Is.LessThan(10), "At least one torrent must remain active");
    }

    [Test]
    public void Tick_should_never_stop_force_start_torrents_in_upload_stopped()
    {
        _configService.UploadStoppedMinPercentage.Returns(100);
        _configService.UploadStoppedMaxPercentage.Returns(100);

        var forceStartTorrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000_000,
            Uploaded = 0,
            Progress = 1.0,
            ForceStart = true,
            InfoHash = "force1"
        };
        var normalTorrent1 = new Torrent
        {
            Id = 2,
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000_000,
            Uploaded = 0,
            Progress = 1.0,
            ForceStart = false,
            InfoHash = "normal1"
        };
        var normalTorrent2 = new Torrent
        {
            Id = 3,
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000_000,
            Uploaded = 0,
            Progress = 1.0,
            ForceStart = false,
            InfoHash = "normal2"
        };

        var torrents = new List<Torrent> { forceStartTorrent, normalTorrent1, normalTorrent2 };
        _torrentService.GetAll().Returns(torrents);
        _distributionManager.DistributeUploadSpeeds(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(callInfo =>
            {
                var count = callInfo.ArgAt<int>(0);
                var speeds = new long[count];
                for (var i = 0; i < count; i++)
                {
                    speeds[i] = 100_000;
                }

                return speeds;
            });

        CallTick();

        Assert.That(
            forceStartTorrent.Uploaded,
            Is.GreaterThan(0),
            "ForceStart torrent should never be stopped by upload stopped percentage");
    }

    [Test]
    public void Tick_should_keep_at_least_one_active_when_upload_stopped_100_percent()
    {
        _configService.UploadStoppedMinPercentage.Returns(100);
        _configService.UploadStoppedMaxPercentage.Returns(100);

        var torrents = new List<Torrent>();
        for (var i = 0; i < 5; i++)
        {
            torrents.Add(new Torrent
            {
                Id = i + 1,
                Status = TorrentStatus.Seeding,
                TotalSize = 1_000_000_000,
                Uploaded = 0,
                Progress = 1.0,
                InfoHash = $"hash{i}"
            });
        }

        _torrentService.GetAll().Returns(torrents);
        _distributionManager.DistributeUploadSpeeds(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(callInfo =>
            {
                var count = callInfo.ArgAt<int>(0);
                var speeds = new long[count];
                for (var i = 0; i < count; i++)
                {
                    speeds[i] = 100_000;
                }

                return speeds;
            });

        CallTick();

        var activeCount = torrents.Count(t => t.Uploaded > 0);
        Assert.That(
            activeCount,
            Is.GreaterThanOrEqualTo(1),
            "At least one torrent must remain active even with 100% stopped percentage");
    }

    [Test]
    public void Tick_should_stop_some_downloads_when_download_stopped_percentage_set()
    {
        _configService.DownloadStoppedMinPercentage.Returns(50);
        _configService.DownloadStoppedMaxPercentage.Returns(50);

        var torrents = new List<Torrent>();
        for (var i = 0; i < 10; i++)
        {
            torrents.Add(new Torrent
            {
                Id = i + 1,
                Status = TorrentStatus.Downloading,
                TotalSize = 1_000_000_000,
                Downloaded = 0,
                InfoHash = $"hash{i}"
            });
        }

        _torrentService.GetAll().Returns(torrents);
        _distributionManager.DistributeDownloadSpeeds(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(callInfo =>
            {
                var count = callInfo.ArgAt<int>(0);
                var speeds = new long[count];
                for (var i = 0; i < count; i++)
                {
                    speeds[i] = 100_000;
                }

                return speeds;
            });

        CallTick();

        var zeroDownloadCount = torrents.Count(t => t.Downloaded == 0);
        Assert.That(zeroDownloadCount, Is.GreaterThan(0), "Some torrents should have zero download due to stopped percentage");
        Assert.That(zeroDownloadCount, Is.LessThan(10), "At least one torrent must remain downloading");
    }

    [Test]
    public void Tick_should_include_force_start_downloading_torrent_when_autostart_false()
    {
        _configService.AutoStart.Returns(false);

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            TotalSize = 1_000_000_000,
            Downloaded = 0,
            ForceStart = true,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100_000 });

        CallTick();

        _distributionManager.Received(1).DistributeDownloadSpeeds(
            1, Arg.Any<long>(), Arg.Any<double[]>());
        Assert.That(torrent.Downloaded, Is.GreaterThan(0));
    }

    [Test]
    public void Tick_should_not_continue_download_during_seeding_when_progress_complete()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000,
            Downloaded = 1_000_000,
            Progress = 1.0,
            InfoHash = "abc123"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100_000 });

        CallTick();

        Assert.That(
            torrent.Downloaded,
            Is.EqualTo(1_000_000),
            "Should not add more downloaded bytes when progress is already 1.0");
    }

    [Test]
    public void HasForceStartTorrents_should_return_true_for_force_start_downloading()
    {
        var method = typeof(SeedingEngine).GetMethod("HasForceStartTorrents",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            ForceStart = true
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var result = (bool)method.Invoke(_engine, null);

        Assert.That(result, Is.True);
    }

    [Test]
    public void HasForceStartTorrents_should_return_true_for_force_start_seeding()
    {
        var method = typeof(SeedingEngine).GetMethod("HasForceStartTorrents",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            ForceStart = true
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var result = (bool)method.Invoke(_engine, null);

        Assert.That(result, Is.True);
    }

    [Test]
    public void HasForceStartTorrents_should_return_false_for_force_start_stopped()
    {
        var method = typeof(SeedingEngine).GetMethod("HasForceStartTorrents",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Stopped,
            ForceStart = true
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var result = (bool)method.Invoke(_engine, null);

        Assert.That(result, Is.False);
    }

    [Test]
    public void HasForceStartTorrents_should_return_false_when_no_force_start()
    {
        var method = typeof(SeedingEngine).GetMethod("HasForceStartTorrents",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            ForceStart = false
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var result = (bool)method.Invoke(_engine, null);

        Assert.That(result, Is.False);
    }

    [Test]
    public void SelectStoppedTorrents_should_return_empty_when_max_percentage_zero()
    {
        var method = typeof(SeedingEngine).GetMethod("SelectStoppedTorrents",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding },
            new Torrent { Id = 2, Status = TorrentStatus.Seeding }
        };

        _configService.UploadStoppedMinPercentage.Returns(0);
        _configService.UploadStoppedMaxPercentage.Returns(0);

        var result = (HashSet<int>)method.Invoke(_engine, new object[] { torrents });

        Assert.That(result.Count, Is.EqualTo(0));
    }

    [Test]
    public void SelectStoppedTorrents_should_return_empty_when_all_are_force_start()
    {
        var method = typeof(SeedingEngine).GetMethod("SelectStoppedTorrents",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, ForceStart = true },
            new Torrent { Id = 2, Status = TorrentStatus.Seeding, ForceStart = true }
        };

        _configService.UploadStoppedMinPercentage.Returns(50);
        _configService.UploadStoppedMaxPercentage.Returns(50);

        var result = (HashSet<int>)method.Invoke(_engine, new object[] { torrents });

        Assert.That(result.Count, Is.EqualTo(0));
    }

    [Test]
    public void SelectDownloadStoppedTorrents_should_return_empty_when_max_percentage_zero()
    {
        var method = typeof(SeedingEngine).GetMethod("SelectDownloadStoppedTorrents",
            BindingFlags.NonPublic | BindingFlags.Instance);

        _configService.DownloadStoppedMinPercentage.Returns(0);
        _configService.DownloadStoppedMaxPercentage.Returns(0);

        var result = (HashSet<int>)method.Invoke(_engine, new object[] { 5 });

        Assert.That(result.Count, Is.EqualTo(0));
    }

    [Test]
    public void SelectDownloadStoppedTorrents_should_stop_some_when_percentage_set()
    {
        var method = typeof(SeedingEngine).GetMethod("SelectDownloadStoppedTorrents",
            BindingFlags.NonPublic | BindingFlags.Instance);

        _configService.DownloadStoppedMinPercentage.Returns(50);
        _configService.DownloadStoppedMaxPercentage.Returns(50);

        var result = (HashSet<int>)method.Invoke(_engine, new object[] { 10 });

        Assert.That(result.Count, Is.GreaterThan(0));
        Assert.That(result.Count, Is.LessThan(10), "At least one torrent must remain active");
    }

    [Test]
    public void SelectDownloadStoppedTorrents_should_return_empty_for_single_torrent()
    {
        var method = typeof(SeedingEngine).GetMethod("SelectDownloadStoppedTorrents",
            BindingFlags.NonPublic | BindingFlags.Instance);

        _configService.DownloadStoppedMinPercentage.Returns(100);
        _configService.DownloadStoppedMaxPercentage.Returns(100);

        var result = (HashSet<int>)method.Invoke(_engine, new object[] { 1 });

        Assert.That(
            result.Count,
            Is.EqualTo(0),
            "Single torrent should never be stopped (stoppedCount capped at torrentCount - 1)");
    }

    [Test]
    public void Tick_should_use_config_upload_speed_when_scheduler_is_zero()
    {
        _configService.MaxUploadSpeedKbps.Returns(75);
        _speedScheduler.GetCurrentLimits().Returns(new SpeedLimits
        {
            MaxUploadSpeed = SpeedLimits.Unlimited,
            MaxDownloadSpeed = 1_048_576,
            IsScheduleActive = false
        });

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Progress = 1.0,
            InfoHash = "abc"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100 });

        CallTick();

        _distributionManager.Received().DistributeUploadSpeeds(
            1, Arg.Is<long>(s => s == 75 * 1024), Arg.Any<double[]>());
    }

    [Test]
    public void SelectStoppedTorrents_should_stop_some_non_force_start_with_percentage()
    {
        var method = typeof(SeedingEngine).GetMethod("SelectStoppedTorrents",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, ForceStart = true },
            new Torrent { Id = 2, Status = TorrentStatus.Seeding, ForceStart = false },
            new Torrent { Id = 3, Status = TorrentStatus.Seeding, ForceStart = false },
            new Torrent { Id = 4, Status = TorrentStatus.Seeding, ForceStart = false },
            new Torrent { Id = 5, Status = TorrentStatus.Seeding, ForceStart = false }
        };

        _configService.UploadStoppedMinPercentage.Returns(50);
        _configService.UploadStoppedMaxPercentage.Returns(50);

        var result = (HashSet<int>)method.Invoke(_engine, new object[] { torrents });

        Assert.That(result.Count, Is.GreaterThan(0), "Should stop some non-ForceStart torrents");
        Assert.That(result, Does.Not.Contain(0), "ForceStart torrent at index 0 should never be stopped");
    }

    // ExecuteAsync loop-body tests

    [Test]
    public async Task ExecuteAsync_starts_main_loop_and_exits_on_cancellation()
    {
        // AutoStart=true from Setup; just need PeerIdPrefix mocked
        _configService.PeerIdPrefix.Returns("-SD1000-");
        _torrentService.GetAll().Returns(new List<Torrent>());

        await _engine.StartAsync(CancellationToken.None);
        await Task.Delay(200); // let at least one Tick() complete

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _engine.StopAsync(stopCts.Token);

        Assert.That(stopCts.IsCancellationRequested, Is.False, "StopAsync should complete before timeout");
        _torrentService.Received().GetAll();
    }

    [Test]
    public async Task ExecuteAsync_with_autostart_false_and_precancelled_token_exits_immediately()
    {
        _configService.AutoStart.Returns(false);
        _torrentService.GetAll().Returns(new List<Torrent>());

        // Pre-cancel the token so ExecuteAsync returns at the if(stoppingToken.IsCancellationRequested) check
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await _engine.StartAsync(cts.Token);

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _engine.StopAsync(stopCts.Token);

        Assert.That(stopCts.IsCancellationRequested, Is.False, "StopAsync should complete before timeout");
    }

    [Test]
    public async Task ExecuteAsync_with_autostart_false_enters_wait_loop_and_exits_on_cancellation()
    {
        _configService.AutoStart.Returns(false);
        _torrentService.GetAll().Returns(new List<Torrent>()); // no ForceStart torrents

        await _engine.StartAsync(CancellationToken.None);
        await Task.Delay(100); // let it enter Task.Delay(5s) in the wait loop

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _engine.StopAsync(stopCts.Token);

        Assert.That(stopCts.IsCancellationRequested, Is.False, "StopAsync should complete before timeout");
        // HasForceStartTorrents() calls GetAll - verify the wait loop was entered
        _torrentService.Received().GetAll();
    }
}
