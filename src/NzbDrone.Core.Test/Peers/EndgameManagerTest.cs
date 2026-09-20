using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Blocklist;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Encryption;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers.MultiTracker;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class EndgameManagerTest
{
    private IConfigService _configService;
    private ITorrentService _torrentService;
    private IConnectionManager _connectionManager;
    private IPeerDiscoveryService _peerDiscovery;
    private IMultiTrackerManager _multiTracker;
    private IMseSkeyRegistry _mseSkeyRegistry;
    private PeerServer _server;
    private List<PeerConnection> _connections;
    private List<TcpListener> _listeners;
    private List<TcpClient> _clients;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _torrentService = Substitute.For<ITorrentService>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        _multiTracker = Substitute.For<IMultiTrackerManager>();
        _mseSkeyRegistry = Substitute.For<IMseSkeyRegistry>();

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

        _server = new PeerServer(_configService, _torrentService, _connectionManager, _peerDiscovery, _multiTracker, mseSkeyRegistry: _mseSkeyRegistry);
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
        _clients.Add(clientTcp);
        var serverTcp = listener.AcceptTcpClient();
        listener.Stop();

        var clientConn = new PeerConnection(clientTcp);
        var serverConn = new PeerConnection(serverTcp);
        _connections.Add(clientConn);
        _connections.Add(serverConn);
        return (clientConn, serverConn);
    }

    private void InvokeHandleMessage(PeerConnection connection, PeerMessage message, Torrent torrent = null)
    {
        var method = typeof(PeerServer).GetMethod(
            "HandleMessage",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_server, new object[] { connection, message, torrent });
    }

    [Test]
    public void IsInEndgame_should_return_true_when_missing_blocks_less_than_or_equal_to_in_flight_requests()
    {
        var manager = new EndgameManager();

        Assert.That(manager.IsInEndgame(missingBlockCount: 5, inFlightRequestsCount: 5), Is.True);
        Assert.That(manager.IsInEndgame(missingBlockCount: 3, inFlightRequestsCount: 5), Is.True);
        Assert.That(manager.IsInEndgame(missingBlockCount: 1, inFlightRequestsCount: 2), Is.True);
    }

    [Test]
    public void IsInEndgame_should_return_false_when_missing_blocks_greater_than_in_flight_or_zero()
    {
        var manager = new EndgameManager();

        Assert.That(manager.IsInEndgame(missingBlockCount: 10, inFlightRequestsCount: 5), Is.False);
        Assert.That(manager.IsInEndgame(missingBlockCount: 1, inFlightRequestsCount: 0), Is.False);
        Assert.That(manager.IsInEndgame(missingBlockCount: 0, inFlightRequestsCount: 0), Is.False);
        Assert.That(manager.IsInEndgame(missingBlockCount: 0, inFlightRequestsCount: 5), Is.False);
        Assert.That(manager.IsInEndgame(missingBlockCount: -1, inFlightRequestsCount: 5), Is.False);
    }

    [Test]
    public void RegisterRequest_and_OnBlockReceived_tracks_and_clears_duplicate_peer_requests()
    {
        var manager = new EndgameManager();
        var (c1, s1) = CreateTestPair();
        var (c2, s2) = CreateTestPair();
        var (c3, s3) = CreateTestPair();

        manager.RegisterRequest(torrentId: 42, pieceIndex: 1, begin: 16384, length: 16384, peer: s1);
        manager.RegisterRequest(torrentId: 42, pieceIndex: 1, begin: 16384, length: 16384, peer: s2);
        manager.RegisterRequest(torrentId: 42, pieceIndex: 1, begin: 16384, length: 16384, peer: s3);

        Assert.That(manager.HasRequested(42, 1, 16384, 16384, s1), Is.True);
        Assert.That(manager.HasRequested(42, 1, 16384, 16384, s2), Is.True);
        Assert.That(manager.HasRequested(42, 1, 16384, 16384, s3), Is.True);

        // s1 delivers the block
        var cancels = manager.OnBlockReceived(torrentId: 42, pieceIndex: 1, begin: 16384, length: 16384, sourcePeer: s1);

        Assert.That(cancels, Has.Count.EqualTo(2));
        Assert.That(cancels, Contains.Item(s2));
        Assert.That(cancels, Contains.Item(s3));
        Assert.That(cancels, Does.Not.Contain(s1));

        // Subsequent block reception for same block returns empty because it was cleared
        var subsequentCancels = manager.OnBlockReceived(torrentId: 42, pieceIndex: 1, begin: 16384, length: 16384, sourcePeer: s2);
        Assert.That(subsequentCancels, Is.Empty);
    }

    [Test]
    public void Receiving_block_delivers_Cancel_messages_to_all_other_peers_with_duplicate_requests()
    {
        var (client1, server1) = CreateTestPair();
        var (client2, server2) = CreateTestPair();
        var (client3, server3) = CreateTestPair();

        var torrent = new Torrent
        {
            Id = 10,
            Name = "Test Torrent",
            PieceCount = 1,
            PieceLength = 32768,
            TotalSize = 32768,
            Status = TorrentStatus.Downloading
        };

        // Both server1 and server2 were issued duplicate requests for block (0, 0, 16384)
        _server.EndgameManager.RegisterRequest(torrent.Id, 0, 0, 16384, server1);
        _server.EndgameManager.RegisterRequest(torrent.Id, 0, 0, 16384, server2);

        // Prepare piece payload from server1
        var payload = new byte[8 + 16384];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), 0);
        new Random(42).NextBytes(payload.AsSpan(8));

        var pieceMsg = new PeerMessage
        {
            Type = PeerMessageType.Piece,
            Payload = payload
        };

        InvokeHandleMessage(server1, pieceMsg, torrent);

        // client2 should receive a Cancel message from server2
        var received = client2.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Cancel));
        Assert.That(received.Payload, Is.Not.Null);
        Assert.That(received.Payload.Length, Is.EqualTo(12));

        var cancelledPiece = BinaryPrimitives.ReadInt32BigEndian(received.Payload.AsSpan(0, 4));
        var cancelledBegin = BinaryPrimitives.ReadInt32BigEndian(received.Payload.AsSpan(4, 4));
        var cancelledLength = BinaryPrimitives.ReadInt32BigEndian(received.Payload.AsSpan(8, 4));

        Assert.That(cancelledPiece, Is.EqualTo(0));
        Assert.That(cancelledBegin, Is.EqualTo(0));
        Assert.That(cancelledLength, Is.EqualTo(16384));
    }

    [Test]
    public void Receiving_Cancel_parses_12_byte_payload_and_discards_queued_outgoing_block()
    {
        var (clientConn, serverConn) = CreateTestPair();
        serverConn.PendingRequestCount = 3;

        var targetBlock = new PeerRequest(2, 16384, 16384);
        var otherBlock = new PeerRequest(3, 0, 16384);

        serverConn.OutboundQueue.Add(targetBlock);
        serverConn.OutboundQueue.Add(otherBlock);

        serverConn.PendingIncomingRequests.Add(targetBlock);
        serverConn.PendingIncomingRequests.Add(otherBlock);

        var cancelPayload = new byte[12];
        BinaryPrimitives.WriteInt32BigEndian(cancelPayload.AsSpan(0, 4), 2);
        BinaryPrimitives.WriteInt32BigEndian(cancelPayload.AsSpan(4, 4), 16384);
        BinaryPrimitives.WriteInt32BigEndian(cancelPayload.AsSpan(8, 4), 16384);

        var cancelMsg = new PeerMessage
        {
            Type = PeerMessageType.Cancel,
            Payload = cancelPayload
        };

        InvokeHandleMessage(serverConn, cancelMsg);

        Assert.That(serverConn.PendingRequestCount, Is.EqualTo(2));
        Assert.That(serverConn.OutboundQueue, Does.Not.Contain(targetBlock));
        Assert.That(serverConn.OutboundQueue, Contains.Item(otherBlock));
        Assert.That(serverConn.PendingIncomingRequests, Does.Not.Contain(targetBlock));
        Assert.That(serverConn.PendingIncomingRequests, Contains.Item(otherBlock));
    }

    [Test]
    public void DuplicateEndgameRequests_floods_missing_blocks_across_available_unchoked_peers_with_piece()
    {
        var (client1, server1) = CreateTestPair();
        var (client2, server2) = CreateTestPair();

        server1.PeerChoking = false;
        server1.PeerPieces = new[] { true, true };
        server2.PeerChoking = false;
        server2.PeerPieces = new[] { true, true };

        var torrent = new Torrent
        {
            Id = 7,
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            Name = "Endgame Torrent",
            PieceCount = 2,
            PieceLength = 16384,
            TotalSize = 32768,
            Status = TorrentStatus.Downloading
        };

        server1.MatchedTorrent = torrent;
        server2.MatchedTorrent = torrent;
        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { server1, server2 });

        var picker = _server.PiecePicker;
        picker.AddActivePiece(0, 16384, 16384);

        // Request piece 0 on server1 -> block is now requested, remaining = 1, inflight = 1 => Endgame!
        var block1 = _server.RequestBlock(server1, 0);
        Assert.That(block1, Is.Not.Null);
        Assert.That(server1.PendingRequestCount, Is.EqualTo(1));

        // Duplicate requests across remaining available peers
        var duplicated = _server.DuplicateEndgameRequests(torrent);

        Assert.That(duplicated, Has.Count.EqualTo(1));
        Assert.That(duplicated[0].Peer, Is.EqualTo(server2));
        Assert.That(duplicated[0].Block.PieceIndex, Is.EqualTo(0));
        Assert.That(server2.PendingRequestCount, Is.EqualTo(1));

        // Verify client2 received the Request message
        var msg = client2.ReceiveMessage();
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg.Type, Is.EqualTo(PeerMessageType.Request));
        Assert.That(msg.Payload.Length, Is.EqualTo(12));

        var reqPiece = BinaryPrimitives.ReadInt32BigEndian(msg.Payload.AsSpan(0, 4));
        Assert.That(reqPiece, Is.EqualTo(0));
    }
}
