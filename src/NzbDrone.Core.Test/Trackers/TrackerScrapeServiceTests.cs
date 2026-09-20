using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using NzbDrone.Core.Trackers.Metrics;
using NzbDrone.Core.Trackers.MultiTracker;
using Seedarr.Api.V1.Torrents;

namespace NzbDrone.Core.Test.Trackers;

[TestFixture]
public class TrackerScrapeServiceTests
{
    private IMultiTrackerManager _multiTracker;
    private ITrackerEntryService _trackerEntryService;
    private ITorrentService _torrentService;
    private ITrackerMetricService _trackerMetricService;
    private TrackerScrapeService _service;

    [SetUp]
    public void SetUp()
    {
        _multiTracker = Substitute.For<IMultiTrackerManager>();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _torrentService = Substitute.For<ITorrentService>();
        _trackerMetricService = Substitute.For<ITrackerMetricService>();

        _service = new TrackerScrapeService(
            _multiTracker,
            _trackerEntryService,
            _torrentService,
            _trackerMetricService);
    }

    [Test]
    public async Task ScrapeTorrentAsync_updates_LastScrape_Seeders_Leechers_and_records_metric()
    {
        const int torrentId = 1;
        var torrent = new Torrent
        {
            Id = torrentId,
            Name = "Test Torrent",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Seeders = 0,
            Leechers = 0,
            Availability = 0.0
        };

        var entry = new TrackerEntry
        {
            Id = 10,
            TorrentId = torrentId,
            Url = "http://tracker.example.com/announce",
            Enabled = true,
            Seeders = 0,
            Leechers = 0,
            LastScrape = null
        };

        _torrentService.Get(torrentId).Returns(torrent);
        _trackerEntryService.GetByTorrentId(torrentId).Returns(new List<TrackerEntry> { entry });

        var scrapeResponse = new TrackerScrapeResponse
        {
            Success = true,
            Complete = 42,
            Incomplete = 13,
            Downloaded = 100
        };
        _multiTracker.Scrape(entry, torrent.InfoHash).Returns(scrapeResponse);

        var result = await _service.ScrapeTorrentAsync(torrentId);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Seeders, Is.EqualTo(42));
        Assert.That(result.Leechers, Is.EqualTo(13));
        Assert.That(result.Status, Is.EqualTo("Working"));
        Assert.That(result.SuccessfulScrapes, Is.EqualTo(1));
        Assert.That(result.FailedScrapes, Is.EqualTo(0));

        Assert.That(entry.LastScrape, Is.Not.Null);
        Assert.That(entry.Seeders, Is.EqualTo(42));
        Assert.That(entry.Leechers, Is.EqualTo(13));
        Assert.That(entry.Downloaded, Is.EqualTo(100));
        Assert.That(entry.Status, Is.EqualTo(TrackerStatus.Working));

        _trackerEntryService.Received(1).Update(entry);
        _trackerMetricService.Received(1).RecordScrape(
            entry.Url,
            Arg.Any<long>(),
            true,
            42,
            13,
            100,
            null);

