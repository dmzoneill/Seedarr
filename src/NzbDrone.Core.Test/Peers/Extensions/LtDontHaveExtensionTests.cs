using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Peers.Extensions;

[TestFixture]
public class LtDontHaveExtensionTests
{
    private IConfigService _configService;
    private IConnectionManager _connectionManager;
    private ITorrentService _torrentService;
    private ChokeManager _chokeManager;
    private List<PeerConnection> _connections;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.MaxUploadSlots.Returns(2);
        _configService.ExtensionLtDontHave.Returns(true);

        _connections = new List<PeerConnection>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _connectionManager.GetAllConnections().Returns(_ => _connections);
        _connectionManager.GetConnections(Arg.Any<string>()).Returns(callInfo =>
        {
            var hash = callInfo.Arg<string>();
            return _connections.Where(c => string.Equals(c.InfoHash, hash, StringComparison.OrdinalIgnoreCase)).ToList();
        });
        _connectionManager.GetDynamicUploadSlotCount(Arg.Any<string>()).Returns(_ => _configService.MaxUploadSlots);

        _torrentService = Substitute.For<ITorrentService>();
        _chokeManager = new ChokeManager(_connectionManager, _configService, _torrentService);
    }

    private PeerConnection CreatePeer(string infoHash, int port, bool interested = true, long rate = 1000)
    {
        var stream = new MemoryStream();
        var conn = new PeerConnection(stream, "127.0.0.1", port);
        typeof(PeerConnection).GetProperty("InfoHash")?.SetValue(conn, infoHash);
        conn.PeerInterested = interested;
        conn.AmChoking = true;
        conn.UploadRate = rate;
        conn.DownloadRate = rate;
        _connections.Add(conn);
        return conn;
    }

    [Test]
    public void SupportsLtDontHave_should_return_true_when_extension_negotiated()
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 5000);
        Assert.That(conn.SupportsLtDontHave, Is.False);

        conn.RemoteExtensions["lt_donthave"] = 3;
        Assert.That(conn.SupportsLtDontHave, Is.True);
        Assert.That(conn.RemoteLtDontHaveId, Is.EqualTo(3));
    }

    [Test]
    public void SendLtDontHave_should_return_false_when_remote_peer_does_not_support_extension()
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 5000);
        var result = conn.SendLtDontHave(5);

        Assert.That(result, Is.False);
    }

    [Test]
    public void SendLtDontHave_should_encode_4_byte_big_endian_piece_index_and_send_extended_message()
    {
        var stream = new MemoryStream();
        var conn = new PeerConnection(stream, "127.0.0.1", 5000);
        conn.RemoteExtensions["lt_donthave"] = 7;

        var result = conn.SendLtDontHave(0x12345678);

        Assert.That(result, Is.True);
        var bytes = stream.ToArray();
        // Wire framing: [length: 4 bytes] [msg_type: 1 byte (20)] [ext_id: 1 byte (7)] [payload: 4 bytes]
        Assert.That(bytes.Length, Is.EqualTo(4 + 1 + 1 + 4));

        var msgLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0, 4));
        Assert.That(msgLength, Is.EqualTo(6));
        Assert.That(bytes[4], Is.EqualTo((byte)PeerMessageType.Extended));
        Assert.That(bytes[5], Is.EqualTo(7));

        var pieceIndex = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(6, 4));
        Assert.That(pieceIndex, Is.EqualTo(0x12345678));
    }

    [Test]
    public void HandleLtDontHave_should_clear_bitfield_and_peerpieces_and_raise_event()
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 5000);
        // 8 pieces: all set (0xFF)
        conn.Bitfield = new byte[] { 0xFF };
        Assert.That(conn.HaveCount, Is.EqualTo(8));
        Assert.That(conn.PeerPieces[3], Is.True);

        int? eventPiece = null;
        int? callbackPiece = null;
        conn.LtDontHaveReceived += p => eventPiece = p;
        conn.OnLtDontHaveReceived = p => callbackPiece = p;

        var payload = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(payload, 3);

        var handled = conn.HandleLtDontHave(payload);

        Assert.That(handled, Is.True);
        Assert.That(eventPiece, Is.EqualTo(3));
        Assert.That(callbackPiece, Is.EqualTo(3));
        Assert.That(conn.PeerPieces[3], Is.False);
        Assert.That(conn.HaveCount, Is.EqualTo(7));

        // Bit 3 in byte 0 is cleared (bitmask ~(1 << (7 - 3)) = ~(1 << 4) = ~0x10 = 0xEF)
        Assert.That(conn.Bitfield[0], Is.EqualTo(0xEF));
    }

    [Test]
    public void HandleLtDontHave_should_clear_seed_state_when_piece_removed()
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 5000);
        conn.PeerPieces = new bool[] { true, true };
        conn.Progress = 1.0;
        conn.IsSeed = true;

        var payload = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(payload, 1);

        conn.HandleLtDontHave(payload);

        Assert.That(conn.PeerPieces[1], Is.False);
        Assert.That(conn.HaveCount, Is.EqualTo(1));
        Assert.That(conn.Progress, Is.EqualTo(0.5));
        Assert.That(conn.IsSeed, Is.False);
    }

    [Test]
    public void HandleLtDontHave_should_reevaluate_interested_via_interest_evaluator()
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 5000);
        conn.PeerPieces = new bool[] { true, false };
        conn.IsInterested = true;
        conn.InterestEvaluator = c => c.PeerPieces != null && c.PeerPieces[0];

        Assert.That(conn.IsInterested, Is.True);

        var payload = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(payload, 0);

        conn.HandleLtDontHave(payload);

        Assert.That(conn.PeerPieces[0], Is.False);
        Assert.That(conn.IsInterested, Is.False);
    }

    [Test]
    public void HandleLtDontHave_should_set_uninterested_when_peer_loses_all_pieces()
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 5000);
        conn.PeerPieces = new bool[] { true };
        conn.IsInterested = true;

        var payload = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(payload, 0);

        conn.HandleLtDontHave(payload);

        Assert.That(conn.PeerPieces[0], Is.False);
        Assert.That(conn.IsInterested, Is.False);
    }

    [Test]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(5)]
    public void HandleLtDontHave_should_reject_malformed_payload_lengths(int length)
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 5000);
        conn.Bitfield = new byte[] { 0xFF };
        var badPayload = new byte[length];

        var handled = conn.HandleLtDontHave(badPayload);

        Assert.That(handled, Is.False);
        Assert.That(conn.Bitfield[0], Is.EqualTo(0xFF));
    }

    [Test]
    public void HandleLtDontHave_should_reject_null_payload()
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 5000);
        conn.Bitfield = new byte[] { 0xFF };

        var handled = conn.HandleLtDontHave(null);

        Assert.That(handled, Is.False);
        Assert.That(conn.Bitfield[0], Is.EqualTo(0xFF));
    }

    [Test]
    public void HandleLtDontHave_should_reject_negative_piece_index()
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 5000);
        conn.Bitfield = new byte[] { 0xFF };

        var payload = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(payload, -1);

        var handled = conn.HandleLtDontHave(payload);

        Assert.That(handled, Is.False);
        Assert.That(conn.Bitfield[0], Is.EqualTo(0xFF));
    }

    [Test]
    public void HandleLtDontHave_should_reject_out_of_range_piece_index()
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 5000);
        conn.PeerPieces = new bool[] { true, true, true };

        var payload = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(payload, 100);

        var handled = conn.HandleLtDontHave(payload);

        Assert.That(handled, Is.False);
        Assert.That(conn.PeerPieces.All(p => p), Is.True);
    }

    [Test]
    public void HandleMessage_should_route_extended_lt_donthave_message()
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 5000);
        conn.PeerPieces = new bool[] { true, true, true };
        conn.RemoteExtensions["lt_donthave"] = 4;

        int? receivedPiece = null;
        conn.LtDontHaveReceived += p => receivedPiece = p;

        var extendedPayload = new byte[5];
        extendedPayload[0] = 4; // ext id
        BinaryPrimitives.WriteInt32BigEndian(extendedPayload.AsSpan(1, 4), 2);

        conn.HandleMessage(new PeerMessage
        {
            Type = PeerMessageType.Extended,
            Payload = extendedPayload
        });

        Assert.That(receivedPiece, Is.EqualTo(2));
        Assert.That(conn.PeerPieces[2], Is.False);
    }

    [Test]
    public void ChokeManager_should_prioritize_active_leecher_over_partial_seed_for_regular_unchoke()
    {
        // 2 slots total: 1 regular slot, 1 optimistic slot
        _configService.MaxUploadSlots.Returns(2);

        var activeLeecher = CreatePeer("hash1", 1001, interested: true, rate: 100);
        activeLeecher.IsPartialSeed = false;
        activeLeecher.HasMissingWantedPieces = true;

        var partialSeed = CreatePeer("hash1", 1002, interested: true, rate: 1000);
        partialSeed.IsPartialSeed = true;
        partialSeed.HasMissingWantedPieces = false;

        _chokeManager.ProcessRegularUnchoke();

        // The regular slot MUST go to the active leecher, despite partial seed having higher rate
        Assert.That(activeLeecher.AmChoking, Is.False, "Active leecher should be unchoked");
        Assert.That(partialSeed.AmChoking, Is.True, "Partial seed should not consume regular upload slot");
    }

    [Test]
    public void ChokeManager_should_not_grant_optimistic_unchoke_to_partial_seeds()
    {
        _configService.MaxUploadSlots.Returns(3);

        var activeLeecher = CreatePeer("hash1", 1001, interested: true);
        activeLeecher.IsPartialSeed = false;
        activeLeecher.HasMissingWantedPieces = true;

        var partialSeed = CreatePeer("hash1", 1002, interested: true);
        partialSeed.IsPartialSeed = true;
        partialSeed.HasMissingWantedPieces = false;

        _chokeManager.ProcessOptimisticUnchoke();

        // Only active leecher should receive optimistic unchoke
        Assert.That(activeLeecher.IsOptimisticUnchoked, Is.True);
        Assert.That(partialSeed.IsOptimisticUnchoked, Is.False);
        Assert.That(partialSeed.AmChoking, Is.True);
    }

    [Test]
    public void ChokeManager_should_choke_unchoked_peer_when_it_becomes_partial_seed()
    {
        _configService.MaxUploadSlots.Returns(2);

        var peer1 = CreatePeer("hash1", 1001, interested: true);
        peer1.AmChoking = false;

        var peer2 = CreatePeer("hash1", 1002, interested: true);
        peer2.AmChoking = true;

        // peer1 completes its wanted files and becomes a partial seed
        _chokeManager.PeerBecamePartialSeed(peer1);

        Assert.That(peer1.AmChoking, Is.True, "Partial seed should be choked immediately");
        Assert.That(peer2.AmChoking, Is.False, "Next eligible leecher should be promoted");
    }

    [Test]
    public void ChokeManager_CanUnchoke_should_return_false_for_partial_seed()
    {
        var partialSeed = CreatePeer("hash1", 1001);
        partialSeed.IsPartialSeed = true;

        Assert.That(_chokeManager.CanUnchoke(partialSeed), Is.False);
    }
}
