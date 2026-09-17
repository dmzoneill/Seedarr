using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Seeding.Distribution;
using NzbDrone.Core.Seeding.Scheduling;
using NzbDrone.Core.Simulation.Swarm;
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
    private ICategoryService _categoryService;
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
        _categoryService = Substitute.For<ICategoryService>();

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
            new RandomNumberGenerator(42),
            categoryService: _categoryService);
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
    public void ProcessDownloading_when_torrent_has_no_limit_inherits_category_download_limit()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            Category = "LinuxISO",
            DownloadLimit = 0, // Unconfigured, inherits category
            Downloaded = 0,
            TotalSize = 10_000_000,
            Progress = 0.0
        };
        var torrents = new List<Torrent> { torrent };

        _categoryService.GetByName("LinuxISO").Returns(new Category
        {
            Name = "LinuxISO",
            DefaultDownloadLimit = 100 // 100 KB/s = 102,400 B/s
        });

        _stopPolicy.SelectDownloadStoppedTorrents(torrents).Returns(new HashSet<int>());
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 500_000 });

        _subject.ProcessDownloading(torrents, new SpeedLimits { MaxDownloadSpeed = 500_000 }, TimeSpan.FromSeconds(1));

        Assert.That(torrent.Downloaded, Is.EqualTo(100 * 1024));
    }

    [Test]
    public void ProcessDownloading_when_torrent_has_minus_one_unlimited_overrides_category_download_limit()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            Category = "LinuxISO",
            DownloadLimit = -1, // Explicitly unlimited override
            Downloaded = 0,
            TotalSize = 10_000_000,
            Progress = 0.0
        };
        var torrents = new List<Torrent> { torrent };

        _categoryService.GetByName("LinuxISO").Returns(new Category
        {
            Name = "LinuxISO",
            DefaultDownloadLimit = 100 // 100 KB/s
        });

        _stopPolicy.SelectDownloadStoppedTorrents(torrents).Returns(new HashSet<int>());
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 500_000 });

        _subject.ProcessDownloading(torrents, new SpeedLimits { MaxDownloadSpeed = 500_000 }, TimeSpan.FromSeconds(1));

        // Should not be throttled by category limit of 100 KB/s
        Assert.That(torrent.Downloaded, Is.EqualTo(500_000));
    }

    [Test]
    public void ProcessDownloading_when_torrent_has_explicit_positive_limit_overrides_category_download_limit()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            Category = "LinuxISO",
            DownloadLimit = 50, // 50 KB/s override
            Downloaded = 0,
            TotalSize = 10_000_000,
            Progress = 0.0
        };
        var torrents = new List<Torrent> { torrent };

        _categoryService.GetByName("LinuxISO").Returns(new Category
        {
            Name = "LinuxISO",
            DefaultDownloadLimit = 100 // 100 KB/s
        });

        _stopPolicy.SelectDownloadStoppedTorrents(torrents).Returns(new HashSet<int>());
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 500_000 });

        _subject.ProcessDownloading(torrents, new SpeedLimits { MaxDownloadSpeed = 500_000 }, TimeSpan.FromSeconds(1));

        Assert.That(torrent.Downloaded, Is.EqualTo(50 * 1024));
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
    public void ProcessSeeding_when_torrent_has_no_limit_inherits_category_upload_limit()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            Category = "Movies",
            UploadLimit = 0, // Unconfigured, inherits category
            Uploaded = 0,
            TotalSize = 10_000_000,
            Progress = 1.0
        };
        var torrents = new List<Torrent> { torrent };

        _categoryService.GetByName("Movies").Returns(new Category
        {
            Name = "Movies",
            DefaultUploadLimit = 50 // 50 KB/s = 51,200 B/s
        });

        _stopPolicy.SelectStoppedTorrents(torrents).Returns(new HashSet<int>());
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 250_000 });

        _subject.ProcessSeeding(torrents, new SpeedLimits { MaxUploadSpeed = 250_000, MaxDownloadSpeed = 500_000 }, TimeSpan.FromSeconds(1));

        Assert.That(torrent.Uploaded, Is.EqualTo(50 * 1024));
    }

    [Test]
    public void ProcessSeeding_when_torrent_has_minus_one_unlimited_overrides_category_upload_limit()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            Category = "Movies",
            UploadLimit = -1, // Explicitly unlimited override
            Uploaded = 0,
            TotalSize = 10_000_000,
            Progress = 1.0
        };
        var torrents = new List<Torrent> { torrent };

        _categoryService.GetByName("Movies").Returns(new Category
        {
            Name = "Movies",
            DefaultUploadLimit = 50 // 50 KB/s
        });

        _stopPolicy.SelectStoppedTorrents(torrents).Returns(new HashSet<int>());
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 250_000 });

        _subject.ProcessSeeding(torrents, new SpeedLimits { MaxUploadSpeed = 250_000, MaxDownloadSpeed = 500_000 }, TimeSpan.FromSeconds(1));

        // Should not be throttled by category limit of 50 KB/s
        Assert.That(torrent.Uploaded, Is.EqualTo(250_000));
    }

    [Test]
    public void ProcessSeeding_when_torrent_has_explicit_positive_limit_overrides_category_upload_limit()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            Category = "Movies",
            UploadLimit = 20, // 20 KB/s override
            Uploaded = 0,
            TotalSize = 10_000_000,
            Progress = 1.0
        };
        var torrents = new List<Torrent> { torrent };

        _categoryService.GetByName("Movies").Returns(new Category
        {
            Name = "Movies",
            DefaultUploadLimit = 50 // 50 KB/s
        });

        _stopPolicy.SelectStoppedTorrents(torrents).Returns(new HashSet<int>());
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 250_000 });

        _subject.ProcessSeeding(torrents, new SpeedLimits { MaxUploadSpeed = 250_000, MaxDownloadSpeed = 500_000 }, TimeSpan.FromSeconds(1));

        Assert.That(torrent.Uploaded, Is.EqualTo(20 * 1024));
    }

    [Test]
    public void GetPriorityWeight_returns_expected_values()
    {
        Assert.That(SpeedPolicy.GetPriorityWeight(2), Is.EqualTo(2.0));
        Assert.That(SpeedPolicy.GetPriorityWeight(0), Is.EqualTo(0.5));
        Assert.That(SpeedPolicy.GetPriorityWeight(1), Is.EqualTo(1.0));
        Assert.That(SpeedPolicy.GetPriorityWeight(99), Is.EqualTo(1.0));
    }

    [Test]
    public void ProcessSeeding_when_recommendation_is_pause_allocates_zero_bytes()
    {
        var swarmAnalyzer = Substitute.For<ISwarmAnalyzer>();
        _configService.SwarmIntelligenceEnabled.Returns(true);

        swarmAnalyzer.Analyze(Arg.Any<SwarmSnapshot>()).Returns(new SwarmRecommendation
        {
            Recommendation = SeedingRecommendation.Pause,
            Confidence = 1.0,
            Reason = "No active leeches"
        });

        var subject = new SpeedPolicy(
            _distributionManager,
            _speedScheduler,
            _configService,
            _eventLogService,
            _stateMachine,
            _stopPolicy,
            new RandomNumberGenerator(42),
            swarmAnalyzer: swarmAnalyzer,
            categoryService: _categoryService);

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            Uploaded = 0,
            TotalSize = 10_000_000,
            Progress = 1.0,
            Seeders = 1,
            Leechers = 0
        };
        var torrents = new List<Torrent> { torrent };

        _stopPolicy.SelectStoppedTorrents(torrents).Returns(new HashSet<int>());
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 250_000 });

        subject.ProcessSeeding(torrents, new SpeedLimits { MaxUploadSpeed = 250_000, MaxDownloadSpeed = 500_000 }, TimeSpan.FromSeconds(1));

        Assert.That(torrent.Uploaded, Is.EqualTo(0));
    }

    [Test]
    public void ProcessSeeding_when_leech_count_is_zero_allocates_zero_bytes()
    {
        var swarmAnalyzer = Substitute.For<ISwarmAnalyzer>();
        _configService.SwarmIntelligenceEnabled.Returns(true);

        swarmAnalyzer.Analyze(Arg.Any<SwarmSnapshot>()).Returns(new SwarmRecommendation
        {
            Recommendation = SeedingRecommendation.Maintain,
            Confidence = 0.8,
            Reason = "Maintain"
        });

        var subject = new SpeedPolicy(
            _distributionManager,
            _speedScheduler,
            _configService,
            _eventLogService,
            _stateMachine,
            _stopPolicy,
            new RandomNumberGenerator(42),
            swarmAnalyzer: swarmAnalyzer,
            categoryService: _categoryService);

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            Uploaded = 0,
            TotalSize = 10_000_000,
            Progress = 1.0,
            Seeders = 5,
            Leechers = 0
        };
        var torrents = new List<Torrent> { torrent };

        _stopPolicy.SelectStoppedTorrents(torrents).Returns(new HashSet<int>());
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 250_000 });

        subject.ProcessSeeding(torrents, new SpeedLimits { MaxUploadSpeed = 250_000, MaxDownloadSpeed = 500_000 }, TimeSpan.FromSeconds(1));

        Assert.That(torrent.Uploaded, Is.EqualTo(0));
    }
}
