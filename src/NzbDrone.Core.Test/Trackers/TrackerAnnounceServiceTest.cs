using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.Vpn;
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
    private IEventAggregator _eventAggregator;
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
        _eventAggregator = Substitute.For<IEventAggregator>();

        _configService.ListeningPort.Returns(51413);
        _configService.AnnounceIntervalSeconds.Returns(1800);

        _service = new TrackerAnnounceService(
            _trackerEntryService,
            _multiTracker,
            _peerDiscovery,
            _eventLogService,
            _configService,
            _trackerMetricService,
            eventAggregator: _eventAggregator,
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

        _trackerEntryService.Received(2).Update(tracker);
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

        _trackerEntryService.Received(2).Update(tracker);
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
    public void AnnounceTorrent_should_generate_dynamic_peer_id_and_key_without_hardcoded_default()
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
        _configService.PrimaryClient.Returns("qBittorrent");

        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var results = _service.AnnounceTorrent(torrent, force: true);

        Assert.That(results.Count, Is.EqualTo(1));
        _multiTracker.Received(1).Announce(
            Arg.Is<TrackerAnnounceRequest>(r =>
                r.PeerId != "-SD1000-000000000000" &&
                r.PeerId.Length == 20 &&
                r.PeerId.StartsWith("-qB4420-") &&
                r.Key != null &&
                r.Key.Length == 8),
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

    [Test]
    public void AnnounceTorrent_should_defer_announce_when_torrent_is_vpn_paused()
    {
        var torrent = new Torrent
        {
            Id = 60,
            Name = "VpnPaused.Torrent",
            InfoHash = "6666777788889999000011112222333344445555",
            Status = TorrentStatus.Downloading,
            IsVpnPaused = true
        };

        var results = _service.AnnounceTorrent(torrent, force: true);

        Assert.That(results, Is.Empty);
        _multiTracker.DidNotReceive().Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>());
    }

    [Test]
    public void AnnounceTorrent_should_defer_announce_when_vpn_kill_switch_triggered()
    {
        var torrent = new Torrent
        {
            Id = 61,
            Name = "Active.Torrent",
            InfoHash = "7777888899990000111122223333444455556666",
            Status = TorrentStatus.Downloading,
            IsVpnPaused = false
        };

        _service.Handle(new VpnKillSwitchTriggeredEvent("tun0"));

        var results = _service.AnnounceTorrent(torrent, force: true);

        Assert.That(results, Is.Empty);
        _multiTracker.DidNotReceive().Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>());
    }

    [Test]
    public void ScrapeTorrent_should_defer_scrape_when_torrent_is_vpn_paused()
    {
        var torrent = new Torrent
        {
            Id = 62,
            Name = "Scrape.Torrent",
            InfoHash = "8888999900001111222233334444555566667777",
            Status = TorrentStatus.Downloading,
            IsVpnPaused = true
        };

        var result = _service.ScrapeTorrent(torrent);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Does.Contain("deferred"));
        _multiTracker.DidNotReceive().Scrape(Arg.Any<string>(), Arg.Any<List<List<string>>>());
    }

    [Test]
    public async Task Handle_VpnInterfaceRestoredEvent_schedules_staggered_announces()
    {
        var torrent1 = new Torrent
        {
            Id = 70,
            Name = "Restored1",
            InfoHash = "1111111122222222333333334444444455555555",
            Status = TorrentStatus.Downloading,
            IsVpnPaused = false
        };
        var torrent2 = new Torrent
        {
            Id = 71,
            Name = "Restored2",
            InfoHash = "2222222233333333444444445555555566666666",
            Status = TorrentStatus.Seeding,
            IsVpnPaused = false
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var tracker1 = new TrackerEntry { Id = 1, TorrentId = 70, Url = "http://tracker1.org/announce", Enabled = true };
        var tracker2 = new TrackerEntry { Id = 2, TorrentId = 71, Url = "http://tracker2.org/announce", Enabled = true };
        _trackerEntryService.GetByTorrentId(70).Returns(new List<TrackerEntry> { tracker1 });
        _trackerEntryService.GetByTorrentId(71).Returns(new List<TrackerEntry> { tracker2 });

        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        _service.DelayAsync = (delay, ct) =>
        {
            return Task.CompletedTask;
        };

        _service.Handle(new VpnInterfaceRestoredEvent("tun0"));

        Assert.That(_service.IsStaggeredAnnounceScheduled, Is.True);

        // Wait briefly for async queue drain with 0 delay
        for (var i = 0; i < 50; i++)
        {
            if (_service.PendingStaggeredAnnounces == 0)
            {
                break;
            }

            await Task.Delay(20);
        }

        Assert.That(_service.PendingStaggeredAnnounces, Is.EqualTo(0));
    }

    [Test]
    public void AnnounceTracker_should_trip_circuit_breaker_when_warning_message_is_present()
    {
        var torrent = new Torrent
        {
            Id = 80,
            Name = "Warning.Movie",
            InfoHash = "1234567890123456789012345678901234567890",
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry
        {
            Id = 1,
            TorrentId = 80,
            Url = "http://tracker.safety.org/announce",
            Enabled = true,
            Status = TrackerStatus.Working
        };

        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse
            {
                Success = true,
                Interval = 1800,
                WarningMessage = "Unrealistic upload rate detected - account flagged"
            });

        var result = _service.AnnounceTracker(torrent, tracker, force: true);

        Assert.That(result.Success, Is.False);
        Assert.That(result.WarningMessage, Is.EqualTo("Unrealistic upload rate detected - account flagged"));
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Paused));
        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Status == TorrentStatus.Paused));

        Assert.That(tracker.Enabled, Is.False);
        Assert.That(tracker.NextAnnounce, Is.Null);
        Assert.That(tracker.Status, Is.EqualTo(TrackerStatus.Disabled));
        Assert.That(tracker.WarningMessage, Is.EqualTo("Unrealistic upload rate detected - account flagged"));
        _trackerEntryService.Received(2).Update(tracker);

        _eventLogService.Received(1).Error(
            80,
            "Tracker",
            Arg.Is<string>(s => s.Contains("CRITICAL") && s.Contains("circuit breaker")));

        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentStatusChangedEvent>(e =>
            e.Torrent.Id == 80 && e.NewStatus == TorrentStatus.Paused));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.Source == "TrackerSafety" && e.Message.Contains("Unrealistic upload rate detected")));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<TrackerWarningEvent>(e =>
            e.TrackerUrl == "http://tracker.safety.org/announce" && e.WarningMessage == "Unrealistic upload rate detected - account flagged"));
    }

    [Test]
    public void AnnounceTracker_should_trip_circuit_breaker_when_anti_cheat_failure_reason_is_present()
    {
        var torrent = new Torrent
        {
            Id = 81,
            Name = "AntiCheat.Movie",
            InfoHash = "2345678901234567890123456789012345678901",
            Status = TorrentStatus.Downloading
        };

        var tracker = new TrackerEntry
        {
            Id = 2,
            TorrentId = 81,
            Url = "http://tracker.anticheat.org/announce",
            Enabled = true,
            Status = TrackerStatus.Working
        };

        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse
            {
                Success = false,
                FailureReason = "Account flagged: seed speed throttled (anti-cheat)"
            });

        var result = _service.AnnounceTracker(torrent, tracker, force: true);

        Assert.That(result.Success, Is.False);
        Assert.That(result.WarningMessage, Is.EqualTo("Account flagged: seed speed throttled (anti-cheat)"));
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Paused));
        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Status == TorrentStatus.Paused));

        Assert.That(tracker.Enabled, Is.False);
        Assert.That(tracker.NextAnnounce, Is.Null);
        _trackerEntryService.Received(2).Update(tracker);
    }

    [Test]
    public void AnnounceTorrent_should_halt_further_announces_when_circuit_breaker_trips()
    {
        var torrent = new Torrent
        {
            Id = 82,
            Name = "MultiTracker.Movie",
            InfoHash = "3456789012345678901234567890123456789012",
            Status = TorrentStatus.Seeding
        };

        var tracker1 = new TrackerEntry { Id = 1, TorrentId = 82, Url = "http://badtracker.org/announce", Enabled = true };
        var tracker2 = new TrackerEntry { Id = 2, TorrentId = 82, Url = "http://goodtracker.org/announce", Enabled = true };

        _trackerEntryService.GetByTorrentId(82).Returns(new List<TrackerEntry> { tracker1, tracker2 });

        _multiTracker.Announce(
                Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == tracker1.Url),
                Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse
            {
                Success = true,
                WarningMessage = "Account flagged for ratio review"
            });

        var results = _service.AnnounceTorrent(torrent, force: true);

        Assert.That(results.Count, Is.EqualTo(1));
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Paused));

        // tracker2 was never announced to because tracker1 tripped the circuit breaker
        _multiTracker.DidNotReceive().Announce(
            Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == tracker2.Url),
            Arg.Any<List<List<string>>>());
    }

    [Test]
    public void AnnounceTracker_should_maintain_monotonic_uploaded_bytes_when_torrent_uploaded_is_less()
    {
        var torrent = new Torrent
        {
            Id = 83,
            Name = "Monotonic.Movie",
            InfoHash = "4567890123456789012345678901234567890123",
            Uploaded = 30000000,
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry
        {
            Id = 1,
            TorrentId = 83,
            Url = "http://tracker.org/announce",
            Enabled = true,
            LastAnnouncedUploaded = 50000000
        };

        TrackerAnnounceRequest capturedRequest = null;
        _multiTracker.Announce(
                Arg.Do<TrackerAnnounceRequest>(r => capturedRequest = r),
                Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true, Interval = 1800 });

        var result = _service.AnnounceTracker(torrent, tracker, force: true);

        Assert.That(result.Success, Is.True);
        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest.Uploaded, Is.EqualTo(50000000));
        Assert.That(tracker.LastAnnouncedUploaded, Is.EqualTo(50000000));
        Assert.That(result.LastAnnouncedUploaded, Is.EqualTo(50000000));
    }

    [Test]
    public void AnnounceTracker_should_update_last_announced_uploaded_when_torrent_uploaded_is_greater()
    {
        var torrent = new Torrent
        {
            Id = 84,
            Name = "MonotonicGrowth.Movie",
            InfoHash = "5678901234567890123456789012345678901234",
            Uploaded = 80000000,
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry
        {
            Id = 1,
            TorrentId = 84,
            Url = "http://tracker.org/announce",
            Enabled = true,
            LastAnnouncedUploaded = 50000000
        };

        TrackerAnnounceRequest capturedRequest = null;
        _multiTracker.Announce(
                Arg.Do<TrackerAnnounceRequest>(r => capturedRequest = r),
                Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true, Interval = 1800 });

        var result = _service.AnnounceTracker(torrent, tracker, force: true);

        Assert.That(result.Success, Is.True);
        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest.Uploaded, Is.EqualTo(80000000));
        Assert.That(tracker.LastAnnouncedUploaded, Is.EqualTo(80000000));
        Assert.That(result.LastAnnouncedUploaded, Is.EqualTo(80000000));
    }

    [Test]
    public void CalculateJitteredInterval_should_apply_jitter_within_expected_range()
    {
        const double baseInterval = 1800.0;
        const double minExpected = baseInterval * 0.95; // 1710
        const double maxExpected = baseInterval * 1.05; // 1890

        var results = new List<double>();
        for (var i = 0; i < 200; i++)
        {
            var jittered = TrackerAnnounceService.CalculateJitteredInterval(baseInterval, 0);
            Assert.That(jittered, Is.GreaterThanOrEqualTo(minExpected), "Interval dropped below -5% range");
            Assert.That(jittered, Is.LessThanOrEqualTo(maxExpected), "Interval exceeded +5% range");
            results.Add(jittered);
        }

        var distinctCount = results.Distinct().Count();
        Assert.That(distinctCount, Is.GreaterThan(1), "Jitter did not produce variation");
    }

    [Test]
    public void CalculateJitteredInterval_should_clamp_to_min_interval_when_jittered_drops_below()
    {
        const double baseInterval = 1800.0;
        const int minInterval = 1750;

        // Even with max negative jitter (-0.05 => 1710), it should clamp to 1750
        var jittered = TrackerAnnounceService.CalculateJitteredInterval(baseInterval, minInterval, jitterFraction: -0.05);
        Assert.That(jittered, Is.EqualTo(1750.0));

        // With positive jitter (+0.05 => 1890), 1890 is above minInterval, so stays 1890
        var jitteredPositive = TrackerAnnounceService.CalculateJitteredInterval(baseInterval, minInterval, jitterFraction: 0.05);
        Assert.That(jitteredPositive, Is.EqualTo(1890.0));
    }

    [Test]
    public void AnnounceTracker_should_schedule_next_announce_with_jitter_in_the_future()
    {
        var torrent = new Torrent
        {
            Id = 85,
            Name = "Jitter.Movie",
            InfoHash = "6789012345678901234567890123456789012345",
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry
        {
            Id = 1,
            TorrentId = 85,
            Url = "http://tracker.org/announce",
            Enabled = true
        };

        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse
            {
                Success = true,
                Interval = 1800,
                MinInterval = 0
            });

        var beforeAnnounce = DateTime.UtcNow;
        var result = _service.AnnounceTracker(torrent, tracker, force: true);

        Assert.That(result.Success, Is.True);
        Assert.That(tracker.NextAnnounce.HasValue, Is.True);
        Assert.That(tracker.NextAnnounce.Value, Is.GreaterThan(beforeAnnounce));
        // 1800 * 0.95 = 1710s, with small buffer
        Assert.That(tracker.NextAnnounce.Value, Is.GreaterThanOrEqualTo(beforeAnnounce.AddSeconds(1705)));
        // 1800 * 1.05 = 1890s, with small buffer
        Assert.That(tracker.NextAnnounce.Value, Is.LessThanOrEqualTo(DateTime.UtcNow.AddSeconds(1895)));
    }

    [Test]
    public void AnnounceTracker_should_set_status_to_Announcing_and_publish_status_changed_event_before_io()
    {
        var torrent = new Torrent
        {
            Id = 90,
            Name = "Status.Test.Movie",
            InfoHash = "1111222233334444555566667777888899990000",
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry
        {
            Id = 42,
            TorrentId = 90,
            Url = "http://tracker.example.com/announce",
            Enabled = true,
            Status = TrackerStatus.Unknown
        };

        TrackerStatus? statusDuringIo = null;
        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(x =>
            {
                statusDuringIo = tracker.Status;
                return new TrackerAnnounceResponse
                {
                    Success = true,
                    Complete = 20,
                    Incomplete = 5,
                    Interval = 1800
                };
            });

        var result = _service.AnnounceTracker(torrent, tracker, force: true);

        Assert.That(result.Success, Is.True);
        Assert.That(statusDuringIo, Is.EqualTo(TrackerStatus.Announcing));
        Assert.That(tracker.Status, Is.EqualTo(TrackerStatus.Working));

        _trackerEntryService.Received(2).Update(tracker);

        _eventAggregator.Received(1).PublishEvent(Arg.Is<TrackerStatusChangedEvent>(e =>
            e.Torrent.Id == 90 &&
            e.Tracker.Id == 42 &&
            e.PreviousStatus == TrackerStatus.Unknown &&
            e.NewStatus == TrackerStatus.Announcing));

        _eventAggregator.Received(1).PublishEvent(Arg.Is<TrackerStatusChangedEvent>(e =>
            e.Torrent.Id == 90 &&
            e.Tracker.Id == 42 &&
            e.PreviousStatus == TrackerStatus.Announcing &&
            e.NewStatus == TrackerStatus.Working));

        _eventAggregator.Received(1).PublishEvent(Arg.Is<TrackerAnnounceEvent>(e =>
            e.Torrent.Id == 90 &&
            e.TrackerId == 42 &&
            e.Status == TrackerStatus.Working &&
            e.IsSuccess));
    }

    [Test]
    public void AnnounceTracker_should_transition_from_Announcing_to_Failed_on_failure_and_publish_events()
    {
        var torrent = new Torrent
        {
            Id = 91,
            Name = "Failed.Status.Test",
            InfoHash = "2222333344445555666677778888999900001111",
            Status = TorrentStatus.Downloading
        };

        var tracker = new TrackerEntry
        {
            Id = 43,
            TorrentId = 91,
            Url = "http://badtracker.example.com/announce",
            Enabled = true,
            Status = TrackerStatus.Working
        };

        TrackerStatus? statusDuringIo = null;
        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(x =>
            {
                statusDuringIo = tracker.Status;
                return new TrackerAnnounceResponse
                {
                    Success = false,
                    FailureReason = "Connection timed out"
                };
            });

        var result = _service.AnnounceTracker(torrent, tracker, force: true);

        Assert.That(result.Success, Is.False);
        Assert.That(statusDuringIo, Is.EqualTo(TrackerStatus.Announcing));
        Assert.That(tracker.Status, Is.EqualTo(TrackerStatus.Failed));

        _trackerEntryService.Received(2).Update(tracker);

        _eventAggregator.Received(1).PublishEvent(Arg.Is<TrackerStatusChangedEvent>(e =>
            e.Torrent.Id == 91 &&
            e.Tracker.Id == 43 &&
            e.PreviousStatus == TrackerStatus.Working &&
            e.NewStatus == TrackerStatus.Announcing));

        _eventAggregator.Received(1).PublishEvent(Arg.Is<TrackerStatusChangedEvent>(e =>
            e.Torrent.Id == 91 &&
            e.Tracker.Id == 43 &&
            e.PreviousStatus == TrackerStatus.Announcing &&
            e.NewStatus == TrackerStatus.Failed));

        _eventAggregator.Received(1).PublishEvent(Arg.Is<TrackerAnnounceEvent>(e =>
            e.Torrent.Id == 91 &&
            e.TrackerId == 43 &&
            e.Status == TrackerStatus.Failed &&
            !e.IsSuccess &&
            e.ErrorMessage == "Connection timed out"));
    }

    [Test]
    public void AnnounceTracker_should_return_rate_limited_failure_when_manual_announce_within_min_announce_interval()
    {
        var torrent = new Torrent
        {
            Id = 101,
            Name = "RateLimit.Torrent",
            InfoHash = "1111222233334444555566667777888899990000",
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry
        {
            Id = 1,
            TorrentId = 101,
            Url = "http://tracker.example.com/announce",
            Enabled = true,
            TotalAnnounces = 1,
            LastAnnounce = DateTime.UtcNow.AddSeconds(-30),
            MinAnnounceInterval = 120
        };

        var result = _service.AnnounceTracker(torrent, tracker, force: true);

        Assert.That(result.Success, Is.False);
        Assert.That(result.RetryAfterSeconds, Is.GreaterThan(0));
        Assert.That(result.FailureReason, Does.Contain("Rate limited: minimum announce interval of 120s not elapsed"));
        _multiTracker.DidNotReceive().Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>());
    }

    [Test]
    public void AnnounceTracker_should_succeed_when_manual_announce_after_min_announce_interval()
    {
        var torrent = new Torrent
        {
            Id = 102,
            Name = "Allowed.Torrent",
            InfoHash = "2222333344445555666677778888999900001111",
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry
        {
            Id = 2,
            TorrentId = 102,
            Url = "http://tracker.example.com/announce",
            Enabled = true,
            TotalAnnounces = 1,
            LastAnnounce = DateTime.UtcNow.AddSeconds(-130),
            MinAnnounceInterval = 120
        };

        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true, Interval = 1800 });

        var result = _service.AnnounceTracker(torrent, tracker, force: true);

        Assert.That(result.Success, Is.True);
        _multiTracker.Received(1).Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>());
    }

    [Test]
    public void AnnounceTracker_should_not_block_first_announce()
    {
        var torrent = new Torrent
        {
            Id = 103,
            Name = "FirstAnnounce.Torrent",
            InfoHash = "3333444455556666777788889999000011112222",
            Status = TorrentStatus.Downloading
        };

        var tracker = new TrackerEntry
        {
            Id = 3,
            TorrentId = 103,
            Url = "http://tracker.example.com/announce",
            Enabled = true,
            TotalAnnounces = 0,
            LastAnnounce = null,
            MinAnnounceInterval = 120
        };

        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true, Interval = 1800 });

        var result = _service.AnnounceTracker(torrent, tracker, force: true);

        Assert.That(result.Success, Is.True);
        _multiTracker.Received(1).Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>());
    }

    [Test]
    public void AnnounceTorrent_should_rate_limit_trackers_within_min_announce_interval()
    {
        var torrent = new Torrent
        {
            Id = 104,
            Name = "MultiTracker.RateLimit",
            InfoHash = "4444555566667777888899990000111122223333",
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry
        {
            Id = 4,
            TorrentId = 104,
            Url = "http://tracker.example.com/announce",
            Enabled = true,
            TotalAnnounces = 1,
            LastAnnounce = DateTime.UtcNow.AddSeconds(-20),
            MinAnnounceInterval = 60
        };

        _trackerEntryService.GetByTorrentId(104).Returns(new List<TrackerEntry> { tracker });

        var results = _service.AnnounceTorrent(torrent, force: true);

        Assert.That(results.Count, Is.EqualTo(1));
        Assert.That(results[0].Success, Is.False);
        Assert.That(results[0].RetryAfterSeconds, Is.GreaterThan(0));
        Assert.That(results[0].FailureReason, Does.Contain("Rate limited: minimum announce interval of 60s not elapsed"));
        _multiTracker.DidNotReceive().Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>());
    }

    [Test]
    public void AnnounceTorrent_should_fallback_to_half_interval_or_60_when_min_interval_omitted()
    {
        var torrent = new Torrent
        {
            Id = 105,
            Name = "FallbackInterval.Torrent",
            InfoHash = "5555666677778888999900001111222233334444",
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry
        {
            Id = 5,
            TorrentId = 105,
            Url = "http://tracker.example.com/announce",
            Enabled = true,
            TotalAnnounces = 1,
            LastAnnounce = DateTime.UtcNow.AddSeconds(-15),
            MinAnnounceInterval = 0,
            AnnounceInterval = 1800
        };

        _trackerEntryService.GetByTorrentId(105).Returns(new List<TrackerEntry> { tracker });

        var results = _service.AnnounceTorrent(torrent, force: true);

        Assert.That(results.Count, Is.EqualTo(1));
        Assert.That(results[0].Success, Is.False);
        Assert.That(results[0].FailureReason, Does.Contain("Rate limited: minimum announce interval of 60s not elapsed"));
    }

    [Test]
    public void AnnounceTorrent_should_not_leak_simulated_upload_bytes_to_external_tracker_when_torrent_is_simulated()
    {
        var torrent = new Torrent
        {
            Id = 201,
            Name = "Simulated.Ratio.Torrent",
            InfoHash = "1111222233334444555566667777888899990000",
            Uploaded = 100_000_000,
            RealUploaded = 0,
            SimulatedUploaded = 100_000_000,
            IsSimulated = true,
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry { Id = 1, TorrentId = 201, Url = "http://external-tracker.org/announce", Enabled = true };
        _trackerEntryService.GetByTorrentId(201).Returns(new List<TrackerEntry> { tracker });
        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var results = _service.AnnounceTorrent(torrent, force: true);

        Assert.That(results.Count, Is.EqualTo(1));
        _multiTracker.Received(1).Announce(
            Arg.Is<TrackerAnnounceRequest>(r => r.Uploaded == 0),
            Arg.Any<List<List<string>>>());
    }

    [Test]
    public void AnnounceTorrent_should_report_real_wire_bytes_to_external_tracker_when_simulation_mode_enabled()
    {
        _configService.SimulationModeEnabled.Returns(true);

        var torrent = new Torrent
        {
            Id = 202,
            Name = "Simulated.Mode.Torrent",
            InfoHash = "2222333344445555666677778888999900001111",
            Uploaded = 75_000_000,
            RealUploaded = 12_345,
            SimulatedUploaded = 74_987_655,
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry { Id = 2, TorrentId = 202, Url = "http://external-tracker.org/announce", Enabled = true };
        _trackerEntryService.GetByTorrentId(202).Returns(new List<TrackerEntry> { tracker });
        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var results = _service.AnnounceTorrent(torrent, force: true);

        Assert.That(results.Count, Is.EqualTo(1));
        _multiTracker.Received(1).Announce(
            Arg.Is<TrackerAnnounceRequest>(r => r.Uploaded == 12_345),
            Arg.Any<List<List<string>>>());
    }

    [Test]
    public void AnnounceTorrent_should_allow_simulated_upload_bytes_to_loopback_or_mock_tracker()
    {
        var torrent = new Torrent
        {
            Id = 203,
            Name = "Local.Simulated.Torrent",
            InfoHash = "3333444455556666777788889999000011112222",
            Uploaded = 50_000_000,
            RealUploaded = 0,
            SimulatedUploaded = 50_000_000,
            IsSimulated = true,
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry { Id = 3, TorrentId = 203, Url = "http://127.0.0.1:8989/announce", Enabled = true };
        _trackerEntryService.GetByTorrentId(203).Returns(new List<TrackerEntry> { tracker });
        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var results = _service.AnnounceTorrent(torrent, force: true);

        Assert.That(results.Count, Is.EqualTo(1));
        _multiTracker.Received(1).Announce(
            Arg.Is<TrackerAnnounceRequest>(r => r.Uploaded == 50_000_000),
            Arg.Any<List<List<string>>>());
    }

    [Test]
    public void AnnounceTorrent_should_align_peer_id_with_client_profile_from_torrent_ClientProfile()
    {
        var torrent = new Torrent
        {
            Id = 204,
            Name = "Transmission.Torrent",
            InfoHash = "4444555566667777888899990000111122223333",
            ClientProfile = "Transmission",
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry { Id = 4, TorrentId = 204, Url = "http://tracker.example.com/announce", Enabled = true };
        _trackerEntryService.GetByTorrentId(204).Returns(new List<TrackerEntry> { tracker });
        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var results = _service.AnnounceTorrent(torrent, force: true);

        Assert.That(results.Count, Is.EqualTo(1));
        _multiTracker.Received(1).Announce(
            Arg.Is<TrackerAnnounceRequest>(r =>
                r.PeerId.StartsWith("-TR3000-") &&
                r.UserAgent.Contains("Transmission") &&
                r.ClientProfile != null &&
                r.ClientProfile.Name.Contains("Transmission")),
            Arg.Any<List<List<string>>>());
    }

    [Test]
    public void AnnounceTorrent_should_align_peer_id_with_BitTorrentUserAgent_when_client_behavior_is_disabled()
    {
        var torrent = new Torrent
        {
            Id = 205,
            Name = "CustomAgent.Torrent",
            InfoHash = "5555666677778888999900001111222233334444",
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry { Id = 5, TorrentId = 205, Url = "http://tracker.example.com/announce", Enabled = true };
        _trackerEntryService.GetByTorrentId(205).Returns(new List<TrackerEntry> { tracker });
        _configService.ClientBehaviorEngineEnabled.Returns(false);
        _configService.BitTorrentUserAgent.Returns("Transmission/3.00");

        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var results = _service.AnnounceTorrent(torrent, force: true);

        Assert.That(results.Count, Is.EqualTo(1));
        _multiTracker.Received(1).Announce(
            Arg.Is<TrackerAnnounceRequest>(r =>
                r.PeerId.StartsWith("-TR3000-") &&
                r.UserAgent == "Transmission/3.00" &&
                r.ClientProfile != null &&
                r.ClientProfile.Name.Contains("Transmission")),
            Arg.Any<List<List<string>>>());
    }

    [Test]
    public void AnnounceTorrent_should_use_IClientProfileFactory_when_available()
    {
        var mockProfileFactory = Substitute.For<IClientProfileFactory>();
        var transmissionProfile = new NzbDrone.Core.Simulation.ClientBehavior.Profiles.TransmissionProfile();
        mockProfileFactory.GetAvailableProviders().Returns(new List<IClientProfile> { transmissionProfile });

        var service = new TrackerAnnounceService(
            _trackerEntryService,
            _multiTracker,
            _peerDiscovery,
            _eventLogService,
            _configService,
            _trackerMetricService,
            eventAggregator: null,
            torrentService: _torrentService,
            clientBehaviorSimulator: null,
            vpnKillSwitchService: null,
            clientProfileFactory: mockProfileFactory);

        var torrent = new Torrent
        {
            Id = 206,
            Name = "Factory.Torrent",
            InfoHash = "6666777788889999000011112222333344445555",
            ClientProfile = "Transmission 3.00",
            Status = TorrentStatus.Seeding
        };

        var tracker = new TrackerEntry { Id = 6, TorrentId = 206, Url = "http://tracker.example.com/announce", Enabled = true };
        _trackerEntryService.GetByTorrentId(206).Returns(new List<TrackerEntry> { tracker });
        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var results = service.AnnounceTorrent(torrent, force: true);

        Assert.That(results.Count, Is.EqualTo(1));
        mockProfileFactory.Received().GetAvailableProviders();
        _multiTracker.Received(1).Announce(
            Arg.Is<TrackerAnnounceRequest>(r => r.PeerId.StartsWith("-TR3000-")),
            Arg.Any<List<List<string>>>());
    }

    [Test]
    public void IsLoopbackOrMockTracker_should_correctly_identify_loopback_and_external_urls()
    {
        Assert.That(TrackerAnnounceService.IsLoopbackOrMockTracker("http://127.0.0.1:1337/announce"), Is.True);
        Assert.That(TrackerAnnounceService.IsLoopbackOrMockTracker("http://localhost:8080/announce"), Is.True);
        Assert.That(TrackerAnnounceService.IsLoopbackOrMockTracker("http://[::1]:8080/announce"), Is.True);
        Assert.That(TrackerAnnounceService.IsLoopbackOrMockTracker("mock://tracker.local/announce"), Is.True);
        Assert.That(TrackerAnnounceService.IsLoopbackOrMockTracker("http://tracker.example.com/announce"), Is.False);
        Assert.That(TrackerAnnounceService.IsLoopbackOrMockTracker("udp://tracker.openbittorrent.com:6969/announce"), Is.False);
    }
}
