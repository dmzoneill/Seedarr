using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.TrackerServer;

namespace NzbDrone.Core.Test.TrackerServer;

[TestFixture]
public class PeerDatabaseTest
{
    private PeerDatabase _peerDatabase;

    [SetUp]
    public void Setup()
    {
        _peerDatabase = new PeerDatabase();
    }

    [TearDown]
    public void TearDown()
    {
        _peerDatabase?.Dispose();
    }

    [Test]
    public void AddPeer_should_add_new_peer_to_empty_database()
    {
        _peerDatabase.AddPeer("abc123", "192.168.1.1", 6881, "peer1");

        var peers = _peerDatabase.GetPeers("abc123");

        Assert.That(peers, Has.Count.EqualTo(1));
        Assert.That(peers[0].Ip, Is.EqualTo("192.168.1.1"));
        Assert.That(peers[0].Port, Is.EqualTo(6881));
        Assert.That(peers[0].PeerId, Is.EqualTo("peer1"));
    }

    [Test]
    public void AddPeer_should_update_existing_peer_last_announce()
    {
        _peerDatabase.AddPeer("abc123", "192.168.1.1", 6881, "peer1");
        var firstPeers = _peerDatabase.GetPeers("abc123");
        var firstAnnounce = firstPeers[0].LastAnnounce;

        System.Threading.Thread.Sleep(10);
        _peerDatabase.AddPeer("abc123", "192.168.1.1", 6881, "peer1");
        var secondPeers = _peerDatabase.GetPeers("abc123");

        Assert.That(secondPeers[0].LastAnnounce, Is.GreaterThanOrEqualTo(firstAnnounce));
    }

    [Test]
    public void AddPeer_should_update_existing_peer_id()
    {
        _peerDatabase.AddPeer("abc123", "192.168.1.1", 6881, "peer1");
        _peerDatabase.AddPeer("abc123", "192.168.1.1", 6881, "peer2");

        var peers = _peerDatabase.GetPeers("abc123");

        Assert.That(peers, Has.Count.EqualTo(1));
        Assert.That(peers[0].PeerId, Is.EqualTo("peer2"));
    }

    [Test]
    public void AddPeer_should_add_multiple_peers_for_same_infohash()
    {
        _peerDatabase.AddPeer("abc123", "192.168.1.1", 6881, "peer1");
        _peerDatabase.AddPeer("abc123", "192.168.1.2", 6882, "peer2");
        _peerDatabase.AddPeer("abc123", "192.168.1.3", 6883, "peer3");

        var peers = _peerDatabase.GetPeers("abc123");

        Assert.That(peers, Has.Count.EqualTo(3));
    }

    [Test]
    public void AddPeer_should_add_peers_for_different_infohashes()
    {
        _peerDatabase.AddPeer("hash1", "192.168.1.1", 6881, "peer1");
        _peerDatabase.AddPeer("hash2", "192.168.1.2", 6882, "peer2");

        var peers1 = _peerDatabase.GetPeers("hash1");
        var peers2 = _peerDatabase.GetPeers("hash2");

        Assert.That(peers1, Has.Count.EqualTo(1));
        Assert.That(peers2, Has.Count.EqualTo(1));
    }

    [Test]
    public void AddPeer_should_distinguish_same_ip_different_port()
    {
        _peerDatabase.AddPeer("abc123", "192.168.1.1", 6881, "peer1");
        _peerDatabase.AddPeer("abc123", "192.168.1.1", 6882, "peer2");

        var peers = _peerDatabase.GetPeers("abc123");

        Assert.That(peers, Has.Count.EqualTo(2));
    }

    [Test]
    public void AddPeer_should_be_case_insensitive_for_infohash()
    {
        _peerDatabase.AddPeer("ABC123", "192.168.1.1", 6881, "peer1");

        var peers = _peerDatabase.GetPeers("abc123");

        Assert.That(peers, Has.Count.EqualTo(1));
    }

    [Test]
    public void RemovePeer_should_remove_existing_peer()
    {
        _peerDatabase.AddPeer("abc123", "192.168.1.1", 6881, "peer1");
        _peerDatabase.RemovePeer("abc123", "192.168.1.1", 6881);

        var peers = _peerDatabase.GetPeers("abc123");

        Assert.That(peers, Is.Empty);
    }

