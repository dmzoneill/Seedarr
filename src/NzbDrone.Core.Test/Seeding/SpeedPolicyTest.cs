using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Seeding.Distribution;
using NzbDrone.Core.Seeding.Scheduling;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Seeding;

[TestFixture]
public class SpeedPolicyTest
{
    private ISpeedDistributionManager _distributionManager;
    private ISpeedScheduler _speedScheduler;
    private IConfigService _configService;
    private ITorrentEventLogService _eventLogService;
    private ITorrentStateMachine _stateMachine;
    private IStopPolicy _stopPolicy;
    private SpeedPolicy _subject;

    [SetUp]
    public void Setup()
    {
        _distributionManager = Substitute.For<ISpeedDistributionManager>();
        _speedScheduler = Substitute.For<ISpeedScheduler>();
        _configService = Substitute.For<IConfigService>();
        _eventLogService = Substitute.For<ITorrentEventLogService>();
        _stateMachine = Substitute.For<ITorrentStateMachine>();
        _stopPolicy = Substitute.For<IStopPolicy>();

        _configService.AlternativeSpeedEnabled.Returns(false);
        _configService.MaxUploadSpeedKbps.Returns(100);
        _configService.MaxDownloadSpeedKbps.Returns(200);
        _configService.SpeedVariationMin.Returns(1.0);
        _configService.SpeedVariationMax.Returns(1.0);
        _configService.DownloadThresholdPercent.Returns(100);
        _configService.SeederUploadActivityProbability.Returns(1.0);

        _speedScheduler.GetCurrentLimits().Returns(new SpeedLimits
        {
            MaxUploadSpeed = 100 * 1024,
            MaxDownloadSpeed = 200 * 1024
        });

        _subject = new SpeedPolicy(
            _distributionManager,
            _speedScheduler,
            _configService,
            _eventLogService,
            _stateMachine,
            _stopPolicy,
            new Random(42));
    }

    [Test]
    public void GetEffectiveLimits_returns_merged_speed_limits()
    {
        var limits = _subject.GetEffectiveLimits();

        Assert.That(limits.MaxUploadSpeed, Is.EqualTo(100 * 1024));
        Assert.That(limits.MaxDownloadSpeed, Is.EqualTo(200 * 1024));
    }

    [Test]
    public void ProcessDownloading_distributes_speeds_and_updates_downloaded()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            Downloaded = 0,
            TotalSize = 1000,
            Progress = 0.0
        };
        var torrents = new List<Torrent> { torrent };

        _stopPolicy.SelectDownloadStoppedTorrents(torrents).Returns(new HashSet<int>());
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 500 });

        _subject.ProcessDownloading(torrents, new SpeedLimits { MaxDownloadSpeed = 500 }, TimeSpan.FromSeconds(1));

        Assert.That(torrent.Downloaded, Is.EqualTo(500));
        Assert.That(torrent.Progress, Is.EqualTo(0.5));
        _stateMachine.Received(1).CheckDownloadThreshold(torrent, 1.0);
    }

    [Test]
    public void ProcessSeeding_distributes_speeds_and_updates_uploaded()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            Uploaded = 0,
            TotalSize = 1000,
            Progress = 1.0
        };
        var torrents = new List<Torrent> { torrent };

        _stopPolicy.SelectStoppedTorrents(torrents).Returns(new HashSet<int>());
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 250 });

        _subject.ProcessSeeding(torrents, new SpeedLimits { MaxUploadSpeed = 250, MaxDownloadSpeed = 500 }, TimeSpan.FromSeconds(1));

        Assert.That(torrent.Uploaded, Is.EqualTo(250));
        Assert.That(torrent.Ratio, Is.EqualTo(0.25));
    }

    [Test]
    public void GetPriorityWeight_returns_expected_values()
    {
        Assert.That(SpeedPolicy.GetPriorityWeight(2), Is.EqualTo(2.0));
        Assert.That(SpeedPolicy.GetPriorityWeight(0), Is.EqualTo(0.5));
        Assert.That(SpeedPolicy.GetPriorityWeight(1), Is.EqualTo(1.0));
        Assert.That(SpeedPolicy.GetPriorityWeight(99), Is.EqualTo(1.0));
    }
}
