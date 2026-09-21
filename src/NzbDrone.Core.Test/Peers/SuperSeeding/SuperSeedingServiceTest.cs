using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Peers.SuperSeeding;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Peers.SuperSeeding;

[TestFixture]
public class SuperSeedingServiceTest
{
    private ITorrentService _torrentService;
    private IConnectionManager _connectionManager;
    private ITorrentEventLogService _eventLogService;
    private IFastExtensionHandler _fastExtensionHandler;
    private IEventAggregator _eventAggregator;
    private SuperSeedingService _service;
    private List<PeerConnection> _connections;
    private List<TcpListener> _listeners;
    private List<TcpClient> _clients;

    [SetUp]
    public void SetUp()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _eventLogService = Substitute.For<ITorrentEventLogService>();
        _fastExtensionHandler = new FastExtensionHandler();
        _eventAggregator = Substitute.For<IEventAggregator>();

        _service = new SuperSeedingService(
            _torrentService,
            _connectionManager,
            _eventLogService,
            _fastExtensionHandler,
            _eventAggregator);

        _connections = new List<PeerConnection>();
        _listeners = new List<TcpListener>();
        _clients = new List<TcpClient>();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var conn in _connections)
        {
            try
            {
                conn.Dispose();
            }
            catch
            {
            }
        }

        foreach (var client in _clients)
        {
            try
            {
                client.Dispose();
            }
            catch
            {
            }
        }

        foreach (var listener in _listeners)
        {
            try
            {
                listener.Stop();
            }
            catch
            {
            }
        }
    }

    private (PeerConnection Client, PeerConnection Server) CreateTestPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listeners.Add(listener);

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var clientTcp = new TcpClient();
        clientTcp.Connect(IPAddress.Loopback, port);
        var serverTcp = listener.AcceptTcpClient();
        listener.Stop();

        var clientConn = new PeerConnection(clientTcp);
        var serverConn = new PeerConnection(serverTcp);
        _connections.Add(clientConn);
        _connections.Add(serverConn);
        return (clientConn, serverConn);
    }

    private static byte[] BuildRequestPayload(int index, int begin, int length)
    {
        var payload = new byte[12];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), index);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), begin);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(8, 4), length);
        return payload;
    }

    [Test]
    public void SendInitialAvailability_WhenSuperSeeding_AndFastExtensionSupported_SendsHaveNone()
    {
        var (clientConn, serverConn) = CreateTestPair();
        serverConn.SupportsFastExtension = true;

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            PieceCount = 20,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SuperSeeding = true
        };

        _service.SendInitialAvailability(serverConn, torrent);

        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.HaveNone));
    }

    [Test]
    public void SendInitialAvailability_WhenSuperSeeding_AndFastExtensionNotSupported_SendsEmptyBitfield()
    {
        var (clientConn, serverConn) = CreateTestPair();
        serverConn.SupportsFastExtension = false;

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            PieceCount = 16,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SuperSeeding = true
        };

        var serviceWithoutFast = new SuperSeedingService(
            _torrentService,
            _connectionManager,
            _eventLogService,
            fastExtensionHandler: null,
            eventAggregator: _eventAggregator);

        serviceWithoutFast.SendInitialAvailability(serverConn, torrent);

        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Bitfield));
        Assert.That(received.Payload, Is.Not.Null);
        Assert.That(received.Payload.Length, Is.EqualTo(2));
        Assert.That(received.Payload[0], Is.EqualTo(0));
        Assert.That(received.Payload[1], Is.EqualTo(0));
    }

    [Test]
    public void ProgressivePieceRevelation_OffersOnePieceAtATime()
    {
        var (clientConn, serverConn) = CreateTestPair();
        serverConn.AmChoking = false;

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            PieceCount = 10,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SuperSeeding = true
        };

        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { serverConn });

        // First allocation reveals piece 0
        var allocated = _service.AllocateAndRevealPiece(serverConn, torrent);
        Assert.That(allocated, Is.True);
        Assert.That(serverConn.AssignedSuperSeedingPiece, Is.EqualTo(0));

        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Have));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(received.Payload), Is.EqualTo(0));

        // Subsequent allocation attempt before piece 0 propagates MUST fail (at most one piece at a time)
        var secondAllocation = _service.AllocateAndRevealPiece(serverConn, torrent);
        Assert.That(secondAllocation, Is.False);
        Assert.That(serverConn.AssignedSuperSeedingPiece, Is.EqualTo(0));
    }

    [Test]
    public void IncomingHaveMessages_FromOtherPeers_ConfirmPieceDistribution_AndUnlockNewPieceOfferings()
    {
        var (client1, server1) = CreateTestPair();
        var (client2, server2) = CreateTestPair();
        server1.AmChoking = false;
        server2.AmChoking = false;

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            PieceCount = 10,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SuperSeeding = true
        };
        server1.MatchedTorrent = torrent;
        server2.MatchedTorrent = torrent;

        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { server1, server2 });

        // Initially offer piece 0 to server1
        _service.AllocateAndRevealPiece(server1, torrent);
        Assert.That(server1.AssignedSuperSeedingPiece, Is.EqualTo(0));
        var initialMsg = client1.ReceiveMessage();
        Assert.That(initialMsg?.Type, Is.EqualTo(PeerMessageType.Have));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(initialMsg.Payload), Is.EqualTo(0));

        // Server1 confirms it received piece 0 (uploaded to server1)
        _service.OnPeerHave(server1, torrent, 0);

        // Server1 should NOT be offered a new piece yet because piece 0 has not propagated to another peer
        Assert.That(server1.AssignedSuperSeedingPiece, Is.EqualTo(0));

        // Now Server2 (3rd-party peer) reports HAVE(0), confirming server1 shared piece 0 into the swarm!
        _service.OnPeerHave(server2, torrent, 0);

        // Now piece 0 is confirmed propagated! Server1 is unlocked and offered piece 1
        Assert.That(server1.AssignedSuperSeedingPiece, Is.EqualTo(1));

        var advanceMsg = client1.ReceiveMessage();
        Assert.That(advanceMsg, Is.Not.Null);
        Assert.That(advanceMsg.Type, Is.EqualTo(PeerMessageType.Have));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(advanceMsg.Payload), Is.EqualTo(1));
    }

    [Test]
    public void UnofferedPieceBlockRequests_AreRejectedWithRejectRequest_WhenFastSupported()
    {
        var (clientConn, serverConn) = CreateTestPair();
        serverConn.AmChoking = false;
        serverConn.SupportsFastExtension = true;
        serverConn.AssignedSuperSeedingPiece = 2;

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            PieceCount = 10,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SuperSeeding = true
        };

        // Peer requests piece 5 (which was not offered/assigned to it)
        var requestPayload = BuildRequestPayload(5, 0, 16384);
        var allowed = _service.IsRequestAllowed(serverConn, torrent, 5, requestPayload);

        Assert.That(allowed, Is.False);

        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.RejectRequest));
        var rejectedIndex = BinaryPrimitives.ReadInt32BigEndian(received.Payload.AsSpan(0, 4));
        Assert.That(rejectedIndex, Is.EqualTo(5));
    }

    [Test]
    public void UnofferedPieceBlockRequests_AreDropped_WhenFastNotSupported()
    {
        var (clientConn, serverConn) = CreateTestPair();
        serverConn.AmChoking = false;
        serverConn.SupportsFastExtension = false;
        serverConn.AssignedSuperSeedingPiece = 2;

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            PieceCount = 10,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SuperSeeding = true
        };

        var serviceWithoutFast = new SuperSeedingService(
            _torrentService,
            _connectionManager,
            _eventLogService,
            fastExtensionHandler: null,
            eventAggregator: _eventAggregator);

        // Peer requests piece 5 (unassigned) without fast extension
        var requestPayload = BuildRequestPayload(5, 0, 16384);
        var allowed = serviceWithoutFast.IsRequestAllowed(serverConn, torrent, 5, requestPayload);

        Assert.That(allowed, Is.False);

        // Client receives nothing; send KeepAlive to verify socket queue is clear
        serverConn.SendKeepAlive();
        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.KeepAlive));
    }

    [Test]
    public void AutomaticExit_WhenSecondarySeedConnects_WithHaveAll()
    {
        var (client1, server1) = CreateTestPair();
        server1.SupportsFastExtension = true;

        var torrent = new Torrent
        {
            Id = 42,
            Name = "TestTorrent",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            PieceCount = 10,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SuperSeeding = true
        };

        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { server1 });

        // Secondary seed announces HaveAll
        _service.OnPeerHaveAll(server1, torrent);

        // Verify SuperSeeding mode exited
        Assert.That(torrent.SuperSeeding, Is.False);
        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 42 && !t.SuperSeeding));
        _eventLogService.Received(1).Info(42, "SuperSeeding", Arg.Is<string>(s => s.Contains("secondary seed joined")));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<SuperSeedingExitedEvent>(e => e.Torrent.Id == 42 && e.Reason == "secondary seed joined"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentUpdatedEvent>(e => e.Torrent.Id == 42 && !e.Torrent.SuperSeeding));

        // Connected peers should receive full availability broadcast (HaveAll)
        var msg = client1.ReceiveMessage();
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg.Type, Is.EqualTo(PeerMessageType.HaveAll));
    }

    [Test]
    public void AutomaticExit_WhenSecondarySeedConnects_WithFullBitfield()
    {
        var (client1, server1) = CreateTestPair();
        server1.SupportsFastExtension = false;

        var torrent = new Torrent
        {
            Id = 43,
            Name = "BitfieldSeedTorrent",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            PieceCount = 4,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SuperSeeding = true
        };

        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { server1 });

        // Secondary seed announces bitfield with all 4 pieces
        var fullBitfield = new bool[] { true, true, true, true };
        server1.PeerPieces = fullBitfield;
        server1.HaveCount = 4;
        server1.Progress = 1.0;
        server1.IsSeed = true;

        _service.OnPeerBitfield(server1, torrent, fullBitfield);

        Assert.That(torrent.SuperSeeding, Is.False);
        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 43 && !t.SuperSeeding));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<SuperSeedingExitedEvent>(e => e.Torrent.Id == 43 && e.Reason == "secondary seed joined"));

        // Connected peer without fast extension receives full bitfield
        var msg = client1.ReceiveMessage();
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg.Type, Is.EqualTo(PeerMessageType.Bitfield));
        Assert.That(msg.Payload[0], Is.EqualTo(0xF0)); // 4 pieces = 11110000 = 0xF0
    }

    [Test]
    public void AutomaticExit_WhenSwarmAvailabilityReachesComplete()
    {
        var (client1, server1) = CreateTestPair();
        var (client2, server2) = CreateTestPair();
        server1.SupportsFastExtension = true;
        server2.SupportsFastExtension = true;

        var torrent = new Torrent
        {
            Id = 44,
            Name = "SwarmAvailTorrent",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            PieceCount = 4,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SuperSeeding = true
        };

        // Peer 1 has pieces [0, 1]. Peer 2 has pieces [2, 3].
        // Together, the union of peer bitfields covers 100% of the pieces!
        server1.PeerPieces = new bool[] { true, true, false, false };
        server1.HaveCount = 2;
        server1.Progress = 0.5;
        server1.IsSeed = false;

        server2.PeerPieces = new bool[] { false, false, true, true };
        server2.HaveCount = 2;
        server2.Progress = 0.5;
        server2.IsSeed = false;

        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { server1, server2 });

        // Peer 2 announces its bitfield, completing swarm availability >= 1.0
        _service.OnPeerBitfield(server2, torrent, server2.PeerPieces);

        Assert.That(torrent.SuperSeeding, Is.False);
        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 44 && !t.SuperSeeding));
        _eventLogService.Received(1).Info(44, "SuperSeeding", Arg.Is<string>(s => s.Contains("swarm availability >= 1.0")));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<SuperSeedingExitedEvent>(e => e.Torrent.Id == 44 && e.Reason == "swarm availability >= 1.0"));

        // Both peers receive HaveAll broadcast
        var msg1 = client1.ReceiveMessage();
        var msg2 = client2.ReceiveMessage();
        Assert.That(msg1?.Type, Is.EqualTo(PeerMessageType.HaveAll));
        Assert.That(msg2?.Type, Is.EqualTo(PeerMessageType.HaveAll));
    }
}
