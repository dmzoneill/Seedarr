using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.TrackerServer;
using NzbDrone.Core.Seeding.Distribution;
using NzbDrone.Core.Seeding.Scheduling;
using NzbDrone.Core.Test.Torrents;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Seeding;

[TestFixture]
public class SeedingEngineLostUpdateTests
{
    private FullRowTorrentStore _store;
    private ISpeedDistributionManager _distributionManager;
    private ISpeedScheduler _speedScheduler;
    private IConfigService _configService;
    private IEventAggregator _eventAggregator;
    private IPeerDatabase _peerDatabase;
    private IConnectionManager _connectionManager;
    private ITorrentEventLogService _eventLogService;
    private SeedingEngine _engine;

    [SetUp]
    public void Setup()
    {
        _store = new FullRowTorrentStore();
        _distributionManager = Substitute.For<ISpeedDistributionManager>();
        _speedScheduler = Substitute.For<ISpeedScheduler>();
        _configService = Substitute.For<IConfigService>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _peerDatabase = Substitute.For<IPeerDatabase>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _eventLogService = Substitute.For<ITorrentEventLogService>();

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
        _configService.StalledNoSeedsTimeoutSeconds.Returns(60);
        _configService.TrafficPatternProfile.Returns("off");
        _configService.UiRefreshRateSec.Returns(1);

        _speedScheduler.GetCurrentLimits().Returns(new SpeedLimits
        {
            MaxUploadSpeed = 1_048_576,
            MaxDownloadSpeed = 1_048_576,
            IsScheduleActive = false
        });

        _distributionManager.DistributeUploadSpeeds(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100_000 });
        _distributionManager.DistributeDownloadSpeeds(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 100_000 });

        _engine = new SeedingEngine(
            _store,
            _distributionManager,
            _speedScheduler,
            _configService,
            _eventAggregator,
            _peerDatabase,
            _connectionManager,
            _eventLogService);
    }

    [Test]
    public void Tick_does_not_revert_pause_or_user_edits_made_after_the_snapshot()
    {
        _store.Seed(new Torrent
        {
            Id = 1,
            Name = "Concurrent",
            InfoHash = "abc123",
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000,
            Downloaded = 1_000_000,
            Uploaded = 1_000,
            Progress = 1.0,
            Label = "old-label",
            Category = "old-category",
            RatioLimit = 1.0,
            Priority = 1,
            Active = true
        });

        _store.AfterSnapshot = () =>
        {
            _store.Pause(1);
            _store.Mutate(1, row =>
            {
                row.Uploaded = 50_000;
                row.Downloaded = 1_000_000;
                row.Label = "user-label";
                row.Category = "movies";
                row.RatioLimit = 4.5;
                row.Priority = 9;
            });
        };

        _engine.TickForTesting(TimeSpan.FromSeconds(1));

        var saved = _store.Get(1);
        Assert.That(saved.Status, Is.EqualTo(TorrentStatus.Paused), "user pause was overwritten by the tick's stale full-row write");
        Assert.That(saved.Uploaded, Is.GreaterThanOrEqualTo(50_000), "upload counter rolled back by the tick's stale snapshot");
        Assert.That(saved.Label, Is.EqualTo("user-label"));
        Assert.That(saved.Category, Is.EqualTo("movies"));
        Assert.That(saved.RatioLimit, Is.EqualTo(4.5));
        Assert.That(saved.Priority, Is.EqualTo(9));
        Assert.That(saved.Active, Is.False);
    }

    [Test]
    public void Tick_keeps_a_newer_counter_when_status_is_unchanged()
    {
        _store.Seed(new Torrent
        {
            Id = 7,
            Name = "Counters",
            InfoHash = "def456",
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000,
            Downloaded = 1_000_000,
            Uploaded = 1_000,
            Progress = 1.0,
            Label = "keep",
            Active = true
        });

        _store.AfterSnapshot = () =>
        {
            _store.Mutate(7, row =>
            {
                row.Uploaded = 80_000;
                row.Label = "renamed";
            });
        };

        _engine.TickForTesting(TimeSpan.FromSeconds(1));

        var saved = _store.Get(7);
        Assert.That(saved.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(saved.Uploaded, Is.GreaterThanOrEqualTo(80_000), "a stale tick must not move Uploaded backwards");
        Assert.That(saved.Label, Is.EqualTo("renamed"));
    }

    [Test]
    public void Tick_persists_its_own_upload_progress_when_nothing_else_writes()
    {
        _store.Seed(new Torrent
        {
            Id = 3,
            Name = "Quiet",
            InfoHash = "aaa111",
            Status = TorrentStatus.Seeding,
            TotalSize = 1_000_000,
            Downloaded = 1_000_000,
            Uploaded = 1_000,
            Progress = 1.0,
            Label = "stable",
            Category = "linux"
        });

        _engine.TickForTesting(TimeSpan.FromSeconds(1));

        var saved = _store.Get(3);
        Assert.That(saved.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(saved.Uploaded, Is.GreaterThan(1_000));
        Assert.That(saved.Label, Is.EqualTo("stable"));
        Assert.That(saved.Category, Is.EqualTo("linux"));
        Assert.That(saved.Active, Is.True);
    }
}
