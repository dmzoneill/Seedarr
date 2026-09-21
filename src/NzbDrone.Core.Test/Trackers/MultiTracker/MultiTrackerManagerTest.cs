using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Trackers;
using NzbDrone.Core.Trackers.MultiTracker;

namespace NzbDrone.Core.Test.Trackers.MultiTracker;

[TestFixture]
public class MultiTrackerManagerTest
{
    private IConfigService _configService;
    private ITrackerProvider _httpTracker;
    private ITrackerProvider _udpTracker;
    private MultiTrackerManager _manager;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.MultiTrackerEnabled.Returns(true);
        _configService.MultiTrackerFailoverEnabled.Returns(true);
        _configService.AnnounceToAllTiers.Returns(false);
        _configService.AnnounceToAllInTier.Returns(false);
        _configService.FailoverMaxConsecutiveFailures.Returns(3);
        _configService.FailoverBackoffBaseSeconds.Returns(60);
        _configService.FailoverMaxBackoffSeconds.Returns(3600);

        _httpTracker = Substitute.For<ITrackerProvider>();
        _httpTracker.Name.Returns("HTTP");

        _udpTracker = Substitute.For<ITrackerProvider>();
        _udpTracker.Name.Returns("UDP");

        _manager = new MultiTrackerManager(
            new List<ITrackerProvider> { _httpTracker, _udpTracker },
            _configService);
    }

    [Test]
    public void Announce_should_return_failure_when_no_trackers_available_and_multitracker_disabled()
    {
        _configService.MultiTrackerEnabled.Returns(false);
        var request = CreateRequest();

        var result = _manager.Announce(request, new List<List<string>>());

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo("No trackers available"));
    }

    [Test]
    public void Announce_should_use_first_tracker_when_multitracker_disabled()
    {
        _configService.MultiTrackerEnabled.Returns(false);
        _httpTracker.Announce(Arg.Any<TrackerAnnounceRequest>())
            .Returns(new TrackerAnnounceResponse { Success = true, Interval = 1800 });

        var request = CreateRequest();
        var announceList = new List<List<string>> { new() { "http://tracker1.com/announce" } };

        var result = _manager.Announce(request, announceList);

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public void Announce_should_try_next_tracker_on_failure()
    {
        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://tracker1.com/announce"))
            .Returns(new TrackerAnnounceResponse { Success = false, FailureReason = "timeout" });
        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://tracker2.com/announce"))
            .Returns(new TrackerAnnounceResponse { Success = true, Interval = 900 });

        var request = CreateRequest();
        var announceList = new List<List<string>>
        {
            new() { "http://tracker1.com/announce", "http://tracker2.com/announce" }
        };

        var result = _manager.Announce(request, announceList);

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public void Announce_should_return_all_trackers_failed_when_all_fail()
    {
        _httpTracker.Announce(Arg.Any<TrackerAnnounceRequest>())
            .Returns(new TrackerAnnounceResponse { Success = false, FailureReason = "error" });

        var request = CreateRequest();
        var announceList = new List<List<string>>
        {
            new() { "http://tracker1.com/announce" }
        };

        var result = _manager.Announce(request, announceList);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo("All trackers failed"));
    }

    [Test]
    public void Announce_should_select_udp_provider_for_udp_urls()
    {
        _udpTracker.Announce(Arg.Any<TrackerAnnounceRequest>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var request = CreateRequest();
        var announceList = new List<List<string>>
        {
            new() { "udp://tracker.example.com:6969/announce" }
        };

        var result = _manager.Announce(request, announceList);

        _udpTracker.Received(1).Announce(Arg.Any<TrackerAnnounceRequest>());
    }

    [Test]
    public void Announce_should_fail_for_unknown_protocol()
    {
        var request = CreateRequest();
        var announceList = new List<List<string>>
        {
            new() { "wss://tracker.example.com/announce" }
        };

        var result = _manager.Announce(request, announceList);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void Announce_should_stop_at_first_successful_tier_when_not_announce_all_tiers()
    {
        _configService.AnnounceToAllTiers.Returns(false);
        _httpTracker.Announce(Arg.Any<TrackerAnnounceRequest>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var request = CreateRequest();
        var announceList = new List<List<string>>
        {
            new() { "http://tier1.com/announce" },
            new() { "http://tier2.com/announce" }
        };

        _manager.Announce(request, announceList);

        _httpTracker.Received(1).Announce(Arg.Any<TrackerAnnounceRequest>());
    }

    [Test]
    public void Announce_should_try_all_tiers_when_announce_to_all_tiers()
    {
        _configService.AnnounceToAllTiers.Returns(true);
        _httpTracker.Announce(Arg.Any<TrackerAnnounceRequest>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var request = CreateRequest();
        var announceList = new List<List<string>>
        {
            new() { "http://tier1.com/announce" },
            new() { "http://tier2.com/announce" }
        };

        _manager.Announce(request, announceList);

        _httpTracker.Received(2).Announce(Arg.Any<TrackerAnnounceRequest>());
    }

    [Test]
    public void Announce_should_stop_at_first_success_in_tier_when_not_announce_all_in_tier()
    {
        _configService.AnnounceToAllInTier.Returns(false);
        _httpTracker.Announce(Arg.Any<TrackerAnnounceRequest>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var request = CreateRequest();
        var announceList = new List<List<string>>
        {
            new() { "http://t1.com/announce", "http://t2.com/announce" }
        };

        _manager.Announce(request, announceList);

        _httpTracker.Received(1).Announce(Arg.Any<TrackerAnnounceRequest>());
    }

    [Test]
    public void Announce_should_try_all_in_tier_when_announce_to_all_in_tier()
    {
        _configService.AnnounceToAllInTier.Returns(true);
        _httpTracker.Announce(Arg.Any<TrackerAnnounceRequest>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var request = CreateRequest();
        var announceList = new List<List<string>>
        {
            new() { "http://t1.com/announce", "http://t2.com/announce" }
        };

        _manager.Announce(request, announceList);

        _httpTracker.Received(2).Announce(Arg.Any<TrackerAnnounceRequest>());
    }

    [Test]
    public void Announce_should_only_announce_to_primary_tracker_when_torrent_is_private_even_if_multi_tier_enabled()
    {
        _configService.AnnounceToAllTiers.Returns(true);
        _configService.AnnounceToAllInTier.Returns(true);
        _httpTracker.Announce(Arg.Any<TrackerAnnounceRequest>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var request = CreateRequest();
        request.IsPrivate = true;

        var announceList = new List<List<string>>
        {
            new() { "http://primary.tracker.org/announce1", "http://primary.tracker.org/announce2" },
            new() { "http://primary.tracker.org/announce3" }
        };

        var result = _manager.Announce(request, announceList);

        Assert.That(result.Success, Is.True);
        // Even though AnnounceToAllTiers and AnnounceToAllInTier are true, private torrent must only announce to primary!
        _httpTracker.Received(1).Announce(Arg.Any<TrackerAnnounceRequest>());
        _httpTracker.Received(1).Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://primary.tracker.org/announce1"));
    }

    [Test]
    public void Announce_should_fail_over_sequentially_for_private_torrent_when_primary_fails()
    {
        _configService.AnnounceToAllTiers.Returns(true);
        _configService.AnnounceToAllInTier.Returns(true);

        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://primary.tracker.org/announce1"))
            .Returns(new TrackerAnnounceResponse { Success = false, FailureReason = "offline" });
        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://primary.tracker.org/announce2"))
            .Returns(new TrackerAnnounceResponse { Success = true, Interval = 1800 });
        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://primary.tracker.org/announce3"))
            .Returns(new TrackerAnnounceResponse { Success = true, Interval = 1800 });

        var request = CreateRequest();
        request.IsPrivate = true;

        var announceList = new List<List<string>>
        {
            new() { "http://primary.tracker.org/announce1", "http://primary.tracker.org/announce2" },
            new() { "http://primary.tracker.org/announce3" }
        };

        var result = _manager.Announce(request, announceList);

        Assert.That(result.Success, Is.True);
        // Primary failed, so it tried mirror 2 which succeeded, and then stopped without trying tier 2
        _httpTracker.Received(1).Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://primary.tracker.org/announce1"));
        _httpTracker.Received(1).Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://primary.tracker.org/announce2"));
        _httpTracker.DidNotReceive().Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://primary.tracker.org/announce3"));
    }

    [Test]
    public void Announce_should_respect_announce_to_all_tiers_and_in_tier_for_public_torrent()
    {
        _configService.AnnounceToAllTiers.Returns(true);
        _configService.AnnounceToAllInTier.Returns(true);
        _httpTracker.Announce(Arg.Any<TrackerAnnounceRequest>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var request = CreateRequest();
        request.IsPrivate = false;

        var announceList = new List<List<string>>
        {
            new() { "http://t1.com/announce", "http://t2.com/announce" },
            new() { "http://t3.com/announce", "http://t4.com/announce" }
        };

        var result = _manager.Announce(request, announceList);

        Assert.That(result.Success, Is.True);
        _httpTracker.Received(4).Announce(Arg.Any<TrackerAnnounceRequest>());
    }

    [Test]
    public void Announce_should_skip_unauthorized_domain_trackers_on_private_torrent()
    {
        _configService.AnnounceToAllTiers.Returns(false);
        _configService.AnnounceToAllInTier.Returns(false);

        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://private.tracker.org/announce"))
            .Returns(new TrackerAnnounceResponse { Success = false, FailureReason = "down" });

        var request = CreateRequest();
        request.IsPrivate = true;

        var announceList = new List<List<string>>
        {
            new() { "http://private.tracker.org/announce", "udp://tracker.opentrackr.org:1337/announce" }
        };

        var result = _manager.Announce(request, announceList);

        Assert.That(result.Success, Is.False);
        _httpTracker.Received(1).Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://private.tracker.org/announce"));
        // Unauthorized public tracker on different domain must be skipped!
        _udpTracker.DidNotReceive().Announce(Arg.Any<TrackerAnnounceRequest>());
    }

    [Test]
    public void Announce_should_skip_backed_off_tracker()
    {
        _configService.FailoverMaxConsecutiveFailures.Returns(1);
        _httpTracker.Announce(Arg.Any<TrackerAnnounceRequest>())
            .Returns(
                new TrackerAnnounceResponse { Success = false, FailureReason = "error" },
                new TrackerAnnounceResponse { Success = true });

        var request = CreateRequest();
        var announceList = new List<List<string>>
        {
            new() { "http://tracker.com/announce" }
        };

        _manager.Announce(request, announceList);
        var result = _manager.Announce(request, announceList);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void Scrape_should_return_failure_when_no_trackers_and_multitracker_disabled()
    {
        _configService.MultiTrackerEnabled.Returns(false);

        var result = _manager.Scrape("abcd1234", new List<List<string>>());

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo("No trackers available"));
    }

    [Test]
    public void Scrape_should_use_first_tracker_when_multitracker_disabled()
    {
        _configService.MultiTrackerEnabled.Returns(false);
        _httpTracker.Scrape(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new TrackerScrapeResponse { Success = true, Complete = 10 });

        var announceList = new List<List<string>> { new() { "http://tracker1.com/announce" } };

        var result = _manager.Scrape("abcd1234", announceList);

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public void Scrape_should_return_all_trackers_failed()
    {
        _httpTracker.Scrape(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new TrackerScrapeResponse { Success = false, FailureReason = "error" });

        var announceList = new List<List<string>>
        {
            new() { "http://tracker1.com/announce" }
        };

        var result = _manager.Scrape("abcd1234", announceList);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo("All trackers failed"));
    }

    [Test]
    public void Announce_should_record_failure_and_compute_backoff()
    {
        _configService.FailoverMaxConsecutiveFailures.Returns(1);
        _configService.FailoverBackoffBaseSeconds.Returns(60);
        _httpTracker.Announce(Arg.Any<TrackerAnnounceRequest>())
            .Returns(new TrackerAnnounceResponse { Success = false, FailureReason = "error" });

        var request = CreateRequest();
        var announceList = new List<List<string>>
        {
            new() { "http://tracker.com/announce" }
        };

        _manager.Announce(request, announceList);

        var failureStates = (System.Collections.IDictionary)typeof(MultiTrackerManager)
            .GetField("_failureStates", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(_manager);

        Assert.That(failureStates.Contains("http://tracker.com/announce"), Is.True);
    }

    [Test]
    public void Announce_should_reset_failure_on_success()
    {
        _configService.MultiTrackerFailoverEnabled.Returns(true);
        _httpTracker.Announce(Arg.Any<TrackerAnnounceRequest>())
            .Returns(
                new TrackerAnnounceResponse { Success = false, FailureReason = "error" },
                new TrackerAnnounceResponse { Success = true });

        var request = CreateRequest();
        var announceList = new List<List<string>>
        {
            new() { "http://tracker.com/announce" }
        };

        _manager.Announce(request, announceList);

        _configService.FailoverMaxConsecutiveFailures.Returns(999);
        _manager.Announce(request, announceList);

        var failureStates = (System.Collections.IDictionary)typeof(MultiTrackerManager)
            .GetField("_failureStates", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(_manager);

        Assert.That(failureStates.Contains("http://tracker.com/announce"), Is.False);
    }

    [Test]
    public void IsTrackerBackedOff_should_return_false_when_failover_disabled()
    {
        _configService.MultiTrackerFailoverEnabled.Returns(false);

        var method = typeof(MultiTrackerManager).GetMethod("IsTrackerBackedOff", BindingFlags.NonPublic | BindingFlags.Instance, new[] { typeof(string) });
        var result = (bool)method.Invoke(_manager, new object[] { "http://tracker.com/announce" });

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsTrackerBackedOff_should_return_false_for_unknown_tracker()
    {
        var method = typeof(MultiTrackerManager).GetMethod("IsTrackerBackedOff", BindingFlags.NonPublic | BindingFlags.Instance, new[] { typeof(string) });
        var result = (bool)method.Invoke(_manager, new object[] { "http://unknown.com/announce" });

        Assert.That(result, Is.False);
    }

    [Test]
    public void Announce_should_isolate_torrent_specific_failure_and_allow_other_torrents_to_announce()
    {
        _configService.FailoverMaxConsecutiveFailures.Returns(1);
        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.InfoHash == "AABBCCDD00112233445566778899AABBCCDDEEFF"))
            .Returns(new TrackerAnnounceResponse { Success = false, FailureReason = "unregistered torrent" });
        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.InfoHash == "11223344556677889900AABBCCDDEEFF00112233"))
            .Returns(new TrackerAnnounceResponse { Success = true, Interval = 1800 });

        var requestA = CreateRequest();
        var requestB = new TrackerAnnounceRequest
        {
            InfoHash = "11223344556677889900AABBCCDDEEFF00112233",
            PeerId = "-qB4420-otherpeer123",
            Port = 6881,
            Uploaded = 0,
            Downloaded = 0,
            Left = 1000,
            NumWant = 50
        };

        var announceList = new List<List<string>>
        {
            new() { "http://tracker.com/announce" }
        };

        // Torrent A fails with application-level rejection
        var resultA1 = _manager.Announce(requestA, announceList);
        Assert.That(resultA1.Success, Is.False);

        // Torrent A is now backed off on this tracker
        var resultA2 = _manager.Announce(requestA, announceList);
        Assert.That(resultA2.Success, Is.False);
        Assert.That(resultA2.FailureReason, Is.EqualTo("All trackers failed"));

        // Torrent B sharing the same tracker URL must NOT be backed off and should succeed!
        var resultB = _manager.Announce(requestB, announceList);
        Assert.That(resultB.Success, Is.True);
    }

    [Test]
    public void Announce_should_back_off_all_torrents_on_network_error()
    {
        _configService.FailoverMaxConsecutiveFailures.Returns(1);
        _httpTracker.Announce(Arg.Any<TrackerAnnounceRequest>())
            .Returns(new TrackerAnnounceResponse { Success = false, FailureReason = "Connection refused" });

        var requestA = CreateRequest();
        var requestB = new TrackerAnnounceRequest
        {
            InfoHash = "11223344556677889900AABBCCDDEEFF00112233",
            PeerId = "-qB4420-otherpeer123",
            Port = 6881,
            Uploaded = 0,
            Downloaded = 0,
            Left = 1000,
            NumWant = 50
        };

        var announceList = new List<List<string>>
        {
            new() { "http://tracker.com/announce" }
        };

        // Torrent A encounters a network error
        _manager.Announce(requestA, announceList);

        // Tracker host is now backed off globally; Torrent B should also be backed off
        var resultB = _manager.Announce(requestB, announceList);
        Assert.That(resultB.Success, Is.False);
        Assert.That(resultB.FailureReason, Is.EqualTo("All trackers failed"));
    }

    [Test]
    public void IsTrackerBackedOff_with_infohash_should_isolate_by_infohash()
    {
        _configService.FailoverMaxConsecutiveFailures.Returns(1);

        var recordFailureMethod = typeof(MultiTrackerManager).GetMethod(
            "RecordFailure",
            BindingFlags.NonPublic | BindingFlags.Instance,
            new[] { typeof(string), typeof(string), typeof(bool) });

        var isBackedOffMethod = typeof(MultiTrackerManager).GetMethod(
            "IsTrackerBackedOff",
            BindingFlags.NonPublic | BindingFlags.Instance,
            new[] { typeof(string), typeof(string) });

        var hashA = "AABBCCDD00112233445566778899AABBCCDDEEFF";
        var hashB = "11223344556677889900AABBCCDDEEFF00112233";
        var trackerUrl = "http://tracker.com/announce";

        // Record torrent-level failure for Torrent A
        recordFailureMethod.Invoke(_manager, new object[] { hashA, trackerUrl, false });

        var backedOffA = (bool)isBackedOffMethod.Invoke(_manager, new object[] { hashA, trackerUrl });
        var backedOffB = (bool)isBackedOffMethod.Invoke(_manager, new object[] { hashB, trackerUrl });

        Assert.That(backedOffA, Is.True);
        Assert.That(backedOffB, Is.False);
    }

    [Test]
    public void IsTrackerBackedOff_should_return_true_for_all_torrents_when_host_backed_off()
    {
        _configService.FailoverMaxConsecutiveFailures.Returns(1);

        var recordFailureMethod = typeof(MultiTrackerManager).GetMethod(
            "RecordFailure",
            BindingFlags.NonPublic | BindingFlags.Instance,
            new[] { typeof(string), typeof(string), typeof(bool) });

        var isBackedOffMethod = typeof(MultiTrackerManager).GetMethod(
            "IsTrackerBackedOff",
            BindingFlags.NonPublic | BindingFlags.Instance,
            new[] { typeof(string), typeof(string) });

        var hashA = "AABBCCDD00112233445566778899AABBCCDDEEFF";
        var hashB = "11223344556677889900AABBCCDDEEFF00112233";
        var trackerUrl = "http://tracker.com/announce";

        // Record host-level network failure
        recordFailureMethod.Invoke(_manager, new object[] { hashA, trackerUrl, true });

        var backedOffA = (bool)isBackedOffMethod.Invoke(_manager, new object[] { hashA, trackerUrl });
        var backedOffB = (bool)isBackedOffMethod.Invoke(_manager, new object[] { hashB, trackerUrl });

        Assert.That(backedOffA, Is.True);
        Assert.That(backedOffB, Is.True);
    }

    [Test]
    public void GetProvider_should_return_http_for_http_url()
    {
        var method = typeof(MultiTrackerManager).GetMethod("GetProvider", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = method.Invoke(_manager, new object[] { "http://tracker.com/announce" });

        Assert.That(result, Is.SameAs(_httpTracker));
    }

    [Test]
    public void GetProvider_should_return_http_for_https_url()
    {
        var method = typeof(MultiTrackerManager).GetMethod("GetProvider", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = method.Invoke(_manager, new object[] { "https://tracker.com/announce" });

        Assert.That(result, Is.SameAs(_httpTracker));
    }

    [Test]
    public void GetProvider_should_return_udp_for_udp_url()
    {
        var method = typeof(MultiTrackerManager).GetMethod("GetProvider", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = method.Invoke(_manager, new object[] { "udp://tracker.com:6969/announce" });

        Assert.That(result, Is.SameAs(_udpTracker));
    }

    [Test]
    public void GetProvider_should_return_null_for_unknown_protocol()
    {
        var method = typeof(MultiTrackerManager).GetMethod("GetProvider", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = method.Invoke(_manager, new object[] { "wss://tracker.com/announce" });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Announce_should_handle_tracker_throwing_exception()
    {
        _httpTracker.Announce(Arg.Any<TrackerAnnounceRequest>())
            .Returns(x => throw new Exception("network error"));

        var request = CreateRequest();
        var announceList = new List<List<string>>
        {
            new() { "http://tracker.com/announce" }
        };

        var result = _manager.Announce(request, announceList);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void Scrape_should_handle_tracker_throwing_exception()
    {
        _httpTracker.Scrape(Arg.Any<string>(), Arg.Any<string>())
            .Returns(x => throw new Exception("network error"));

        var announceList = new List<List<string>>
        {
            new() { "http://tracker.com/announce" }
        };

        var result = _manager.Scrape("abcd1234", announceList);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void Announce_should_return_failure_when_multitracker_disabled_and_empty_tier()
    {
        _configService.MultiTrackerEnabled.Returns(false);
        var request = CreateRequest();
        var announceList = new List<List<string>> { new() };

        var result = _manager.Announce(request, announceList);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void Announce_should_failover_to_next_tier_when_all_trackers_in_first_tier_fail()
    {
        _configService.AnnounceToAllTiers.Returns(false);
        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://tier1-t1.com/announce"))
            .Returns(new TrackerAnnounceResponse { Success = false, FailureReason = "offline" });
        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://tier1-t2.com/announce"))
            .Returns(new TrackerAnnounceResponse { Success = false, FailureReason = "timeout" });
        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://tier2-t1.com/announce"))
            .Returns(new TrackerAnnounceResponse { Success = true, Complete = 12, Incomplete = 3, Interval = 1800 });

        var request = CreateRequest();
        var announceList = new List<List<string>>
        {
            new() { "http://tier1-t1.com/announce", "http://tier1-t2.com/announce" },
            new() { "http://tier2-t1.com/announce" }
        };

        var result = _manager.Announce(request, announceList);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Complete, Is.EqualTo(12));
        _httpTracker.Received(1).Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://tier1-t1.com/announce"));
        _httpTracker.Received(1).Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://tier1-t2.com/announce"));
        _httpTracker.Received(1).Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://tier2-t1.com/announce"));
    }

    [Test]
    public void Announce_should_stop_tier_traversal_when_first_tier_succeeds_and_announce_to_all_tiers_is_false()
    {
        _configService.AnnounceToAllTiers.Returns(false);
        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://tier1-t1.com/announce"))
            .Returns(new TrackerAnnounceResponse { Success = true, Complete = 5, Interval = 1800 });
        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://tier2-t1.com/announce"))
            .Returns(new TrackerAnnounceResponse { Success = true, Complete = 20, Interval = 1800 });

        var request = CreateRequest();
        var announceList = new List<List<string>>
        {
            new() { "http://tier1-t1.com/announce" },
            new() { "http://tier2-t1.com/announce" }
        };

        var result = _manager.Announce(request, announceList);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Complete, Is.EqualTo(5));
        _httpTracker.Received(1).Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://tier1-t1.com/announce"));
        _httpTracker.DidNotReceive().Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://tier2-t1.com/announce"));
    }

    [Test]
    public void Announce_should_promote_successful_tracker_to_head_of_its_tier()
    {
        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://tracker1.com/announce"))
            .Returns(new TrackerAnnounceResponse { Success = false, FailureReason = "timeout" });
        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://tracker2.com/announce"))
            .Returns(new TrackerAnnounceResponse { Success = true, Interval = 1800 });

        var request = CreateRequest();
        var announceList = new List<List<string>>
        {
            new() { "http://tracker1.com/announce", "http://tracker2.com/announce", "http://tracker3.com/announce" }
        };

        var result = _manager.Announce(request, announceList);

        Assert.That(result.Success, Is.True);
        // BEP 12: Successful tracker is moved to index 0 of its tier
        Assert.That(announceList[0][0], Is.EqualTo("http://tracker2.com/announce"));
        Assert.That(announceList[0][1], Is.EqualTo("http://tracker1.com/announce"));
        Assert.That(announceList[0][2], Is.EqualTo("http://tracker3.com/announce"));
    }

    [Test]
    public void ShuffleTier_should_randomize_order_of_trackers_within_tier_using_fisher_yates()
    {
        var tier = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            tier.Add($"http://tracker{i}.com/announce");
        }

        var original = new List<string>(tier);
        MultiTrackerManager.ShuffleTier(tier);

        // Same elements preserved
        Assert.That(tier, Is.EquivalentTo(original));
        // Elements should not be in identical order with high probability
        Assert.That(tier, Is.Not.EqualTo(original));
    }

    [Test]
    public void Announce_should_shuffle_trackers_within_tiers_when_event_is_started()
    {
        _httpTracker.Announce(Arg.Any<TrackerAnnounceRequest>())
            .Returns(new TrackerAnnounceResponse { Success = true });

        var request = CreateRequest();
        request.Event = AnnounceEvent.Started;

        var tier = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            tier.Add($"http://tracker{i}.com/announce");
        }

        var original = new List<string>(tier);
        var announceList = new List<List<string>> { tier };

        _manager.Announce(request, announceList);

        Assert.That(announceList[0], Is.EquivalentTo(original));
    }

    [Test]
    public void Announce_should_apply_exponential_backoff_and_auto_disable_after_five_consecutive_failures()
    {
        _configService.MultiTrackerFailoverEnabled.Returns(true);
        _configService.FailoverBackoffBaseSeconds.Returns(60);
        _configService.FailoverMaxBackoffSeconds.Returns(3600);
        _configService.FailoverMaxConsecutiveFailures.Returns(5);

        _httpTracker.Announce(Arg.Any<TrackerAnnounceRequest>())
            .Returns(new TrackerAnnounceResponse { Success = false, FailureReason = "Connection refused" });

        var request = CreateRequest();
        var trackerUrl = "http://failing-tracker.com/announce";
        var announceList = new List<List<string>> { new() { trackerUrl } };

        // Failures 1 through 4: tracker backed off but not disabled
        for (var i = 1; i <= 4; i++)
        {
            var res = _manager.Announce(request, announceList);
            Assert.That(res.Success, Is.False);
            Assert.That(_manager.IsTrackerDisabled(trackerUrl), Is.False);
        }

        // Failure 5: auto-disabled
        var res5 = _manager.Announce(request, announceList);
        Assert.That(res5.Success, Is.False);
        Assert.That(_manager.IsTrackerDisabled(trackerUrl), Is.True);
    }

    [Test]
    public void Announce_should_deduplicate_peers_across_multiple_tracker_responses()
    {
        _configService.AnnounceToAllTiers.Returns(true);
        _configService.AnnounceToAllInTier.Returns(true);

        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://tier1.com/announce"))
            .Returns(new TrackerAnnounceResponse
            {
                Success = true,
                Complete = 5,
                Incomplete = 2,
                Peers = new List<TrackerPeer>
                {
                    new() { Ip = "1.2.3.4", Port = 6881, PeerId = "peerA" },
                    new() { Ip = "5.6.7.8", Port = 6881, PeerId = "peerB" }
                }
            });

        _httpTracker.Announce(Arg.Is<TrackerAnnounceRequest>(r => r.TrackerUrl == "http://tier2.com/announce"))
            .Returns(new TrackerAnnounceResponse
            {
                Success = true,
                Complete = 8,
                Incomplete = 1,
                Peers = new List<TrackerPeer>
                {
                    new() { Ip = "5.6.7.8", Port = 6881, PeerId = "peerB" }, // Duplicate!
                    new() { Ip = "9.10.11.12", Port = 6882, PeerId = "peerC" }
                }
            });

        var request = CreateRequest();
        var announceList = new List<List<string>>
        {
            new() { "http://tier1.com/announce" },
            new() { "http://tier2.com/announce" }
        };

        var result = _manager.Announce(request, announceList);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Peers.Count, Is.EqualTo(3));
        var endpoints = result.Peers.Select(p => $"{p.Ip}:{p.Port}").ToList();
        Assert.That(endpoints, Does.Contain("1.2.3.4:6881"));
        Assert.That(endpoints, Does.Contain("5.6.7.8:6881"));
        Assert.That(endpoints, Does.Contain("9.10.11.12:6882"));
    }

    [Test]
    public void CalculateResponseTimeEma_should_calculate_correct_exponential_moving_average()
    {
        // First sample initializes EMA
        var ema = MultiTrackerManager.CalculateResponseTimeEma(100.0, 0.0);
        Assert.That(ema, Is.EqualTo(100.0));

        // Second sample with alpha = 0.2: 0.2 * 200 + 0.8 * 100 = 40 + 80 = 120
        ema = MultiTrackerManager.CalculateResponseTimeEma(200.0, ema, 0.2);
        Assert.That(ema, Is.EqualTo(120.0).Within(0.001));

        // Third sample: 0.2 * 50 + 0.8 * 120 = 10 + 96 = 106
        ema = MultiTrackerManager.CalculateResponseTimeEma(50.0, ema, 0.2);
        Assert.That(ema, Is.EqualTo(106.0).Within(0.001));
    }

    private static TrackerAnnounceRequest CreateRequest()
    {
        return new TrackerAnnounceRequest
        {
            InfoHash = "AABBCCDD00112233445566778899AABBCCDDEEFF",
            PeerId = "-qB4420-abcdefghijkl",
            Port = 6881,
            Uploaded = 0,
            Downloaded = 0,
            Left = 1000,
            NumWant = 50
        };
    }
}
