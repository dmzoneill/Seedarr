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
}
