using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Peers.SuperSeeding;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.SuperSeedingTests;

[TestFixture]
public class SuperSeedingServiceTests
{
    private const string DefaultInfoHash = "0123456789abcdef0123456789abcdef01234567";

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

    private (PeerConnection Client, PeerConnection Server) CreateTestPair(string peerId = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listeners.Add(listener);

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var clientTcp = new TcpClient();
        clientTcp.Connect(IPAddress.Loopback, port);
        var serverTcp = listener.AcceptTcpClient();
        listener.Stop();

        _clients.Add(clientTcp);

        var clientConn = new PeerConnection(clientTcp) { MessageReadTimeoutMs = 2000 };
        var serverConn = new PeerConnection(serverTcp) { MessageReadTimeoutMs = 2000 };

        if (!string.IsNullOrEmpty(peerId))
        {
            serverConn.PeerId = peerId;
            clientConn.PeerId = "cli_" + peerId;
        }

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

    private static Torrent CreateDefaultTorrent(int pieceCount = 10, bool superSeeding = true)
    {
        return new Torrent
        {
            Id = 100,
            Name = "SuperSeedTestTorrent",
            InfoHash = DefaultInfoHash,
            PieceCount = pieceCount,
            PieceLength = 16384,
            TotalSize = (long)pieceCount * 16384,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SuperSeeding = superSeeding
        };
    }


    [Test]
    public void SendInitialAvailability_WhenFastExtensionSupported_SendsHaveNone()
    {
        var (clientConn, serverConn) = CreateTestPair("peer_fast_init______");
        serverConn.SupportsFastExtension = true;
        var torrent = CreateDefaultTorrent();

        _service.SendInitialAvailability(serverConn, torrent);

        var received = clientConn.ReceiveMessage();
        received.Should().NotBeNull();
        received.Type.Should().Be(PeerMessageType.HaveNone);
    }

    [Test]
    public void SendInitialAvailability_WhenFastExtensionNotSupported_SendsEmptyBitfield()
    {
        var (clientConn, serverConn) = CreateTestPair("peer_nofast_init____");
        serverConn.SupportsFastExtension = false;
        var torrent = CreateDefaultTorrent(pieceCount: 16);

        var serviceWithoutFast = new SuperSeedingService(
            _torrentService,
            _connectionManager,
            _eventLogService,
            fastExtensionHandler: null,
            eventAggregator: _eventAggregator);

        serviceWithoutFast.SendInitialAvailability(serverConn, torrent);

        var received = clientConn.ReceiveMessage();
        received.Should().NotBeNull();
        received.Type.Should().Be(PeerMessageType.Bitfield);
        received.Payload.Should().NotBeNull();
        received.Payload.Length.Should().Be(2);
        received.Payload[0].Should().Be(0);
        received.Payload[1].Should().Be(0);
    }

    [Test]
    public void AllocateAndRevealPiece_WhenPeerEligibleAndUnchoked_RevealsUnseededPiece_AndSendsHave()
    {
        var (clientConn, serverConn) = CreateTestPair("peer_reveal_test____");
        serverConn.AmChoking = false;
        var torrent = CreateDefaultTorrent();

        var allocated = _service.AllocateAndRevealPiece(serverConn, torrent);

        allocated.Should().BeTrue();
        serverConn.AssignedSuperSeedingPiece.Should().Be(0);

        var received = clientConn.ReceiveMessage();
        received.Should().NotBeNull();
        received.Type.Should().Be(PeerMessageType.Have);
        BinaryPrimitives.ReadInt32BigEndian(received.Payload).Should().Be(0);
    }

    [Test]
    public void AllocateAndRevealPiece_WhenPeerChoked_DoesNotAllocate()
    {
        var (_, serverConn) = CreateTestPair("peer_choked_________");
        serverConn.AmChoking = true;
        var torrent = CreateDefaultTorrent();

        var allocated = _service.AllocateAndRevealPiece(serverConn, torrent);

        allocated.Should().BeFalse();
        serverConn.AssignedSuperSeedingPiece.Should().BeNull();
    }

    [Test]
    public void AllocateAndRevealPiece_OnlyOffersOnePieceAtATime_PerPeer()
    {
        var (clientConn, serverConn) = CreateTestPair("peer_one_piece______");
        serverConn.AmChoking = false;
        var torrent = CreateDefaultTorrent();

        var firstAllocation = _service.AllocateAndRevealPiece(serverConn, torrent);
        firstAllocation.Should().BeTrue();
        serverConn.AssignedSuperSeedingPiece.Should().Be(0);

        // Read initial HAVE message from socket
        var received = clientConn.ReceiveMessage();
        received.Should().NotBeNull();

        // Attempting second allocation before piece 0 is marked propagated must return false
        var secondAllocation = _service.AllocateAndRevealPiece(serverConn, torrent);
        secondAllocation.Should().BeFalse();
        serverConn.AssignedSuperSeedingPiece.Should().Be(0);
    }

    [Test]
    public void AllocateAndRevealPiece_WhenMultiplePeers_DistributesDistinctUnseededPieces()
    {
        var (client1, server1) = CreateTestPair("peer1_______________");
        var (client2, server2) = CreateTestPair("peer2_______________");
        server1.AmChoking = false;
        server2.AmChoking = false;
        var torrent = CreateDefaultTorrent();

        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { server1, server2 });

        var alloc1 = _service.AllocateAndRevealPiece(server1, torrent);
        var alloc2 = _service.AllocateAndRevealPiece(server2, torrent);

        alloc1.Should().BeTrue();
        alloc2.Should().BeTrue();
        server1.AssignedSuperSeedingPiece.Should().Be(0);
        server2.AssignedSuperSeedingPiece.Should().Be(1);

        var msg1 = client1.ReceiveMessage();
        var msg2 = client2.ReceiveMessage();
        BinaryPrimitives.ReadInt32BigEndian(msg1.Payload).Should().Be(0);
        BinaryPrimitives.ReadInt32BigEndian(msg2.Payload).Should().Be(1);
    }

