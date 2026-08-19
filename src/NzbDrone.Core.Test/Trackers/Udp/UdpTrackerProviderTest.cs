using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Trackers;
using NzbDrone.Core.Trackers.Udp;

namespace NzbDrone.Core.Test.Trackers.Udp;

[TestFixture]
public class UdpTrackerProviderTest
{
    private UdpTrackerProvider _provider;
    private IConfigService _configService;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.UdpTrackerTimeoutSeconds.Returns(5);
        _provider = new UdpTrackerProvider(_configService);
    }

    [Test]
    public void Name_should_return_udp()
    {
        Assert.That(_provider.Name, Is.EqualTo("UDP"));
    }

    [Test]
    public void BuildAnnouncePacket_should_create_98_byte_packet()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = new TrackerAnnounceRequest
        {
            InfoHash = "AABBCCDD00112233445566778899AABBCCDDEEFF",
            PeerId = "-qB4420-abcdefghijkl",
            Port = 6881,
            Uploaded = 1024,
            Downloaded = 2048,
            Left = 4096,
            Event = "started",
            NumWant = 50
        };

        var result = (byte[])method.Invoke(null, new object[] { 12345L, 67890, request });

        Assert.That(result.Length, Is.EqualTo(98));
    }

    [Test]
    public void BuildAnnouncePacket_should_encode_connection_id()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();

        var result = (byte[])method.Invoke(null, new object[] { 0x0102030405060708L, 0, request });

        Assert.That(result[0], Is.EqualTo(0x01));
        Assert.That(result[1], Is.EqualTo(0x02));
        Assert.That(result[7], Is.EqualTo(0x08));
    }

    [Test]
    public void BuildAnnouncePacket_should_set_action_to_announce()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        var action = ReadInt32BigEndian(result, 8);
        Assert.That(action, Is.EqualTo(1));
    }

    [Test]
    public void BuildAnnouncePacket_should_encode_info_hash()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.InfoHash = "AABBCCDDEE112233445566778899AABBCCDDEEFF";

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        Assert.That(result[16], Is.EqualTo(0xAA));
        Assert.That(result[17], Is.EqualTo(0xBB));
    }

    [Test]
    public void BuildAnnouncePacket_should_encode_started_event_as_2()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Event = "started";

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        var eventValue = ReadInt32BigEndian(result, 80);
        Assert.That(eventValue, Is.EqualTo(2));
    }

    [Test]
    public void BuildAnnouncePacket_should_encode_completed_event_as_1()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Event = "completed";

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        var eventValue = ReadInt32BigEndian(result, 80);
        Assert.That(eventValue, Is.EqualTo(1));
    }

    [Test]
    public void BuildAnnouncePacket_should_encode_stopped_event_as_3()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Event = "stopped";

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        var eventValue = ReadInt32BigEndian(result, 80);
        Assert.That(eventValue, Is.EqualTo(3));
    }

    [Test]
    public void BuildAnnouncePacket_should_encode_empty_event_as_0()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Event = "";

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        var eventValue = ReadInt32BigEndian(result, 80);
        Assert.That(eventValue, Is.EqualTo(0));
    }

    [Test]
    public void BuildAnnouncePacket_should_encode_port()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Port = 0x1A2B;

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        Assert.That(result[96], Is.EqualTo(0x1A));
        Assert.That(result[97], Is.EqualTo(0x2B));
    }

    [Test]
    public void ParseAnnounceResponse_should_return_failure_for_short_response()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("ParseAnnounceResponse", BindingFlags.NonPublic | BindingFlags.Static);

        var result = (TrackerAnnounceResponse)method.Invoke(null, new object[] { new byte[10], 0 });

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo("Response too short"));
    }

    [Test]
    public void ParseAnnounceResponse_should_parse_valid_response()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("ParseAnnounceResponse", BindingFlags.NonPublic | BindingFlags.Static);
        var response = new byte[20];
        WriteInt32BigEndian(response, 0, 1); // ActionAnnounce
        WriteInt32BigEndian(response, 8, 1800);
        WriteInt32BigEndian(response, 12, 5);
        WriteInt32BigEndian(response, 16, 10);

        var result = (TrackerAnnounceResponse)method.Invoke(null, new object[] { response, 0 });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Interval, Is.EqualTo(1800));
        Assert.That(result.Incomplete, Is.EqualTo(5));
        Assert.That(result.Complete, Is.EqualTo(10));
    }

    [Test]
    public void ParseAnnounceResponse_should_parse_peers_from_response()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("ParseAnnounceResponse", BindingFlags.NonPublic | BindingFlags.Static);
        var response = new byte[26];
        WriteInt32BigEndian(response, 0, 1); // ActionAnnounce
        WriteInt32BigEndian(response, 8, 1800);
        response[20] = 192;
        response[21] = 168;
        response[22] = 1;
        response[23] = 1;
        response[24] = (byte)(6881 >> 8);
        response[25] = (byte)(6881 & 0xFF);

        var result = (TrackerAnnounceResponse)method.Invoke(null, new object[] { response, 0 });

        Assert.That(result.Peers.Count, Is.EqualTo(1));
        Assert.That(result.Peers[0].Ip, Is.EqualTo("192.168.1.1"));
        Assert.That(result.Peers[0].Port, Is.EqualTo(6881));
    }

    [Test]
    public void WriteInt64BigEndian_should_encode_correctly()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("WriteInt64BigEndian", BindingFlags.NonPublic | BindingFlags.Static);
        var buffer = new byte[8];

        method.Invoke(null, new object[] { buffer, 0, 0x0102030405060708L });

        Assert.That(buffer[0], Is.EqualTo(0x01));
        Assert.That(buffer[7], Is.EqualTo(0x08));
    }

    [Test]
    public void WriteInt32BigEndian_should_encode_correctly()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("WriteInt32BigEndian", BindingFlags.NonPublic | BindingFlags.Static);
        var buffer = new byte[4];

        method.Invoke(null, new object[] { buffer, 0, 0x01020304 });

        Assert.That(buffer[0], Is.EqualTo(0x01));
        Assert.That(buffer[3], Is.EqualTo(0x04));
    }

    [Test]
    public void ReadInt64BigEndian_should_decode_correctly()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("ReadInt64BigEndian", BindingFlags.NonPublic | BindingFlags.Static);
        var buffer = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };

        var result = (long)method.Invoke(null, new object[] { buffer, 0 });

        Assert.That(result, Is.EqualTo(0x0102030405060708L));
    }

    [Test]
    public void ReadInt32BigEndian_should_decode_correctly()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("ReadInt32BigEndian", BindingFlags.NonPublic | BindingFlags.Static);
        var buffer = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        var result = (int)method.Invoke(null, new object[] { buffer, 0 });

        Assert.That(result, Is.EqualTo(0x01020304));
    }

    [Test]
    public void Announce_should_return_failure_on_exception()
    {
        var request = new TrackerAnnounceRequest
        {
            TrackerUrl = "udp://nonexistent.invalid:9999/announce",
            InfoHash = "AABBCCDDEE112233445566778899AABBCCDDEEFF",
            PeerId = "-qB4420-abcdefghijkl",
            Port = 6881
        };

        var result = _provider.Announce(request);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.Not.Empty);
    }

    [Test]
    public void Scrape_should_return_failure_on_exception()
    {
        var result = _provider.Scrape("AABBCCDDEE112233445566778899AABBCCDDEEFF", "udp://nonexistent.invalid:9999/announce");

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.Not.Empty);
    }

    [Test]
    public void BuildAnnouncePacket_should_encode_numwant()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.NumWant = 200;

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        var numWant = ReadInt32BigEndian(result, 92);
        Assert.That(numWant, Is.EqualTo(200));
    }

    [Test]
    public void WriteInt16BigEndian_should_encode_correctly()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("WriteInt16BigEndian", BindingFlags.NonPublic | BindingFlags.Static);
        var buffer = new byte[4];

        method.Invoke(null, new object[] { buffer, 1, (short)0x1A2B });

        Assert.That(buffer[0], Is.EqualTo(0x00));
        Assert.That(buffer[1], Is.EqualTo(0x1A));
        Assert.That(buffer[2], Is.EqualTo(0x2B));
        Assert.That(buffer[3], Is.EqualTo(0x00));
    }

    [Test]
    public void BuildAnnouncePacket_should_encode_downloaded_at_offset_56()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Downloaded = 0x0102030405060708L;

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        var downloaded = ReadInt64BigEndian(result, 56);
        Assert.That(downloaded, Is.EqualTo(0x0102030405060708L));
    }

    [Test]
    public void BuildAnnouncePacket_should_encode_left_at_offset_64()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Left = 0x0A0B0C0D0E0F1011L;

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        var left = ReadInt64BigEndian(result, 64);
        Assert.That(left, Is.EqualTo(0x0A0B0C0D0E0F1011L));
    }

    [Test]
    public void BuildAnnouncePacket_should_encode_uploaded_at_offset_72()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Uploaded = 0x1112131415161718L;

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        var uploaded = ReadInt64BigEndian(result, 72);
        Assert.That(uploaded, Is.EqualTo(0x1112131415161718L));
    }

    [Test]
    public void BuildAnnouncePacket_should_encode_null_event_as_0()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Event = null;

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        var eventValue = ReadInt32BigEndian(result, 80);
        Assert.That(eventValue, Is.EqualTo(0));
    }

    [Test]
    public void BuildAnnouncePacket_should_encode_peer_id_at_offset_36()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.PeerId = "-qB4420-abcdefghijkl";

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        var peerIdBytes = new byte[20];
        Array.Copy(result, 36, peerIdBytes, 0, 20);
        var peerId = Encoding.ASCII.GetString(peerIdBytes);
        Assert.That(peerId, Is.EqualTo("-qB4420-abcdefghijkl"));
    }

    [Test]
    public void BuildAnnouncePacket_should_pad_short_peer_id_to_20_chars()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.PeerId = "short";

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        var peerIdBytes = new byte[20];
        Array.Copy(result, 36, peerIdBytes, 0, 20);
        var peerId = Encoding.ASCII.GetString(peerIdBytes);
        Assert.That(peerId, Is.EqualTo("short               "));
    }

    [Test]
    public void ParseAnnounceResponse_should_return_empty_peers_for_exact_20_byte_response()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("ParseAnnounceResponse", BindingFlags.NonPublic | BindingFlags.Static);
        var response = new byte[20];
        WriteInt32BigEndian(response, 0, 1); // ActionAnnounce
        WriteInt32BigEndian(response, 8, 900);
        WriteInt32BigEndian(response, 12, 3);
        WriteInt32BigEndian(response, 16, 7);

        var result = (TrackerAnnounceResponse)method.Invoke(null, new object[] { response, 0 });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Peers.Count, Is.EqualTo(0));
        Assert.That(result.Interval, Is.EqualTo(900));
        Assert.That(result.Incomplete, Is.EqualTo(3));
        Assert.That(result.Complete, Is.EqualTo(7));
    }

    [Test]
    public void ParseAnnounceResponse_should_parse_multiple_peers()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("ParseAnnounceResponse", BindingFlags.NonPublic | BindingFlags.Static);
        var response = new byte[32];
        WriteInt32BigEndian(response, 0, 1); // ActionAnnounce
        WriteInt32BigEndian(response, 8, 1800);

        // Peer 1: 10.0.0.1:8080
        response[20] = 10;
        response[21] = 0;
        response[22] = 0;
        response[23] = 1;
        response[24] = (byte)(8080 >> 8);
        response[25] = (byte)(8080 & 0xFF);

        // Peer 2: 172.16.0.5:51413
        response[26] = 172;
        response[27] = 16;
        response[28] = 0;
        response[29] = 5;
        response[30] = (byte)(51413 >> 8);
        response[31] = (byte)(51413 & 0xFF);

        var result = (TrackerAnnounceResponse)method.Invoke(null, new object[] { response, 0 });

        Assert.That(result.Peers.Count, Is.EqualTo(2));
        Assert.That(result.Peers[0].Ip, Is.EqualTo("10.0.0.1"));
        Assert.That(result.Peers[0].Port, Is.EqualTo(8080));
        Assert.That(result.Peers[1].Ip, Is.EqualTo("172.16.0.5"));
        Assert.That(result.Peers[1].Port, Is.EqualTo(51413));
    }

    [Test]
    public void ParseAnnounceResponse_should_skip_partial_peer_at_end()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("ParseAnnounceResponse", BindingFlags.NonPublic | BindingFlags.Static);
        var response = new byte[25];
        WriteInt32BigEndian(response, 0, 1); // ActionAnnounce
        WriteInt32BigEndian(response, 8, 1800);
        response[20] = 192;
        response[21] = 168;
        response[22] = 1;
        response[23] = 1;
        response[24] = 0x1A;

        var result = (TrackerAnnounceResponse)method.Invoke(null, new object[] { response, 0 });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Peers.Count, Is.EqualTo(0));
    }

    private static TrackerAnnounceRequest CreateRequest()
    {
        return new TrackerAnnounceRequest
        {
            InfoHash = "AABBCCDDEE112233445566778899AABBCCDDEEFF",
            PeerId = "-qB4420-abcdefghijkl",
            Port = 6881,
            Uploaded = 0,
            Downloaded = 0,
            Left = 1000,
            NumWant = 50
        };
    }

    private static long ReadInt64BigEndian(byte[] buffer, int offset)
    {
        return ((long)buffer[offset] << 56) |
            ((long)buffer[offset + 1] << 48) |
            ((long)buffer[offset + 2] << 40) |
            ((long)buffer[offset + 3] << 32) |
            ((long)buffer[offset + 4] << 24) |
            ((long)buffer[offset + 5] << 16) |
            ((long)buffer[offset + 6] << 8) |
            buffer[offset + 7];
    }

    private static int ReadInt32BigEndian(byte[] buffer, int offset)
    {
        return (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];
    }

    private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteInt64BigEndian(byte[] buffer, int offset, long value)
    {
        buffer[offset] = (byte)(value >> 56);
        buffer[offset + 1] = (byte)(value >> 48);
        buffer[offset + 2] = (byte)(value >> 40);
        buffer[offset + 3] = (byte)(value >> 32);
        buffer[offset + 4] = (byte)(value >> 24);
        buffer[offset + 5] = (byte)(value >> 16);
        buffer[offset + 6] = (byte)(value >> 8);
        buffer[offset + 7] = (byte)value;
    }

    // ---- loopback integration tests ----

    [Test]
    public void GenerateTransactionId_should_return_non_negative_value()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("GenerateTransactionId",
            BindingFlags.NonPublic | BindingFlags.Static);

        var result = (int)method!.Invoke(null, null)!;

        Assert.That(result, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void Announce_should_return_success_when_tracker_responds_correctly()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var serverTask = Task.Run(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);

            // Respond to Connect request: action=0, echo transactionId, connectionId=1
            var connectReq = server.Receive(ref ep);
            var connectResp = new byte[16];
            WriteInt32BigEndian(connectResp, 0, 0);
            Array.Copy(connectReq, 12, connectResp, 4, 4);
            WriteInt64BigEndian(connectResp, 8, 1L);
            server.Send(connectResp, connectResp.Length, ep);

            // Respond to Announce request: action=1, interval=1800, leechers=5, seeders=10
            var announceReq = server.Receive(ref ep);
            var announceResp = new byte[20];
            WriteInt32BigEndian(announceResp, 0, 1);
            Array.Copy(announceReq, 12, announceResp, 4, 4);
            WriteInt32BigEndian(announceResp, 8, 1800);
            WriteInt32BigEndian(announceResp, 12, 5);
            WriteInt32BigEndian(announceResp, 16, 10);
            server.Send(announceResp, announceResp.Length, ep);
        });

        var request = new TrackerAnnounceRequest
        {
            TrackerUrl = $"udp://127.0.0.1:{serverPort}/announce",
            InfoHash = "AABBCCDDEE112233445566778899AABBCCDDEEFF",
            PeerId = "-qB4420-abcdefghijkl",
            Port = 6881
        };

        var result = _provider.Announce(request);
        serverTask.Wait(TimeSpan.FromSeconds(5));

        Assert.That(result.Success, Is.True);
        Assert.That(result.Interval, Is.EqualTo(1800));
        Assert.That(result.Incomplete, Is.EqualTo(5));
        Assert.That(result.Complete, Is.EqualTo(10));
    }

    [Test]
    public void Announce_should_parse_peers_from_tracker_response()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var serverTask = Task.Run(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);

            // Respond to Connect request
            var connectReq = server.Receive(ref ep);
            var connectResp = new byte[16];
            WriteInt32BigEndian(connectResp, 0, 0);
            Array.Copy(connectReq, 12, connectResp, 4, 4);
            WriteInt64BigEndian(connectResp, 8, 1L);
            server.Send(connectResp, connectResp.Length, ep);

            // Respond to Announce with one peer (10.0.0.1:8080) in 20+6 byte response
            var announceReq = server.Receive(ref ep);
            var announceResp = new byte[26];
            WriteInt32BigEndian(announceResp, 0, 1);
            Array.Copy(announceReq, 12, announceResp, 4, 4);
            WriteInt32BigEndian(announceResp, 8, 1800);
            announceResp[20] = 10;
            announceResp[21] = 0;
            announceResp[22] = 0;
            announceResp[23] = 1;
            announceResp[24] = (byte)(8080 >> 8);
            announceResp[25] = (byte)(8080 & 0xFF);
            server.Send(announceResp, announceResp.Length, ep);
        });

        var request = new TrackerAnnounceRequest
        {
            TrackerUrl = $"udp://127.0.0.1:{serverPort}/announce",
            InfoHash = "AABBCCDDEE112233445566778899AABBCCDDEEFF",
            PeerId = "-qB4420-abcdefghijkl",
            Port = 6881
        };

        var result = _provider.Announce(request);
        serverTask.Wait(TimeSpan.FromSeconds(5));

        Assert.That(result.Success, Is.True);
        Assert.That(result.Peers.Count, Is.EqualTo(1));
        Assert.That(result.Peers[0].Ip, Is.EqualTo("10.0.0.1"));
        Assert.That(result.Peers[0].Port, Is.EqualTo(8080));
    }

    [Test]
    public void Announce_should_return_failure_when_connect_response_is_too_short()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        Task.Run(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            server.Receive(ref ep);
            server.Send(new byte[5], 5, ep);
        });

        var request = new TrackerAnnounceRequest
        {
            TrackerUrl = $"udp://127.0.0.1:{serverPort}/announce",
            InfoHash = "AABBCCDDEE112233445566778899AABBCCDDEEFF",
            PeerId = "-qB4420-abcdefghijkl",
            Port = 6881
        };

        var result = _provider.Announce(request);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo("UDP connect response too short"));
    }

    [Test]
    public void Scrape_should_return_success_when_tracker_responds_correctly()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var serverTask = Task.Run(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);

            // Respond to Connect
            var connectReq = server.Receive(ref ep);
            var connectResp = new byte[16];
            WriteInt32BigEndian(connectResp, 0, 0);
            Array.Copy(connectReq, 12, connectResp, 4, 4);
            WriteInt64BigEndian(connectResp, 8, 2L);
            server.Send(connectResp, connectResp.Length, ep);

            // Respond to Scrape: action=2, seeders=50, downloaded=200, leechers=3
            var scrapeReq = server.Receive(ref ep);
            var scrapeResp = new byte[20];
            WriteInt32BigEndian(scrapeResp, 0, 2);
            Array.Copy(scrapeReq, 12, scrapeResp, 4, 4);
            WriteInt32BigEndian(scrapeResp, 8, 50);
            WriteInt32BigEndian(scrapeResp, 12, 200);
            WriteInt32BigEndian(scrapeResp, 16, 3);
            server.Send(scrapeResp, scrapeResp.Length, ep);
        });

        var result = _provider.Scrape(
            "AABBCCDDEE112233445566778899AABBCCDDEEFF",
            $"udp://127.0.0.1:{serverPort}/announce");

        serverTask.Wait(TimeSpan.FromSeconds(5));

        Assert.That(result.Success, Is.True);
        Assert.That(result.Complete, Is.EqualTo(50));
        Assert.That(result.Downloaded, Is.EqualTo(200));
        Assert.That(result.Incomplete, Is.EqualTo(3));
    }

    [Test]
    public void Scrape_should_return_failure_when_response_is_too_short()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        Task.Run(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);

            // Handle Connect normally
            var connectReq = server.Receive(ref ep);
            var connectResp = new byte[16];
            WriteInt32BigEndian(connectResp, 0, 0);
            Array.Copy(connectReq, 12, connectResp, 4, 4);
            WriteInt64BigEndian(connectResp, 8, 3L);
            server.Send(connectResp, connectResp.Length, ep);

            // Respond to Scrape with too-short response (< 20 bytes)
            server.Receive(ref ep);
            server.Send(new byte[10], 10, ep);
        });

        var result = _provider.Scrape(
            "AABBCCDDEE112233445566778899AABBCCDDEEFF",
            $"udp://127.0.0.1:{serverPort}/announce");

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo("Response too short"));
    }
}
