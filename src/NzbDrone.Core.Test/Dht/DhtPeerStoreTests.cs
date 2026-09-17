using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NUnit.Framework;
using NzbDrone.Core.Dht;

namespace NzbDrone.Core.Test.Dht;

[TestFixture]
public class DhtPeerStoreTests
{
    private DhtPeerStore _store;
    private byte[] _infoHash;

    [SetUp]
    public void Setup()
    {
        _store = new DhtPeerStore(30);
        _infoHash = new byte[20];
        _infoHash[0] = 0xAB;
        _infoHash[1] = 0xCD;
    }

    [TearDown]
    public void TearDown()
    {
        _store?.Dispose();
    }

    [Test]
    public void AddPeer_should_add_new_peer()
    {
        _store.AddPeer(_infoHash, IPAddress.Parse("192.168.1.1"), 6881);

        Assert.That(_store.HasPeers(_infoHash), Is.True);
    }

    [Test]
    public void AddPeer_should_update_existing_peer_last_seen()
    {
        _store.AddPeer(_infoHash, IPAddress.Parse("192.168.1.1"), 6881);
        _store.AddPeer(_infoHash, IPAddress.Parse("192.168.1.1"), 6881);

        var peers = _store.GetPeers(_infoHash);
        Assert.That(peers.Count, Is.EqualTo(1));
    }

    [Test]
    public void AddPeer_should_allow_different_peers_for_same_hash()
    {
        _store.AddPeer(_infoHash, IPAddress.Parse("192.168.1.1"), 6881);
        _store.AddPeer(_infoHash, IPAddress.Parse("192.168.1.2"), 6882);

        var peers = _store.GetPeers(_infoHash);
        Assert.That(peers.Count, Is.EqualTo(2));
    }

    [Test]
    public void AddPeer_should_allow_same_ip_different_port()
    {
        _store.AddPeer(_infoHash, IPAddress.Parse("192.168.1.1"), 6881);
        _store.AddPeer(_infoHash, IPAddress.Parse("192.168.1.1"), 6882);

        var peers = _store.GetPeers(_infoHash);
        Assert.That(peers.Count, Is.EqualTo(2));
    }

    [Test]
    public void GetPeers_should_return_empty_for_unknown_hash()
    {
        var unknownHash = new byte[20];
        unknownHash[0] = 0xFF;

        var peers = _store.GetPeers(unknownHash);

        Assert.That(peers, Is.Empty);
    }

    [Test]
    public void GetPeers_should_return_compact_peer_format()
    {
        _store.AddPeer(_infoHash, IPAddress.Parse("192.168.1.1"), 6881);

        var peers = _store.GetPeers(_infoHash);

        Assert.That(peers.Count, Is.EqualTo(1));
        Assert.That(peers[0].Length, Is.EqualTo(6));
    }

    [Test]
    public void GetPeers_should_encode_ip_correctly()
    {
        _store.AddPeer(_infoHash, IPAddress.Parse("10.20.30.40"), 6881);

        var peers = _store.GetPeers(_infoHash);

        Assert.That(peers[0][0], Is.EqualTo(10));
        Assert.That(peers[0][1], Is.EqualTo(20));
        Assert.That(peers[0][2], Is.EqualTo(30));
        Assert.That(peers[0][3], Is.EqualTo(40));
    }

    [Test]
    public void GetPeers_should_encode_port_big_endian()
    {
        _store.AddPeer(_infoHash, IPAddress.Parse("10.0.0.1"), 0x1A2B);

        var peers = _store.GetPeers(_infoHash);

        Assert.That(peers[0][4], Is.EqualTo(0x1A));
        Assert.That(peers[0][5], Is.EqualTo(0x2B));
    }

    [Test]
    public void HasPeers_should_return_false_for_unknown_hash()
    {
        var unknownHash = new byte[20];

        Assert.That(_store.HasPeers(unknownHash), Is.False);
    }

    [Test]
    public void HasPeers_should_return_true_when_peers_exist()
    {
        _store.AddPeer(_infoHash, IPAddress.Parse("192.168.1.1"), 6881);

        Assert.That(_store.HasPeers(_infoHash), Is.True);
    }