    [Test]
    public void AllocateAndRevealPiece_WhenAllPiecesOfferedOrPropagated_ReturnsFalse()
    {
        var (_, server1) = CreateTestPair("peer_p1_____________");
        var (_, server2) = CreateTestPair("peer_p2_____________");
        var (_, server3) = CreateTestPair("peer_p3_____________");
        server1.AmChoking = false;
        server2.AmChoking = false;
        server3.AmChoking = false;

        // Torrent with only 2 pieces total
        var torrent = CreateDefaultTorrent(pieceCount: 2);

        _service.AllocateAndRevealPiece(server1, torrent).Should().BeTrue();
        _service.AllocateAndRevealPiece(server2, torrent).Should().BeTrue();

        // No unseeded pieces left; piece 0 and 1 are both Offered
        var alloc3 = _service.AllocateAndRevealPiece(server3, torrent);
        alloc3.Should().BeFalse();
        server3.AssignedSuperSeedingPiece.Should().BeNull();
    }



    [Test]
    public void IsRequestAllowed_WhenPieceMatchesAssignedPiece_ReturnsTrue()
    {
        var (_, serverConn) = CreateTestPair("peer_req_match______");
        serverConn.AssignedSuperSeedingPiece = 4;
        var torrent = CreateDefaultTorrent();

        var allowed = _service.IsRequestAllowed(serverConn, torrent, pieceIndex: 4);

        allowed.Should().BeTrue();
    }

    [Test]
    public void IsRequestAllowed_WhenPieceDoesNotMatchAssignedPiece_FastSupported_SendsRejectRequest()
    {
        var (clientConn, serverConn) = CreateTestPair("peer_req_reject_____");
        serverConn.SupportsFastExtension = true;
        serverConn.AssignedSuperSeedingPiece = 2;
        var torrent = CreateDefaultTorrent();

        var requestPayload = BuildRequestPayload(index: 5, begin: 0, length: 16384);
        var allowed = _service.IsRequestAllowed(serverConn, torrent, pieceIndex: 5, requestPayload);

        allowed.Should().BeFalse();

        var received = clientConn.ReceiveMessage();
        received.Should().NotBeNull();
        received.Type.Should().Be(PeerMessageType.RejectRequest);
        var rejectedPiece = BinaryPrimitives.ReadInt32BigEndian(received.Payload.AsSpan(0, 4));
        rejectedPiece.Should().Be(5);
    }

