using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Simulation.ClientBehavior;
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
    private ITorrentService _torrentService;
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
        _torrentService = Substitute.For<ITorrentService>();

        _configService.ListeningPort.Returns(51413);
        _configService.AnnounceIntervalSeconds.Returns(1800);

        _service = new TrackerAnnounceService(
            _trackerEntryService,
            _multiTracker,
            _peerDiscovery,
            _eventLogService,
            _configService,
            _trackerMetricService,
            eventAggregator: null,
            torrentService: _torrentService);
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

    [Test]
    public void AnnounceTracker_should_update_tracker_entry_state_on_success()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Test.Movie",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry
        {
            Id = 1,
            TorrentId = 1,
            Url = "http://goodtracker.com/announce",
            Enabled = true,
            Status = TrackerStatus.Unknown,
            ConsecutiveFailures = 2,
            SuccessfulAnnounces = 0,
            TotalAnnounces = 0
        };

        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse
            {
                Success = true,
                Complete = 15,
                Incomplete = 3,
                Interval = 1800,
                MinInterval = 900
            });

        var result = _service.AnnounceTracker(torrent, tracker, force: true);

        Assert.That(result.Success, Is.True);
        Assert.That(tracker.Status, Is.EqualTo(TrackerStatus.Working));
        Assert.That(tracker.Seeders, Is.EqualTo(15));
        Assert.That(tracker.Leechers, Is.EqualTo(3));
        Assert.That(tracker.SuccessfulAnnounces, Is.EqualTo(1));
        Assert.That(tracker.TotalAnnounces, Is.EqualTo(1));
        Assert.That(tracker.ConsecutiveFailures, Is.EqualTo(0));
        Assert.That(tracker.ErrorMessage, Is.Null);
        Assert.That(tracker.LastAnnounce, Is.Not.Null);

        _trackerEntryService.Received(1).Update(tracker);
    }

    [Test]
    public void AnnounceTracker_should_update_tracker_entry_state_on_failure()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Test.Movie",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Status = TorrentStatus.Downloading
        };

        var tracker = new TrackerEntry
        {
            Id = 1,
            TorrentId = 1,
            Url = "http://unreachable.com/announce",
            Enabled = true,
            Status = TrackerStatus.Working,
            ConsecutiveFailures = 1,
            SuccessfulAnnounces = 5,
            TotalAnnounces = 6
        };

        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse
            {
                Success = false,
                FailureReason = "Connection refused"
            });

        var result = _service.AnnounceTracker(torrent, tracker, force: true);

        Assert.That(result.Success, Is.False);
        Assert.That(tracker.Status, Is.EqualTo(TrackerStatus.Failed));
        Assert.That(tracker.ConsecutiveFailures, Is.EqualTo(2));
        Assert.That(tracker.ErrorMessage, Is.EqualTo("Connection refused"));
        Assert.That(tracker.LastErrorTime, Is.Not.Null);
        Assert.That(tracker.TotalAnnounces, Is.EqualTo(7));
        Assert.That(tracker.SuccessfulAnnounces, Is.EqualTo(5));

        _trackerEntryService.Received(1).Update(tracker);
    }

    [Test]
    public void AnnounceTorrent_should_send_stopped_event_when_torrent_is_stopped()
    {
        var torrent = new Torrent
        {
            Id = 50,
            Name = "Stopped.Movie",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Status = TorrentStatus.Stopped
        };

        var tracker = new TrackerEntry { Id = 1, TorrentId = 50, Url = "http://tracker.org/announce", Enabled = true };
        _trackerEntryService.GetByTorrentId(50).Returns(new List<TrackerEntry> { tracker });

        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var results = _service.AnnounceTorrent(torrent, force: true);

        Assert.That(results.Count, Is.EqualTo(1));
        _multiTracker.Received(1).Announce(
            Arg.Is<TrackerAnnounceRequest>(r => r.Event == AnnounceEvent.Stopped && r.NumWant == 0),
            Arg.Any<List<List<string>>>());
    }

    [Test]
    public async Task HandleStoppedEventAsync_should_announce_stopped_event_to_trackers()
    {
        var torrent = new Torrent
        {
            Id = 60,
            Name = "Stopped.Show",
            InfoHash = "fedcba9876543210fedcba9876543210fedcba98",
            Status = TorrentStatus.Stopped
        };

        var tracker = new TrackerEntry { Id = 2, TorrentId = 60, Url = "http://tracker2.org/announce", Enabled = true };
        _torrentService.Get(60).Returns(torrent);
        _trackerEntryService.GetByTorrentId(60).Returns(new List<TrackerEntry> { tracker });

        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        await _service.HandleStoppedEventAsync(new SeedingStoppedEvent(60));

        _multiTracker.Received(1).Announce(
            Arg.Is<TrackerAnnounceRequest>(r => r.Event == AnnounceEvent.Stopped && r.NumWant == 0),
            Arg.Any<List<List<string>>>());
    }

    [Test]
    public void AnnounceTorrent_should_populate_user_agent_from_client_profile()
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

        var tracker = new TrackerEntry { Id = 1, TorrentId = 42, Url = "http://tracker1.org/announce", Enabled = true };
        _trackerEntryService.GetByTorrentId(42).Returns(new List<TrackerEntry> { tracker });
        _configService.ClientBehaviorEngineEnabled.Returns(true);
        _configService.AnonymousMode.Returns(false);

        var clientProfile = Substitute.For<IClientProfile>();
        clientProfile.UserAgent.Returns("Transmission/3.00");
        clientProfile.Name.Returns("Transmission 3.00");
        clientProfile.GeneratePeerId().Returns("-TR3000-123456789012");

        var session = new TorrentClientSession
        {
            Profile = clientProfile,
            PeerId = "-TR3000-123456789012",
            AnnounceKey = "ABCD1234",
            ProfileName = "Transmission 3.00",
            CreatedAt = DateTime.UtcNow
        };

        var simulator = Substitute.For<IClientBehaviorSimulator>();
        simulator.GetOrCreateSession(torrent.InfoHash, torrent.IsPrivate).Returns(session);
        simulator.GetProfileForTorrent(torrent.InfoHash, torrent.IsPrivate).Returns(clientProfile);

        var service = new TrackerAnnounceService(
            _trackerEntryService,
            _multiTracker,
            _peerDiscovery,
            _eventLogService,
            _configService,
            _trackerMetricService,
            eventAggregator: null,
            torrentService: _torrentService,
            clientBehaviorSimulator: simulator);

        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var results = service.AnnounceTorrent(torrent, force: true);

        Assert.That(results.Count, Is.EqualTo(1));
        _multiTracker.Received(1).Announce(
            Arg.Is<TrackerAnnounceRequest>(r => r.UserAgent == "Transmission/3.00" && r.PeerId == "-TR3000-123456789012"),
            Arg.Any<List<List<string>>>());
    }

    [Test]
    public void AnnounceTorrent_should_fallback_to_config_user_agent_when_client_behavior_is_disabled()
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

        var tracker = new TrackerEntry { Id = 1, TorrentId = 42, Url = "http://tracker1.org/announce", Enabled = true };
        _trackerEntryService.GetByTorrentId(42).Returns(new List<TrackerEntry> { tracker });
        _configService.ClientBehaviorEngineEnabled.Returns(false);
        _configService.BitTorrentUserAgent.Returns("qBittorrent/4.4.2");

        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var results = _service.AnnounceTorrent(torrent, force: true);

        Assert.That(results.Count, Is.EqualTo(1));
        _multiTracker.Received(1).Announce(
            Arg.Is<TrackerAnnounceRequest>(r => r.UserAgent == "qBittorrent/4.4.2"),
            Arg.Any<List<List<string>>>());
    }

    [Test]
    public void BEP10_extension_handshake_dictionary_should_include_client_version_v()
    {
        var configService = Substitute.For<IConfigService>();
        var manager = new ExtensionManager(configService);
        var profile = Substitute.For<IClientProfile>();
        profile.UserAgent.Returns("qBittorrent/4.4.2");
        profile.Name.Returns("qBittorrent 4.4.2");

        var bytes = manager.BuildExtensionHandshake(false, profile);
        var parser = new BencodeParser();
        using var stream = new MemoryStream(bytes);
        var dict = parser.Parse<BDictionary>(stream);

        Assert.That(dict.ContainsKey("v"), Is.True);
        Assert.That(((BString)dict["v"]).ToString(), Is.EqualTo("qBittorrent/4.4.2"));
    }

    [Test]
    public void Handle_TorrentFinishedEvent_should_send_completed_event_with_left_zero()
    {
        var torrent = new Torrent
        {
            Id = 55,
            Name = "Finished.Torrent",
            InfoHash = "1111222233334444555566667777888899990000",
            Uploaded = 500,
            Downloaded = 1000,
            TotalSize = 1000,
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry { Id = 1, TorrentId = 55, Url = "http://tracker.org/announce", Enabled = true };
        _trackerEntryService.GetByTorrentId(55).Returns(new List<TrackerEntry> { tracker });
        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        _service.Handle(new TorrentFinishedEvent(torrent));

        _multiTracker.Received(1).Announce(
            Arg.Is<TrackerAnnounceRequest>(r => r.Event == AnnounceEvent.Completed && r.Left == 0),
            Arg.Any<List<List<string>>>());
    }

    [Test]
    public void Handle_TorrentFinishedEvent_should_not_send_completed_if_already_complete_at_startup()
    {
        var startupTorrent = new Torrent
        {
            Id = 56,
            Name = "Already.Complete",
            InfoHash = "2222333344445555666677778888999900001111",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            TotalSize = 1000,
            Downloaded = 1000
        };

        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetAll().Returns(new List<Torrent> { startupTorrent });

        var tracker = new TrackerEntry { Id = 1, TorrentId = 56, Url = "http://tracker.org/announce", Enabled = true };
        _trackerEntryService.GetByTorrentId(56).Returns(new List<TrackerEntry> { tracker });

        var service = new TrackerAnnounceService(
            _trackerEntryService,
            _multiTracker,
            _peerDiscovery,
            _eventLogService,
            _configService,
            _trackerMetricService,
            eventAggregator: null,
            torrentService: torrentService);

        service.Handle(new TorrentFinishedEvent(startupTorrent));

        _multiTracker.DidNotReceive().Announce(
            Arg.Is<TrackerAnnounceRequest>(r => r.Event == AnnounceEvent.Completed),
            Arg.Any<List<List<string>>>());
    }

    [Test]
    public void Handle_TorrentFinishedEvent_should_not_send_completed_if_already_complete_on_hash_check()
    {
        var torrent = new Torrent
        {
            Id = 57,
            Name = "HashCheck.Torrent",
            InfoHash = "3333444455556666777788889999000011112222",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            TotalSize = 1000,
            Downloaded = 1000
        };

        var tracker = new TrackerEntry { Id = 1, TorrentId = 57, Url = "http://tracker.org/announce", Enabled = true };
        _trackerEntryService.GetByTorrentId(57).Returns(new List<TrackerEntry> { tracker });

        _service.Handle(new TorrentHashCheckCompletedEvent(torrent, true));
        _service.Handle(new TorrentFinishedEvent(torrent));

        _multiTracker.DidNotReceive().Announce(
            Arg.Is<TrackerAnnounceRequest>(r => r.Event == AnnounceEvent.Completed),
            Arg.Any<List<List<string>>>());
    }

    [Test]
    public void Handle_TorrentPausedEvent_should_send_stopped_event()
    {
        var torrent = new Torrent
        {
            Id = 58,
            Name = "Paused.Movie",
            InfoHash = "4444555566667777888899990000111122223333",
            Status = TorrentStatus.Paused
        };

        var tracker = new TrackerEntry { Id = 1, TorrentId = 58, Url = "http://tracker.org/announce", Enabled = true };
        _trackerEntryService.GetByTorrentId(58).Returns(new List<TrackerEntry> { tracker });
        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        _service.Handle(new TorrentPausedEvent(torrent));

        _multiTracker.Received(1).Announce(
            Arg.Is<TrackerAnnounceRequest>(r => r.Event == AnnounceEvent.Stopped && r.NumWant == 0),
            Arg.Any<List<List<string>>>());
    }

    [Test]
    public void Handle_TorrentDeletedEvent_should_send_stopped_event()
    {
        var torrent = new Torrent
        {
            Id = 59,
            Name = "Deleted.Movie",
            InfoHash = "5555666677778888999900001111222233334444",
            Status = TorrentStatus.Stopped
        };

        var tracker = new TrackerEntry { Id = 1, TorrentId = 59, Url = "http://tracker.org/announce", Enabled = true };
        _trackerEntryService.GetByTorrentId(59).Returns(new List<TrackerEntry> { tracker });
        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        _service.Handle(new TorrentDeletedEvent(59, torrent));

        _multiTracker.Received(1).Announce(
            Arg.Is<TrackerAnnounceRequest>(r => r.Event == AnnounceEvent.Stopped && r.NumWant == 0),
            Arg.Any<List<List<string>>>());
    }
}