    [Test]
    public void AddPeer_should_remove_expired_peers_during_add()
    {
        using var shortTtlStore = new DhtPeerStore(0);
        shortTtlStore.AddPeer(_infoHash, IPAddress.Parse("192.168.1.1"), 6881);

        Assert.That(shortTtlStore.HasPeers(_infoHash), Is.False);
    }

    [Test]
    public void GetPeers_should_not_return_expired_peers()
    {
        using var shortTtlStore = new DhtPeerStore(0);
        shortTtlStore.AddPeer(_infoHash, IPAddress.Parse("192.168.1.1"), 6881);

        var peers = shortTtlStore.GetPeers(_infoHash);

        Assert.That(peers, Is.Empty);
    }

    [Test]
    public void AddPeer_should_handle_multiple_info_hashes()
    {
        var hash2 = new byte[20];
        hash2[0] = 0xFF;

        _store.AddPeer(_infoHash, IPAddress.Parse("10.0.0.1"), 6881);
        _store.AddPeer(hash2, IPAddress.Parse("10.0.0.2"), 6882);

        Assert.That(_store.HasPeers(_infoHash), Is.True);
        Assert.That(_store.HasPeers(hash2), Is.True);
    }

    [Test]
    public void GetPeers_should_encode_port_256_correctly()
    {
        _store.AddPeer(_infoHash, IPAddress.Parse("10.0.0.1"), 256);

        var peers = _store.GetPeers(_infoHash);

        Assert.That(peers[0][4], Is.EqualTo(1));
        Assert.That(peers[0][5], Is.EqualTo(0));
    }

    [Test]
    public void GetPeers_should_encode_max_port_correctly()
    {
        _store.AddPeer(_infoHash, IPAddress.Parse("10.0.0.1"), 65535);

        var peers = _store.GetPeers(_infoHash);

        Assert.That(peers[0][4], Is.EqualTo(0xFF));
        Assert.That(peers[0][5], Is.EqualTo(0xFF));
    }

    // ── Strict Bounds on Peer Storage (MaxPeersPerInfoHash = 100) ────

    [Test]
    public void AddPeer_should_enforce_max_100_peers_per_infohash_and_evict_oldest_peer()
    {
        var currentTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using var store = new DhtPeerStore(30, timeProvider: () => currentTime);

        for (var i = 1; i <= 100; i++)
        {
            currentTime = currentTime.AddSeconds(1);
            store.AddPeer(_infoHash, IPAddress.Parse($"10.0.0.{(i % 250) + 1}"), 5000 + i);
        }

        Assert.That(store.GetPeerCount(_infoHash), Is.EqualTo(100));
        Assert.That(store.GetPeers(_infoHash).Count, Is.EqualTo(100));

        var oldestPeerEncoded = DhtPeerStore.EncodeCompactPeer(IPAddress.Parse("10.0.0.2"), 5001);
        var existingPeers = store.GetPeers(_infoHash);
        Assert.That(existingPeers.Any(p => p.SequenceEqual(oldestPeerEncoded)), Is.True);

        currentTime = currentTime.AddSeconds(1);
        var newPeerAddress = IPAddress.Parse("10.1.1.1");
        var newPeerPort = 9000;
        store.AddPeer(_infoHash, newPeerAddress, newPeerPort);

        Assert.That(store.GetPeerCount(_infoHash), Is.EqualTo(100));
        var updatedPeers = store.GetPeers(_infoHash);
        Assert.That(updatedPeers.Count, Is.EqualTo(100));

        Assert.That(updatedPeers.Any(p => p.SequenceEqual(oldestPeerEncoded)), Is.False);

        var newPeerEncoded = DhtPeerStore.EncodeCompactPeer(newPeerAddress, newPeerPort);
        Assert.That(updatedPeers.Any(p => p.SequenceEqual(newPeerEncoded)), Is.True);
    }