    [Test]
    public void IsRequestAllowed_WhenPieceDoesNotMatchAssignedPiece_FastNotSupported_DropsRequestSilently()
    {
        var (clientConn, serverConn) = CreateTestPair("peer_req_nofast_____");
        serverConn.SupportsFastExtension = false;
        serverConn.AssignedSuperSeedingPiece = 2;
        var torrent = CreateDefaultTorrent();

        var serviceWithoutFast = new SuperSeedingService(
            _torrentService,
            _connectionManager,
            _eventLogService,
            fastExtensionHandler: null,
            eventAggregator: _eventAggregator);

        var requestPayload = BuildRequestPayload(index: 5, begin: 0, length: 16384);
        var allowed = serviceWithoutFast.IsRequestAllowed(serverConn, torrent, pieceIndex: 5, requestPayload);

        allowed.Should().BeFalse();

        // Send an Unchoke message to verify the queue only has the Unchoke message
        serverConn.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
        var received = clientConn.ReceiveMessage();
        received.Should().NotBeNull();
        received.Type.Should().Be(PeerMessageType.Unchoke);
    }

    [Test]
    public void IsRequestAllowed_WhenSuperSeedingDisabled_AllowsAnyRequest()
    {
        var (_, serverConn) = CreateTestPair("peer_ss_disabled___");
        serverConn.AssignedSuperSeedingPiece = null;
        var torrent = CreateDefaultTorrent(superSeeding: false);

        var allowed = _service.IsRequestAllowed(serverConn, torrent, pieceIndex: 7);

        allowed.Should().BeTrue();
    }

    [Test]
    public void HandleBlockUploaded_TracksBytesUploaded_AndRecordsPieceUploadedWhenComplete()
    {
        var (_, serverConn) = CreateTestPair("peer_upload_test____");
        serverConn.AmChoking = false;
        var torrent = CreateDefaultTorrent(pieceCount: 5);
        torrent.PieceLength = 32768;
        torrent.TotalSize = 5 * 32768;

        _service.AllocateAndRevealPiece(serverConn, torrent);
        serverConn.AssignedSuperSeedingPiece.Should().Be(0);

        // Upload first 16KB block (half piece)
        _service.HandleBlockUploaded(serverConn, torrent, pieceIndex: 0, bytesUploaded: 16384);
        serverConn.AssignedPieceBytesUploaded.Should().Be(16384);

        var tracker = _service.GetOrCreateTracker(torrent);
        tracker.GetPieceState(0).Should().Be(SuperSeedingPieceState.Offered);

        // Upload second 16KB block (completes piece)
        _service.HandleBlockUploaded(serverConn, torrent, pieceIndex: 0, bytesUploaded: 16384);
        serverConn.AssignedPieceBytesUploaded.Should().Be(32768);

        tracker.GetPieceState(0).Should().Be(SuperSeedingPieceState.Uploaded);
    }

    [Test]
    public void HandleBlockUploaded_LastPiece_CalculatesCorrectFinalPieceSize()
    {
        var (_, serverConn) = CreateTestPair("peer_last_piece_____");
        serverConn.AmChoking = false;

        // 3 pieces, PieceLength = 16384, TotalSize = 40000 -> pieces 0,1 are 16384, piece 2 is 7232 bytes
        var torrent = CreateDefaultTorrent(pieceCount: 3);
        torrent.PieceLength = 16384;
        torrent.TotalSize = 40000;

        var tracker = _service.GetOrCreateTracker(torrent);
        tracker.RecordPieceOffered(serverConn.PeerId, pieceIndex: 2);
        serverConn.AssignedSuperSeedingPiece = 2;

        _service.HandleBlockUploaded(serverConn, torrent, pieceIndex: 2, bytesUploaded: 7232);

        tracker.GetPieceState(2).Should().Be(SuperSeedingPieceState.Uploaded);
    }



    [Test]
    public void OnPeerHave_FeedbackFromAssignedPeer_ConfirmsUploaded_DoesNotPropagate()
    {
        var (_, server1) = CreateTestPair("peer_have_assigned__");
        server1.AmChoking = false;
        var torrent = CreateDefaultTorrent();

        _service.AllocateAndRevealPiece(server1, torrent);
        server1.AssignedSuperSeedingPiece.Should().Be(0);

        // Peer 1 confirms it received piece 0
        _service.OnPeerHave(server1, torrent, pieceIndex: 0);

        var tracker = _service.GetOrCreateTracker(torrent);
        tracker.GetPieceState(0).Should().Be(SuperSeedingPieceState.Uploaded);

        // Peer 1 must still have piece 0 assigned, not offered piece 1 yet
        server1.AssignedSuperSeedingPiece.Should().Be(0);
    }