    [Test]
    public void RemovePeer_should_not_throw_when_infohash_not_found()
    {
        Assert.DoesNotThrow(() => _peerDatabase.RemovePeer("nonexistent", "192.168.1.1", 6881));
    }

    [Test]
    public void RemovePeer_should_not_throw_when_peer_not_found()
    {
        _peerDatabase.AddPeer("abc123", "192.168.1.1", 6881, "peer1");

        Assert.DoesNotThrow(() => _peerDatabase.RemovePeer("abc123", "192.168.1.2", 6881));

        var peers = _peerDatabase.GetPeers("abc123");
        Assert.That(peers, Has.Count.EqualTo(1));
    }

    [Test]
    public void RemovePeer_should_remove_infohash_entry_when_last_peer_removed()
    {
        _peerDatabase.AddPeer("abc123", "192.168.1.1", 6881, "peer1");
        _peerDatabase.RemovePeer("abc123", "192.168.1.1", 6881);

        var hashes = _peerDatabase.GetAllInfoHashes();

        Assert.That(hashes, Is.Empty);
    }

    [Test]
    public void RemovePeer_should_keep_other_peers_intact()
    {
        _peerDatabase.AddPeer("abc123", "192.168.1.1", 6881, "peer1");
        _peerDatabase.AddPeer("abc123", "192.168.1.2", 6882, "peer2");
        _peerDatabase.RemovePeer("abc123", "192.168.1.1", 6881);

        var peers = _peerDatabase.GetPeers("abc123");

        Assert.That(peers, Has.Count.EqualTo(1));
        Assert.That(peers[0].Ip, Is.EqualTo("192.168.1.2"));
    }

    [Test]
    public void GetPeers_should_return_empty_list_for_unknown_infohash()
    {
        var peers = _peerDatabase.GetPeers("nonexistent");

        Assert.That(peers, Is.Empty);
    }

