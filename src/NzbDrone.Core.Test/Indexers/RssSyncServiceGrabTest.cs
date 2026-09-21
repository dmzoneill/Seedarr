using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class RssSyncServiceGrabTest
{
    private IRssRuleEvaluator _ruleEvaluator;
    private IIndexerStatusService _indexerStatusService;
    private IRssSeenReleaseRepository _seenReleaseRepository;
    private ITorrentService _torrentService;
    private IRssGrabHistoryRepository _grabHistoryRepository;
    private RssSyncService _service;

    [SetUp]
    public void SetUp()
    {
        _ruleEvaluator = Substitute.For<IRssRuleEvaluator>();
        _indexerStatusService = Substitute.For<IIndexerStatusService>();
        _seenReleaseRepository = Substitute.For<IRssSeenReleaseRepository>();
        _torrentService = Substitute.For<ITorrentService>();
        _grabHistoryRepository = Substitute.For<IRssGrabHistoryRepository>();

        _service = new RssSyncService(
            _ruleEvaluator,
            _indexerStatusService,
            _seenReleaseRepository,
            _torrentService,
            _grabHistoryRepository);
    }

    [Test]
    public void GrabRelease_should_map_SavePath_Tags_SequentialDownload_and_InitialStatus_to_Torrent()
    {
        var rule = new RssRule
        {
            Id = 5,
            Name = "4K Movies Profile",
            SavePath = "/data/downloads/movies4k",
            Tags = new List<int> { 10, 20 },
            SequentialDownload = true,
            InitialStatus = TorrentStatus.Paused,
            CategoryId = 2000
        };

        var release = new ReleaseInfo
        {
            Title = "Movie.Title.2024.2160p.WEB-DL.x265",
            InfoHash = "1234567890abcdef1234567890abcdef12345678",
            Size = 5000000000,
            IndexerId = 2,
            Indexer = "Prowlarr"
        };

        _torrentService.ExistsByInfoHash(Arg.Any<string>()).Returns(false);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(callInfo => callInfo.Arg<Torrent>());

        var result = _service.GrabRelease(release, rule);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Torrent, Is.Not.Null);
        Assert.That(result.Torrent.SavePath, Is.EqualTo("/data/downloads/movies4k"));
        Assert.That(result.Torrent.TagIds, Is.EquivalentTo(new[] { 10, 20 }));
        Assert.That(result.Torrent.SequentialDownload, Is.True);
        Assert.That(result.Torrent.Status, Is.EqualTo(TorrentStatus.Paused));

        _grabHistoryRepository.Received(1).Insert(Arg.Is<RssGrabHistory>(h =>
            h.ReleaseTitle == release.Title &&
            h.RuleId == rule.Id &&
            h.RuleName == rule.Name &&
            h.Status == "Grabbed" &&
            h.InfoHash == release.InfoHash));

        _seenReleaseRepository.Received(1).MarkSeen(
            release.IndexerId,
            release,
            RssSeenStatus.Grabbed,
            rule.Id);
    }

    [Test]
    public void GrabRelease_when_duplicate_should_record_failed_audit_history()
    {
        var rule = new RssRule
        {
            Id = 1,
            Name = "Default"
        };

        var release = new ReleaseInfo
        {
            Title = "Duplicate.Torrent.1080p",
            InfoHash = "alreadyexists123",
            IndexerId = 1
        };

        _torrentService.ExistsByInfoHash("alreadyexists123").Returns(true);

        var result = _service.GrabRelease(release, rule);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("already exists"));

        _grabHistoryRepository.Received(1).Insert(Arg.Is<RssGrabHistory>(h =>
            h.Status == "Failed" &&
            h.ErrorMessage.Contains("already exists")));
    }

    [Test]
    public void GrabRelease_when_infohash_in_download_history_should_record_failed_audit_history()
    {
        var downloadHistoryRepo = Substitute.For<IDownloadHistoryRepository>();
        var service = new RssSyncService(
            _ruleEvaluator,
            _indexerStatusService,
            _seenReleaseRepository,
            _torrentService,
            _grabHistoryRepository,
            downloadHistoryRepository: downloadHistoryRepo);

        var rule = new RssRule
        {
            Id = 1,
            Name = "Default"
        };

        var release = new ReleaseInfo
        {
            Title = "History.Duplicate.Torrent.1080p",
            InfoHash = "historydup123",
            IndexerId = 1
        };

        _torrentService.ExistsByInfoHash("historydup123").Returns(false);
        downloadHistoryRepo.FindByInfoHash("historydup123").Returns(new DownloadHistory { InfoHash = "historydup123" });

        var result = service.GrabRelease(release, rule);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("download history"));

        _grabHistoryRepository.Received(1).Insert(Arg.Is<RssGrabHistory>(h =>
            h.Status == "Failed" &&
            h.ErrorMessage.Contains("download history")));
    }
}
