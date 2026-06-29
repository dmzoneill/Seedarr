using System;
using System.Collections.Generic;
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

        var method = typeof(MultiTrackerManager).GetMethod("IsTrackerBackedOff", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (bool)method.Invoke(_manager, new object[] { "http://tracker.com/announce" });

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsTrackerBackedOff_should_return_false_for_unknown_tracker()
    {
        var method = typeof(MultiTrackerManager).GetMethod("IsTrackerBackedOff", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (bool)method.Invoke(_manager, new object[] { "http://unknown.com/announce" });

        Assert.That(result, Is.False);
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
