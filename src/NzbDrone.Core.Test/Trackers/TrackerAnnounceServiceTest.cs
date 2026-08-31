using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using NzbDrone.Core.Trackers.Metrics;
using NzbDrone.Core.Trackers.MultiTracker;

namespace NzbDrone.Core.Test.Trackers;

[TestFixture]
public class TrackerAnnounceServiceTest
{
    private ITrackerEntryService _trackerEntryService;
    private IMultiTrackerManager _multiTracker;
    private IPeerDiscoveryService _peerDiscovery;
    private ITorrentEventLogService _eventLogService;
    private IConfigService _configService;
    private ITrackerMetricService _trackerMetricService;
    private TrackerAnnounceService _service;

    [SetUp]
    public void Setup()
    {
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _multiTracker = Substitute.For<IMultiTrackerManager>();
        _peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        _eventLogService = Substitute.For<ITorrentEventLogService>();
        _configService = Substitute.For<IConfigService>();
        _trackerMetricService = Substitute.For<ITrackerMetricService>();

        _configService.ListeningPort.Returns(51413);
        _configService.AnnounceIntervalSeconds.Returns(1800);

        _service = new TrackerAnnounceService(
            _trackerEntryService,
            _multiTracker,
            _peerDiscovery,
            _eventLogService,
            _configService,
            _trackerMetricService);
    }

    [Test]
    public void AnnounceTorrent_should_log_announcing_and_success_for_each_enabled_tracker()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Test.Movie.2024",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Uploaded = 1000,
            Downloaded = 500,
            TotalSize = 2000,
            Status = TorrentStatus.Seeding
        };

        var tracker1 = new TrackerEntry { Id = 1, TorrentId = 42, Url = "http://tracker1.org/announce", Enabled = true };
        var tracker2 = new TrackerEntry { Id = 2, TorrentId = 42, Url = "udp://tracker2.org:1337/announce", Enabled = true };

        _trackerEntryService.GetByTorrentId(42).Returns(new List<TrackerEntry> { tracker1, tracker2 });

        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse
            {
                Success = true,
                Complete = 10,
                Incomplete = 2,
                Interval = 1800,
                Peers = new List<TrackerPeer> { new() { Ip = "1.2.3.4", Port = 6881 } }
            });

        var results = _service.AnnounceTorrent(torrent, force: true);

        Assert.That(results.Count, Is.EqualTo(2));
        Assert.That(results[0].Success, Is.True);
        Assert.That(results[1].Success, Is.True);

        // Verify that per-tracker announce started logs were emitted
        _eventLogService.Received(1).Info(
            42,
            "Tracker",
            Arg.Is<string>(s => s.Contains("Announcing to tracker: http://tracker1.org/announce")));

        _eventLogService.Received(1).Info(
            42,
            "Tracker",
            Arg.Is<string>(s => s.Contains("Announcing to tracker: udp://tracker2.org:1337/announce")));

        // Verify success logs
        _eventLogService.Received(1).Info(
            42,
            "Tracker",
            Arg.Is<string>(s => s.Contains("Tracker announce succeeded: http://tracker1.org/announce") && s.Contains("Seeders: 10")));

        _eventLogService.Received(1).Info(
            42,
            "Tracker",
            Arg.Is<string>(s => s.Contains("Tracker announce succeeded: udp://tracker2.org:1337/announce") && s.Contains("Seeders: 10")));

        // Verify peers discovered logged and added
        _peerDiscovery.Received(2).AddPeers(torrent.InfoHash, Arg.Any<List<TrackerPeer>>(), "tracker");

        // Verify tracker metrics telemetry recorded
        _trackerMetricService.Received(1).RecordAnnounce(
            "http://tracker1.org/announce", 42, 1000, 500, 1500, Arg.Any<long>(), true, 10, 2, 1, null);
    }

    [Test]
    public void AnnounceTracker_should_log_failure_when_tracker_fails()
    {
        var torrent = new Torrent
        {
            Id = 10,
            Name = "Test.Show",
            InfoHash = "abcdefabcdefabcdefabcdefabcdefabcdefabcd",
            Uploaded = 0,
            Downloaded = 0,
            TotalSize = 1000,
            Status = TorrentStatus.Downloading
        };

        var tracker = new TrackerEntry { Id = 5, TorrentId = 10, Url = "http://badtracker.com/announce", Enabled = true };

        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse
            {
                Success = false,
                FailureReason = "Connection timed out"
            });

        var result = _service.AnnounceTracker(torrent, tracker, force: true);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo("Connection timed out"));

        _eventLogService.Received(1).Warn(
            10,
            "Tracker",
            Arg.Is<string>(s => s.Contains("Tracker announce failed: http://badtracker.com/announce") && s.Contains("Connection timed out")));
    }
}