    [Test]
    public void GetPeers_should_return_active_peers()
    {
        _peerDatabase.AddPeer("abc123", "192.168.1.1", 6881, "peer1");
        _peerDatabase.AddPeer("abc123", "192.168.1.2", 6882, "peer2");

        var peers = _peerDatabase.GetPeers("abc123");

        Assert.That(peers, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetPeers_should_return_copies_not_references()
    {
        _peerDatabase.AddPeer("abc123", "192.168.1.1", 6881, "peer1");

        var peers1 = _peerDatabase.GetPeers("abc123");
        var peers2 = _peerDatabase.GetPeers("abc123");

        Assert.That(peers1, Is.Not.SameAs(peers2));
    }

    [Test]
    public void GetStats_should_return_zero_stats_for_unknown_infohash()
    {
        var stats = _peerDatabase.GetStats("nonexistent");

        Assert.That(stats.Complete, Is.EqualTo(0));
        Assert.That(stats.Incomplete, Is.EqualTo(0));
        Assert.That(stats.Downloaded, Is.EqualTo(0));
    }

    [Test]
    public void GetStats_should_return_stats_for_known_infohash()
    {
        _peerDatabase.AddPeer("abc123", "192.168.1.1", 6881, "peer1");
        _peerDatabase.AddPeer("abc123", "192.168.1.2", 6882, "peer2");

        var stats = _peerDatabase.GetStats("abc123");

        Assert.That(stats.Complete, Is.EqualTo(2));
        Assert.That(stats.Downloaded, Is.EqualTo(0));
        Assert.That(stats.Incomplete, Is.EqualTo(0));
    }

    [Test]
    public void GetAllInfoHashes_should_return_empty_list_when_no_peers()
    {
        var hashes = _peerDatabase.GetAllInfoHashes();

        Assert.That(hashes, Is.Empty);
    }

    [Test]
    public void GetAllInfoHashes_should_return_all_active_infohashes()
    {
        _peerDatabase.AddPeer("hash1", "192.168.1.1", 6881, "peer1");
        _peerDatabase.AddPeer("hash2", "192.168.1.2", 6882, "peer2");
        _peerDatabase.AddPeer("hash3", "192.168.1.3", 6883, "peer3");

        var hashes = _peerDatabase.GetAllInfoHashes();

        Assert.That(hashes, Has.Count.EqualTo(3));
        Assert.That(hashes, Does.Contain("hash1"));
        Assert.That(hashes, Does.Contain("hash2"));
        Assert.That(hashes, Does.Contain("hash3"));
    }

    [Test]
    public void GetAllStats_should_return_empty_dictionary_when_no_peers()
    {
        var stats = _peerDatabase.GetAllStats();

        Assert.That(stats, Is.Empty);
    }

    [Test]
    public void GetAllStats_should_return_all_active_torrents_stats()
    {
        _peerDatabase.AddPeer("hash1", "192.168.1.1", 6881, "peer1");
        _peerDatabase.AddPeer("hash1", "192.168.1.2", 6882, "peer2");
        _peerDatabase.AddPeer("hash2", "192.168.1.3", 6883, "peer3");

        var stats = _peerDatabase.GetAllStats();

        Assert.That(stats, Has.Count.EqualTo(2));
        Assert.That(stats.ContainsKey("hash1"), Is.True);
        Assert.That(stats["hash1"].Complete, Is.EqualTo(2));
        Assert.That(stats.ContainsKey("hash2"), Is.True);
        Assert.That(stats["hash2"].Complete, Is.EqualTo(1));
    }

    [Test]
    public void GetTotalPeerCount_should_return_zero_when_empty()
    {
        var count = _peerDatabase.GetTotalPeerCount();

        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public void GetTotalPeerCount_should_count_active_peers_across_infohashes()
    {
        _peerDatabase.AddPeer("hash1", "192.168.1.1", 6881, "peer1");
        _peerDatabase.AddPeer("hash1", "192.168.1.2", 6882, "peer2");
        _peerDatabase.AddPeer("hash2", "192.168.1.3", 6883, "peer3");

        var count = _peerDatabase.GetTotalPeerCount();

        Assert.That(count, Is.EqualTo(3));
    }

    [Test]
    public void GetTotalTorrentCount_should_return_zero_when_empty()
    {
        var count = _peerDatabase.GetTotalTorrentCount();

        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public void GetTotalTorrentCount_should_count_active_torrents()
    {
        _peerDatabase.AddPeer("hash1", "192.168.1.1", 6881, "peer1");
        _peerDatabase.AddPeer("hash2", "192.168.1.2", 6882, "peer2");

        var count = _peerDatabase.GetTotalTorrentCount();

        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public void GetTotalTorrentCount_should_not_count_infohash_with_no_active_peers()
    {
        _peerDatabase.AddPeer("hash1", "192.168.1.1", 6881, "peer1");
        _peerDatabase.RemovePeer("hash1", "192.168.1.1", 6881);

        var count = _peerDatabase.GetTotalTorrentCount();

        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public void AddPeer_should_be_thread_safe()
    {
        var tasks = new List<Task>();

        for (var i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() => _peerDatabase.AddPeer("hash1", $"10.0.0.{index % 256}", 6881 + index, $"peer{index}")));
        }

        Task.WaitAll(tasks.ToArray());

        var peers = _peerDatabase.GetPeers("hash1");
        Assert.That(peers.Count, Is.EqualTo(100));
    }

    [Test]
    public void RemovePeer_should_be_thread_safe()
    {
        for (var i = 0; i < 50; i++)
        {
            _peerDatabase.AddPeer("hash1", $"10.0.0.{i}", 6881, $"peer{i}");
        }

        var tasks = new List<Task>();

        for (var i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() => _peerDatabase.RemovePeer("hash1", $"10.0.0.{index}", 6881)));
        }

        Task.WaitAll(tasks.ToArray());

        var peers = _peerDatabase.GetPeers("hash1");
        Assert.That(peers, Is.Empty);
    }

    [Test]
    public void Mixed_operations_should_be_thread_safe()
    {
        var tasks = new List<Task>();

        for (var i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                _peerDatabase.AddPeer("hash1", $"10.0.0.{index}", 6881, $"peer{index}");
                _peerDatabase.GetPeers("hash1");
                _peerDatabase.GetStats("hash1");
                _peerDatabase.GetAllInfoHashes();
                _peerDatabase.GetTotalPeerCount();
                _peerDatabase.GetTotalTorrentCount();
            }));
        }

