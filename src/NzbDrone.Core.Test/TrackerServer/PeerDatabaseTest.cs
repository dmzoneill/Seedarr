using System;
using System.Collections.Generic;
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
        Assert.That(stats.Downloaded, Is.EqualTo(2));
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
}