    [Test]
    public void AddPeer_reannouncing_existing_peer_should_refresh_last_seen_without_eviction()
    {
        var currentTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using var store = new DhtPeerStore(30, maxPeersPerInfoHash: 2, timeProvider: () => currentTime);

        currentTime = currentTime.AddSeconds(1);
        var peer1 = IPAddress.Parse("10.0.0.1");
        store.AddPeer(_infoHash, peer1, 5001);

        currentTime = currentTime.AddSeconds(1);
        var peer2 = IPAddress.Parse("10.0.0.2");
        store.AddPeer(_infoHash, peer2, 5002);

        currentTime = currentTime.AddSeconds(5);
        store.AddPeer(_infoHash, peer1, 5001);

        Assert.That(store.GetPeerCount(_infoHash), Is.EqualTo(2));

        currentTime = currentTime.AddSeconds(1);
        var peer3 = IPAddress.Parse("10.0.0.3");
        store.AddPeer(_infoHash, peer3, 5003);

        var peers = store.GetPeers(_infoHash);
        Assert.That(peers.Count, Is.EqualTo(2));

        var peer1Encoded = DhtPeerStore.EncodeCompactPeer(peer1, 5001);
        var peer2Encoded = DhtPeerStore.EncodeCompactPeer(peer2, 5002);
        var peer3Encoded = DhtPeerStore.EncodeCompactPeer(peer3, 5003);

        Assert.That(peers.Any(p => p.SequenceEqual(peer1Encoded)), Is.True);
        Assert.That(peers.Any(p => p.SequenceEqual(peer3Encoded)), Is.True);
        Assert.That(peers.Any(p => p.SequenceEqual(peer2Encoded)), Is.False);
    }

    // ── Strict Bounds on Info-Hashes (MaxInfoHashes = 5000 & LRU Eviction) ─

    [Test]
    public void AddPeer_should_enforce_max_5000_infohashes_cap_and_evict_lru_infohash()
    {
        using var store = new DhtPeerStore(30);
        var peerIp = IPAddress.Parse("192.168.1.1");

        var hashes = new List<byte[]>(5001);
        for (var i = 0; i < 5001; i++)
        {
            var hash = new byte[20];
            hash[0] = (byte)(i >> 8);
            hash[1] = (byte)i;
            hashes.Add(hash);
        }

        for (var i = 0; i < 5000; i++)
        {
            store.AddPeer(hashes[i], peerIp, 6881);
        }

        Assert.That(store.InfoHashCount, Is.EqualTo(5000));
        Assert.That(store.HasPeers(hashes[0]), Is.True);
        Assert.That(store.HasPeers(hashes[1]), Is.True);

        store.GetPeers(hashes[0]);

        store.AddPeer(hashes[5000], peerIp, 6881);

        Assert.That(store.InfoHashCount, Is.EqualTo(5000));
        Assert.That(store.HasPeers(hashes[1]), Is.False);
        Assert.That(store.HasPeers(hashes[0]), Is.True);
        Assert.That(store.HasPeers(hashes[5000]), Is.True);
    }

    [Test]
    public void AddPeer_with_custom_cap_should_evict_least_recently_used_infohash()
    {
        using var store = new DhtPeerStore(30, maxInfoHashes: 3);
        var peerIp = IPAddress.Parse("10.0.0.1");

        var hashA = new byte[20];
        hashA[0] = 0x01;
        var hashB = new byte[20];
        hashB[0] = 0x02;
        var hashC = new byte[20];
        hashC[0] = 0x03;
        var hashD = new byte[20];
        hashD[0] = 0x04;

        store.AddPeer(hashA, peerIp, 6881);
        store.AddPeer(hashB, peerIp, 6881);
        store.AddPeer(hashC, peerIp, 6881);

        Assert.That(store.InfoHashCount, Is.EqualTo(3));

        store.GetPeers(hashA);

        store.AddPeer(hashD, peerIp, 6881);

        Assert.That(store.InfoHashCount, Is.EqualTo(3));
        Assert.That(store.HasPeers(hashB), Is.False);
        Assert.That(store.HasPeers(hashA), Is.True);
        Assert.That(store.HasPeers(hashC), Is.True);
        Assert.That(store.HasPeers(hashD), Is.True);
    }

