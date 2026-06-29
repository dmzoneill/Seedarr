using System;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Extensions;

namespace NzbDrone.Core.Test.Peers.Extensions;

[TestFixture]
public class FastExtensionHandlerTest
{
    private FastExtensionHandler _handler;
    private byte[] _infoHash;

    [SetUp]
    public void Setup()
    {
        _handler = new FastExtensionHandler();
        _infoHash = new byte[20];
        for (var i = 0; i < 20; i++)
        {
            _infoHash[i] = (byte)(i + 1);
        }
    }

    private static PeerConnection CreateTestConnection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        var serverClient = listener.AcceptTcpClient();
        listener.Stop();
        return new PeerConnection(serverClient);
    }

    [Test]
    public void ComputeAllowedFastSet_should_return_empty_when_piece_count_is_zero()
    {
        var result = _handler.ComputeAllowedFastSet("192.168.1.100", _infoHash, 0, 10);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ComputeAllowedFastSet_should_return_empty_when_piece_count_is_negative()
    {
        var result = _handler.ComputeAllowedFastSet("192.168.1.100", _infoHash, -5, 10);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ComputeAllowedFastSet_should_use_default_set_size_when_zero()
    {
        var result = _handler.ComputeAllowedFastSet("192.168.1.100", _infoHash, 1000, 0);

        Assert.That(result.Count, Is.EqualTo(10));
    }

    [Test]
    public void ComputeAllowedFastSet_should_return_correct_count()
    {
        var result = _handler.ComputeAllowedFastSet("10.0.0.1", _infoHash, 1000, 5);

        Assert.That(result.Count, Is.EqualTo(5));
    }

    [Test]
    public void ComputeAllowedFastSet_should_return_deterministic_result()
    {
        var result1 = _handler.ComputeAllowedFastSet("10.0.0.1", _infoHash, 1000, 10);
        var result2 = _handler.ComputeAllowedFastSet("10.0.0.1", _infoHash, 1000, 10);

        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void ComputeAllowedFastSet_should_return_indices_within_piece_count()
    {
        var pieceCount = 100;
        var result = _handler.ComputeAllowedFastSet("10.0.0.1", _infoHash, pieceCount, 10);

        Assert.That(result, Is.All.GreaterThanOrEqualTo(0));
        Assert.That(result, Is.All.LessThan(pieceCount));
    }

    [Test]
    public void ComputeAllowedFastSet_should_return_empty_for_invalid_ip()
    {
        var result = _handler.ComputeAllowedFastSet("not-an-ip", _infoHash, 1000, 10);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ComputeAllowedFastSet_should_return_empty_for_ipv6()
    {
        var result = _handler.ComputeAllowedFastSet("::1", _infoHash, 1000, 10);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ComputeAllowedFastSet_should_produce_same_result_for_same_subnet()
    {
        var result1 = _handler.ComputeAllowedFastSet("192.168.1.1", _infoHash, 1000, 10);
        var result2 = _handler.ComputeAllowedFastSet("192.168.1.2", _infoHash, 1000, 10);

        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void SerializeHaveAll_should_return_correct_message_type()
    {
        var message = _handler.SerializeHaveAll();

        Assert.That(message.Type, Is.EqualTo((PeerMessageType)FastMessageType.HaveAll));
    }

    [Test]
    public void SerializeHaveAll_should_have_empty_payload()
    {
        var message = _handler.SerializeHaveAll();

        Assert.That(message.Payload, Is.Empty);
    }

    [Test]
    public void SerializeHaveNone_should_return_correct_message_type()
    {
        var message = _handler.SerializeHaveNone();

        Assert.That(message.Type, Is.EqualTo((PeerMessageType)FastMessageType.HaveNone));
    }

    [Test]
    public void SerializeSuggestPiece_should_have_4_byte_payload()
    {
        var message = _handler.SerializeSuggestPiece(42);

        Assert.That(message.Payload.Length, Is.EqualTo(4));
    }

    [Test]
    public void SerializeSuggestPiece_should_encode_piece_index_big_endian()
    {
        var message = _handler.SerializeSuggestPiece(0x01020304);

        Assert.That(message.Payload[0], Is.EqualTo(0x01));
        Assert.That(message.Payload[1], Is.EqualTo(0x02));
        Assert.That(message.Payload[2], Is.EqualTo(0x03));
        Assert.That(message.Payload[3], Is.EqualTo(0x04));
    }

    [Test]
    public void SerializeRejectRequest_should_have_12_byte_payload()
    {
        var message = _handler.SerializeRejectRequest(1, 0, 16384);

        Assert.That(message.Payload.Length, Is.EqualTo(12));
    }

    [Test]
    public void SerializeRejectRequest_should_encode_all_fields_big_endian()
    {
        var message = _handler.SerializeRejectRequest(0x00000001, 0x00001000, 0x00004000);

        Assert.That(message.Payload[0], Is.EqualTo(0x00));
        Assert.That(message.Payload[1], Is.EqualTo(0x00));
        Assert.That(message.Payload[2], Is.EqualTo(0x00));
        Assert.That(message.Payload[3], Is.EqualTo(0x01));
        Assert.That(message.Payload[4], Is.EqualTo(0x00));
        Assert.That(message.Payload[5], Is.EqualTo(0x00));
        Assert.That(message.Payload[6], Is.EqualTo(0x10));
        Assert.That(message.Payload[7], Is.EqualTo(0x00));
        Assert.That(message.Payload[8], Is.EqualTo(0x00));
        Assert.That(message.Payload[9], Is.EqualTo(0x00));
        Assert.That(message.Payload[10], Is.EqualTo(0x40));
        Assert.That(message.Payload[11], Is.EqualTo(0x00));
    }

    [Test]
    public void SerializeAllowedFast_should_encode_piece_index()
    {
        var message = _handler.SerializeAllowedFast(256);

        Assert.That(message.Type, Is.EqualTo((PeerMessageType)FastMessageType.AllowedFast));
        Assert.That(message.Payload.Length, Is.EqualTo(4));
        Assert.That(message.Payload[0], Is.EqualTo(0x00));
        Assert.That(message.Payload[1], Is.EqualTo(0x00));
        Assert.That(message.Payload[2], Is.EqualTo(0x01));
        Assert.That(message.Payload[3], Is.EqualTo(0x00));
    }

    [Test]
    public void Deserialize_should_parse_have_all()
    {
        var peerMessage = new PeerMessage
        {
            Type = (PeerMessageType)FastMessageType.HaveAll,
            Payload = Array.Empty<byte>()
        };

        var result = _handler.Deserialize(peerMessage);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Type, Is.EqualTo(FastMessageType.HaveAll));
    }

    [Test]
    public void Deserialize_should_parse_suggest_piece()
    {
        var peerMessage = _handler.SerializeSuggestPiece(99);

        var result = _handler.Deserialize(peerMessage);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Type, Is.EqualTo(FastMessageType.SuggestPiece));
        Assert.That(result.PieceIndex, Is.EqualTo(99));
    }

    [Test]
    public void Deserialize_should_return_null_for_short_suggest_piece_payload()
    {
        var peerMessage = new PeerMessage
        {
            Type = (PeerMessageType)FastMessageType.SuggestPiece,
            Payload = new byte[2]
        };

        var result = _handler.Deserialize(peerMessage);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Deserialize_should_parse_reject_request()
    {
        var peerMessage = _handler.SerializeRejectRequest(5, 1024, 16384);

        var result = _handler.Deserialize(peerMessage);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Type, Is.EqualTo(FastMessageType.RejectRequest));
        Assert.That(result.PieceIndex, Is.EqualTo(5));
        Assert.That(result.Begin, Is.EqualTo(1024));
        Assert.That(result.Length, Is.EqualTo(16384));
    }

    [Test]
    public void Deserialize_should_return_null_for_short_reject_request()
    {
        var peerMessage = new PeerMessage
        {
            Type = (PeerMessageType)FastMessageType.RejectRequest,
            Payload = new byte[8]
        };

        var result = _handler.Deserialize(peerMessage);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Deserialize_should_return_null_for_unknown_type()
    {
        var peerMessage = new PeerMessage
        {
            Type = (PeerMessageType)0xFF,
            Payload = Array.Empty<byte>()
        };

        var result = _handler.Deserialize(peerMessage);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void BuildRejectForRequest_should_return_null_for_null_payload()
    {
        var result = _handler.BuildRejectForRequest(null);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void BuildRejectForRequest_should_return_null_for_short_payload()
    {
        var result = _handler.BuildRejectForRequest(new byte[8]);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void BuildRejectForRequest_should_return_reject_message()
    {
        var requestPayload = new byte[12];
        requestPayload[3] = 7;
        requestPayload[7] = 0;
        requestPayload[8] = 0;
        requestPayload[9] = 0;
        requestPayload[10] = 0x40;
        requestPayload[11] = 0x00;

        var result = _handler.BuildRejectForRequest(requestPayload);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Type, Is.EqualTo((PeerMessageType)FastMessageType.RejectRequest));
        Assert.That(result.Payload.Length, Is.EqualTo(12));
    }

    [Test]
    public void IsFastPeer_should_return_false_for_unregistered_peer()
    {
        using var connection = CreateTestConnection();

        var result = _handler.IsFastPeer(connection);

        Assert.That(result, Is.False);
    }

    [Test]
    public void GetAllowedFastSet_should_return_empty_for_unregistered_peer()
    {
        using var connection = CreateTestConnection();

        var result = _handler.GetAllowedFastSet(connection);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void UnregisterPeer_should_remove_peer()
    {
        using var connection = CreateTestConnection();
        _handler.RegisterFastPeer(connection, _infoHash, 1000, 10);

        _handler.UnregisterPeer(connection);

        Assert.That(_handler.IsFastPeer(connection), Is.False);
        Assert.That(_handler.GetAllowedFastSet(connection), Is.Empty);
    }

    [Test]
    public void Deserialize_should_parse_have_none()
    {
        var peerMessage = new PeerMessage
        {
            Type = (PeerMessageType)FastMessageType.HaveNone,
            Payload = Array.Empty<byte>()
        };

        var result = _handler.Deserialize(peerMessage);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Type, Is.EqualTo(FastMessageType.HaveNone));
    }

    [Test]
    public void Deserialize_should_parse_allowed_fast()
    {
        var peerMessage = _handler.SerializeAllowedFast(42);

        var result = _handler.Deserialize(peerMessage);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Type, Is.EqualTo(FastMessageType.AllowedFast));
        Assert.That(result.PieceIndex, Is.EqualTo(42));
    }

    [Test]
    public void Deserialize_should_return_null_for_null_payload_on_allowed_fast()
    {
        var peerMessage = new PeerMessage
        {
            Type = (PeerMessageType)FastMessageType.AllowedFast,
            Payload = null
        };

        var result = _handler.Deserialize(peerMessage);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void SerializeHaveNone_should_have_empty_payload()
    {
        var message = _handler.SerializeHaveNone();

        Assert.That(message.Payload, Is.Empty);
    }

    [Test]
    public void SerializeSuggestPiece_should_return_correct_message_type()
    {
        var message = _handler.SerializeSuggestPiece(0);

        Assert.That(message.Type, Is.EqualTo((PeerMessageType)FastMessageType.SuggestPiece));
    }

    [Test]
    public void SerializeRejectRequest_should_return_correct_message_type()
    {
        var message = _handler.SerializeRejectRequest(0, 0, 0);

        Assert.That(message.Type, Is.EqualTo((PeerMessageType)FastMessageType.RejectRequest));
    }

    [Test]
    public void ComputeAllowedFastSet_should_differ_for_different_subnets()
    {
        var result1 = _handler.ComputeAllowedFastSet("192.168.1.1", _infoHash, 1000, 10);
        var result2 = _handler.ComputeAllowedFastSet("10.0.0.1", _infoHash, 1000, 10);

        Assert.That(result1, Is.Not.EqualTo(result2));
    }

    [Test]
    public void BuildRejectForRequest_should_preserve_fields_from_request()
    {
        var original = _handler.SerializeRejectRequest(42, 8192, 16384);

        var reject = _handler.BuildRejectForRequest(original.Payload);
        var parsed = _handler.Deserialize(reject);

        Assert.That(parsed.PieceIndex, Is.EqualTo(42));
        Assert.That(parsed.Begin, Is.EqualTo(8192));
        Assert.That(parsed.Length, Is.EqualTo(16384));
    }

    [Test]
    public void HandleMessage_should_handle_have_all_without_error()
    {
        using var connection = CreateTestConnection();
        var message = _handler.SerializeHaveAll();

        Assert.DoesNotThrow(() => _handler.HandleMessage(connection, message, 100));
    }

    [Test]
    public void HandleMessage_should_handle_have_none_without_error()
    {
        using var connection = CreateTestConnection();
        var message = _handler.SerializeHaveNone();

        Assert.DoesNotThrow(() => _handler.HandleMessage(connection, message, 100));
    }

    [Test]
    public void HandleMessage_should_handle_suggest_piece_without_error()
    {
        using var connection = CreateTestConnection();
        var message = _handler.SerializeSuggestPiece(5);

        Assert.DoesNotThrow(() => _handler.HandleMessage(connection, message, 100));
    }

    [Test]
    public void HandleMessage_should_handle_reject_request_without_error()
    {
        using var connection = CreateTestConnection();
        var message = _handler.SerializeRejectRequest(1, 0, 16384);

        Assert.DoesNotThrow(() => _handler.HandleMessage(connection, message, 100));
    }

    [Test]
    public void HandleMessage_should_record_allowed_fast_piece()
    {
        using var connection = CreateTestConnection();
        var message = _handler.SerializeAllowedFast(77);

        _handler.HandleMessage(connection, message, 100);

        var set = _handler.GetAllowedFastSet(connection);
        Assert.That(set, Does.Contain(77));
    }

    [Test]
    public void HandleMessage_should_ignore_invalid_message()
    {
        using var connection = CreateTestConnection();
        var message = new PeerMessage
        {
            Type = (PeerMessageType)0xFF,
            Payload = Array.Empty<byte>()
        };

        Assert.DoesNotThrow(() => _handler.HandleMessage(connection, message, 100));
    }

    [Test]
    public void HandleMessage_should_accumulate_allowed_fast_pieces()
    {
        using var connection = CreateTestConnection();

        _handler.HandleMessage(connection, _handler.SerializeAllowedFast(10), 100);
        _handler.HandleMessage(connection, _handler.SerializeAllowedFast(20), 100);
        _handler.HandleMessage(connection, _handler.SerializeAllowedFast(30), 100);

        var set = _handler.GetAllowedFastSet(connection);
        Assert.That(set.Count, Is.EqualTo(3));
        Assert.That(set, Does.Contain(10));
        Assert.That(set, Does.Contain(20));
        Assert.That(set, Does.Contain(30));
    }

    [Test]
    public void RegisterFastPeer_should_mark_peer_as_fast()
    {
        using var connection = CreateTestConnection();

        _handler.RegisterFastPeer(connection, _infoHash, 1000, 5);

        Assert.That(_handler.IsFastPeer(connection), Is.True);
    }

    [Test]
    public void RegisterFastPeer_should_populate_allowed_fast_set()
    {
        using var connection = CreateTestConnection();

        _handler.RegisterFastPeer(connection, _infoHash, 1000, 5);

        var set = _handler.GetAllowedFastSet(connection);
        Assert.That(set, Is.Not.Empty);
    }

    [Test]
    public void GetAllowedFastSet_should_return_copy_not_reference()
    {
        using var connection = CreateTestConnection();
        _handler.RegisterFastPeer(connection, _infoHash, 1000, 5);

        var set1 = _handler.GetAllowedFastSet(connection);
        var set2 = _handler.GetAllowedFastSet(connection);

        Assert.That(set1, Is.EqualTo(set2));
        Assert.That(ReferenceEquals(set1, set2), Is.False);
    }

    [Test]
    public void SendHaveAllOrBitfield_should_send_have_all_for_fast_peer_with_all_pieces()
    {
        using var connection = CreateTestConnection();
        _handler.RegisterFastPeer(connection, _infoHash, 100, 5);

        Assert.DoesNotThrow(() => _handler.SendHaveAllOrBitfield(connection, 100, true));
    }

    [Test]
    public void SendHaveAllOrBitfield_should_send_bitfield_for_fast_peer_without_all_pieces()
    {
        using var connection = CreateTestConnection();
        _handler.RegisterFastPeer(connection, _infoHash, 100, 5);

        Assert.DoesNotThrow(() => _handler.SendHaveAllOrBitfield(connection, 100, false));
    }

    [Test]
    public void SendHaveAllOrBitfield_should_send_bitfield_for_non_fast_peer()
    {
        using var connection = CreateTestConnection();

        Assert.DoesNotThrow(() => _handler.SendHaveAllOrBitfield(connection, 100, true));
    }

    [Test]
    public void ComputeAllowedFastSet_should_cap_at_piece_count_when_few_pieces()
    {
        var result = _handler.ComputeAllowedFastSet("192.168.1.100", _infoHash, 3, 10);

        Assert.That(result.Count, Is.LessThanOrEqualTo(3));
    }

    [Test]
    public void ComputeAllowedFastSet_should_produce_unique_indices()
    {
        var result = _handler.ComputeAllowedFastSet("192.168.1.100", _infoHash, 10000, 20);

        Assert.That(result.Count, Is.EqualTo(20));
    }

    [Test]
    public void Deserialize_should_return_null_for_short_allowed_fast_payload()
    {
        var peerMessage = new PeerMessage
        {
            Type = (PeerMessageType)FastMessageType.AllowedFast,
            Payload = new byte[2]
        };

        var result = _handler.Deserialize(peerMessage);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Deserialize_should_return_null_for_null_suggest_piece_payload()
    {
        var peerMessage = new PeerMessage
        {
            Type = (PeerMessageType)FastMessageType.SuggestPiece,
            Payload = null
        };

        var result = _handler.Deserialize(peerMessage);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Deserialize_should_return_null_for_null_reject_request_payload()
    {
        var peerMessage = new PeerMessage
        {
            Type = (PeerMessageType)FastMessageType.RejectRequest,
            Payload = null
        };

        var result = _handler.Deserialize(peerMessage);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ComputeAllowedFastSet_should_handle_negative_set_size()
    {
        var result = _handler.ComputeAllowedFastSet("192.168.1.100", _infoHash, 1000, -5);

        Assert.That(result.Count, Is.EqualTo(10));
    }

    [Test]
    public void UnregisterPeer_should_not_throw_for_unregistered_peer()
    {
        using var connection = CreateTestConnection();

        Assert.DoesNotThrow(() => _handler.UnregisterPeer(connection));
    }

    [Test]
    public void ComputeAllowedFastSet_should_return_empty_for_null_ip()
    {
        var result = _handler.ComputeAllowedFastSet(null, _infoHash, 1000, 10);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ComputeAllowedFastSet_should_return_empty_for_empty_ip()
    {
        var result = _handler.ComputeAllowedFastSet("", _infoHash, 1000, 10);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void RegisterFastPeer_should_overwrite_set_on_re_registration()
    {
        using var connection = CreateTestConnection();

        _handler.RegisterFastPeer(connection, _infoHash, 1000, 5);
        var firstSetSize = _handler.GetAllowedFastSet(connection).Count;

        // Re-register with different hash — set should be replaced, not merged
        var zeroHash = new byte[20];
        _handler.RegisterFastPeer(connection, zeroHash, 1000, 3);
        var secondSet = _handler.GetAllowedFastSet(connection);

        Assert.That(_handler.IsFastPeer(connection), Is.True);
        Assert.That(secondSet.Count, Is.LessThanOrEqualTo(3));
    }

    [Test]
    public void RegisterFastPeer_two_connections_maintain_independent_sets()
    {
        using var conn1 = CreateTestConnection();
        using var conn2 = CreateTestConnection();

        _handler.RegisterFastPeer(conn1, _infoHash, 1000, 5);
        _handler.RegisterFastPeer(conn2, _infoHash, 1000, 7);

        Assert.That(_handler.IsFastPeer(conn1), Is.True);
        Assert.That(_handler.IsFastPeer(conn2), Is.True);

        _handler.UnregisterPeer(conn1);

        Assert.That(_handler.IsFastPeer(conn1), Is.False);
        Assert.That(_handler.GetAllowedFastSet(conn1), Is.Empty);
        Assert.That(_handler.IsFastPeer(conn2), Is.True);
        Assert.That(_handler.GetAllowedFastSet(conn2), Is.Not.Empty);
    }

    [Test]
    public void SendHaveAllOrBitfield_should_use_else_branch_for_fast_peer_with_zero_piece_count()
    {
        using var connection = CreateTestConnection();
        _handler.RegisterFastPeer(connection, _infoHash, 100, 5);

        // pieceCount=0 fails the condition (IsFastPeer && pieceCount > 0 && !allPiecesAvailable),
        // so execution falls to the else branch regardless of fast-peer status.
        Assert.DoesNotThrow(() => _handler.SendHaveAllOrBitfield(connection, 0, false));
    }

    [Test]
    public void HandleMessage_AllowedFast_should_augment_set_of_already_registered_fast_peer()
    {
        using var connection = CreateTestConnection();

        // Seed with 1 allowed-fast piece via RegisterFastPeer
        _handler.RegisterFastPeer(connection, _infoHash, 1000, 1);
        var initialCount = _handler.GetAllowedFastSet(connection).Count;

        // Inject a known piece via HandleMessage — tests the "key already exists in _fastSets"
        // branch inside RecordAllowedFastPiece when called after RegisterFastPeer
        const int extraPiece = 999;
        _handler.HandleMessage(connection, _handler.SerializeAllowedFast(extraPiece), 1000);

        var finalSet = _handler.GetAllowedFastSet(connection);
        Assert.That(finalSet, Does.Contain(extraPiece));
        Assert.That(finalSet.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void UnregisterPeer_then_re_register_should_produce_fresh_fast_set()
    {
        using var connection = CreateTestConnection();

        _handler.RegisterFastPeer(connection, _infoHash, 1000, 5);
        _handler.UnregisterPeer(connection);

        Assert.That(_handler.IsFastPeer(connection), Is.False);

        _handler.RegisterFastPeer(connection, _infoHash, 1000, 3);

        Assert.That(_handler.IsFastPeer(connection), Is.True);
        Assert.That(_handler.GetAllowedFastSet(connection).Count, Is.LessThanOrEqualTo(3));
    }

    [Test]
    public void ComputeAllowedFastSet_should_return_set_of_exactly_one_when_setsize_is_one()
    {
        var result = _handler.ComputeAllowedFastSet("10.0.0.1", _infoHash, 1000, 1);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result, Is.All.GreaterThanOrEqualTo(0));
        Assert.That(result, Is.All.LessThan(1000));
    }

    [Test]
    public void HandleMessage_should_handle_null_deserialization_result_without_error()
    {
        using var connection = CreateTestConnection();

        // A message with payload too short for SuggestPiece returns null from Deserialize,
        // and HandleMessage should silently return without touching the connection.
        var badMessage = new PeerMessage
        {
            Type = (PeerMessageType)FastMessageType.SuggestPiece,
            Payload = new byte[1]
        };

        Assert.DoesNotThrow(() => _handler.HandleMessage(connection, badMessage, 100));
        Assert.That(_handler.GetAllowedFastSet(connection), Is.Empty);
    }
}
