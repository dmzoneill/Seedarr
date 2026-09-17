using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Tags;

[TestFixture]
public class AutoTaggerServiceTest
{
    private IAutoTaggerRuleRepository _ruleRepository;
    private ITorrentService _torrentService;
    private ITagService _tagService;
    private AutoTaggerService _subject;

    [SetUp]
    public void SetUp()
    {
        _ruleRepository = Substitute.For<IAutoTaggerRuleRepository>();
        _torrentService = Substitute.For<ITorrentService>();
        _tagService = Substitute.For<ITagService>();
        _subject = new AutoTaggerService(_ruleRepository, _torrentService, _tagService);
    }

    [Test]
    public void EvaluateTorrent_regex_rule_matches_title_and_adds_tag()
    {
        var rule = new AutoTaggerRule
        {
            Id = 1,
            Name = "4K Matcher",
            TagId = 10,
            RuleType = AutoTaggerRuleType.Regex,
            Pattern = @"(2160p|4K)",
            IsEnabled = true,
            Priority = 1
        };

        _ruleRepository.All().Returns(new List<AutoTaggerRule> { rule });

        var torrent = new Torrent
        {
            Id = 1,
            Name = "The.Matrix.1999.2160p.UHD.BluRay.x265",
            TagIds = new List<int>()
        };

        _subject.EvaluateTorrent(torrent);

        Assert.That(torrent.TagIds, Contains.Item(10));
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public void EvaluateTorrent_tracker_domain_rule_matches_announce_url_and_adds_tag()
    {
        var rule = new AutoTaggerRule
        {
            Id = 2,
            Name = "PTP Tracker",
            TagId = 20,
            RuleType = AutoTaggerRuleType.TrackerDomain,
            Pattern = "*passthepopcorn.me*",
            IsEnabled = true,
            Priority = 1
        };

        _ruleRepository.All().Returns(new List<AutoTaggerRule> { rule });

        var torrent = new Torrent
        {
            Id = 2,
            Name = "Some.Movie.1080p",
            TrackerUrl = "https://tracker.passthepopcorn.me:443/announce/abcdef123456",
            TagIds = new List<int>()
        };

        _subject.EvaluateTorrent(torrent);

        Assert.That(torrent.TagIds, Contains.Item(20));
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public void EvaluateTorrent_size_rule_matches_greater_than_10gb_and_adds_tag()
    {
        var rule = new AutoTaggerRule
        {
            Id = 3,
            Name = "Large Release",
            TagId = 30,
            RuleType = AutoTaggerRuleType.Size,
            Pattern = "> 10GB",
            IsEnabled = true,
            Priority = 1
        };

        _ruleRepository.All().Returns(new List<AutoTaggerRule> { rule });

        var torrentSmall = new Torrent
        {
            Id = 3,
            Name = "Small.Release.mkv",
            TotalSize = 5L * 1024 * 1024 * 1024, // 5GB
            TagIds = new List<int>()
        };

        var torrentLarge = new Torrent
        {
            Id = 4,
            Name = "Large.Release.mkv",
            TotalSize = 15L * 1024 * 1024 * 1024, // 15GB
            TagIds = new List<int>()
        };

        _subject.EvaluateTorrent(torrentSmall);
        Assert.That(torrentSmall.TagIds, Does.Not.Contain(30));
        _torrentService.DidNotReceive().Update(torrentSmall);

        _subject.EvaluateTorrent(torrentLarge);
        Assert.That(torrentLarge.TagIds, Contains.Item(30));
        _torrentService.Received(1).Update(torrentLarge);
    }

    [Test]
    public void Handle_TorrentAddedEvent_evaluates_torrent()
    {
        var rule = new AutoTaggerRule
        {
            Id = 1,
            Name = "Remux",
            TagId = 40,
            RuleType = AutoTaggerRuleType.Regex,
            Pattern = "REMUX",
            IsEnabled = true,
            Priority = 0
        };

        _ruleRepository.All().Returns(new List<AutoTaggerRule> { rule });

        var torrent = new Torrent
        {
            Id = 5,
            Name = "Inception.2010.REMUX.1080p.AVC.DTS-HD.MA.5.1",
            TagIds = new List<int>()
        };

        _subject.Handle(new TorrentAddedEvent(torrent));

        Assert.That(torrent.TagIds, Contains.Item(40));
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public void EvaluateTorrent_skips_disabled_rules()
    {
        var disabledRule = new AutoTaggerRule
        {
            Id = 1,
            Name = "Disabled Rule",
            TagId = 50,
            RuleType = AutoTaggerRuleType.Regex,
            Pattern = "Test",
            IsEnabled = false,
            Priority = 10
        };

        _ruleRepository.All().Returns(new List<AutoTaggerRule> { disabledRule });

        var torrent = new Torrent
        {
            Id = 6,
            Name = "Test.Torrent.Release",
            TagIds = new List<int>()
        };

        _subject.EvaluateTorrent(torrent);

        Assert.That(torrent.TagIds, Does.Not.Contain(50));
        _torrentService.DidNotReceive().Update(torrent);
    }

    [Test]
    public void EvaluateTorrent_does_not_re_add_existing_tags()
    {
        var rule = new AutoTaggerRule
        {
            Id = 1,
            Name = "HDR Rule",
            TagId = 60,
            RuleType = AutoTaggerRuleType.Quality,
            Pattern = "HDR",
            IsEnabled = true,
            Priority = 1
        };

        _ruleRepository.All().Returns(new List<AutoTaggerRule> { rule });

        var torrent = new Torrent
        {
            Id = 7,
            Name = "Movie.2024.HDR.2160p",
            TagIds = new List<int> { 60 }
        };

        _subject.EvaluateTorrent(torrent);

        Assert.That(torrent.TagIds.Count, Is.EqualTo(1));
        _torrentService.DidNotReceive().Update(torrent);
    }

    [Test]
    public void Handle_MediaEnrichedEvent_and_TorrentDownloadCompletedEvent_evaluate_torrents()
    {
        var rule = new AutoTaggerRule
        {
            Id = 1,
            Name = "Flac Rule",
            TagId = 70,
            RuleType = AutoTaggerRuleType.MediaInfo,
            Pattern = "FLAC",
            IsEnabled = true,
            Priority = 1
        };

        _ruleRepository.All().Returns(new List<AutoTaggerRule> { rule });

        var torrent1 = new Torrent
        {
            Id = 8,
            Name = "Album.2024.FLAC.Lossless",
            TagIds = new List<int>()
        };

        var torrent2 = new Torrent
        {
            Id = 9,
            Name = "Another.Album.FLAC",
            TagIds = new List<int>()
        };

        _torrentService.Get(8).Returns(torrent1);

        _subject.Handle(new MediaEnrichedEvent { TorrentId = 8 });
        Assert.That(torrent1.TagIds, Contains.Item(70));

        _subject.Handle(new TorrentDownloadCompletedEvent(torrent2));
        Assert.That(torrent2.TagIds, Contains.Item(70));
    }

    [Test]
    public void EvaluateAll_evaluates_every_torrent_in_library()
    {
        var rule = new AutoTaggerRule
        {
            Id = 1,
            Name = "4K Rule",
            TagId = 80,
            RuleType = AutoTaggerRuleType.Regex,
            Pattern = "4K",
            IsEnabled = true,
            Priority = 1
        };

        _ruleRepository.All().Returns(new List<AutoTaggerRule> { rule });

        var t1 = new Torrent { Id = 1, Name = "Movie.1.4K", TagIds = new List<int>() };
        var t2 = new Torrent { Id = 2, Name = "Movie.2.1080p", TagIds = new List<int>() };
        var t3 = new Torrent { Id = 3, Name = "Movie.3.4K.HDR", TagIds = new List<int>() };

        _torrentService.GetAll().Returns(new List<Torrent> { t1, t2, t3 });

        _subject.EvaluateAll();

        Assert.That(t1.TagIds, Contains.Item(80));
        Assert.That(t2.TagIds, Does.Not.Contain(80));
        Assert.That(t3.TagIds, Contains.Item(80));

        _torrentService.Received(1).Update(t1);
        _torrentService.Received(1).Update(t3);
        _torrentService.DidNotReceive().Update(t2);
    }
}
