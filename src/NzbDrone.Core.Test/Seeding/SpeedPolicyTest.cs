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
using NzbDrone.Core.Tags;
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
    private ITagService _tagService;
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
        _tagService = Substitute.For<ITagService>();

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
            categoryService: _categoryService,
            tagService: _tagService);
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
            Progress = 1.0,
            Leechers = 10,
            SeedingTime = 300
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
    public void ProcessSeeding_when_downloaded_is_positive_calculates_ratio_against_downloaded()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            Downloaded = 500,
            Uploaded = 0,
            TotalSize = 1000,
            Progress = 1.0,
            Leechers = 10,
            SeedingTime = 300
        };
        var torrents = new List<Torrent> { torrent };

        _stopPolicy.SelectStoppedTorrents(torrents).Returns(new HashSet<int>());
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 250 });

        _subject.ProcessSeeding(torrents, new SpeedLimits { MaxUploadSpeed = 250, MaxDownloadSpeed = 500 }, TimeSpan.FromSeconds(1));

        Assert.That(torrent.Uploaded, Is.EqualTo(250));
        Assert.That(torrent.Ratio, Is.EqualTo(0.5));
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
            Progress = 1.0,
            Leechers = 10,
            SeedingTime = 300
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
            Progress = 1.0,
            Leechers = 100,
            SeedingTime = 300
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
            Progress = 1.0,
            Leechers = 10,
            SeedingTime = 300
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

    [Test]
    public void GetUploadLimit_when_torrent_has_no_tags_and_no_category_returns_zero()
    {
        var torrent = new Torrent { Id = 1, UploadLimit = 0 };

        var limit = _subject.GetUploadLimit(torrent);

        Assert.That(limit, Is.EqualTo(0));
    }

    [Test]
    public void GetUploadLimit_when_torrent_has_explicit_limit_returns_torrent_limit()
    {
        var torrent = new Torrent { Id = 1, UploadLimit = 500, TagIds = new List<int> { 1 } };
        _tagService.Get(1).Returns(new Tag { Id = 1, UploadLimitKbps = 200 });

        var limit = _subject.GetUploadLimit(torrent);

        Assert.That(limit, Is.EqualTo(500));
    }

    [Test]
    public void GetUploadLimit_when_torrent_has_tag_with_limit_returns_tag_limit()
    {
        var torrent = new Torrent { Id = 1, UploadLimit = 0, TagIds = new List<int> { 1 } };
        _tagService.Get(1).Returns(new Tag { Id = 1, UploadLimitKbps = 250 });

        var limit = _subject.GetUploadLimit(torrent);

        Assert.That(limit, Is.EqualTo(250));
    }

    [Test]
    public void GetUploadLimit_when_torrent_has_multiple_tags_enforces_lowest_positive_limit()
    {
        var torrent = new Torrent { Id = 1, UploadLimit = 0, TagIds = new List<int> { 1, 2, 3 } };
        _tagService.Get(1).Returns(new Tag { Id = 1, UploadLimitKbps = 300 });
        _tagService.Get(2).Returns(new Tag { Id = 2, UploadLimitKbps = 150 });
        _tagService.Get(3).Returns(new Tag { Id = 3, UploadLimitKbps = null });

        var limit = _subject.GetUploadLimit(torrent);

        Assert.That(limit, Is.EqualTo(150));
    }

    [Test]
    public void GetUploadLimit_tag_limit_takes_precedence_over_category_default_limit()
    {
        var torrent = new Torrent { Id = 1, UploadLimit = 0, Category = "LinuxISO", TagIds = new List<int> { 1 } };
        _categoryService.GetByName("LinuxISO").Returns(new Category { Name = "LinuxISO", DefaultUploadLimit = 500 });
        _tagService.Get(1).Returns(new Tag { Id = 1, UploadLimitKbps = 100 });

        var limit = _subject.GetUploadLimit(torrent);

        Assert.That(limit, Is.EqualTo(100));
    }

    [Test]
    public void GetDownloadLimit_when_torrent_has_tag_with_limit_returns_tag_limit()
    {
        var torrent = new Torrent { Id = 1, DownloadLimit = 0, TagIds = new List<int> { 1 } };
        _tagService.Get(1).Returns(new Tag { Id = 1, DownloadLimitKbps = 400 });

        var limit = _subject.GetDownloadLimit(torrent);

        Assert.That(limit, Is.EqualTo(400));
    }

    [Test]
    public void GetDownloadLimit_when_torrent_has_multiple_tags_enforces_lowest_positive_limit()
    {
        var torrent = new Torrent { Id = 1, DownloadLimit = 0, TagIds = new List<int> { 1, 2 } };
        _tagService.Get(1).Returns(new Tag { Id = 1, DownloadLimitKbps = 600 });
        _tagService.Get(2).Returns(new Tag { Id = 2, DownloadLimitKbps = 200 });

        var limit = _subject.GetDownloadLimit(torrent);

        Assert.That(limit, Is.EqualTo(200));
    }

    [Test]
    public void GetDownloadLimit_tag_limit_takes_precedence_over_category_default_limit()
    {
        var torrent = new Torrent { Id = 1, DownloadLimit = 0, Category = "LinuxISO", TagIds = new List<int> { 1 } };
        _categoryService.GetByName("LinuxISO").Returns(new Category { Name = "LinuxISO", DefaultDownloadLimit = 800 });
        _tagService.Get(1).Returns(new Tag { Id = 1, DownloadLimitKbps = 300 });

        var limit = _subject.GetDownloadLimit(torrent);

        Assert.That(limit, Is.EqualTo(300));
    }

    [Test]
    public void ProcessDownloading_applies_tag_download_limit()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            DownloadLimit = 0,
            Downloaded = 0,
            TotalSize = 10_000_000,
            TagIds = new List<int> { 1 }
        };
        var torrents = new List<Torrent> { torrent };

        _tagService.Get(1).Returns(new Tag { Id = 1, DownloadLimitKbps = 150 });
        _stopPolicy.SelectDownloadStoppedTorrents(torrents).Returns(new HashSet<int>());
        _distributionManager.DistributeDownloadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 500_000 });

        _subject.ProcessDownloading(torrents, new SpeedLimits { MaxDownloadSpeed = 500_000 }, TimeSpan.FromSeconds(1));

        Assert.That(torrent.Downloaded, Is.EqualTo(150 * 1024));
    }

    [Test]
    public void ProcessSeeding_applies_tag_upload_limit()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            UploadLimit = 0,
            Uploaded = 0,
            TotalSize = 10_000_000,
            Progress = 1.0,
            Seeders = 5,
            Leechers = 2,
            SeedingTime = 300,
            TagIds = new List<int> { 1 }
        };
        var torrents = new List<Torrent> { torrent };

        _tagService.Get(1).Returns(new Tag { Id = 1, UploadLimitKbps = 75 });
        _stopPolicy.SelectStoppedTorrents(torrents).Returns(new HashSet<int>());
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 500_000 });

        _subject.ProcessSeeding(torrents, new SpeedLimits { MaxUploadSpeed = 500_000 }, TimeSpan.FromSeconds(1));

        Assert.That(torrent.Uploaded, Is.EqualTo(75 * 1024));
    }

    [Test]
    public void CalculateEffectiveUploadSpeed_when_torrent_has_zero_leechers_returns_zero()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Leechers = 0,
            SeedingTime = 300
        };

        var speed = _subject.CalculateEffectiveUploadSpeed(torrent, 50_000_000);

        Assert.That(speed, Is.EqualTo(0));
    }

    [Test]
    public void CalculateEffectiveUploadSpeed_when_torrent_has_one_leecher_clamps_to_per_leecher_ceiling()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Leechers = 1,
            SeedingTime = 300
        };

        var speed = _subject.CalculateEffectiveUploadSpeed(torrent, 50_000_000);

        Assert.That(speed, Is.EqualTo(SpeedPolicy.DefaultMaxUploadPerLeecherBps));
    }

    [Test]
    public void CalculateEffectiveUploadSpeed_during_warmup_phase_scales_speed_and_reaches_full_speed_after_window()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var torrent = new Torrent
        {
            Id = 1,
            Leechers = 10,
            SeedingTime = 0
        };

        // At 30 seconds after start:
        // rampFactor = Math.Clamp(30.0 / 180.0, 0.05, 1.0) = 1.0 / 6.0
        // targetSpeed = 6_000_000 (well within 10 * 2.5MB/s = 25MB/s ceiling)
        _subject.SetSeedingStartTime(torrent.Id, now.AddSeconds(-30));
        var speedAt30s = _subject.CalculateEffectiveUploadSpeed(torrent, 6_000_000, now);
        Assert.That(speedAt30s, Is.EqualTo(1_000_000));

        // At 180 seconds after start:
        // rampFactor = 1.0 (100%)
        _subject.SetSeedingStartTime(torrent.Id, now.AddSeconds(-180));
        var speedAt180s = _subject.CalculateEffectiveUploadSpeed(torrent, 6_000_000, now);
        Assert.That(speedAt180s, Is.EqualTo(6_000_000));
    }

    [Test]
    public void ComputeUploadSpeed_is_alias_for_CalculateEffectiveUploadSpeed()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Leechers = 0
        };

        Assert.That(_subject.ComputeUploadSpeed(torrent, 10_000), Is.EqualTo(0));
    }

    [Test]
    public void ResetSeedingStartTime_clears_tracked_start_time()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var torrent = new Torrent
        {
            Id = 1,
            Leechers = 10,
            SeedingTime = 0
        };

        _subject.SetSeedingStartTime(torrent.Id, now.AddSeconds(-30));
        var speedWarmUp = _subject.CalculateEffectiveUploadSpeed(torrent, 6_000_000, now);
        Assert.That(speedWarmUp, Is.EqualTo(1_000_000));

        _subject.ResetSeedingStartTime(torrent.Id);

        // When not tracked and SeedingTime == 0, returns unscaled clamped ceiling
        var speedAfterReset = _subject.CalculateEffectiveUploadSpeed(torrent, 6_000_000, now);
        Assert.That(speedAfterReset, Is.EqualTo(6_000_000));
    }

    [Test]
    public void EventHandlers_reset_seeding_start_time_on_status_change_paused_or_deleted()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Paused,
            Leechers = 10,
            SeedingTime = 0
        };

        _subject.SetSeedingStartTime(torrent.Id, now.AddSeconds(-30));
        _subject.Handle(new TorrentPausedEvent(torrent));
        Assert.That(_subject.CalculateEffectiveUploadSpeed(torrent, 6_000_000, now), Is.EqualTo(6_000_000));

        _subject.SetSeedingStartTime(torrent.Id, now.AddSeconds(-30));
        _subject.Handle(new TorrentStatusChangedEvent(torrent, TorrentStatus.Seeding, TorrentStatus.Stopped));
        Assert.That(_subject.CalculateEffectiveUploadSpeed(torrent, 6_000_000, now), Is.EqualTo(6_000_000));

        _subject.SetSeedingStartTime(torrent.Id, now.AddSeconds(-30));
        _subject.Handle(new TorrentDeletedEvent(torrent.Id, torrent));
        Assert.That(_subject.CalculateEffectiveUploadSpeed(torrent, 6_000_000, now), Is.EqualTo(6_000_000));
    }

    [Test]
    public void ProcessSeeding_when_zero_leechers_without_swarm_intelligence_accumulates_zero_bytes()
    {
        _configService.SwarmIntelligenceEnabled.Returns(false);

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            Uploaded = 0,
            TotalSize = 10_000_000,
            Progress = 1.0,
            Seeders = 5,
            Leechers = 0,
            SeedingTime = 300
        };
        var torrents = new List<Torrent> { torrent };

        _stopPolicy.SelectStoppedTorrents(torrents).Returns(new HashSet<int>());
        _distributionManager.DistributeUploadSpeeds(1, Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(new long[] { 250_000 });

        _subject.ProcessSeeding(torrents, new SpeedLimits { MaxUploadSpeed = 250_000, MaxDownloadSpeed = 500_000 }, TimeSpan.FromSeconds(1));

        Assert.That(torrent.Uploaded, Is.EqualTo(0));
    }
}