        _torrentService.Received(1).Update(Arg.Is<Torrent>(t =>
            t.Id == torrentId &&
            t.Seeders == 42 &&
            t.Leechers == 13 &&
            t.Availability >= 42.0));
    }

    [Test]
    public async Task ScrapeAllTorrentsAsync_handles_failed_trackers_gracefully_without_aborting_remaining_scrapes()
    {
        var torrent1 = new Torrent { Id = 1, Name = "Torrent 1", InfoHash = "1111111111111111111111111111111111111111" };
        var torrent2 = new Torrent { Id = 2, Name = "Torrent 2", InfoHash = "2222222222222222222222222222222222222222" };
        var torrent3 = new Torrent { Id = 3, Name = "Torrent 3", InfoHash = "3333333333333333333333333333333333333333" };

        var entry1 = new TrackerEntry { Id = 10, TorrentId = 1, Url = "http://tracker1.example.com/announce", Enabled = true };
        var entry2 = new TrackerEntry { Id = 20, TorrentId = 2, Url = "http://tracker2.example.com/announce", Enabled = true };
        var entry3 = new TrackerEntry { Id = 30, TorrentId = 3, Url = "http://tracker3.example.com/announce", Enabled = true };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2, torrent3 });
        _torrentService.Get(1).Returns(torrent1);
        _torrentService.Get(2).Returns(torrent2);
        _torrentService.Get(3).Returns(torrent3);

        _trackerEntryService.GetByTorrentId(1).Returns(new List<TrackerEntry> { entry1 });
        _trackerEntryService.GetByTorrentId(2).Returns(new List<TrackerEntry> { entry2 });
        _trackerEntryService.GetByTorrentId(3).Returns(new List<TrackerEntry> { entry3 });

        // Tracker 1 returns failure response
        _multiTracker.Scrape(entry1, torrent1.InfoHash).Returns(new TrackerScrapeResponse
        {
            Success = false,
            FailureReason = "Connection timeout"
        });

        // Tracker 2 throws an exception
        _multiTracker.Scrape(entry2, torrent2.InfoHash).Throws(new InvalidOperationException("Socket error"));

        // Tracker 3 succeeds
        _multiTracker.Scrape(entry3, torrent3.InfoHash).Returns(new TrackerScrapeResponse
        {
            Success = true,
            Complete = 25,
            Incomplete = 5,
            Downloaded = 50
        });

        var count = await _service.ScrapeAllTorrentsAsync();

        Assert.That(count, Is.EqualTo(3));

        // Metric records for all 3
        _trackerMetricService.Received(1).RecordScrape(
            entry1.Url,
            Arg.Any<long>(),
            false,
            0,
            0,
            0,
            "Connection timeout");

        _trackerMetricService.Received(1).RecordScrape(
            entry2.Url,
            Arg.Any<long>(),
            false,
            0,
            0,
            0,
            "Socket error");

        _trackerMetricService.Received(1).RecordScrape(
            entry3.Url,
            Arg.Any<long>(),
            true,
            25,
            5,
            50,
            null);

        // Torrent 3 updated successfully
        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 3 && t.Seeders == 25 && t.Leechers == 5));
    }

    [Test]
    public async Task ScrapeTorrentAsync_returns_NotFound_when_torrent_does_not_exist()
    {
        _torrentService.Get(999).Returns((Torrent)null);

        var result = await _service.ScrapeTorrentAsync(999);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Status, Is.EqualTo("NotFound"));
    }

    [Test]
    public async Task ScrapeTorrentAsync_returns_NoTrackers_when_no_active_trackers()
    {
        var torrent = new Torrent
        {
            Id = 5,
            InfoHash = "5555555555555555555555555555555555555555",
            TrackerUrl = null
        };
        _torrentService.Get(5).Returns(torrent);
        _trackerEntryService.GetByTorrentId(5).Returns(new List<TrackerEntry>());

        var result = await _service.ScrapeTorrentAsync(5);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Status, Is.EqualTo("NoTrackers"));
    }

    [Test]
    public void TrackerScrapeJob_executes_ScrapeAllTorrentsAsync()
    {
        var scrapeService = Substitute.For<ITrackerScrapeService>();
        scrapeService.ScrapeAllTorrentsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(5));

        var job = new TrackerScrapeJob(scrapeService);
        Assert.That(job.DefaultInterval, Is.EqualTo(60));

        job.Execute();

        scrapeService.Received(1).ScrapeAllTorrentsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Controller_Scrape_endpoint_returns_Ok_with_result_when_torrent_exists()
    {
        const int torrentId = 12;
        var torrent = new Torrent
        {
            Id = torrentId,
            Name = "Active Torrent"
        };
        _torrentService.Get(torrentId).Returns(torrent);

        var scrapeService = Substitute.For<ITrackerScrapeService>();
        var expectedResult = new TrackerScrapeResult
        {
            TorrentId = torrentId,
            Success = true,
            Seeders = 15,
            Leechers = 3,
            Status = "Working"
        };
        scrapeService.ScrapeTorrentAsync(torrentId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(expectedResult));

        var controller = new TorrentController(
            _torrentService,
            Substitute.For<ITorrentFileService>(),
            _trackerEntryService,
            Substitute.For<ITorrentImportService>(),
            Substitute.For<IConnectionManager>(),
            Substitute.For<ITorrentEventLogService>(),
            Substitute.For<IConfigService>(),
            Substitute.For<NzbDrone.SignalR.IBroadcastSignalRMessage>(),
            new TorrentResourceValidator(),
            trackerScrapeService: scrapeService);

        var actionResult = await controller.Scrape(torrentId);

        Assert.That(actionResult, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult;
        Assert.That(okResult.Value, Is.SameAs(expectedResult));
    }

    [Test]
    public async Task Controller_Scrape_endpoint_returns_NotFound_when_torrent_does_not_exist()
    {
        const int torrentId = 99;
        _torrentService.Get(torrentId).Returns((Torrent)null);

        var scrapeService = Substitute.For<ITrackerScrapeService>();
        var controller = new TorrentController(
            _torrentService,
            Substitute.For<ITorrentFileService>(),
            _trackerEntryService,
            Substitute.For<ITorrentImportService>(),
            Substitute.For<IConnectionManager>(),
            Substitute.For<ITorrentEventLogService>(),
            Substitute.For<IConfigService>(),
            Substitute.For<NzbDrone.SignalR.IBroadcastSignalRMessage>(),
            new TorrentResourceValidator(),
            trackerScrapeService: scrapeService);

        var actionResult = await controller.Scrape(torrentId);

        Assert.That(actionResult, Is.InstanceOf<NotFoundResult>());
        await scrapeService.DidNotReceive().ScrapeTorrentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
