using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using NUnit.Framework;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Extensions;

namespace NzbDrone.Core.Test.Peers.Extensions;

[TestFixture]
public class FastExtensionTest
{
    private FastExtensionHandler _handler;

    [SetUp]
    public void SetUp()
    {
        _handler = new FastExtensionHandler();
    }

    private static PeerConnection CreateConnection(string remoteIp = "127.0.0.1", int remotePort = 6881)
    {
        var ms = new MemoryStream();
        return new PeerConnection(ms, remoteIp, remotePort);
    }

    [Test]
    public void ComputeAllowedFastSet_should_compute_correct_deterministic_piece_indices_according_to_bep6()
    {
        // BEP 6 canonical test vector:
        // IP 80.4.4.200, 1313 pieces, infohash: 20 bytes of 0xaa
        var ip = IPAddress.Parse("80.4.4.200");
        var infoHash = new byte[20];
        Array.Fill(infoHash, (byte)0xAA);

        // 7 piece allowed fast set for torrent with 1313 pieces: 1059, 431, 808, 1217, 287, 376, 1188
        var result7 = _handler.ComputeAllowedFastSet(infoHash, ip, 1313, 7);
        var expected7 = new[] { 1059, 431, 808, 1217, 287, 376, 1188 };
        Assert.That(result7.Count, Is.EqualTo(7));
        Assert.That(result7, Is.EquivalentTo(expected7));

        // 9 piece allowed fast set for torrent with 1313 pieces: 1059, 431, 808, 1217, 287, 376, 1188, 353, 508
        var result9 = _handler.ComputeAllowedFastSet(infoHash, ip, 1313, 9);
        var expected9 = new[] { 1059, 431, 808, 1217, 287, 376, 1188, 353, 508 };
        Assert.That(result9.Count, Is.EqualTo(9));
        Assert.That(result9, Is.EquivalentTo(expected9));

        // Also test string overload
        var resultStringOverload = _handler.ComputeAllowedFastSet("80.4.4.200", infoHash, 1313, 7);
        Assert.That(resultStringOverload, Is.EquivalentTo(expected7));
    }

    [Test]
    public void HandleMessage_suggest_piece_should_add_index_to_suggested_pieces()
    {
        using var connection = CreateConnection();
        var message = _handler.SerializeSuggestPiece(42);

        _handler.HandleMessage(connection, message, 100);

        Assert.That(connection.SuggestedPieces, Contains.Item(42));
        Assert.That(connection.SuggestedPieces.Count, Is.EqualTo(1));

        // Suggest another piece
        var message2 = _handler.SerializeSuggestPiece(99);
        _handler.HandleMessage(connection, message2, 100);

        Assert.That(connection.SuggestedPieces, Contains.Item(99));
        Assert.That(connection.SuggestedPieces.Count, Is.EqualTo(2));
    }

    [Test]
    public void HandleMessage_allowed_fast_should_add_index_to_allowed_fast_pieces()
    {
        using var connection = CreateConnection();
        var message = _handler.SerializeAllowedFast(15);

        _handler.HandleMessage(connection, message, 100);

        Assert.That(connection.AllowedFastPieces, Contains.Item(15));
        Assert.That(connection.AllowedFastPieces.Count, Is.EqualTo(1));

        // Add another allowed fast piece
        var message2 = _handler.SerializeAllowedFast(27);
        _handler.HandleMessage(connection, message2, 100);

        Assert.That(connection.AllowedFastPieces, Contains.Item(27));
        Assert.That(connection.AllowedFastPieces.Count, Is.EqualTo(2));
    }

    [Test]
    public void HandleMessage_have_all_should_mark_connection_having_all_pieces()
    {
        using var connection = CreateConnection();
        var message = _handler.SerializeHaveAll();

        _handler.HandleMessage(connection, message, 50);

        Assert.That(connection.Progress, Is.EqualTo(1.0));
        Assert.That(connection.PeerPieces, Is.Not.Null);
        Assert.That(connection.PeerPieces.Length, Is.EqualTo(50));
        Assert.That(connection.PeerPieces, Is.All.True);
        Assert.That(connection.IsSeed, Is.True);
    }