    [Test]
    public void OnPeerHave_FeedbackFromThirdPartyPeer_ConfirmsPropagation_AndUnlocksAssignedPeer()
    {
        var (client1, server1) = CreateTestPair("peer1_target________");
        var (_, server2) = CreateTestPair("peer2_reporter______");
        server1.AmChoking = false;
        server2.AmChoking = false;
        var torrent = CreateDefaultTorrent();

        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { server1, server2 });

        _service.AllocateAndRevealPiece(server1, torrent);
        server1.AssignedSuperSeedingPiece.Should().Be(0);

        // Drain the initial HAVE message from client1
        client1.ReceiveMessage();

        // Assigned peer confirms receipt
        _service.OnPeerHave(server1, torrent, pieceIndex: 0);

        // 3rd party peer reports HAVE(0) -> confirms piece 0 propagated into swarm!
        _service.OnPeerHave(server2, torrent, pieceIndex: 0);

        var tracker = _service.GetOrCreateTracker(torrent);
        tracker.GetPieceState(0).Should().Be(SuperSeedingPieceState.Propagated);

        // Server 1 should now be unlocked and assigned piece 1
        server1.AssignedSuperSeedingPiece.Should().Be(1);

        var advanceMsg = client1.ReceiveMessage();
        advanceMsg.Should().NotBeNull();
        advanceMsg.Type.Should().Be(PeerMessageType.Have);
        BinaryPrimitives.ReadInt32BigEndian(advanceMsg.Payload).Should().Be(1);
    }

    [Test]
    public void OnPeerBitfield_ThirdPartyPeerReportsPiece_UnlocksOriginalPeer()
    {
        var (client1, server1) = CreateTestPair("peer1_bitfield_tgt__");
        var (_, server2) = CreateTestPair("peer2_bitfield_rep__");
        server1.AmChoking = false;
        server2.AmChoking = false;
        var torrent = CreateDefaultTorrent(pieceCount: 5);

        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { server1, server2 });

        _service.AllocateAndRevealPiece(server1, torrent);
        server1.AssignedSuperSeedingPiece.Should().Be(0);

        // Drain initial have
        client1.ReceiveMessage();

        // Mark piece 0 uploaded to server 1
        var tracker = _service.GetOrCreateTracker(torrent);
        tracker.RecordPieceUploaded(server1.PeerId, pieceIndex: 0);

        // Server 2 reports bitfield containing piece 0
        var bitfield = new bool[] { true, false, false, false, false };
        server2.PeerPieces = bitfield;

        _service.OnPeerBitfield(server2, torrent, bitfield);

        tracker.GetPieceState(0).Should().Be(SuperSeedingPieceState.Propagated);
        server1.AssignedSuperSeedingPiece.Should().Be(1);
    }

    [Test]
    public void CheckTimeouts_WhenPeerHoldsPieceBeyondTimeout_ChokesPeerAndResetsPieceForAllocation()
    {
        var (client1, server1) = CreateTestPair("peer1_selfish_______");
        var (client2, server2) = CreateTestPair("peer2_good__________");
        server1.AmChoking = false;
        server2.AmChoking = false;
        var torrent = CreateDefaultTorrent(pieceCount: 5);

        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { server1, server2 });

        // Allocate piece 0 to server1
        _service.AllocateAndRevealPiece(server1, torrent);
        client1.ReceiveMessage(); // Drain initial have

        // Simulate server1 completed upload 10 minutes ago
        var tracker = _service.GetOrCreateTracker(torrent);
        var uploadTime = DateTime.UtcNow.AddMinutes(-10);
        tracker.RecordPieceUploaded(server1.PeerId, pieceIndex: 0, uploadTime);

        // Run timeout check with current time (exceeding default 90s timeout)
        var timeouts = _service.CheckTimeouts(torrent, DateTime.UtcNow);

        timeouts.Should().HaveCount(1);
        timeouts[0].PeerId.Should().Be(server1.PeerId);
        timeouts[0].PieceIndex.Should().Be(0);

        // Server1 should now be choked, have its assigned piece cleared, and have received a Choke message
        server1.AmChoking.Should().BeTrue();
        server1.AssignedSuperSeedingPiece.Should().BeNull();

        var chokeMsg = client1.ReceiveMessage();
        chokeMsg.Should().NotBeNull();
        chokeMsg.Type.Should().Be(PeerMessageType.Choke);

        // Piece 0 was reset to Unseeded and re-allocated to available unchoked peer (server2)
        server2.AssignedSuperSeedingPiece.Should().Be(0);

        var server2Msg = client2.ReceiveMessage();
        server2Msg.Should().NotBeNull();
        server2Msg.Type.Should().Be(PeerMessageType.Have);
        BinaryPrimitives.ReadInt32BigEndian(server2Msg.Payload).Should().Be(0);
    }

    [Test]
    public void OnPeerDisconnected_ResetsAssignedPieceAndClearsTrackerState()
    {
        var (_, serverConn) = CreateTestPair("peer_disconnect_____");
        serverConn.AmChoking = false;
        var torrent = CreateDefaultTorrent();

        _service.AllocateAndRevealPiece(serverConn, torrent);
        serverConn.AssignedSuperSeedingPiece.Should().Be(0);

        _service.OnPeerDisconnected(serverConn, torrent);

        serverConn.AssignedSuperSeedingPiece.Should().BeNull();
        serverConn.AssignedPieceBytesUploaded.Should().Be(0);
    }



    [Test]
    public void ExitSuperSeeding_WhenPeerSendsHaveAll_ExitsAndBroadcastsFullAvailability()
    {
        var (client1, server1) = CreateTestPair("seed_peer___________");
        server1.SupportsFastExtension = true;
        var torrent = CreateDefaultTorrent();

        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { server1 });

        _service.OnPeerHaveAll(server1, torrent);

        torrent.SuperSeeding.Should().BeFalse();
        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 100 && !t.SuperSeeding));
        _eventLogService.Received(1).Info(100, "SuperSeeding", Arg.Is<string>(s => s.Contains("secondary seed joined")));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<SuperSeedingExitedEvent>(e => e.Torrent.Id == 100 && e.Reason == "secondary seed joined"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentUpdatedEvent>(e => e.Torrent.Id == 100 && !e.Torrent.SuperSeeding));

        // FastExtension peer receives HaveAll message
        var msg = client1.ReceiveMessage();
        msg.Should().NotBeNull();
        msg.Type.Should().Be(PeerMessageType.HaveAll);
    }

    [Test]
    public void ExitSuperSeeding_WhenPeerConnectsWithFullBitfield_ExitsAndBroadcastsFullBitfield()
    {
        var (client1, server1) = CreateTestPair("full_bf_peer________");
        server1.SupportsFastExtension = false;
        var torrent = CreateDefaultTorrent(pieceCount: 8);

        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { server1 });

        var fullBitfield = new bool[] { true, true, true, true, true, true, true, true };
        server1.PeerPieces = fullBitfield;
        server1.HaveCount = 8;
        server1.Progress = 1.0;
        server1.IsSeed = true;

        _service.OnPeerBitfield(server1, torrent, fullBitfield);

        torrent.SuperSeeding.Should().BeFalse();
        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 100 && !t.SuperSeeding));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<SuperSeedingExitedEvent>(e => e.Torrent.Id == 100 && e.Reason == "secondary seed joined"));

        // Non-FastExtension peer receives full bitfield (1 byte = 0xFF for 8 pieces)
        var msg = client1.ReceiveMessage();
        msg.Should().NotBeNull();
        msg.Type.Should().Be(PeerMessageType.Bitfield);
        msg.Payload[0].Should().Be(0xFF);
    }

    [Test]
    public void ExitSuperSeeding_WhenSwarmAvailabilityReaches100Percent_ExitsSuperSeeding()
    {
        var (client1, server1) = CreateTestPair("avail_p1____________");
        var (client2, server2) = CreateTestPair("avail_p2____________");
        server1.SupportsFastExtension = true;
        server2.SupportsFastExtension = true;

        var torrent = CreateDefaultTorrent(pieceCount: 4);

        // Peer 1 has pieces [0, 1]. Peer 2 has pieces [2, 3].
        server1.PeerPieces = new bool[] { true, true, false, false };
        server1.HaveCount = 2;
        server1.Progress = 0.5;

        server2.PeerPieces = new bool[] { false, false, true, true };
        server2.HaveCount = 2;
        server2.Progress = 0.5;

        _connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { server1, server2 });

        // Peer 2 sends bitfield, completing cumulative swarm coverage across all pieces
        _service.OnPeerBitfield(server2, torrent, server2.PeerPieces);

        torrent.SuperSeeding.Should().BeFalse();
        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 100 && !t.SuperSeeding));
        _eventLogService.Received(1).Info(100, "SuperSeeding", Arg.Is<string>(s => s.Contains("swarm availability >= 1.0")));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<SuperSeedingExitedEvent>(e => e.Torrent.Id == 100 && e.Reason == "swarm availability >= 1.0"));

        var msg1 = client1.ReceiveMessage();
        var msg2 = client2.ReceiveMessage();
        msg1.Should().NotBeNull();
        msg1.Type.Should().Be(PeerMessageType.HaveAll);
        msg2.Should().NotBeNull();
        msg2.Type.Should().Be(PeerMessageType.HaveAll);
    }



    [Test]
    public void SuperSeedingTracker_TracksStateCountsAccurately()
    {
        var tracker = new SuperSeedingTracker(pieceCount: 4);

        tracker.UnseededCount.Should().Be(4);
        tracker.OfferedCount.Should().Be(0);
        tracker.UploadedCount.Should().Be(0);
        tracker.PropagatedCount.Should().Be(0);

        // Offer piece 0
        tracker.RecordPieceOffered("p1", 0);
        tracker.UnseededCount.Should().Be(3);
        tracker.OfferedCount.Should().Be(1);

        // Upload piece 0
        tracker.RecordPieceUploaded("p1", 0);
        tracker.OfferedCount.Should().Be(0);
        tracker.UploadedCount.Should().Be(1);

        // 3rd party reports piece 0 -> Propagated
        var res = tracker.RecordPieceHave("p2", 0);
        res.NewlyPropagated.Should().BeTrue();
        res.FreedPeerId.Should().Be("p1");

        tracker.UploadedCount.Should().Be(0);
        tracker.PropagatedCount.Should().Be(1);
    }

    [Test]
    public void SuperSeedingTracker_RecordPeerBitfield_PropagatesMultiplePieces()
    {
        var tracker = new SuperSeedingTracker(pieceCount: 4);

        tracker.RecordPieceOffered("p1", 0);
        tracker.RecordPieceUploaded("p1", 0);

        tracker.RecordPieceOffered("p2", 1);
        tracker.RecordPieceUploaded("p2", 1);

        // Third party reports bitfield with both piece 0 and 1
        var bitfield = new bool[] { true, true, false, false };
        var propagations = tracker.RecordPeerBitfield("p3", bitfield);

        propagations.Should().HaveCount(2);
        propagations.Select(p => p.PieceIndex).Should().Contain(new[] { 0, 1 });
        tracker.PropagatedCount.Should().Be(2);
    }

    [Test]
    public void SuperSeedingTracker_MarkAndUnmarkPeerSelfish_TogglesEligibility()
    {
        var tracker = new SuperSeedingTracker(pieceCount: 4);

        tracker.IsPeerEligible("peer_test").Should().BeTrue();

        tracker.MarkPeerSelfish("peer_test");
        tracker.IsPeerSelfish("peer_test").Should().BeTrue();
        tracker.IsPeerEligible("peer_test").Should().BeFalse();

        tracker.UnmarkPeerSelfish("peer_test");
        tracker.IsPeerSelfish("peer_test").Should().BeFalse();
        tracker.IsPeerEligible("peer_test").Should().BeTrue();
    }

    [Test]
    public void SuperSeedingTracker_ResetPiece_RestoresUnseededState()
    {
        var tracker = new SuperSeedingTracker(pieceCount: 4);

        tracker.RecordPieceOffered("peer_1", 2);
        tracker.GetPieceState(2).Should().Be(SuperSeedingPieceState.Offered);

        var reset = tracker.ResetPiece(2);
        reset.Should().BeTrue();
        tracker.GetPieceState(2).Should().Be(SuperSeedingPieceState.Unseeded);
    }

}
