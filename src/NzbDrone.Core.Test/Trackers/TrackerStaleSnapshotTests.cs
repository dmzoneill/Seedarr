using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Test.Torrents;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using NzbDrone.Core.Trackers.Metrics;
using NzbDrone.Core.Trackers.MultiTracker;

namespace NzbDrone.Core.Test.Trackers;

[TestFixture]
public class TrackerStaleSnapshotTests
{
    private FullRowTorrentStore _store;
    private ITrackerEntryService _trackerEntryService;
    private IMultiTrackerManager _multiTracker;
    private IPeerDiscoveryService _peerDiscovery;
    private ITorrentEventLogService _eventLogService;
    private IConfigService _configService;
    private IEventAggregator _eventAggregator;
    private TrackerAnnounceService _announce;
    private TrackerScrapeService _scrape;

    [SetUp]
    public void Setup()
    {
        _store = new FullRowTorrentStore();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _multiTracker = Substitute.For<IMultiTrackerManager>();
        _peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        _eventLogService = Substitute.For<ITorrentEventLogService>();
        _configService = Substitute.For<IConfigService>();
        _eventAggregator = Substitute.For<IEventAggregator>();

        _configService.ListeningPort.Returns(51413);
        _configService.AnnounceIntervalSeconds.Returns(1800);

        _announce = new TrackerAnnounceService(
            _trackerEntryService,
            _multiTracker,
            _peerDiscovery,
            _eventLogService,
            _configService,
            trackerMetricService: Substitute.For<ITrackerMetricService>(),
            eventAggregator: _eventAggregator,
            torrentService: _store);

        _scrape = new TrackerScrapeService(
            _multiTracker,
            _trackerEntryService,
            _store,
            Substitute.For<ITrackerMetricService>());
    }

    [Test]
    public async Task Announce_with_stale_snapshot_does_not_roll_back_uploaded_or_pause()
    {
        var infoHash = "0123456789abcdef0123456789abcdef01234567";
        _store.Seed(new Torrent
        {
            Id = 1,
            Name = "StaleAnnounce",
            InfoHash = infoHash,
            Status = TorrentStatus.Seeding,
            Uploaded = 1_000,
            Downloaded = 500,
            TotalSize = 2_000,
            Progress = 0.25,
            Label = "old",
            Category = "old-cat"
        });

        _trackerEntryService.GetByTorrentId(1).Returns(new List<TrackerEntry>
        {
            new() { Id = 9, TorrentId = 1, Url = "http://tracker.example/announce", Enabled = true, Status = TrackerStatus.Working }
        });

        _multiTracker.AnnounceAsync(
                Arg.Any<TrackerAnnounceRequest>(),
                Arg.Any<List<List<string>>>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _store.Mutate(1, row =>
                {
                    row.Uploaded = 50_000;
                    row.Downloaded = 2_000;
                    row.Status = TorrentStatus.Paused;
                    row.Active = false;
                    row.Label = "user-label";
                    row.Category = "movies";
                });

                return Task.FromResult(new TrackerAnnounceResponse
                {
                    Success = true,
                    Complete = 12,
                    Incomplete = 4,
                    Interval = 1800
                });
            });

        var stale = _store.Get(1);
        Assert.That(stale.Uploaded, Is.EqualTo(1_000));
        Assert.That(stale.Status, Is.EqualTo(TorrentStatus.Seeding));

        await _announce.AnnounceTorrentAsync(stale, force: true);