    [Test]
    public void HandleMessage_have_none_should_mark_connection_having_zero_pieces()
    {
        using var connection = CreateConnection();
        connection.PeerPieces = new bool[50];
        Array.Fill(connection.PeerPieces, true);
        connection.Progress = 1.0;

        var message = _handler.SerializeHaveNone();
        _handler.HandleMessage(connection, message, 50);

        Assert.That(connection.Progress, Is.EqualTo(0.0));
        Assert.That(connection.PeerPieces, Is.Not.Null);
        Assert.That(connection.PeerPieces.Length, Is.EqualTo(50));
        Assert.That(connection.PeerPieces, Is.All.False);
    }

    [Test]
    public void SendHaveAllOrBitfield_should_send_have_none_when_has_none_is_true()
    {
        using var connection = CreateConnection();
        var infoHash = new byte[20];
        _handler.RegisterFastPeer(connection, infoHash, 100, 5);

        var ms = (MemoryStream)typeof(PeerConnection)
            .GetField("_activeStream", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(connection)!;
        ms.SetLength(0);

        _handler.SendHaveAllOrBitfield(connection, 100, hasAll: false, hasNone: true);

        var data = ms.ToArray();
        Assert.That(data.Length, Is.GreaterThanOrEqualTo(5));
        var msgType = (FastMessageType)data[4];
        Assert.That(msgType, Is.EqualTo(FastMessageType.HaveNone));
    }

    [Test]
    public void SendHaveAllOrBitfield_should_send_have_all_when_has_all_is_true()
    {
        using var connection = CreateConnection();
        var infoHash = new byte[20];
        _handler.RegisterFastPeer(connection, infoHash, 100, 5);

        var ms = (MemoryStream)typeof(PeerConnection)
            .GetField("_activeStream", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(connection)!;
        ms.SetLength(0);

        _handler.SendHaveAllOrBitfield(connection, 100, hasAll: true, hasNone: false);

        var data = ms.ToArray();
        Assert.That(data.Length, Is.GreaterThanOrEqualTo(5));
        var msgType = (FastMessageType)data[4];
        Assert.That(msgType, Is.EqualTo(FastMessageType.HaveAll));
    }

    [Test]
    public void SendHaveAllOrBitfield_should_send_dynamic_bitfield_when_partial_bitfield_provided()
    {
        using var connection = CreateConnection();
        var ms = (MemoryStream)typeof(PeerConnection)
            .GetField("_activeStream", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(connection)!;
        ms.SetLength(0);

        var dynamicBitfield = new byte[] { 0xA0, 0x05 };
        _handler.SendHaveAllOrBitfield(connection, 16, hasAll: false, hasNone: false, bitfield: dynamicBitfield);

        var data = ms.ToArray();
        Assert.That(data.Length, Is.GreaterThanOrEqualTo(5));
        Assert.That(data[4], Is.EqualTo((byte)PeerMessageType.Bitfield));
        Assert.That(data[5..], Is.EqualTo(dynamicBitfield));
    }

    [Test]
    public void HandleMessage_reject_request_should_decrement_pending_request_count()
    {
        using var connection = CreateConnection();
        connection.PendingRequestCount = 5;

        var message = _handler.SerializeRejectRequest(1, 0, 16384);
        _handler.HandleMessage(connection, message, 100);

        Assert.That(connection.PendingRequestCount, Is.EqualTo(4));
    }

    [Test]
    public void HandleMessage_reject_request_should_not_decrement_pending_request_count_below_zero()
    {
        using var connection = CreateConnection();
        connection.PendingRequestCount = 0;

        var message = _handler.SerializeRejectRequest(1, 0, 16384);
        _handler.HandleMessage(connection, message, 100);

        Assert.That(connection.PendingRequestCount, Is.EqualTo(0));
    }

    [Test]
    public void HandleMessage_reject_request_should_fire_on_request_rejected_event()
    {
        using var connection = CreateConnection();
        PeerConnection eventConnection = null;
        var eventPiece = -1;
        var eventBegin = -1;
        var eventLength = -1;

        _handler.OnRequestRejected += (conn, piece, begin, length) =>
        {
            eventConnection = conn;
            eventPiece = piece;
            eventBegin = begin;
            eventLength = length;
        };

        var message = _handler.SerializeRejectRequest(3, 16384, 16384);
        _handler.HandleMessage(connection, message, 100);

        Assert.That(eventConnection, Is.EqualTo(connection));
        Assert.That(eventPiece, Is.EqualTo(3));
        Assert.That(eventBegin, Is.EqualTo(16384));
        Assert.That(eventLength, Is.EqualTo(16384));
    }

    [Test]
    public void HandleMessage_reject_request_releases_block_in_piece_picker_for_immediate_reassignment_without_waiting_for_timeout()
    {
        using var peer1 = CreateConnection("1.2.3.4", 5000);
        using var peer2 = CreateConnection("5.6.7.8", 5001);
        peer1.PeerChoking = false;
        peer2.PeerChoking = false;
        peer1.PeerPieces = new bool[10];
        peer2.PeerPieces = new bool[10];
        peer1.PeerPieces[2] = true;
        peer2.PeerPieces[2] = true;

        var picker = new PiecePicker();
        picker.AddActivePiece(2, 32768, 16384);

        // Assign block to peer1
        var block = picker.RequestBlock(peer1, 2);
        Assert.That(block, Is.Not.Null);
        Assert.That(block.IsRequested, Is.True);
        Assert.That(block.RequestedFrom, Is.EqualTo(peer1));
        Assert.That(peer1.PendingRequestCount, Is.EqualTo(1));

        // Connect handler to picker
        _handler.OnRequestRejected += (conn, piece, begin, length) =>
        {
            picker.OnBlockRejected(conn, piece, begin, length);
        };

        // Peer1 rejects the request
        var rejectMsg = _handler.SerializeRejectRequest(block.PieceIndex, block.Begin, block.Length);
        _handler.HandleMessage(peer1, rejectMsg, 10);

        // Verify PendingRequestCount decremented on peer1
        Assert.That(peer1.PendingRequestCount, Is.EqualTo(0));

        // Verify block released in active piece block map immediately without timeout
        Assert.That(block.IsRequested, Is.False);
        Assert.That(block.RequestedFrom, Is.Null);

        // Verify block can immediately be reassigned to peer2
        var reassignedBlock = picker.RequestBlock(peer2, 2);
        Assert.That(reassignedBlock, Is.Not.Null);
        Assert.That(reassignedBlock.PieceIndex, Is.EqualTo(block.PieceIndex));
        Assert.That(reassignedBlock.Begin, Is.EqualTo(block.Begin));
        Assert.That(reassignedBlock.RequestedFrom, Is.EqualTo(peer2));
        Assert.That(peer2.PendingRequestCount, Is.EqualTo(1));
    }

    [Test]
    public void SerializeRejectRequest_should_format_12_byte_payload_with_index_begin_length_in_big_endian_order()
    {
        var message = _handler.SerializeRejectRequest(0x01020304, 0x05060708, 0x090A0B0C);

        Assert.That(message.Type, Is.EqualTo((PeerMessageType)FastMessageType.RejectRequest));
        Assert.That(message.Payload, Is.Not.Null);
        Assert.That(message.Payload.Length, Is.EqualTo(12));

        Assert.That(message.Payload[0], Is.EqualTo(0x01));
        Assert.That(message.Payload[1], Is.EqualTo(0x02));
        Assert.That(message.Payload[2], Is.EqualTo(0x03));
        Assert.That(message.Payload[3], Is.EqualTo(0x04));

        Assert.That(message.Payload[4], Is.EqualTo(0x05));
        Assert.That(message.Payload[5], Is.EqualTo(0x06));
        Assert.That(message.Payload[6], Is.EqualTo(0x07));
        Assert.That(message.Payload[7], Is.EqualTo(0x08));

        Assert.That(message.Payload[8], Is.EqualTo(0x09));
        Assert.That(message.Payload[9], Is.EqualTo(0x0A));
        Assert.That(message.Payload[10], Is.EqualTo(0x0B));
        Assert.That(message.Payload[11], Is.EqualTo(0x0C));
    }
}