    // ── Dual-Stack Compact Peer Encoding (BEP 32) ────────────────────

    [Test]
    public void EncodeCompactPeer_should_produce_18_byte_representation_for_ipv6()
    {
        var ipv6 = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7334");
        var port = 0x1A2B;

        var encoded = DhtPeerStore.EncodeCompactPeer(ipv6, port);

        Assert.That(encoded, Is.Not.Null);
        Assert.That(encoded.Length, Is.EqualTo(18));

        var ipBytes = ipv6.GetAddressBytes();
        Assert.That(encoded.Take(16).ToArray(), Is.EqualTo(ipBytes));
        Assert.That(encoded[16], Is.EqualTo(0x1A));
        Assert.That(encoded[17], Is.EqualTo(0x2B));
    }

    [Test]
    public void EncodeCompactPeerIPv6_should_return_null_for_ipv4()
    {
        var ipv4 = IPAddress.Parse("192.168.1.1");
        var result = DhtPeerStore.EncodeCompactPeerIPv6(ipv4, 6881);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void EncodeCompactPeerIPv4_should_return_null_for_ipv6()
    {
        var ipv6 = IPAddress.Parse("fe80::1");
        var result = DhtPeerStore.EncodeCompactPeerIPv4(ipv6, 6881);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetPeers_and_GetPeers6_should_separate_dual_stack_peers()
    {
        var ipv4 = IPAddress.Parse("192.168.1.100");
        var ipv6 = IPAddress.Parse("2001:db8::1");

        _store.AddPeer(_infoHash, ipv4, 6881);
        _store.AddPeer(_infoHash, ipv6, 6882);

        var peers4 = _store.GetPeers(_infoHash);
        var peers6 = _store.GetPeers6(_infoHash);

        Assert.That(peers4.Count, Is.EqualTo(1));
        Assert.That(peers4[0].Length, Is.EqualTo(6));

        Assert.That(peers6.Count, Is.EqualTo(1));
        Assert.That(peers6[0].Length, Is.EqualTo(18));

        var (v4, v6) = _store.GetDualStackPeers(_infoHash);
        Assert.That(v4.Count, Is.EqualTo(1));
        Assert.That(v6.Count, Is.EqualTo(1));
    }

    // ── Peer TTL Expiration and Decoupled Cleanup ────────────────────

    [Test]
    public void CleanupExpiredPeers_should_purge_expired_peers_and_empty_swarms()
    {
        var currentTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using var store = new DhtPeerStore(30, timeProvider: () => currentTime);

        store.AddPeer(_infoHash, IPAddress.Parse("192.168.1.1"), 6881);
        Assert.That(store.HasPeers(_infoHash), Is.True);
        Assert.That(store.GetPeers(_infoHash).Count, Is.EqualTo(1));

        currentTime = currentTime.AddMinutes(31);

        Assert.That(store.HasPeers(_infoHash), Is.False);
        Assert.That(store.GetPeers(_infoHash), Is.Empty);

        store.CleanupExpiredPeers();
        Assert.That(store.InfoHashCount, Is.EqualTo(0));
    }

    [Test]
    public void CleanupExpiredPeers_should_only_remove_expired_peers_leaving_active_peers()
    {
        var currentTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using var store = new DhtPeerStore(30, timeProvider: () => currentTime);

        var peer1 = IPAddress.Parse("10.0.0.1");
        store.AddPeer(_infoHash, peer1, 6881);

        currentTime = currentTime.AddMinutes(20);
        var peer2 = IPAddress.Parse("10.0.0.2");
        store.AddPeer(_infoHash, peer2, 6882);

        currentTime = currentTime.AddMinutes(15);

        store.CleanupExpiredPeers();

        Assert.That(store.InfoHashCount, Is.EqualTo(1));
        var remaining = store.GetPeers(_infoHash);
        Assert.That(remaining.Count, Is.EqualTo(1));
        var peer2Encoded = DhtPeerStore.EncodeCompactPeer(peer2, 6882);
        Assert.That(remaining[0].SequenceEqual(peer2Encoded), Is.True);
    }
}
