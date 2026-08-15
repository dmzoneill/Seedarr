using System.Net;
using NUnit.Framework;
using NzbDrone.Core.Dht;

namespace NzbDrone.Core.Test.Dht;

[TestFixture]
public class DhtPeerStoreTest
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
        var shortTtlStore = new DhtPeerStore(0);
        shortTtlStore.AddPeer(_infoHash, IPAddress.Parse("192.168.1.1"), 6881);

        Assert.That(shortTtlStore.HasPeers(_infoHash), Is.False);
    }

    [Test]
    public void GetPeers_should_not_return_expired_peers()
    {
        var shortTtlStore = new DhtPeerStore(0);
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
}
