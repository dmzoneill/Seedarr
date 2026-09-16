using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class ChokeManagerTest
{
    private IConnectionManager _connectionManager;
    private IConfigService _configService;
    private ChokeManager _subject;
    private List<PeerConnection> _connections;

    [SetUp]
    public void SetUp()
    {
        _connectionManager = Substitute.For<IConnectionManager>();
        _configService = Substitute.For<IConfigService>();
        _connections = new List<PeerConnection>();

        _connectionManager.GetAllConnections().Returns(_connections);
        _configService.MaxUploadSlots.Returns(4);

        _subject = new ChokeManager(_connectionManager, _configService);
    }

    private PeerConnection CreatePeer(string infoHash, int port, bool interested = true, long rate = 1000)
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", port);
        typeof(PeerConnection).GetProperty("InfoHash")?.SetValue(conn, infoHash);
        conn.PeerInterested = interested;
        conn.AmChoking = true;
        conn.UploadRate = rate;
        _connections.Add(conn);
        return conn;
    }

    [Test]
    public void ProcessRegularUnchoke_should_allocate_slots_per_torrent_swarm()
    {
        // 2 torrents, each with 5 interested peers
        // MaxUploadSlots = 4 => regularSlotCount = 3 per torrent
        for (var i = 1; i <= 5; i++)
        {
            CreatePeer("hashA", 1000 + i, rate: i * 100);
            CreatePeer("hashB", 2000 + i, rate: i * 100);
        }

        _subject.ProcessRegularUnchoke();

        var unchokedA = _connections.Where(c => c.InfoHash == "hashA" && !c.AmChoking).ToList();
        var unchokedB = _connections.Where(c => c.InfoHash == "hashB" && !c.AmChoking).ToList();

        // Each torrent should have exactly 3 regular unchoked slots allocated
        Assert.That(unchokedA.Count, Is.EqualTo(3));
        Assert.That(unchokedB.Count, Is.EqualTo(3));

        // The highest upload rate peers should be selected for each torrent
        Assert.That(unchokedA.Select(c => c.RemotePort), Is.EquivalentTo(new[] { 1003, 1004, 1005 }));
        Assert.That(unchokedB.Select(c => c.RemotePort), Is.EquivalentTo(new[] { 2003, 2004, 2005 }));
    }

    [Test]
    public void ProcessOptimisticUnchoke_should_unchoke_one_choked_peer_per_torrent()
    {
        // MaxUploadSlots = 4 => 3 regular + 1 optimistic per torrent
        for (var i = 1; i <= 5; i++)
        {
            CreatePeer("hashA", 1000 + i, rate: i * 100);
            CreatePeer("hashB", 2000 + i, rate: i * 100);
        }

        _subject.ProcessRegularUnchoke();
        _subject.ProcessOptimisticUnchoke();

        var unchokedA = _connections.Where(c => c.InfoHash == "hashA" && !c.AmChoking).ToList();
        var unchokedB = _connections.Where(c => c.InfoHash == "hashB" && !c.AmChoking).ToList();

        // 3 regular + 1 optimistic = 4 per torrent
        Assert.That(unchokedA.Count, Is.EqualTo(4));
        Assert.That(unchokedB.Count, Is.EqualTo(4));

        Assert.That(unchokedA.Count(c => c.IsOptimisticUnchoked), Is.EqualTo(1));
        Assert.That(unchokedB.Count(c => c.IsOptimisticUnchoked), Is.EqualTo(1));
    }

    [Test]
    public void CanUnchoke_should_check_slots_for_specific_torrent_swarm()
    {
        _configService.MaxUploadSlots.Returns(2);

        var connA1 = CreatePeer("hashA", 1001);
        connA1.AmChoking = false;

        var connA2 = CreatePeer("hashA", 1002);
        connA2.AmChoking = false;

        var connA3 = CreatePeer("hashA", 1003);
        connA3.AmChoking = true;

        var connB1 = CreatePeer("hashB", 2001);
        connB1.AmChoking = true;

        // hashA already has 2 unchoked peers (limit 2)
        Assert.That(_subject.CanUnchoke(connA3), Is.False);

        // hashB has 0 unchoked peers, so it can unchoke
        Assert.That(_subject.CanUnchoke(connB1), Is.True);
    }

    [Test]
    public void CanUnchoke_should_return_false_when_infohash_is_empty()
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 3000);
        Assert.That(_subject.CanUnchoke(conn), Is.False);
    }

    [Test]
    public void ProcessRegularUnchoke_should_unchoke_all_interested_when_slots_is_zero()
    {
        _configService.MaxUploadSlots.Returns(0);

        CreatePeer("hashA", 1001);
        CreatePeer("hashA", 1002);
        CreatePeer("hashB", 2001);

        _subject.ProcessRegularUnchoke();

        Assert.That(_connections.All(c => !c.AmChoking), Is.True);
    }
}
