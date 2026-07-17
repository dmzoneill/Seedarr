using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.TrackerServer;

namespace NzbDrone.Core.Test.TrackerServer;

[TestFixture]
public class UdpTrackerServerTest
{
    private const long ProtocolMagic = 0x41727101980;

    private UdpTrackerServer _udpTrackerServer;
    private IPeerDatabase _peerDatabase;
    private IConfigService _configService;

    [SetUp]
    public void Setup()
    {
        _peerDatabase = Substitute.For<IPeerDatabase>();
        _configService = Substitute.For<IConfigService>();

        _configService.TrackerServerEnabled.Returns(true);
        _configService.TrackerUdpEnabled.Returns(true);
        _configService.TrackerAnnounceInterval.Returns(1800);
        _configService.TrackerMaxPeersPerAnnounce.Returns(50);
        _configService.TrackerLogAnnounces.Returns(false);
        _configService.TrackerEnableScrape.Returns(true);
        _configService.TrackerRateLimitPerMinute.Returns(60);

        _udpTrackerServer = new UdpTrackerServer(_peerDatabase, _configService);
    }

    private byte[] InvokeHandleConnect(long connectionId, int transactionId)
    {
        var method = typeof(UdpTrackerServer).GetMethod(
            "HandleConnect",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (byte[])method.Invoke(_udpTrackerServer, new object[] { connectionId, transactionId });
    }

    private byte[] InvokeHandleAnnounce(long connectionId, int transactionId, byte[] data, IPEndPoint remote)
    {
        var method = typeof(UdpTrackerServer).GetMethod(
            "HandleAnnounce",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (byte[])method.Invoke(_udpTrackerServer, new object[] { connectionId, transactionId, data, remote });
    }

    private byte[] InvokeHandleScrape(long connectionId, int transactionId, byte[] data)
    {
        var method = typeof(UdpTrackerServer).GetMethod(
            "HandleScrape",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (byte[])method.Invoke(_udpTrackerServer, new object[] { connectionId, transactionId, data });
    }

    private static byte[] InvokeBuildErrorResponse(int transactionId, string message)
    {
        var method = typeof(UdpTrackerServer).GetMethod(
            "BuildErrorResponse",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (byte[])method.Invoke(null, new object[] { transactionId, message });
    }

    private static byte[] InvokeBuildCompactPeers(List<TrackerPeerEntry> peers, string excludeIp, int excludePort, int maxPeers)
    {
        var method = typeof(UdpTrackerServer).GetMethod(
            "BuildCompactPeers",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (byte[])method.Invoke(null, new object[] { peers, excludeIp, excludePort, maxPeers });
    }

    private static string InvokeConvertInfoHashToHex(byte[] data, int offset)
    {
        var method = typeof(UdpTrackerServer).GetMethod(
            "ConvertInfoHashToHex",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (string)method.Invoke(null, new object[] { data, offset });
    }

    private bool InvokeValidateConnectionId(long connectionId)
    {
        var method = typeof(UdpTrackerServer).GetMethod(
            "ValidateConnectionId",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (bool)method.Invoke(_udpTrackerServer, new object[] { connectionId });
    }

    private long InvokeGenerateConnectionId()
    {
        var method = typeof(UdpTrackerServer).GetMethod(
            "GenerateConnectionId",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (long)method.Invoke(_udpTrackerServer, null);
    }

    private bool InvokeIsRateLimited(string ip)
    {
        var method = typeof(UdpTrackerServer).GetMethod(
            "IsRateLimited",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (bool)method.Invoke(_udpTrackerServer, new object[] { ip });
    }

    private void InvokePurgeExpiredConnections()
    {
        var method = typeof(UdpTrackerServer).GetMethod(
            "PurgeExpiredConnections",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_udpTrackerServer, null);
    }

    private void InvokePurgeExpiredRateLimits()
    {
        var method = typeof(UdpTrackerServer).GetMethod(
            "PurgeExpiredRateLimits",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_udpTrackerServer, null);
    }

    private long RegisterValidConnectionId()
    {
        var response = InvokeHandleConnect(ProtocolMagic, 1234);
        return BinaryPrimitives.ReadInt64BigEndian(response.AsSpan(8, 8));
    }

    private static byte[] BuildAnnounceRequest(long connectionId, int transactionId, byte[] infoHash, byte[] peerId, int eventId, int numWant, ushort port)
    {
        var data = new byte[98];
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(0, 8), connectionId);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8, 4), 1);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12, 4), transactionId);
        Buffer.BlockCopy(infoHash, 0, data, 16, 20);
        Buffer.BlockCopy(peerId, 0, data, 36, 20);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(80, 4), eventId);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(92, 4), numWant);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(96, 2), port);
        return data;
    }

    private void AddConnectionEntry(long connId, DateTime created)
    {
        var field = typeof(UdpTrackerServer).GetField("_connectionIds", BindingFlags.NonPublic | BindingFlags.Instance);
        var dict = (IDictionary)field.GetValue(_udpTrackerServer);
        var entryType = typeof(UdpTrackerServer).GetNestedType("ConnectionEntry", BindingFlags.NonPublic);
        var entry = Activator.CreateInstance(entryType);
        entryType.GetProperty("Created").SetValue(entry, created);
        dict[connId] = entry;
    }

    private int GetConnectionIdCount()
    {
        var field = typeof(UdpTrackerServer).GetField("_connectionIds", BindingFlags.NonPublic | BindingFlags.Instance);
        var dict = (IDictionary)field.GetValue(_udpTrackerServer);
        return dict.Count;
    }

    private void AddRateLimitEntry(string ip, int count, DateTime windowStart)
    {
        var field = typeof(UdpTrackerServer).GetField("_rateLimits", BindingFlags.NonPublic | BindingFlags.Instance);
        var dict = (IDictionary)field.GetValue(_udpTrackerServer);
        var entryType = typeof(UdpTrackerServer).GetNestedType("RateLimitEntry", BindingFlags.NonPublic);
        var entry = Activator.CreateInstance(entryType);
        entryType.GetProperty("Count").SetValue(entry, count);
        entryType.GetProperty("WindowStart").SetValue(entry, windowStart);
        dict[ip] = entry;
    }

    private int GetRateLimitCount()
    {
        var field = typeof(UdpTrackerServer).GetField("_rateLimits", BindingFlags.NonPublic | BindingFlags.Instance);
        var dict = (IDictionary)field.GetValue(_udpTrackerServer);
        return dict.Count;
    }

    private void InvokeHandleDatagram(UdpClient client, byte[] buffer, IPEndPoint remote)
    {
        var method = typeof(UdpTrackerServer).GetMethod(
            "HandleDatagram",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var receiveResult = new UdpReceiveResult(buffer, remote);
        method.Invoke(_udpTrackerServer, new object[] { client, receiveResult });
    }

    private static byte[] BuildDatagram(long connectionId, int action, int transactionId)
    {
        var data = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(0, 8), connectionId);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8, 4), action);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12, 4), transactionId);
        return data;
    }

    [Test]
    public void HandleConnect_should_return_error_for_invalid_magic()
    {
        var result = InvokeHandleConnect(0x12345678, 42);

        Assert.That(result, Is.Not.Null);
        var action = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(0, 4));
        Assert.That(action, Is.EqualTo(3));
        var txId = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(4, 4));
        Assert.That(txId, Is.EqualTo(42));
        var message = Encoding.UTF8.GetString(result, 8, result.Length - 8);
        Assert.That(message, Does.Contain("Invalid protocol magic"));
    }

    [Test]
    public void HandleConnect_should_return_16_byte_response_for_valid_magic()
    {
        var result = InvokeHandleConnect(ProtocolMagic, 99);

        Assert.That(result, Has.Length.EqualTo(16));
    }

    [Test]
    public void HandleConnect_should_set_action_to_zero()
    {
        var result = InvokeHandleConnect(ProtocolMagic, 99);

        var action = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(0, 4));
        Assert.That(action, Is.EqualTo(0));
    }

    [Test]
    public void HandleConnect_should_echo_transaction_id()
    {
        var result = InvokeHandleConnect(ProtocolMagic, 12345);

        var txId = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(4, 4));
        Assert.That(txId, Is.EqualTo(12345));
    }

    [Test]
    public void HandleConnect_should_return_unique_connection_ids()
    {
        var result1 = InvokeHandleConnect(ProtocolMagic, 1);
        var result2 = InvokeHandleConnect(ProtocolMagic, 2);

        var connId1 = BinaryPrimitives.ReadInt64BigEndian(result1.AsSpan(8, 8));
        var connId2 = BinaryPrimitives.ReadInt64BigEndian(result2.AsSpan(8, 8));

        Assert.That(connId1, Is.Not.EqualTo(connId2));
    }

    [Test]
    public void HandleConnect_should_not_return_protocol_magic_as_connection_id()
    {
        var result = InvokeHandleConnect(ProtocolMagic, 1);

        var connId = BinaryPrimitives.ReadInt64BigEndian(result.AsSpan(8, 8));
        Assert.That(connId, Is.Not.EqualTo(ProtocolMagic));
    }

    [Test]
    public void ValidateConnectionId_should_return_false_for_unknown_id()
    {
        var result = InvokeValidateConnectionId(999999);

        Assert.That(result, Is.False);
    }

    [Test]
    public void ValidateConnectionId_should_return_true_for_valid_id()
    {
        var connId = RegisterValidConnectionId();

        var result = InvokeValidateConnectionId(connId);

        Assert.That(result, Is.True);
    }

    [Test]
    public void GenerateConnectionId_should_return_non_magic_value()
    {
        var connId = InvokeGenerateConnectionId();

        Assert.That(connId, Is.Not.EqualTo(ProtocolMagic));
    }

    [Test]
    public void GenerateConnectionId_should_return_unique_values()
    {
        var ids = new HashSet<long>();
        for (var i = 0; i < 100; i++)
        {
            ids.Add(InvokeGenerateConnectionId());
        }

        Assert.That(ids.Count, Is.EqualTo(100));
    }

    [Test]
    public void HandleAnnounce_should_return_error_for_invalid_connection_id()
    {
        var data = new byte[98];
        var result = InvokeHandleAnnounce(999999, 42, data, new IPEndPoint(IPAddress.Loopback, 6881));

        var action = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(0, 4));
        Assert.That(action, Is.EqualTo(3));
        var message = Encoding.UTF8.GetString(result, 8, result.Length - 8);
        Assert.That(message, Does.Contain("Invalid connection_id"));
    }

    [Test]
    public void HandleAnnounce_should_return_error_for_short_data()
    {
        var connId = RegisterValidConnectionId();

        var data = new byte[50];
        var result = InvokeHandleAnnounce(connId, 42, data, new IPEndPoint(IPAddress.Loopback, 6881));

        var action = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(0, 4));
        Assert.That(action, Is.EqualTo(3));
        var message = Encoding.UTF8.GetString(result, 8, result.Length - 8);
        Assert.That(message, Does.Contain("Announce request too short"));
    }

    [Test]
    public void HandleAnnounce_should_process_valid_announce()
    {
        var connId = RegisterValidConnectionId();
        var infoHash = new byte[20];
        Array.Fill(infoHash, (byte)0xAB);
        var peerId = new byte[20];
        Array.Fill(peerId, (byte)0x41);

        var expectedHex = Convert.ToHexString(infoHash).ToLowerInvariant();
        _peerDatabase.GetPeers(expectedHex).Returns(new List<TrackerPeerEntry>());
        _peerDatabase.GetStats(expectedHex).Returns(new ScrapeStats { Complete = 1, Incomplete = 0, Downloaded = 1 });

        var data = BuildAnnounceRequest(connId, 42, infoHash, peerId, 2, 50, 6881);
        var result = InvokeHandleAnnounce(connId, 42, data, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881));

        Assert.That(result, Is.Not.Null);
        var action = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(0, 4));
        Assert.That(action, Is.EqualTo(1));
        var txId = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(4, 4));
        Assert.That(txId, Is.EqualTo(42));
        var interval = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(8, 4));
        Assert.That(interval, Is.EqualTo(1800));
    }

    [Test]
    public void HandleAnnounce_should_add_peer_for_started_event()
    {
        var connId = RegisterValidConnectionId();
        var infoHash = new byte[20];
        Array.Fill(infoHash, (byte)0xCC);
        var peerId = new byte[20];
        Array.Fill(peerId, (byte)0x42);

        var expectedHex = Convert.ToHexString(infoHash).ToLowerInvariant();
        _peerDatabase.GetPeers(expectedHex).Returns(new List<TrackerPeerEntry>());
        _peerDatabase.GetStats(expectedHex).Returns(new ScrapeStats());

        var data = BuildAnnounceRequest(connId, 42, infoHash, peerId, 2, 50, 6881);
        InvokeHandleAnnounce(connId, 42, data, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881));

        _peerDatabase.Received(1).AddPeer(expectedHex, "10.0.0.1", 6881, Arg.Any<string>());
    }

    [Test]
    public void HandleAnnounce_should_remove_peer_on_stopped_event()
    {
        var connId = RegisterValidConnectionId();
        var infoHash = new byte[20];
        Array.Fill(infoHash, (byte)0xDD);
        var peerId = new byte[20];
        Array.Fill(peerId, (byte)0x43);

        var expectedHex = Convert.ToHexString(infoHash).ToLowerInvariant();
        _peerDatabase.GetPeers(expectedHex).Returns(new List<TrackerPeerEntry>());
        _peerDatabase.GetStats(expectedHex).Returns(new ScrapeStats());

        var data = BuildAnnounceRequest(connId, 42, infoHash, peerId, 3, 50, 6881);
        InvokeHandleAnnounce(connId, 42, data, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881));

        _peerDatabase.Received(1).RemovePeer(expectedHex, "10.0.0.1", 6881);
        _peerDatabase.DidNotReceive().AddPeer(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>());
    }

    [Test]
    public void HandleAnnounce_should_use_remote_port_when_port_is_zero()
    {
        var connId = RegisterValidConnectionId();
        var infoHash = new byte[20];
        Array.Fill(infoHash, (byte)0xEE);
        var peerId = new byte[20];
        Array.Fill(peerId, (byte)0x44);

        var expectedHex = Convert.ToHexString(infoHash).ToLowerInvariant();
        _peerDatabase.GetPeers(expectedHex).Returns(new List<TrackerPeerEntry>());
        _peerDatabase.GetStats(expectedHex).Returns(new ScrapeStats());

        var data = BuildAnnounceRequest(connId, 42, infoHash, peerId, 2, 50, 0);
        InvokeHandleAnnounce(connId, 42, data, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 12345));

        _peerDatabase.Received(1).AddPeer(expectedHex, "10.0.0.1", 12345, Arg.Any<string>());
    }

    [Test]
    public void HandleAnnounce_should_cap_numwant_to_config_max()
    {
        _configService.TrackerMaxPeersPerAnnounce.Returns(10);
        var connId = RegisterValidConnectionId();
        var infoHash = new byte[20];
        Array.Fill(infoHash, (byte)0xFF);
        var peerId = new byte[20];
        Array.Fill(peerId, (byte)0x45);

        var expectedHex = Convert.ToHexString(infoHash).ToLowerInvariant();

        var peerList = new List<TrackerPeerEntry>();
        for (var i = 0; i < 20; i++)
        {
            peerList.Add(new TrackerPeerEntry { Ip = $"10.0.1.{i}", Port = 6881 });
        }

        _peerDatabase.GetPeers(expectedHex).Returns(peerList);
        _peerDatabase.GetStats(expectedHex).Returns(new ScrapeStats());

        var data = BuildAnnounceRequest(connId, 42, infoHash, peerId, 2, 200, 9999);
        var result = InvokeHandleAnnounce(connId, 42, data, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 9999));

        var compactPeersLength = result.Length - 20;
        Assert.That(compactPeersLength, Is.LessThanOrEqualTo(10 * 6));
    }

    [Test]
    public void HandleAnnounce_should_return_stats_in_response()
    {
        var connId = RegisterValidConnectionId();
        var infoHash = new byte[20];
        Array.Fill(infoHash, (byte)0xAA);
        var peerId = new byte[20];
        Array.Fill(peerId, (byte)0x46);

        var expectedHex = Convert.ToHexString(infoHash).ToLowerInvariant();
        _peerDatabase.GetPeers(expectedHex).Returns(new List<TrackerPeerEntry>());
        _peerDatabase.GetStats(expectedHex).Returns(new ScrapeStats { Complete = 5, Incomplete = 3, Downloaded = 10 });

        var data = BuildAnnounceRequest(connId, 42, infoHash, peerId, 0, 50, 6881);
        var result = InvokeHandleAnnounce(connId, 42, data, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881));

        var leechers = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(12, 4));
        var seeders = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(16, 4));
        Assert.That(leechers, Is.EqualTo(3));
        Assert.That(seeders, Is.EqualTo(5));
    }

    [Test]
    public void HandleScrape_should_return_error_for_invalid_connection_id()
    {
        var data = new byte[36];
        var result = InvokeHandleScrape(999999, 42, data);

        var action = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(0, 4));
        Assert.That(action, Is.EqualTo(3));
        var message = Encoding.UTF8.GetString(result, 8, result.Length - 8);
        Assert.That(message, Does.Contain("Invalid connection_id"));
    }

    [Test]
    public void HandleScrape_should_return_error_when_scrape_disabled()
    {
        _configService.TrackerEnableScrape.Returns(false);
        var connId = RegisterValidConnectionId();

        var data = new byte[36];
        var result = InvokeHandleScrape(connId, 42, data);

        var action = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(0, 4));
        Assert.That(action, Is.EqualTo(3));
        var message = Encoding.UTF8.GetString(result, 8, result.Length - 8);
        Assert.That(message, Does.Contain("Scrape disabled"));
    }

    [Test]
    public void HandleScrape_should_return_error_for_payload_shorter_than_one_hash()
    {
        var connId = RegisterValidConnectionId();

        var data = new byte[16 + 10];
        var result = InvokeHandleScrape(connId, 42, data);

        var action = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(0, 4));
        Assert.That(action, Is.EqualTo(3));
        var message = Encoding.UTF8.GetString(result, 8, result.Length - 8);
        Assert.That(message, Does.Contain("Invalid scrape request"));
    }

    [Test]
    public void HandleScrape_should_return_error_for_misaligned_payload()
    {
        var connId = RegisterValidConnectionId();

        var data = new byte[16 + 25];
        var result = InvokeHandleScrape(connId, 42, data);

        var action = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(0, 4));
        Assert.That(action, Is.EqualTo(3));
    }

    [Test]
    public void HandleScrape_should_process_single_hash()
    {
        var connId = RegisterValidConnectionId();

        var infoHash = new byte[20];
        Array.Fill(infoHash, (byte)0xBB);
        var expectedHex = Convert.ToHexString(infoHash).ToLowerInvariant();

        _peerDatabase.GetStats(expectedHex).Returns(new ScrapeStats { Complete = 3, Incomplete = 1, Downloaded = 7 });

        var data = new byte[16 + 20];
        Buffer.BlockCopy(infoHash, 0, data, 16, 20);

        var result = InvokeHandleScrape(connId, 42, data);

        var action = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(0, 4));
        Assert.That(action, Is.EqualTo(2));
        var txId = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(4, 4));
        Assert.That(txId, Is.EqualTo(42));
        var seeders = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(8, 4));
        var downloaded = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(12, 4));
        var leechers = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(16, 4));
        Assert.That(seeders, Is.EqualTo(3));
        Assert.That(downloaded, Is.EqualTo(7));
        Assert.That(leechers, Is.EqualTo(1));
    }

    [Test]
    public void HandleScrape_should_process_multiple_hashes()
    {
        var connId = RegisterValidConnectionId();

        var hash1 = new byte[20];
        Array.Fill(hash1, (byte)0x11);
        var hash2 = new byte[20];
        Array.Fill(hash2, (byte)0x22);

        _peerDatabase.GetStats(Arg.Any<string>()).Returns(new ScrapeStats { Complete = 1, Incomplete = 0, Downloaded = 1 });

        var data = new byte[16 + 40];
        Buffer.BlockCopy(hash1, 0, data, 16, 20);
        Buffer.BlockCopy(hash2, 0, data, 36, 20);

        var result = InvokeHandleScrape(connId, 42, data);

        Assert.That(result, Has.Length.EqualTo(8 + (2 * 12)));
    }

    [Test]
    public void BuildErrorResponse_should_contain_action_3()
    {
        var result = InvokeBuildErrorResponse(42, "Test error");

        var action = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(0, 4));
        Assert.That(action, Is.EqualTo(3));
    }

    [Test]
    public void BuildErrorResponse_should_echo_transaction_id()
    {
        var result = InvokeBuildErrorResponse(12345, "Test error");

        var txId = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(4, 4));
        Assert.That(txId, Is.EqualTo(12345));
    }

    [Test]
    public void BuildErrorResponse_should_contain_message()
    {
        var result = InvokeBuildErrorResponse(42, "Something went wrong");

        var message = Encoding.UTF8.GetString(result, 8, result.Length - 8);
        Assert.That(message, Is.EqualTo("Something went wrong"));
    }

    [Test]
    public void BuildErrorResponse_should_have_correct_length()
    {
        var msg = "Error message";
        var result = InvokeBuildErrorResponse(42, msg);

        Assert.That(result, Has.Length.EqualTo(8 + Encoding.UTF8.GetByteCount(msg)));
    }

    [Test]
    public void BuildCompactPeers_should_encode_ip_and_port()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "10.20.30.40", Port = 8080 }
        };

        var result = InvokeBuildCompactPeers(peers, "10.0.0.1", 9999, 50);

        Assert.That(result, Has.Length.EqualTo(6));
        Assert.That(result[0], Is.EqualTo(10));
        Assert.That(result[1], Is.EqualTo(20));
        Assert.That(result[2], Is.EqualTo(30));
        Assert.That(result[3], Is.EqualTo(40));
        Assert.That((result[4] << 8) | result[5], Is.EqualTo(8080));
    }

    [Test]
    public void BuildCompactPeers_should_exclude_requesting_peer()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "10.0.0.1", Port = 6881 },
            new TrackerPeerEntry { Ip = "10.0.0.2", Port = 6882 }
        };

        var result = InvokeBuildCompactPeers(peers, "10.0.0.1", 6881, 50);

        Assert.That(result, Has.Length.EqualTo(6));
    }

    [Test]
    public void BuildCompactPeers_should_limit_to_max_peers()
    {
        var peers = new List<TrackerPeerEntry>();
        for (var i = 0; i < 10; i++)
        {
            peers.Add(new TrackerPeerEntry { Ip = $"10.0.0.{i}", Port = 6881 });
        }

        var result = InvokeBuildCompactPeers(peers, "10.0.1.1", 9999, 3);

        Assert.That(result, Has.Length.EqualTo(18));
    }

    [Test]
    public void BuildCompactPeers_should_return_empty_for_empty_list()
    {
        var result = InvokeBuildCompactPeers(new List<TrackerPeerEntry>(), "10.0.0.1", 6881, 50);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void BuildCompactPeers_should_skip_malformed_ip()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "not.an.ip", Port = 6881 },
            new TrackerPeerEntry { Ip = "10.0.0.1", Port = 6882 }
        };

        var result = InvokeBuildCompactPeers(peers, "10.0.0.99", 9999, 50);

        // The malformed IP is silently skipped via IPAddress.TryParse; only the valid peer is encoded
        Assert.That(result, Has.Length.EqualTo(6));
        Assert.That(result[0], Is.EqualTo(10));
        Assert.That(result[3], Is.EqualTo(1));
    }

    [Test]
    public void BuildCompactPeers_should_skip_ipv6_address()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "::1", Port = 6881 },
            new TrackerPeerEntry { Ip = "10.0.0.2", Port = 6882 }
        };

        var result = InvokeBuildCompactPeers(peers, "10.0.0.99", 9999, 50);

        // IPv6 addresses fail the InterNetwork family check and are silently skipped
        Assert.That(result, Has.Length.EqualTo(6));
        Assert.That(result[3], Is.EqualTo(2));
    }

    [Test]
    public void BuildCompactPeers_should_return_empty_when_all_ips_are_invalid()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "not.an.ip", Port = 6881 },
            new TrackerPeerEntry { Ip = "999.999.999.999", Port = 6882 },
            new TrackerPeerEntry { Ip = "::1", Port = 6883 }
        };

        var result = InvokeBuildCompactPeers(peers, "10.0.0.99", 9999, 50);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ConvertInfoHashToHex_should_convert_bytes_to_lowercase_hex()
    {
        var data = new byte[30];
        for (var i = 0; i < 20; i++)
        {
            data[5 + i] = (byte)(i * 10);
        }

        var result = InvokeConvertInfoHashToHex(data, 5);

        Assert.That(result, Has.Length.EqualTo(40));
        Assert.That(result, Is.EqualTo(result.ToLowerInvariant()));
    }

    [Test]
    public void ConvertInfoHashToHex_should_convert_known_bytes()
    {
        var data = new byte[20];
        data[0] = 0xAB;
        data[1] = 0xCD;
        data[2] = 0xEF;

        var result = InvokeConvertInfoHashToHex(data, 0);

        Assert.That(result, Does.StartWith("abcdef"));
    }

    [Test]
    public void IsRateLimited_should_return_false_when_rate_limit_is_zero()
    {
        _configService.TrackerRateLimitPerMinute.Returns(0);

        var result = InvokeIsRateLimited("10.0.0.1");

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsRateLimited_should_return_false_when_rate_limit_is_negative()
    {
        _configService.TrackerRateLimitPerMinute.Returns(-1);

        var result = InvokeIsRateLimited("10.0.0.1");

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsRateLimited_should_return_false_when_under_limit()
    {
        _configService.TrackerRateLimitPerMinute.Returns(10);

        var result = InvokeIsRateLimited("10.0.0.1");

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsRateLimited_should_return_true_when_over_limit()
    {
        _configService.TrackerRateLimitPerMinute.Returns(3);

        InvokeIsRateLimited("10.0.0.1");
        InvokeIsRateLimited("10.0.0.1");
        InvokeIsRateLimited("10.0.0.1");
        var result = InvokeIsRateLimited("10.0.0.1");

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsRateLimited_should_track_separate_ips_independently()
    {
        _configService.TrackerRateLimitPerMinute.Returns(2);

        InvokeIsRateLimited("10.0.0.1");
        InvokeIsRateLimited("10.0.0.1");
        InvokeIsRateLimited("10.0.0.1");

        var result = InvokeIsRateLimited("10.0.0.2");

        Assert.That(result, Is.False);
    }

    [Test]
    public void PurgeExpiredConnections_should_not_throw_when_empty()
    {
        Assert.DoesNotThrow(() => InvokePurgeExpiredConnections());
    }

    [Test]
    public void PurgeExpiredRateLimits_should_not_throw_when_empty()
    {
        Assert.DoesNotThrow(() => InvokePurgeExpiredRateLimits());
    }

    [Test]
    public void HandleAnnounce_should_handle_completed_event()
    {
        var connId = RegisterValidConnectionId();
        var infoHash = new byte[20];
        Array.Fill(infoHash, (byte)0xAA);
        var peerId = new byte[20];
        Array.Fill(peerId, (byte)0x47);

        var expectedHex = Convert.ToHexString(infoHash).ToLowerInvariant();
        _peerDatabase.GetPeers(expectedHex).Returns(new List<TrackerPeerEntry>());
        _peerDatabase.GetStats(expectedHex).Returns(new ScrapeStats());

        var data = BuildAnnounceRequest(connId, 42, infoHash, peerId, 1, 50, 6881);
        InvokeHandleAnnounce(connId, 42, data, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881));

        _peerDatabase.Received(1).AddPeer(expectedHex, "10.0.0.1", 6881, Arg.Any<string>());
    }

    [Test]
    public void HandleAnnounce_should_handle_negative_numwant()
    {
        var connId = RegisterValidConnectionId();
        var infoHash = new byte[20];
        Array.Fill(infoHash, (byte)0xBB);
        var peerId = new byte[20];
        Array.Fill(peerId, (byte)0x48);

        var expectedHex = Convert.ToHexString(infoHash).ToLowerInvariant();
        _peerDatabase.GetPeers(expectedHex).Returns(new List<TrackerPeerEntry>());
        _peerDatabase.GetStats(expectedHex).Returns(new ScrapeStats());

        var data = BuildAnnounceRequest(connId, 42, infoHash, peerId, 0, -1, 6881);
        var result = InvokeHandleAnnounce(connId, 42, data, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881));

        Assert.That(result, Is.Not.Null);
        var action = BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(0, 4));
        Assert.That(action, Is.EqualTo(1));
    }

    [Test]
    public void HandleAnnounce_should_log_when_logging_enabled()
    {
        _configService.TrackerLogAnnounces.Returns(true);
        var connId = RegisterValidConnectionId();
        var infoHash = new byte[20];
        Array.Fill(infoHash, (byte)0xCC);
        var peerId = new byte[20];
        Array.Fill(peerId, (byte)0x49);

        var expectedHex = Convert.ToHexString(infoHash).ToLowerInvariant();
        _peerDatabase.GetPeers(expectedHex).Returns(new List<TrackerPeerEntry>());
        _peerDatabase.GetStats(expectedHex).Returns(new ScrapeStats());

        var data = BuildAnnounceRequest(connId, 42, infoHash, peerId, 0, 50, 6881);

        Assert.DoesNotThrow(() =>
            InvokeHandleAnnounce(connId, 42, data, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881)));
    }

    [Test]
    public void HandleScrape_should_cap_at_max_74_hashes()
    {
        var connId = RegisterValidConnectionId();

        _peerDatabase.GetStats(Arg.Any<string>()).Returns(new ScrapeStats { Complete = 1, Incomplete = 0, Downloaded = 1 });

        var hashCount = 80;
        var data = new byte[16 + (hashCount * 20)];

        var result = InvokeHandleScrape(connId, 42, data);

        Assert.That(result, Has.Length.EqualTo(8 + (74 * 12)));
    }

    [Test]
    public void ValidateConnectionId_should_return_false_for_expired_id()
    {
        var connId = 987654321L;
        AddConnectionEntry(connId, DateTime.UtcNow.AddMinutes(-3));

        var result = InvokeValidateConnectionId(connId);

        Assert.That(result, Is.False);
    }

    [Test]
    public void ValidateConnectionId_should_remove_expired_entry()
    {
        var connId = 987654321L;
        AddConnectionEntry(connId, DateTime.UtcNow.AddMinutes(-3));

        InvokeValidateConnectionId(connId);

        Assert.That(GetConnectionIdCount(), Is.EqualTo(0));
    }

    [Test]
    public void PurgeExpiredConnections_should_remove_expired_entries()
    {
        AddConnectionEntry(111L, DateTime.UtcNow.AddMinutes(-5));
        AddConnectionEntry(222L, DateTime.UtcNow.AddMinutes(-10));
        AddConnectionEntry(333L, DateTime.UtcNow);

        InvokePurgeExpiredConnections();

        Assert.That(GetConnectionIdCount(), Is.EqualTo(1));
    }

    [Test]
    public void PurgeExpiredConnections_should_keep_fresh_entries()
    {
        AddConnectionEntry(444L, DateTime.UtcNow);
        AddConnectionEntry(555L, DateTime.UtcNow.AddSeconds(-30));

        InvokePurgeExpiredConnections();

        Assert.That(GetConnectionIdCount(), Is.EqualTo(2));
    }

    [Test]
    public void PurgeExpiredRateLimits_should_remove_expired_entries()
    {
        AddRateLimitEntry("10.0.0.1", 5, DateTime.UtcNow.AddMinutes(-3));
        AddRateLimitEntry("10.0.0.2", 3, DateTime.UtcNow.AddMinutes(-5));
        AddRateLimitEntry("10.0.0.3", 1, DateTime.UtcNow);

        InvokePurgeExpiredRateLimits();

        Assert.That(GetRateLimitCount(), Is.EqualTo(1));
    }

    [Test]
    public void PurgeExpiredRateLimits_should_keep_recent_entries()
    {
        AddRateLimitEntry("10.0.0.1", 2, DateTime.UtcNow.AddSeconds(-30));
        AddRateLimitEntry("10.0.0.2", 1, DateTime.UtcNow);

        InvokePurgeExpiredRateLimits();

        Assert.That(GetRateLimitCount(), Is.EqualTo(2));
    }

    [Test]
    public void HandleDatagram_should_return_without_error_for_short_data()
    {
        using var client = new UdpClient(0, AddressFamily.InterNetwork);
        var shortBuffer = new byte[10];
        var remote = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleDatagram(client, shortBuffer, remote));
    }

    [Test]
    public void HandleDatagram_should_handle_unknown_action_without_error()
    {
        var connId = RegisterValidConnectionId();
        using var client = new UdpClient(0, AddressFamily.InterNetwork);
        var datagram = BuildDatagram(connId, 99, 42);
        var remote = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleDatagram(client, datagram, remote));
    }

    [Test]
    public void HandleDatagram_should_process_connect_and_register_connection()
    {
        var initialCount = GetConnectionIdCount();
        using var client = new UdpClient(0, AddressFamily.InterNetwork);
        var datagram = BuildDatagram(ProtocolMagic, 0, 42);
        var remote = new IPEndPoint(IPAddress.Loopback, 12345);

        InvokeHandleDatagram(client, datagram, remote);

        Assert.That(GetConnectionIdCount(), Is.GreaterThan(initialCount));
    }

    [Test]
    public void HandleAnnounce_should_include_compact_peers_in_response()
    {
        var connId = RegisterValidConnectionId();
        var infoHash = new byte[20];
        Array.Fill(infoHash, (byte)0x11);
        var peerId = new byte[20];
        Array.Fill(peerId, (byte)0x50);

        var expectedHex = Convert.ToHexString(infoHash).ToLowerInvariant();
        var peerList = new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "192.168.1.1", Port = 6881 },
            new TrackerPeerEntry { Ip = "192.168.1.2", Port = 6882 },
            new TrackerPeerEntry { Ip = "192.168.1.3", Port = 6883 }
        };
        _peerDatabase.GetPeers(expectedHex).Returns(peerList);
        _peerDatabase.GetStats(expectedHex).Returns(new ScrapeStats { Complete = 3, Incomplete = 1, Downloaded = 5 });

        var data = BuildAnnounceRequest(connId, 42, infoHash, peerId, 2, 50, 6881);
        var result = InvokeHandleAnnounce(connId, 42, data, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881));

        Assert.That(result, Has.Length.EqualTo(20 + (3 * 6)));
        Assert.That(result[20], Is.EqualTo(192));
        Assert.That(result[21], Is.EqualTo(168));
        Assert.That(result[22], Is.EqualTo(1));
        Assert.That(result[23], Is.EqualTo(1));
        Assert.That((result[24] << 8) | result[25], Is.EqualTo(6881));
    }

    [Test]
    public void IsRateLimited_should_reset_counter_after_window_expires()
    {
        _configService.TrackerRateLimitPerMinute.Returns(3);

        InvokeIsRateLimited("10.0.0.99");
        InvokeIsRateLimited("10.0.0.99");
        InvokeIsRateLimited("10.0.0.99");
        var limited = InvokeIsRateLimited("10.0.0.99");
        Assert.That(limited, Is.True);

        AddRateLimitEntry("10.0.0.99", 100, DateTime.UtcNow.AddMinutes(-2));

        var result = InvokeIsRateLimited("10.0.0.99");
        Assert.That(result, Is.False);
    }

    [Test]
    public void HandleScrape_should_return_correct_stats_per_hash()
    {
        var connId = RegisterValidConnectionId();

        var hash1 = new byte[20];
        Array.Fill(hash1, (byte)0x55);
        var hash2 = new byte[20];
        Array.Fill(hash2, (byte)0x66);

        var hex1 = Convert.ToHexString(hash1).ToLowerInvariant();
        var hex2 = Convert.ToHexString(hash2).ToLowerInvariant();

        _peerDatabase.GetStats(hex1).Returns(new ScrapeStats { Complete = 10, Incomplete = 5, Downloaded = 20 });
        _peerDatabase.GetStats(hex2).Returns(new ScrapeStats { Complete = 7, Incomplete = 2, Downloaded = 15 });

        var data = new byte[16 + 40];
        Buffer.BlockCopy(hash1, 0, data, 16, 20);
        Buffer.BlockCopy(hash2, 0, data, 36, 20);

        var result = InvokeHandleScrape(connId, 5555, data);

        Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(0, 4)), Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(4, 4)), Is.EqualTo(5555));
        Assert.That(result, Has.Length.EqualTo(32));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(8, 4)), Is.EqualTo(10));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(12, 4)), Is.EqualTo(20));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(16, 4)), Is.EqualTo(5));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(20, 4)), Is.EqualTo(7));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(24, 4)), Is.EqualTo(15));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(28, 4)), Is.EqualTo(2));
    }

    // ExecuteAsync loop-body tests

    [Test]
    public async Task ExecuteAsync_exits_immediately_when_tracker_server_disabled()
    {
        _configService.TrackerServerEnabled.Returns(false);

        await _udpTrackerServer.StartAsync(CancellationToken.None);
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _udpTrackerServer.StopAsync(stopCts.Token);

        Assert.That(stopCts.IsCancellationRequested, Is.False, "StopAsync should complete before timeout");
    }

    [Test]
    public async Task ExecuteAsync_exits_immediately_when_udp_tracker_disabled()
    {
        _configService.TrackerServerEnabled.Returns(true);
        _configService.TrackerUdpEnabled.Returns(false);

        await _udpTrackerServer.StartAsync(CancellationToken.None);
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _udpTrackerServer.StopAsync(stopCts.Token);

        Assert.That(stopCts.IsCancellationRequested, Is.False, "StopAsync should complete before timeout");
    }

    [Test]
    public async Task ExecuteAsync_binds_and_exits_cleanly_on_cancellation()
    {
        _configService.TrackerServerEnabled.Returns(true);
        _configService.TrackerUdpEnabled.Returns(true);
        _configService.TrackerUdpPort.Returns(0);
        _configService.TrackerBindAddress.Returns("127.0.0.1");

        await _udpTrackerServer.StartAsync(CancellationToken.None);
        await Task.Delay(50);

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _udpTrackerServer.StopAsync(stopCts.Token);

        Assert.That(stopCts.IsCancellationRequested, Is.False, "StopAsync should complete before timeout");
    }

    [Test]
    public async Task ExecuteAsync_exits_gracefully_when_port_already_in_use()
    {
        using var occupied = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)occupied.Client.LocalEndPoint).Port;

        _configService.TrackerServerEnabled.Returns(true);
        _configService.TrackerUdpEnabled.Returns(true);
        _configService.TrackerUdpPort.Returns(port);
        _configService.TrackerBindAddress.Returns("127.0.0.1");

        await _udpTrackerServer.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _udpTrackerServer.StopAsync(stopCts.Token);

        Assert.That(stopCts.IsCancellationRequested, Is.False, "StopAsync should complete before timeout");
    }
}
