using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Seeding;

[TestFixture]
public class StopPolicyTest
{
    private IConfigService _configService;
    private ITagService _tagService;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _tagService = Substitute.For<ITagService>();
    }

    [Test]
    public void SelectStoppedTorrents_returns_empty_when_max_percentage_zero()
    {
        _configService.UploadStoppedMinPercentage.Returns(0);
        _configService.UploadStoppedMaxPercentage.Returns(0);

        var subject = new StopPolicy(_configService, new RandomNumberGenerator(42));
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1 },
            new Torrent { Id = 2 }
        };

        var stopped = subject.SelectStoppedTorrents(torrents);

        Assert.That(stopped, Is.Empty);
    }

    [Test]
    public void SelectStoppedTorrents_never_stops_force_start_torrents()
    {
        _configService.UploadStoppedMinPercentage.Returns(100);
        _configService.UploadStoppedMaxPercentage.Returns(100);

        var subject = new StopPolicy(_configService, new RandomNumberGenerator(42));
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, ForceStart = true },
            new Torrent { Id = 2, ForceStart = true }
        };

        var stopped = subject.SelectStoppedTorrents(torrents);

        Assert.That(stopped, Is.Empty);
    }

    [Test]
    public void SelectStoppedTorrents_leaves_at_least_one_eligible_active()
    {
        _configService.UploadStoppedMinPercentage.Returns(100);
        _configService.UploadStoppedMaxPercentage.Returns(100);

        var subject = new StopPolicy(_configService, new RandomNumberGenerator(42));
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1 },
            new Torrent { Id = 2 },
            new Torrent { Id = 3 }
        };

        var stopped = subject.SelectStoppedTorrents(torrents);

        // stoppedCount = Math.Min(3, 3 - 1) = 2
        Assert.That(stopped.Count, Is.EqualTo(2));
    }

    [Test]
    public void SelectStoppedTorrents_is_deterministic_under_fixed_random()
    {
        _configService.UploadStoppedMinPercentage.Returns(50);
        _configService.UploadStoppedMaxPercentage.Returns(50);

        var subject1 = new StopPolicy(_configService, new RandomNumberGenerator(12345));
        var subject2 = new StopPolicy(_configService, new RandomNumberGenerator(12345));

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1 },
            new Torrent { Id = 2 },
            new Torrent { Id = 3 },
            new Torrent { Id = 4 },
            new Torrent { Id = 5 }
        };

        var stopped1 = subject1.SelectStoppedTorrents(torrents);
        var stopped2 = subject2.SelectStoppedTorrents(torrents);

        Assert.That(stopped1, Is.EquivalentTo(stopped2));
    }

    [Test]
    public void ShouldStop_returns_false_when_torrent_is_null_or_force_start()
    {
        var subject = new StopPolicy(_configService, tagService: _tagService);
        var torrent = new Torrent { Id = 1, ForceStart = true, Ratio = 10.0, SeedingTime = 100_000 };

        Assert.That(subject.ShouldStop(null), Is.False);
        Assert.That(subject.ShouldStop(torrent), Is.False);
    }

    [Test]
    public void ShouldStop_evaluates_tag_min_seed_ratio()
    {
        var subject = new StopPolicy(_configService, tagService: _tagService);
        var tag = new Tag { Id = 1, MinSeedRatio = 2.0 };
        var torrentBelow = new Torrent { Id = 1, Ratio = 1.9, TagIds = new List<int> { 1 } };
        var torrentAbove = new Torrent { Id = 2, Ratio = 2.0, TagIds = new List<int> { 1 } };

        _tagService.Get(1).Returns(tag);

        Assert.That(subject.ShouldStop(torrentBelow), Is.False);
        Assert.That(subject.ShouldStop(torrentAbove), Is.True);
    }

    [Test]
    public void ShouldStop_evaluates_tag_min_seed_time()
    {
        var subject = new StopPolicy(_configService, tagService: _tagService);
        var tag = new Tag { Id = 1, MinSeedTimeSeconds = 86400 };
        var torrentBelow = new Torrent { Id = 1, SeedingTime = 86399, TagIds = new List<int> { 1 } };
        var torrentAbove = new Torrent { Id = 2, SeedingTime = 86400, TagIds = new List<int> { 1 } };

        _tagService.Get(1).Returns(tag);

        Assert.That(subject.ShouldStop(torrentBelow), Is.False);
        Assert.That(subject.ShouldStop(torrentAbove), Is.True);
    }

    [Test]
    public void ShouldStop_evaluates_tag_with_both_ratio_and_time()
    {
        var subject = new StopPolicy(_configService, tagService: _tagService);
        var tag = new Tag { Id = 1, MinSeedRatio = 2.0, MinSeedTimeSeconds = 86400 };
        var torrentNeither = new Torrent { Id = 1, Ratio = 1.0, SeedingTime = 1000, TagIds = new List<int> { 1 } };
        var torrentRatioMet = new Torrent { Id = 2, Ratio = 2.5, SeedingTime = 1000, TagIds = new List<int> { 1 } };
        var torrentTimeMet = new Torrent { Id = 3, Ratio = 0.5, SeedingTime = 90000, TagIds = new List<int> { 1 } };

        _tagService.Get(1).Returns(tag);

        Assert.That(subject.ShouldStop(torrentNeither), Is.False);
        Assert.That(subject.ShouldStop(torrentRatioMet), Is.True);
        Assert.That(subject.ShouldStop(torrentTimeMet), Is.True);
    }

    [Test]
    public void ShouldStop_evaluates_multiple_tags_requiring_all_tag_policies_satisfied()
    {
        var subject = new StopPolicy(_configService, tagService: _tagService);
        var tag1 = new Tag { Id = 1, MinSeedRatio = 1.5 };
        var tag2 = new Tag { Id = 2, MinSeedRatio = 2.0 };

        var torrent = new Torrent { Id = 1, Ratio = 1.8, TagIds = new List<int> { 1, 2 } };

        _tagService.Get(1).Returns(tag1);
        _tagService.Get(2).Returns(tag2);

        // Tag 1 (1.5) is satisfied, but Tag 2 (2.0) is not satisfied yet
        Assert.That(subject.ShouldStop(torrent), Is.False);

        torrent.Ratio = 2.1;
        // Now both Tag 1 and Tag 2 are satisfied
        Assert.That(subject.ShouldStop(torrent), Is.True);
    }

    [Test]
    public void ShouldStop_with_explicit_tags_list_uses_passed_tags()
    {
        var subject = new StopPolicy(_configService);
        var tag = new Tag { Id = 1, MinSeedRatio = 3.0 };
        var torrent = new Torrent { Id = 1, Ratio = 3.5, TagIds = new List<int> { 1 } };

        Assert.That(subject.ShouldStop(torrent, new List<Tag> { tag }), Is.True);
    }

    [Test]
    public void ShouldStop_falls_back_to_torrent_or_global_limit_when_no_tag_policies()
    {
        _configService.GlobalSeedRatioLimit.Returns(1.5);
        var subject = new StopPolicy(_configService, tagService: _tagService);

        var tagNoPolicy = new Tag { Id = 1 }; // No ratio or time limit set
        _tagService.Get(1).Returns(tagNoPolicy);

        var torrentBelow = new Torrent { Id = 1, Ratio = 1.0, TagIds = new List<int> { 1 } };
        var torrentAbove = new Torrent { Id = 2, Ratio = 1.6, TagIds = new List<int> { 1 } };

        Assert.That(subject.ShouldStop(torrentBelow), Is.False);
        Assert.That(subject.ShouldStop(torrentAbove), Is.True);
    }
}