        Assert.DoesNotThrow(() => Task.WaitAll(tasks.ToArray()));
    }

    [Test]
    public void AddPeer_should_set_last_announce_to_approximately_now()
    {
        var before = DateTime.UtcNow;
        _peerDatabase.AddPeer("abc123", "192.168.1.1", 6881, "peer1");
        var after = DateTime.UtcNow;

        var peers = _peerDatabase.GetPeers("abc123");

        Assert.That(peers[0].LastAnnounce, Is.GreaterThanOrEqualTo(before));
        Assert.That(peers[0].LastAnnounce, Is.LessThanOrEqualTo(after));
    }

    [Test]
    public void PruneStalePeers_should_remove_expired_peers_from_abandoned_swarms()
    {
        _peerDatabase.AddPeer("abandoned1", "192.168.1.1", 6881, "peer1");
        _peerDatabase.AddPeer("abandoned1", "192.168.1.2", 6882, "peer2");

        var peers = _peerDatabase.GetPeers("abandoned1");
        peers[0].LastAnnounce = DateTime.UtcNow.AddMinutes(-50);
        peers[1].LastAnnounce = DateTime.UtcNow.AddMinutes(-50);

        Assert.That(_peerDatabase.ContainsSwarm("abandoned1"), Is.True);

        var evictedCount = _peerDatabase.PruneStalePeers();

        Assert.That(evictedCount, Is.EqualTo(2));
        Assert.That(_peerDatabase.GetPeers("abandoned1"), Is.Empty);
    }

    [Test]
    public void PruneStalePeers_should_remove_empty_swarms_from_peers()
    {
        _peerDatabase.AddPeer("abandoned1", "192.168.1.1", 6881, "peer1");
        _peerDatabase.AddPeer("abandoned2", "192.168.1.2", 6882, "peer2");

        _peerDatabase.GetPeers("abandoned1")[0].LastAnnounce = DateTime.UtcNow.AddMinutes(-60);
        _peerDatabase.GetPeers("abandoned2")[0].LastAnnounce = DateTime.UtcNow.AddMinutes(-60);

        Assert.That(_peerDatabase.ContainsSwarm("abandoned1"), Is.True);
        Assert.That(_peerDatabase.ContainsSwarm("abandoned2"), Is.True);

        var evictedCount = _peerDatabase.PruneStalePeers();

        Assert.That(evictedCount, Is.EqualTo(2));
        Assert.That(_peerDatabase.ContainsSwarm("abandoned1"), Is.False);
        Assert.That(_peerDatabase.ContainsSwarm("abandoned2"), Is.False);
        Assert.That(_peerDatabase.GetAllInfoHashes(), Is.Empty);
    }

    [Test]
    public void PruneStalePeers_should_retain_active_peers_within_ttl()
    {
        _peerDatabase.AddPeer("swarm1", "192.168.1.1", 6881, "expired_peer");
        _peerDatabase.AddPeer("swarm1", "192.168.1.2", 6882, "active_peer");
        _peerDatabase.AddPeer("swarm2", "192.168.1.3", 6883, "another_active_peer");

        var peers = _peerDatabase.GetPeers("swarm1");
        peers.First(p => p.PeerId == "expired_peer").LastAnnounce = DateTime.UtcNow.AddMinutes(-50);

        var evictedCount = _peerDatabase.PruneStalePeers();

        Assert.That(evictedCount, Is.EqualTo(1));
        Assert.That(_peerDatabase.ContainsSwarm("swarm1"), Is.True);
        Assert.That(_peerDatabase.ContainsSwarm("swarm2"), Is.True);

        var swarm1Peers = _peerDatabase.GetPeers("swarm1");
        Assert.That(swarm1Peers, Has.Count.EqualTo(1));
        Assert.That(swarm1Peers[0].PeerId, Is.EqualTo("active_peer"));

        var swarm2Peers = _peerDatabase.GetPeers("swarm2");
        Assert.That(swarm2Peers, Has.Count.EqualTo(1));
        Assert.That(swarm2Peers[0].PeerId, Is.EqualTo("another_active_peer"));
    }

    [Test]
    public async Task Periodic_eviction_timer_should_evict_stale_peers_automatically()
    {
        using var db = new PeerDatabase(TimeSpan.FromMilliseconds(50));

        db.AddPeer("auto_prune_swarm", "192.168.1.1", 6881, "peer1");
        db.GetPeers("auto_prune_swarm")[0].LastAnnounce = DateTime.UtcNow.AddMinutes(-50);

        Assert.That(db.ContainsSwarm("auto_prune_swarm"), Is.True);

        var evicted = false;
        for (var i = 0; i < 20; i++)
        {
            if (!db.ContainsSwarm("auto_prune_swarm"))
            {
                evicted = true;
                break;
            }

            await Task.Delay(50);
        }

        Assert.That(evicted, Is.True);
        Assert.That(db.GetPeers("auto_prune_swarm"), Is.Empty);
    }

    [Test]
    public void Dispose_should_cleanly_dispose_eviction_timer()
    {
        var db = new PeerDatabase(TimeSpan.FromMinutes(5));

        Assert.DoesNotThrow(() => db.Dispose());
        Assert.DoesNotThrow(() => db.Dispose());

        Assert.That(db.PruneStalePeers(), Is.EqualTo(0));
    }

    [Test]
    public void AddPeer_should_track_seeder_when_left_is_zero()
    {
        _peerDatabase.AddPeer("hash1", "192.168.1.1", 6881, "peer1", 0);

        var stats = _peerDatabase.GetStats("hash1");
        var peers = _peerDatabase.GetPeers("hash1");

        Assert.That(stats.Complete, Is.EqualTo(1));
        Assert.That(stats.Incomplete, Is.EqualTo(0));
        Assert.That(peers[0].IsSeeder, Is.True);
    }

    [Test]
    public void AddPeer_should_track_leecher_when_left_is_greater_than_zero()
    {
        _peerDatabase.AddPeer("hash1", "192.168.1.1", 6881, "peer1", 1024);

        var stats = _peerDatabase.GetStats("hash1");
        var peers = _peerDatabase.GetPeers("hash1");

        Assert.That(stats.Complete, Is.EqualTo(0));
        Assert.That(stats.Incomplete, Is.EqualTo(1));
        Assert.That(peers[0].IsSeeder, Is.False);
        Assert.That(peers[0].Left, Is.EqualTo(1024));
    }

    [Test]
    public void AddPeer_should_transition_leecher_to_seeder_when_left_becomes_zero()
    {
        _peerDatabase.AddPeer("hash1", "192.168.1.1", 6881, "peer1", 1024);

        var statsBefore = _peerDatabase.GetStats("hash1");
        Assert.That(statsBefore.Complete, Is.EqualTo(0));
        Assert.That(statsBefore.Incomplete, Is.EqualTo(1));

        _peerDatabase.AddPeer("hash1", "192.168.1.1", 6881, "peer1", 0);

        var statsAfter = _peerDatabase.GetStats("hash1");
        Assert.That(statsAfter.Complete, Is.EqualTo(1));
        Assert.That(statsAfter.Incomplete, Is.EqualTo(0));
    }

    [Test]
    public void RecordCompleted_should_increment_downloaded_count()
    {
        _peerDatabase.RecordCompleted("hash1");
        _peerDatabase.RecordCompleted("hash1");

        var stats = _peerDatabase.GetStats("hash1");

        Assert.That(stats.Downloaded, Is.EqualTo(2));
    }

    [Test]
    public void AddPeer_with_completed_event_should_increment_downloaded_count()
    {
        _peerDatabase.AddPeer("hash1", "192.168.1.1", 6881, "peer1", 0, "completed");

        var stats = _peerDatabase.GetStats("hash1");

        Assert.That(stats.Downloaded, Is.EqualTo(1));
        Assert.That(stats.Complete, Is.EqualTo(1));
        Assert.That(stats.Incomplete, Is.EqualTo(0));
    }

    [Test]
    public void GetAllStats_should_distinguish_complete_incomplete_and_downloaded()
    {
        _peerDatabase.AddPeer("hash1", "192.168.1.1", 6881, "peer1", 0);
        _peerDatabase.AddPeer("hash1", "192.168.1.2", 6882, "peer2", 500);
        _peerDatabase.RecordCompleted("hash1");

        var allStats = _peerDatabase.GetAllStats();

        Assert.That(allStats, Does.ContainKey("hash1"));
        Assert.That(allStats["hash1"].Complete, Is.EqualTo(1));
        Assert.That(allStats["hash1"].Incomplete, Is.EqualTo(1));
        Assert.That(allStats["hash1"].Downloaded, Is.EqualTo(1));
    }
}