        var saved = _store.Get(1);
        Assert.That(saved.Uploaded, Is.EqualTo(50_000), "Uploaded counter rolled back by stale full-row Update");
        Assert.That(saved.Downloaded, Is.EqualTo(2_000));
        Assert.That(saved.Status, Is.EqualTo(TorrentStatus.Paused), "Pause reverted by stale full-row Update");
        Assert.That(saved.Label, Is.EqualTo("user-label"));
        Assert.That(saved.Category, Is.EqualTo("movies"));
        Assert.That(saved.Seeders, Is.EqualTo(12));
        Assert.That(saved.Leechers, Is.EqualTo(4));
    }

    [Test]
    public async Task Announce_skips_a_torrent_paused_after_the_caller_loaded_it_and_reports_current_uploaded()
    {
        var infoHash = "abcdefabcdefabcdefabcdefabcdefabcdefabcd";
        _store.Seed(new Torrent
        {
            Id = 2,
            Name = "AlreadyPaused",
            InfoHash = infoHash,
            Status = TorrentStatus.Seeding,
            Uploaded = 1_000,
            Downloaded = 100,
            TotalSize = 1_000,
            Progress = 1.0
        });

        var stale = _store.Get(2);
        _store.Mutate(2, row =>
        {
            row.Status = TorrentStatus.Paused;
            row.Active = false;
            row.Uploaded = 50_000;
        });

        _trackerEntryService.GetByTorrentId(2).Returns(new List<TrackerEntry>
        {
            new() { Id = 3, TorrentId = 2, Url = "http://tracker.example/announce", Enabled = true }
        });

        TrackerAnnounceRequest captured = null;
        _multiTracker.AnnounceAsync(
                Arg.Any<TrackerAnnounceRequest>(),
                Arg.Any<List<List<string>>>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<TrackerAnnounceRequest>();
                return Task.FromResult(new TrackerAnnounceResponse { Success = true, Complete = 1, Incomplete = 1, Interval = 1800 });
            });

        var results = await _announce.AnnounceTorrentAsync(stale, force: true);

        Assert.That(results, Is.Empty, "a torrent paused after the snapshot was taken must not be announced");
        Assert.That(captured, Is.Null);
        var saved = _store.Get(2);
        Assert.That(saved.Status, Is.EqualTo(TorrentStatus.Paused));
        Assert.That(saved.Uploaded, Is.EqualTo(50_000));
    }

    [Test]
    public async Task Scrape_does_not_roll_back_counters_or_status_changed_while_the_scrape_runs()
    {
        _store.Seed(new Torrent
        {
            Id = 4,
            Name = "Scrape",
            InfoHash = "9999888877776666555544443333222211110000",
            Status = TorrentStatus.Downloading,
            Uploaded = 100,
            Downloaded = 200,
            TotalSize = 10_000,
            Progress = 0.02,
            Label = "keep",
            Availability = 0
        });

        _trackerEntryService.GetByTorrentId(4).Returns(new List<TrackerEntry>
        {
            new() { Id = 8, TorrentId = 4, Url = "http://tracker.example/announce", Enabled = true }
        });

        _multiTracker.Scrape(Arg.Any<TrackerEntry>(), Arg.Any<string>())
            .Returns(_ =>
            {
                _store.Mutate(4, row =>
                {
                    row.Uploaded = 9_000;
                    row.Downloaded = 8_000;
                    row.Status = TorrentStatus.Paused;
                    row.Active = false;
                    row.Label = "paused-by-user";
                });

                return new TrackerScrapeResponse
                {
                    Success = true,
                    Complete = 6,
                    Incomplete = 2,
                    Downloaded = 10
                };
            });

        await _scrape.ScrapeTorrentAsync(4);

        var saved = _store.Get(4);
        Assert.That(saved.Uploaded, Is.EqualTo(9_000));
        Assert.That(saved.Downloaded, Is.EqualTo(8_000));
        Assert.That(saved.Status, Is.EqualTo(TorrentStatus.Paused));
        Assert.That(saved.Label, Is.EqualTo("paused-by-user"));
        Assert.That(saved.Seeders, Is.EqualTo(6));
        Assert.That(saved.Leechers, Is.EqualTo(2));
    }

    [Test]
    public async Task AnnounceTracker_skips_when_the_row_was_paused_after_the_caller_loaded_it()
    {
        _store.Seed(new Torrent
        {
            Id = 11,
            Name = "TrackerSkip",
            InfoHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            Status = TorrentStatus.Seeding,
            Uploaded = 10,
            TotalSize = 100
        });
        var stale = _store.Get(11);
        _store.Mutate(11, row =>
        {
            row.Status = TorrentStatus.Paused;
            row.Active = false;
        });

        var result = await _announce.AnnounceTrackerAsync(
            stale,
            new TrackerEntry { Id = 1, TorrentId = 11, Url = "http://tracker.example/announce", Enabled = true },
            force: true);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Does.Contain("no longer active"));
        await _multiTracker.DidNotReceive().AnnounceAsync(
            Arg.Any<TrackerAnnounceRequest>(),
            Arg.Any<List<List<string>>>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Announce_of_an_unsaved_torrent_does_not_persist_swarm_or_pause_state()
    {
        var torrent = new Torrent
        {
            Id = 0,
            Name = "Unsaved",
            InfoHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            Status = TorrentStatus.Seeding,
            Uploaded = 1,
            TotalSize = 10
        };
        _trackerEntryService.GetByTorrentId(0).Returns(new List<TrackerEntry>
        {
            new() { Id = 1, TorrentId = 0, Url = "http://tracker.example/announce", Enabled = true }
        });
        _multiTracker.AnnounceAsync(
                Arg.Any<TrackerAnnounceRequest>(),
                Arg.Any<List<List<string>>>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TrackerAnnounceResponse
            {
                Success = true,
                Complete = 2,
                Incomplete = 1,
                Interval = 1800,
                WarningMessage = "account flagged"
            }));

        var results = await _announce.AnnounceTorrentAsync(torrent, force: true);

        Assert.That(results, Is.Not.Empty);
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Paused));
        Assert.That(_store.FullRowWrites, Is.EqualTo(0));
    }
}
