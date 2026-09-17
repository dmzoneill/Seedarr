using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class PeerDiscoveryServiceTest
{
    private const string InfoHash = "0123456789abcdef0123456789abcdef01234567";
    private PeerDiscoveryService _service;

    [SetUp]
    public void Setup()
    {
        _service = new PeerDiscoveryService();
    }

    [Test]
    public void GetPeers_should_select_LPD_peers_ahead_of_tracker_and_DHT_peers_even_if_DHT_discovered_more_recently()
    {
        var lpdPeer = new TrackerPeer { Ip = "192.168.1.100", Port = 5001 };
        var trackerPeer = new TrackerPeer { Ip = "2.2.2.2", Port = 5002 };
        var dhtPeer = new TrackerPeer { Ip = "1.1.1.1", Port = 5003 };

        _service.AddPeers(InfoHash, new[] { lpdPeer }, "lpd");
        _service.AddPeers(InfoHash, new[] { trackerPeer }, "tracker");
        _service.AddPeers(InfoHash, new[] { dhtPeer }, "dht");

        var peers = _service.GetPeers(InfoHash, 10);

        Assert.That(peers.Count, Is.EqualTo(3));
        Assert.That(peers[0].Source, Is.EqualTo("lpd"));
        Assert.That(peers[0].Ip, Is.EqualTo("192.168.1.100"));
        Assert.That(peers[1].Source, Is.EqualTo("tracker"));
        Assert.That(peers[1].Ip, Is.EqualTo("2.2.2.2"));
        Assert.That(peers[2].Source, Is.EqualTo("dht"));
        Assert.That(peers[2].Ip, Is.EqualTo("1.1.1.1"));
    }

    [Test]
    public void Candidate_eviction_should_protect_LPD_peers_when_list_exceeds_MaxPeersPerTorrent()
    {
        var lpdPeers = new List<TrackerPeer>
        {
            new TrackerPeer { Ip = "192.168.1.10", Port = 5000 },
            new TrackerPeer { Ip = "192.168.1.11", Port = 5001 },
            new TrackerPeer { Ip = "192.168.1.12", Port = 5002 }
        };

        _service.AddPeers(InfoHash, lpdPeers, "lpd");

        var dhtPeers = new List<TrackerPeer>();
        for (var i = 1; i <= 210; i++)
        {
            dhtPeers.Add(new TrackerPeer
            {
                Ip = $"10.0.{i / 256}.{i % 256}",
                Port = 6000 + i
            });
        }

        _service.AddPeers(InfoHash, dhtPeers, "dht");

        var peers = _service.GetPeers(InfoHash, 200);

        Assert.That(peers.Count, Is.EqualTo(200));

        foreach (var lpd in lpdPeers)
        {
            Assert.That(peers.Any(p => p.Ip == lpd.Ip && p.Port == lpd.Port && p.Source == "lpd"), Is.True);
        }
    }

    [Test]
    public void Candidate_eviction_should_evict_failed_peers_before_active_peers()
    {
        var failedPeers = new List<TrackerPeer>();
        for (var i = 1; i <= 5; i++)
        {
            failedPeers.Add(new TrackerPeer { Ip = $"8.8.8.{i}", Port = 7000 + i });
        }

        _service.AddPeers(InfoHash, failedPeers, "tracker");

        for (var i = 1; i <= 5; i++)
        {
            _service.MarkAttempted(InfoHash, $"8.8.8.{i}", 7000 + i, success: false);
            _service.MarkAttempted(InfoHash, $"8.8.8.{i}", 7000 + i, success: false);
            _service.MarkAttempted(InfoHash, $"8.8.8.{i}", 7000 + i, success: false);
        }

        var activePeers = new List<TrackerPeer>();
        for (var i = 1; i <= 200; i++)
        {
            activePeers.Add(new TrackerPeer { Ip = $"9.9.{i / 256}.{i % 256}", Port = 8000 + i });
        }

        _service.AddPeers(InfoHash, activePeers, "dht");

        var lpdPeer = new TrackerPeer { Ip = "192.168.1.99", Port = 51413 };
        _service.AddPeers(InfoHash, new[] { lpdPeer }, "lpd");

        var peers = _service.GetPeers(InfoHash, 200);

        Assert.That(peers.Count, Is.EqualTo(200));
        Assert.That(peers.Any(p => p.Ip == lpdPeer.Ip), Is.True);
        Assert.That(peers.Any(p => failedPeers.Any(fp => fp.Ip == p.Ip)), Is.False);
    }

    [Test]
    public void LPD_peers_should_become_eligible_for_retry_much_sooner_than_WAN_peers()
    {
        var lpdPeer = new TrackerPeer { Ip = "192.168.1.50", Port = 6881 };
        var wanPeer = new TrackerPeer { Ip = "45.33.32.156", Port = 6881 };

        _service.AddPeers(InfoHash, new[] { lpdPeer }, "lpd");
        _service.AddPeers(InfoHash, new[] { wanPeer }, "tracker");

        var candidates = _service.GetPeers(InfoHash, 10);
        var lpdDiscovered = candidates.First(p => p.Source == "lpd");
        var wanDiscovered = candidates.First(p => p.Source == "tracker");

        _service.MarkAttempted(InfoHash, lpdPeer.Ip, lpdPeer.Port, success: false);
        _service.MarkAttempted(InfoHash, wanPeer.Ip, wanPeer.Port, success: false);

        // Simulate 2 minutes having elapsed since the failed attempt
        var twoMinutesAgo = DateTime.UtcNow.AddMinutes(-2);
        lpdDiscovered.LastAttempt = twoMinutesAgo;
        wanDiscovered.LastAttempt = twoMinutesAgo;

        // At 2 minutes elapsed:
        // LPD peer retry backoff is 1 minute -> eligible
        // WAN peer retry backoff is 10 minutes -> not eligible
        var peersAfterTwoMinutes = _service.GetPeers(InfoHash, 10);

        Assert.That(peersAfterTwoMinutes.Count, Is.EqualTo(1));
        Assert.That(peersAfterTwoMinutes[0].Ip, Is.EqualTo(lpdPeer.Ip));
        Assert.That(peersAfterTwoMinutes[0].Source, Is.EqualTo("lpd"));

        // Simulate 11 minutes having elapsed since the failed attempt
        var elevenMinutesAgo = DateTime.UtcNow.AddMinutes(-11);
        lpdDiscovered.LastAttempt = elevenMinutesAgo;
        wanDiscovered.LastAttempt = elevenMinutesAgo;

        var peersAfterElevenMinutes = _service.GetPeers(InfoHash, 10);

        Assert.That(peersAfterElevenMinutes.Count, Is.EqualTo(2));
    }

    [Test]
    public void AddPeers_should_not_downgrade_existing_LPD_peer_when_re_announced_by_tracker()
    {
        var peer = new TrackerPeer { Ip = "192.168.1.50", Port = 6881 };

        _service.AddPeers(InfoHash, new[] { peer }, "lpd");
        _service.AddPeers(InfoHash, new[] { peer }, "tracker");

        var peers = _service.GetPeers(InfoHash, 10);

        Assert.That(peers.Count, Is.EqualTo(1));
        Assert.That(peers[0].Source, Is.EqualTo("lpd"));
    }

    [Test]
    public void AddPeers_should_upgrade_existing_peer_to_higher_priority_source()
    {
        var peer = new TrackerPeer { Ip = "192.168.1.50", Port = 6881 };

        _service.AddPeers(InfoHash, new[] { peer }, "dht");
        _service.AddPeers(InfoHash, new[] { peer }, "pex");

        var peers = _service.GetPeers(InfoHash, 10);

        Assert.That(peers.Count, Is.EqualTo(1));
        Assert.That(peers[0].Source, Is.EqualTo("pex"));
    }

    [Test]
    public void GetPeers_should_update_LastAttempt_immediately_and_prevent_duplicate_return_on_immediate_subsequent_call()
    {
        var peers = new[]
        {
            new TrackerPeer { Ip = "192.168.1.50", Port = 6881 },
            new TrackerPeer { Ip = "192.168.1.51", Port = 6882 }
        };

        _service.AddPeers(InfoHash, peers, "tracker");

        var beforeCall = DateTime.UtcNow;
        var firstBatch = _service.GetPeers(InfoHash, 10);

        Assert.That(firstBatch.Count, Is.EqualTo(2));
        foreach (var peer in firstBatch)
        {
            Assert.That(peer.LastAttempt.HasValue, Is.True);
            Assert.That(peer.LastAttempt.Value, Is.GreaterThanOrEqualTo(beforeCall));
        }

        // An immediate subsequent call should return nothing because candidates are in-flight
        var secondBatch = _service.GetPeers(InfoHash, 10);
        Assert.That(secondBatch, Is.Empty);
    }

    [Test]
    public void MarkAttempted_should_increment_FailCount_and_update_LastAttempt_on_failure()
    {
        var peer = new TrackerPeer { Ip = "10.0.0.5", Port = 51413 };
        _service.AddPeers(InfoHash, new[] { peer }, "tracker");

        var candidates = _service.GetPeers(InfoHash, 10);
        Assert.That(candidates.Count, Is.EqualTo(1));
        var candidate = candidates[0];
        Assert.That(candidate.FailCount, Is.EqualTo(0));

        var timeBeforeFailure = DateTime.UtcNow.AddSeconds(-1);
        _service.MarkAttempted(InfoHash, "10.0.0.5", 51413, success: false);

        Assert.That(candidate.FailCount, Is.EqualTo(1));
        Assert.That(candidate.LastAttempt.HasValue, Is.True);
        Assert.That(candidate.LastAttempt.Value, Is.GreaterThanOrEqualTo(timeBeforeFailure));

        _service.MarkAttempted(InfoHash, "10.0.0.5", 51413, success: false);
        Assert.That(candidate.FailCount, Is.EqualTo(2));
    }

    [Test]
    public void MarkAttempted_should_reset_FailCount_and_update_LastAttempt_on_success()
    {
        var peer = new TrackerPeer { Ip = "10.0.0.6", Port = 51413 };
        _service.AddPeers(InfoHash, new[] { peer }, "tracker");

        var candidates = _service.GetPeers(InfoHash, 10);
        Assert.That(candidates.Count, Is.EqualTo(1));
        var candidate = candidates[0];

        _service.MarkAttempted(InfoHash, "10.0.0.6", 51413, success: false);
        _service.MarkAttempted(InfoHash, "10.0.0.6", 51413, success: false);
        Assert.That(candidate.FailCount, Is.EqualTo(2));

        var timeBeforeSuccess = DateTime.UtcNow.AddSeconds(-1);
        _service.MarkAttempted(InfoHash, "10.0.0.6", 51413, success: true);

        Assert.That(candidate.FailCount, Is.EqualTo(0));
        Assert.That(candidate.LastAttempt.HasValue, Is.True);
        Assert.That(candidate.LastAttempt.Value, Is.GreaterThanOrEqualTo(timeBeforeSuccess));
    }

    [Test]
    public void RemoveTorrent_should_remove_all_stored_peers_for_that_infohash()
    {
        const string otherInfoHash = "fedcba9876543210fedcba9876543210fedcba98";
        var peer1 = new TrackerPeer { Ip = "1.1.1.1", Port = 5000 };
        var peer2 = new TrackerPeer { Ip = "2.2.2.2", Port = 6000 };

        _service.AddPeers(InfoHash, new[] { peer1 }, "tracker");
        _service.AddPeers(otherInfoHash, new[] { peer2 }, "tracker");

        Assert.That(_service.PeerCount(InfoHash), Is.EqualTo(1));
        Assert.That(_service.PeerCount(otherInfoHash), Is.EqualTo(1));

        _service.RemoveTorrent(InfoHash);

        Assert.That(_service.PeerCount(InfoHash), Is.EqualTo(0));
        Assert.That(_service.GetPeers(InfoHash), Is.Empty);
        Assert.That(_service.PeerCount(otherInfoHash), Is.EqualTo(1));
    }

    [Test]
    public void TorrentDeletedEvent_should_evict_torrent_from_PeerDiscoveryService()
    {
        var peer = new TrackerPeer { Ip = "3.3.3.3", Port = 7000 };
        _service.AddPeers(InfoHash, new[] { peer }, "tracker");

        Assert.That(_service.PeerCount(InfoHash), Is.EqualTo(1));

        var deleteEvent = new TorrentDeletedEvent(100, new Torrent { InfoHash = InfoHash });
        _service.Handle(deleteEvent);

        Assert.That(_service.PeerCount(InfoHash), Is.EqualTo(0));
        Assert.That(_service.GetPeers(InfoHash), Is.Empty);
    }

    [Test]
    public void PruneStalePeers_should_purge_stale_failed_and_expired_candidates_while_preserving_active_recent_peers()
    {
        var now = DateTime.UtcNow;

        var peersField = typeof(PeerDiscoveryService).GetField("_peers", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var peersDict = (ConcurrentDictionary<string, List<DiscoveredPeer>>)peersField.GetValue(_service)!;

        var list = new List<DiscoveredPeer>
        {
            // Active recent peer: keep
            new DiscoveredPeer
            {
                Ip = "10.0.0.1",
                Port = 5001,
                Source = "tracker",
                DiscoveredAt = now.AddHours(-1),
                FailCount = 0,
                LastAttempt = null
            },

            // Stale failed peer (failed >= 3 and last attempt > 24h ago): purge
            new DiscoveredPeer
            {
                Ip = "10.0.0.2",
                Port = 5002,
                Source = "tracker",
                DiscoveredAt = now.AddHours(-30),
                FailCount = 3,
                LastAttempt = now.AddHours(-25)
            },

            // Recent failed peer (failed >= 3 but last attempt within 24h): keep
            new DiscoveredPeer
            {
                Ip = "10.0.0.3",
                Port = 5003,
                Source = "tracker",
                DiscoveredAt = now.AddHours(-5),
                FailCount = 3,
                LastAttempt = now.AddHours(-2)
            },

            // Expired candidate (> 48h old and never attempted/connected): purge
            new DiscoveredPeer
            {
                Ip = "10.0.0.4",
                Port = 5004,
                Source = "tracker",
                DiscoveredAt = now.AddHours(-50),
                FailCount = 0,
                LastAttempt = null
            },

            // Expired candidate (> 48h old and has FailCount > 0): purge
            new DiscoveredPeer
            {
                Ip = "10.0.0.5",
                Port = 5005,
                Source = "tracker",
                DiscoveredAt = now.AddHours(-50),
                FailCount = 1,
                LastAttempt = now.AddHours(-10)
            }
        };

        peersDict[InfoHash] = list;

        _service.PruneStalePeers();

        Assert.That(list.Count, Is.EqualTo(2));
        Assert.That(list.Any(p => p.Ip == "10.0.0.1"), Is.True);
        Assert.That(list.Any(p => p.Ip == "10.0.0.3"), Is.True);
        Assert.That(list.Any(p => p.Ip == "10.0.0.2"), Is.False);
        Assert.That(list.Any(p => p.Ip == "10.0.0.4"), Is.False);
        Assert.That(list.Any(p => p.Ip == "10.0.0.5"), Is.False);
    }

    [Test]
    public void PruneStalePeers_should_remove_empty_dictionary_entry_when_all_peers_pruned()
    {
        var now = DateTime.UtcNow;

        var peersField = typeof(PeerDiscoveryService).GetField("_peers", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var peersDict = (ConcurrentDictionary<string, List<DiscoveredPeer>>)peersField.GetValue(_service)!;

        var list = new List<DiscoveredPeer>
        {
            new DiscoveredPeer
            {
                Ip = "10.0.0.2",
                Port = 5002,
                Source = "tracker",
                DiscoveredAt = now.AddHours(-30),
                FailCount = 3,
                LastAttempt = now.AddHours(-25)
            }
        };

        peersDict[InfoHash] = list;

        _service.PruneStalePeers();

        Assert.That(peersDict.ContainsKey(InfoHash), Is.False);
    }
}
