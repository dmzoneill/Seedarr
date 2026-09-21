using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers.MultiTracker;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class SuperSeedingTests
{
    private IConfigService _configService;
    private ITorrentService _torrentService;
    private IConnectionManager _connectionManager;
    private IPeerDiscoveryService _peerDiscovery;
    private IMultiTrackerManager _multiTracker;
    private IFastExtensionHandler _fastExtensionHandler;
    private IEventAggregator _eventAggregator;
    private PeerServer _server;
    private List<PeerConnection> _connections;
    private List<TcpListener> _listeners;
    private List<TcpClient> _clients;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _torrentService = Substitute.For<ITorrentService>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        _multiTracker = Substitute.For<IMultiTrackerManager>();
        _fastExtensionHandler = new FastExtensionHandler();
        _eventAggregator = Substitute.For<IEventAggregator>();

        _configService.MaxGlobalConnections.Returns(200);
        _configService.ListeningPort.Returns(0);
        _configService.EncryptionMode.Returns("enabled");
        _configService.HandshakeTimeoutSeconds.Returns(30);
        _configService.MessageReadTimeoutSeconds.Returns(60);
        _configService.KeepAliveIntervalSeconds.Returns(120);
        _configService.PeerRequestCount.Returns(200);
        _configService.PeerIdleChance.Returns(0.0);
        _configService.PeerContactIntervalSeconds.Returns(300);
        _configService.PexMaxPeersPerMessage.Returns(50);

        _server = new PeerServer(
            _configService,
            _torrentService,
            _connectionManager,
            _peerDiscovery,
            _multiTracker,
            fastExtensionHandler: _fastExtensionHandler,
            eventAggregator: _eventAggregator);

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

        _server?.Dispose();
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
    public void SendInitialAvailability_when_super_seeding_and_fast_extension_supported_sends_HaveNone()
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

        _server.SendInitialAvailability(serverConn, torrent);

        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.HaveNone));
    }

    [Test]
    public void SendInitialAvailability_when_super_seeding_and_fast_extension_not_supported_sends_empty_bitfield()
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

        // Create PeerServer without fast extension handler so it falls back to empty bitfield
        var serverWithoutFast = new PeerServer(
            _configService,
            _torrentService,
            _connectionManager,
            _peerDiscovery,
            _multiTracker,
            fastExtensionHandler: null);

        serverWithoutFast.SendInitialAvailability(serverConn, torrent);

        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Bitfield));
        Assert.That(received.Payload, Is.Not.Null);
        Assert.That(received.Payload.Length, Is.EqualTo(2));
        Assert.That(received.Payload[0], Is.EqualTo(0));
        Assert.That(received.Payload[1], Is.EqualTo(0));

        serverWithoutFast.Dispose();
    }

    [Test]
    public void SendInitialAvailability_when_not_super_seeding_sends_HaveAll()
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
            SuperSeeding = false
        };

        _server.SendInitialAvailability(serverConn, torrent);

        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.HaveAll));
    }

    [Test]
    public void AllocateAndRevealSuperSeedingPiece_allocates_piece_and_sends_Have_to_unchoked_peer()
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

        var allocated = _server.AllocateAndRevealSuperSeedingPiece(serverConn, torrent);

        Assert.That(allocated, Is.True);
        Assert.That(serverConn.AssignedSuperSeedingPiece, Is.EqualTo(0));

        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Have));
        Assert.That(received.Payload, Is.Not.Null);
        var revealedPiece = BinaryPrimitives.ReadInt32BigEndian(received.Payload);
        Assert.That(revealedPiece, Is.EqualTo(0));
    }

    [Test]
    public void AllocateAndRevealSuperSeedingPiece_allocates_distinct_pieces_to_multiple_unchoked_peers()
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

        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { server1, server2 });

        var allocated1 = _server.AllocateAndRevealSuperSeedingPiece(server1, torrent);
        var allocated2 = _server.AllocateAndRevealSuperSeedingPiece(server2, torrent);

        Assert.That(allocated1, Is.True);
        Assert.That(allocated2, Is.True);
        Assert.That(server1.AssignedSuperSeedingPiece, Is.EqualTo(0));
        Assert.That(server2.AssignedSuperSeedingPiece, Is.EqualTo(1));
        Assert.That(server1.AssignedSuperSeedingPiece, Is.Not.EqualTo(server2.AssignedSuperSeedingPiece));

        var msg1 = client1.ReceiveMessage();
        var msg2 = client2.ReceiveMessage();

        Assert.That(msg1?.Type, Is.EqualTo(PeerMessageType.Have));
        Assert.That(msg2?.Type, Is.EqualTo(PeerMessageType.Have));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(msg1.Payload), Is.EqualTo(0));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(msg2.Payload), Is.EqualTo(1));
    }

    [Test]
    public void AllocateAndRevealSuperSeedingPiece_does_not_allocate_to_choked_peer()
    {
        var (clientConn, serverConn) = CreateTestPair();
        serverConn.AmChoking = true;

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            PieceCount = 10,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SuperSeeding = true
        };

        var allocated = _server.AllocateAndRevealSuperSeedingPiece(serverConn, torrent);

        Assert.That(allocated, Is.False);
        Assert.That(serverConn.AssignedSuperSeedingPiece, Is.Null);
    }

    [Test]
    public void HandleMessage_request_matching_assigned_piece_is_accepted_and_fulfilled()
    {
        var (clientConn, serverConn) = CreateTestPair();
        serverConn.AmChoking = false;
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
        serverConn.MatchedTorrent = torrent;

        var requestPayload = BuildRequestPayload(2, 0, 16384);
        var requestMsg = new PeerMessage
        {
            Type = PeerMessageType.Request,
            Payload = requestPayload,
            PayloadLength = requestPayload.Length
        };

        _server.HandleMessage(serverConn, requestMsg, torrent);

        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Piece));
        Assert.That(received.Payload, Is.Not.Null);
        var pieceIndex = BinaryPrimitives.ReadInt32BigEndian(received.Payload.AsSpan(0, 4));
        var begin = BinaryPrimitives.ReadInt32BigEndian(received.Payload.AsSpan(4, 4));
        Assert.That(pieceIndex, Is.EqualTo(2));
        Assert.That(begin, Is.EqualTo(0));
    }

    [Test]
    public void HandleMessage_request_for_unassigned_piece_is_rejected_with_RejectRequest_when_fast_supported()
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
        serverConn.MatchedTorrent = torrent;

        // Peer requests piece 5 which was NOT assigned to it
        var requestPayload = BuildRequestPayload(5, 0, 16384);
        var requestMsg = new PeerMessage
        {
            Type = PeerMessageType.Request,
            Payload = requestPayload,
            PayloadLength = requestPayload.Length
        };

        _server.HandleMessage(serverConn, requestMsg, torrent);

        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.RejectRequest));
        Assert.That(received.Payload, Is.Not.Null);
        var rejectedIndex = BinaryPrimitives.ReadInt32BigEndian(received.Payload.AsSpan(0, 4));
        Assert.That(rejectedIndex, Is.EqualTo(5));
    }

    [Test]
    public void HandleMessage_request_for_unassigned_piece_is_dropped_when_fast_not_supported()
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
        serverConn.MatchedTorrent = torrent;

        var serverWithoutFast = new PeerServer(
            _configService,
            _torrentService,
            _connectionManager,
            _peerDiscovery,
            _multiTracker,
            fastExtensionHandler: null);

        var requestPayload = BuildRequestPayload(5, 0, 16384);
        var requestMsg = new PeerMessage
        {
            Type = PeerMessageType.Request,
            Payload = requestPayload,
            PayloadLength = requestPayload.Length
        };

        serverWithoutFast.HandleMessage(serverConn, requestMsg, torrent);

        // Client should not receive anything because request was dropped
        // Send a NotInterested message from server to prove no earlier Piece or RejectRequest was sent
        serverConn.SendMessage(new PeerMessage { Type = PeerMessageType.NotInterested });
        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.NotInterested));

        serverWithoutFast.Dispose();
    }

    [Test]
    public void HandleMessage_have_from_peer_advances_peer_to_next_piece_only_after_propagation()
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

        // Initially allocate piece 0 to server1
        _server.AllocateAndRevealSuperSeedingPiece(server1, torrent);
        Assert.That(server1.AssignedSuperSeedingPiece, Is.EqualTo(0));
        var initialMsg = client1.ReceiveMessage();
        Assert.That(initialMsg?.Type, Is.EqualTo(PeerMessageType.Have));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(initialMsg.Payload), Is.EqualTo(0));

        // Server1 announces HAVE(0) (reporting it has downloaded it)
        var have0Payload = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(have0Payload, 0);
        var have0Msg = new PeerMessage
        {
            Type = PeerMessageType.Have,
            Payload = have0Payload,
            PayloadLength = 4
        };

        _server.HandleMessage(server1, have0Msg, torrent);

        // Server1 should NOT be advanced yet because piece 0 has not propagated to any other peer
        Assert.That(server1.AssignedSuperSeedingPiece, Is.EqualTo(0));

        // Now Server2 (a secondary peer in the swarm) announces HAVE(0)
        _server.HandleMessage(server2, have0Msg, torrent);

        // Now that Server2 announced HAVE(0), piece 0 has propagated!
        // Server1 should be advanced to its next piece (piece 1)
        Assert.That(server1.AssignedSuperSeedingPiece, Is.EqualTo(1));

        var advanceMsg = client1.ReceiveMessage();
        Assert.That(advanceMsg, Is.Not.Null);
        Assert.That(advanceMsg.Type, Is.EqualTo(PeerMessageType.Have));
        var nextPiece = BinaryPrimitives.ReadInt32BigEndian(advanceMsg.Payload);
        Assert.That(nextPiece, Is.EqualTo(1));
    }

    [Test]
    public void CheckSuperSeedingTimeouts_chokes_selfish_peer_and_reassigns_piece()
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

        // Allocate piece 0 to server1
        _server.AllocateAndRevealSuperSeedingPiece(server1, torrent);
        Assert.That(server1.AssignedSuperSeedingPiece, Is.EqualTo(0));
        _ = client1.ReceiveMessage(); // Consume HAVE(0)

        // Server1 downloads piece 0
        var tracker = _server.GetSuperSeedingTracker(torrent.InfoHash);
        var startTime = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
        tracker.RecordPieceUploaded(server1.PeerId ?? $"{server1.RemoteIp}:{server1.RemotePort}", 0, startTime);

        // Trigger timeout at 95 seconds
        var timeouts = _server.CheckSuperSeedingTimeouts(torrent, startTime.AddSeconds(95));
        Assert.That(timeouts.Count, Is.EqualTo(1));
        Assert.That(timeouts[0].PieceIndex, Is.EqualTo(0));

        // Server1 should now be choked, and piece 0 reassigned to server2
        Assert.That(server1.AmChoking, Is.True);
        Assert.That(server1.AssignedSuperSeedingPiece, Is.Null);
        Assert.That(server2.AssignedSuperSeedingPiece, Is.EqualTo(0));

        var chokeMsg = client1.ReceiveMessage();
        Assert.That(chokeMsg, Is.Not.Null);
        Assert.That(chokeMsg.Type, Is.EqualTo(PeerMessageType.Choke));

        var haveMsg = client2.ReceiveMessage();
        Assert.That(haveMsg, Is.Not.Null);
        Assert.That(haveMsg.Type, Is.EqualTo(PeerMessageType.Have));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(haveMsg.Payload), Is.EqualTo(0));
    }

    [Test]
    public void HandleMessage_HaveAll_from_secondary_seed_triggers_automatic_exit_of_super_seeding()
    {
        var (client1, server1) = CreateTestPair();
        server1.SupportsFastExtension = true;

        var torrent = new Torrent
        {
            Id = 10,
            Name = "HaveAllExitTorrent",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            PieceCount = 8,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SuperSeeding = true
        };
        server1.MatchedTorrent = torrent;

        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { server1 });

        var haveAllMsg = new PeerMessage { Type = PeerMessageType.HaveAll };
        _server.HandleMessage(server1, haveAllMsg, torrent);

        Assert.That(torrent.SuperSeeding, Is.False);
        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 10 && !t.SuperSeeding));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<SuperSeedingExitedEvent>(e => e.Torrent.Id == 10 && e.Reason == "secondary seed joined"));

        var msg = client1.ReceiveMessage();
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg.Type, Is.EqualTo(PeerMessageType.HaveAll));
    }

    [Test]
    public void HandleMessage_Bitfield_from_secondary_seed_triggers_automatic_exit_of_super_seeding()
    {
        var (client1, server1) = CreateTestPair();
        server1.SupportsFastExtension = false;

        var torrent = new Torrent
        {
            Id = 11,
            Name = "BitfieldExitTorrent",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            PieceCount = 8,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SuperSeeding = true
        };
        server1.MatchedTorrent = torrent;

        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { server1 });

        var bitfieldPayload = new byte[] { 0xFF }; // all 8 pieces
        var bitfieldMsg = new PeerMessage
        {
            Type = PeerMessageType.Bitfield,
            Payload = bitfieldPayload,
            PayloadLength = 1
        };

        _server.HandleMessage(server1, bitfieldMsg, torrent);

        Assert.That(torrent.SuperSeeding, Is.False);
        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 11 && !t.SuperSeeding));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<SuperSeedingExitedEvent>(e => e.Torrent.Id == 11 && e.Reason == "secondary seed joined"));

        var msg = client1.ReceiveMessage();
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg.Type, Is.EqualTo(PeerMessageType.Bitfield));
        Assert.That(msg.Payload[0], Is.EqualTo(0xFF));
    }

    [Test]
    public void HandleMessage_Bitfield_completing_swarm_availability_triggers_automatic_exit_of_super_seeding()
    {
        var (client1, server1) = CreateTestPair();
        var (client2, server2) = CreateTestPair();
        server1.SupportsFastExtension = true;
        server2.SupportsFastExtension = true;

        var torrent = new Torrent
        {
            Id = 12,
            Name = "SwarmAvailExitTorrent",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            PieceCount = 8,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SuperSeeding = true
        };
        server1.MatchedTorrent = torrent;
        server2.MatchedTorrent = torrent;

        // Peer 1 has pieces 0-3 (0xF0)
        server1.PeerPieces = new bool[] { true, true, true, true, false, false, false, false };
        server1.HaveCount = 4;
        server1.Progress = 0.5;
        server1.IsSeed = false;

        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { server1, server2 });

        // Peer 2 announces bitfield with pieces 4-7 (0x0F)
        var bitfield2Payload = new byte[] { 0x0F };
        var bitfield2Msg = new PeerMessage
        {
            Type = PeerMessageType.Bitfield,
            Payload = bitfield2Payload,
            PayloadLength = 1
        };

        _server.HandleMessage(server2, bitfield2Msg, torrent);

        Assert.That(torrent.SuperSeeding, Is.False);
        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 12 && !t.SuperSeeding));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<SuperSeedingExitedEvent>(e => e.Torrent.Id == 12 && e.Reason == "swarm availability >= 1.0"));

        var msg1 = client1.ReceiveMessage();
        var msg2 = client2.ReceiveMessage();
        Assert.That(msg1?.Type, Is.EqualTo(PeerMessageType.HaveAll));
        Assert.That(msg2?.Type, Is.EqualTo(PeerMessageType.HaveAll));
    }
}
