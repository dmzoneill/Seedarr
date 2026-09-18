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
            Event = AnnounceEvent.Started,
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
        request.Event = AnnounceEvent.Started;

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        var eventValue = ReadInt32BigEndian(result, 80);
        Assert.That(eventValue, Is.EqualTo(2));
    }

    [Test]
    public void BuildAnnouncePacket_should_encode_completed_event_as_1()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Event = AnnounceEvent.Completed;

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        var eventValue = ReadInt32BigEndian(result, 80);
        Assert.That(eventValue, Is.EqualTo(1));
    }

    [Test]
    public void BuildAnnouncePacket_should_encode_stopped_event_as_3()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Event = AnnounceEvent.Stopped;

        var result = (byte[])method.Invoke(null, new object[] { 0L, 0, request });

        var eventValue = ReadInt32BigEndian(result, 80);
        Assert.That(eventValue, Is.EqualTo(3));
    }

    [Test]
    public void BuildAnnouncePacket_should_encode_none_event_as_0()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("BuildAnnouncePacket", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Event = AnnounceEvent.None;

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

        var result = (TrackerAnnounceResponse)method.Invoke(null, new object[] { new byte[10], 0, AddressFamily.Unspecified });

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

        var result = (TrackerAnnounceResponse)method.Invoke(null, new object[] { response, 0, AddressFamily.Unspecified });

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

        var result = (TrackerAnnounceResponse)method.Invoke(null, new object[] { response, 0, AddressFamily.InterNetwork });

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

        var result = (TrackerAnnounceResponse)method.Invoke(null, new object[] { response, 0, AddressFamily.Unspecified });

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

        var result = (TrackerAnnounceResponse)method.Invoke(null, new object[] { response, 0, AddressFamily.InterNetwork });

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

        var result = (TrackerAnnounceResponse)method.Invoke(null, new object[] { response, 0, AddressFamily.InterNetwork });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Peers.Count, Is.EqualTo(0));
    }

    [Test]
    public void ParseAnnounceResponse_should_parse_ipv6_peers_from_response()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("ParseAnnounceResponse", BindingFlags.NonPublic | BindingFlags.Static);
        var response = new byte[38]; // 20 header + 18 peer
        WriteInt32BigEndian(response, 0, 1); // ActionAnnounce
        WriteInt32BigEndian(response, 8, 1800);

        // 2001:db8::1 in bytes (16 bytes)
        var ipv6Address = IPAddress.Parse("2001:db8::1");
        ipv6Address.GetAddressBytes().CopyTo(response, 20);

        // Port 51413
        response[36] = (byte)(51413 >> 8);
        response[37] = (byte)(51413 & 0xFF);

        var result = (TrackerAnnounceResponse)method.Invoke(null, new object[] { response, 0, AddressFamily.InterNetworkV6 });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Peers.Count, Is.EqualTo(1));
        Assert.That(result.Peers[0].Ip, Is.EqualTo("2001:db8::1"));
        Assert.That(result.Peers[0].Port, Is.EqualTo(51413));
    }

    [Test]
    public void ParseAnnounceResponse_should_parse_multiple_ipv6_peers()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("ParseAnnounceResponse", BindingFlags.NonPublic | BindingFlags.Static);
        var response = new byte[56]; // 20 header + 18 + 18
        WriteInt32BigEndian(response, 0, 1); // ActionAnnounce
        WriteInt32BigEndian(response, 8, 1800);

        // Peer 1: 2001:db8::1:51413
        IPAddress.Parse("2001:db8::1").GetAddressBytes().CopyTo(response, 20);
        response[36] = (byte)(51413 >> 8);
        response[37] = (byte)(51413 & 0xFF);

        // Peer 2: fe80::1:8080
        IPAddress.Parse("fe80::1").GetAddressBytes().CopyTo(response, 38);
        response[54] = (byte)(8080 >> 8);
        response[55] = (byte)(8080 & 0xFF);

        var result = (TrackerAnnounceResponse)method.Invoke(null, new object[] { response, 0, AddressFamily.InterNetworkV6 });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Peers.Count, Is.EqualTo(2));
        Assert.That(result.Peers[0].Ip, Is.EqualTo("2001:db8::1"));
        Assert.That(result.Peers[0].Port, Is.EqualTo(51413));
        Assert.That(result.Peers[1].Ip, Is.EqualTo("fe80::1"));
        Assert.That(result.Peers[1].Port, Is.EqualTo(8080));
    }

    [Test]
    public void ParseAnnounceResponse_should_skip_partial_ipv6_peer_at_end()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("ParseAnnounceResponse", BindingFlags.NonPublic | BindingFlags.Static);
        var response = new byte[48]; // 20 header + 18 valid peer + 10 partial bytes
        WriteInt32BigEndian(response, 0, 1); // ActionAnnounce
        WriteInt32BigEndian(response, 8, 1800);

        IPAddress.Parse("2001:db8::1").GetAddressBytes().CopyTo(response, 20);
        response[36] = (byte)(51413 >> 8);
        response[37] = (byte)(51413 & 0xFF);

        var result = (TrackerAnnounceResponse)method.Invoke(null, new object[] { response, 0, AddressFamily.InterNetworkV6 });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Peers.Count, Is.EqualTo(1));
        Assert.That(result.Peers[0].Ip, Is.EqualTo("2001:db8::1"));
        Assert.That(result.Peers[0].Port, Is.EqualTo(51413));
    }

    [Test]
    public void ParseAnnounceResponse_should_handle_boundary_payload_for_ipv6()
    {
        var method = typeof(UdpTrackerProvider).GetMethod("ParseAnnounceResponse", BindingFlags.NonPublic | BindingFlags.Static);
        // Response with only 17 bytes of peer data (less than required 18 bytes for IPv6)
        var response = new byte[37]; // 20 header + 17 bytes
        WriteInt32BigEndian(response, 0, 1);
        WriteInt32BigEndian(response, 8, 1800);

        var result = (TrackerAnnounceResponse)method.Invoke(null, new object[] { response, 0, AddressFamily.InterNetworkV6 });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Peers.Count, Is.EqualTo(0));
    }

    [Test]
    public void ParseAnnounceResponse_should_support_direct_internal_call_for_ipv4_and_ipv6()
    {
        var responseV4 = new byte[26];
        WriteInt32BigEndian(responseV4, 0, 1);
        WriteInt32BigEndian(responseV4, 8, 1800);
        responseV4[20] = 192;
        responseV4[21] = 168;
        responseV4[22] = 0;
        responseV4[23] = 10;
        responseV4[24] = (byte)(6881 >> 8);
        responseV4[25] = (byte)(6881 & 0xFF);

        var resV4 = UdpTrackerProvider.ParseAnnounceResponse(responseV4, 0);
        Assert.That(resV4.Success, Is.True);
        Assert.That(resV4.Peers.Count, Is.EqualTo(1));
        Assert.That(resV4.Peers[0].Ip, Is.EqualTo("192.168.0.10"));
        Assert.That(resV4.Peers[0].Port, Is.EqualTo(6881));

        var responseV6 = new byte[38];
        WriteInt32BigEndian(responseV6, 0, 1);
        WriteInt32BigEndian(responseV6, 8, 1800);
        IPAddress.Parse("::1").GetAddressBytes().CopyTo(responseV6, 20);
        responseV6[36] = (byte)(6881 >> 8);
        responseV6[37] = (byte)(6881 & 0xFF);

        var resV6 = UdpTrackerProvider.ParseAnnounceResponse(responseV6, 0, AddressFamily.InterNetworkV6);
        Assert.That(resV6.Success, Is.True);
        Assert.That(resV6.Peers.Count, Is.EqualTo(1));
        Assert.That(resV6.Peers[0].Ip, Is.EqualTo("::1"));
        Assert.That(resV6.Peers[0].Port, Is.EqualTo(6881));
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
            var scrapeReq = server.Receive(ref ep);
            var scrapeResp = new byte[10];
            Array.Copy(scrapeReq, 12, scrapeResp, 4, 4);
            server.Send(scrapeResp, scrapeResp.Length, ep);
        });

        var result = _provider.Scrape(
            "AABBCCDDEE112233445566778899AABBCCDDEEFF",
            $"udp://127.0.0.1:{serverPort}/announce");

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo("Response too short"));
    }

    [Test]
    public void CreateClient_should_initialize_socket_with_bound_interface()
    {
        _configService.BindInterface.Returns("tun0");

        using var client = _provider.CreateClient(5000);

        Assert.That(client, Is.Not.Null);
        Assert.That(client.Client, Is.Not.Null);
        Assert.That(client.Client.ReceiveTimeout, Is.EqualTo(5000));
        Assert.That(client.Client.SendTimeout, Is.EqualTo(5000));
    }

    [Test]
    public void CreateClient_should_initialize_socket_when_bind_interface_is_null()
    {
        _configService.BindInterface.Returns((string)null);

        using var client = _provider.CreateClient(3000);

        Assert.That(client, Is.Not.Null);
        Assert.That(client.Client, Is.Not.Null);
        Assert.That(client.Client.ReceiveTimeout, Is.EqualTo(3000));
        Assert.That(client.Client.SendTimeout, Is.EqualTo(3000));
    }

    [Test]
    public void GetTimeout_should_calculate_exponential_backoff_according_to_bep15()
    {
        Assert.That(_provider.GetTimeout(0), Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(_provider.GetTimeout(1), Is.EqualTo(TimeSpan.FromSeconds(10)));
        Assert.That(_provider.GetTimeout(2), Is.EqualTo(TimeSpan.FromSeconds(20)));
        Assert.That(_provider.GetTimeout(3), Is.EqualTo(TimeSpan.FromSeconds(40)));
        Assert.That(_provider.GetTimeout(4), Is.EqualTo(TimeSpan.FromSeconds(80)));
    }

    [Test]
    public void GetTimeout_should_use_configured_timeout_seconds()
    {
        _configService.UdpTrackerTimeoutSeconds.Returns(15);

        Assert.That(_provider.GetTimeout(0), Is.EqualTo(TimeSpan.FromSeconds(15)));
        Assert.That(_provider.GetTimeout(1), Is.EqualTo(TimeSpan.FromSeconds(30)));
        Assert.That(_provider.GetTimeout(2), Is.EqualTo(TimeSpan.FromSeconds(60)));
    }

    [Test]
    public void Announce_should_retry_and_recover_when_first_packet_is_dropped()
    {
        var provider = new FastTimeoutUdpTrackerProvider(_configService);
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var attempts = 0;
        var firstTxId = 0;
        var secondTxId = 0;

        var serverTask = Task.Run(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);

            // Connect request attempt 0: drop it
            var connectReq1 = server.Receive(ref ep);
            firstTxId = ReadInt32BigEndian(connectReq1, 12);
            attempts++;

            // Connect request attempt 1 (retransmitted): respond successfully
            var connectReq2 = server.Receive(ref ep);
            secondTxId = ReadInt32BigEndian(connectReq2, 12);
            attempts++;

            var connectResp = new byte[16];
            WriteInt32BigEndian(connectResp, 0, 0);
            Array.Copy(connectReq2, 12, connectResp, 4, 4);
            WriteInt64BigEndian(connectResp, 8, 42L);
            server.Send(connectResp, connectResp.Length, ep);

            // Announce request: respond successfully
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

        var result = provider.Announce(request);
        serverTask.Wait(TimeSpan.FromSeconds(5));

        Assert.That(result.Success, Is.True);
        Assert.That(attempts, Is.EqualTo(2));
        Assert.That(secondTxId, Is.EqualTo(firstTxId));
        Assert.That(result.Interval, Is.EqualTo(1800));
    }

    [Test]
    public void Announce_should_discard_mismatched_transaction_id_and_accept_matching_response()
    {
        var provider = new FastTimeoutUdpTrackerProvider(_configService);
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var serverTask = Task.Run(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);

            // Connect request
            var connectReq = server.Receive(ref ep);
            var txId = ReadInt32BigEndian(connectReq, 12);

            // Send a packet with wrong/stale transaction ID
            var staleResp = new byte[16];
            WriteInt32BigEndian(staleResp, 0, 0);
            WriteInt32BigEndian(staleResp, 4, txId + 999);
            WriteInt64BigEndian(staleResp, 8, 999L);
            server.Send(staleResp, staleResp.Length, ep);

            // Now send the packet with correct transaction ID
            var validResp = new byte[16];
            WriteInt32BigEndian(validResp, 0, 0);
            WriteInt32BigEndian(validResp, 4, txId);
            WriteInt64BigEndian(validResp, 8, 55L);
            server.Send(validResp, validResp.Length, ep);

            // Announce request
            var announceReq = server.Receive(ref ep);
            var announceResp = new byte[20];
            WriteInt32BigEndian(announceResp, 0, 1);
            Array.Copy(announceReq, 12, announceResp, 4, 4);
            WriteInt32BigEndian(announceResp, 8, 3600);
            WriteInt32BigEndian(announceResp, 12, 1);
            WriteInt32BigEndian(announceResp, 16, 2);
            server.Send(announceResp, announceResp.Length, ep);
        });

        var request = new TrackerAnnounceRequest
        {
            TrackerUrl = $"udp://127.0.0.1:{serverPort}/announce",
            InfoHash = "AABBCCDDEE112233445566778899AABBCCDDEEFF",
            PeerId = "-qB4420-abcdefghijkl",
            Port = 6881
        };

        var result = provider.Announce(request);
        serverTask.Wait(TimeSpan.FromSeconds(5));

        Assert.That(result.Success, Is.True);
        Assert.That(result.Interval, Is.EqualTo(3600));
    }

    [Test]
    public void Announce_should_fail_gracefully_when_max_retries_exceeded()
    {
        var provider = new FastTimeoutUdpTrackerProvider(_configService)
        {
            MaxRetries = 2
        };

        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var receivedAttempts = 0;
        var serverTask = Task.Run(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (receivedAttempts < 3)
            {
                server.Receive(ref ep);
                receivedAttempts++;
            }
        });

        var request = new TrackerAnnounceRequest
        {
            TrackerUrl = $"udp://127.0.0.1:{serverPort}/announce",
            InfoHash = "AABBCCDDEE112233445566778899AABBCCDDEEFF",
            PeerId = "-qB4420-abcdefghijkl",
            Port = 6881
        };

        var result = provider.Announce(request);
        serverTask.Wait(TimeSpan.FromSeconds(5));

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Does.Contain("timed out after 2 retries"));
        Assert.That(receivedAttempts, Is.EqualTo(3));
    }

    [Test]
    public void Scrape_should_retry_and_recover_when_first_scrape_packet_is_dropped()
    {
        var provider = new FastTimeoutUdpTrackerProvider(_configService);
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var scrapeAttempts = 0;
        var firstScrapeTxId = 0;
        var secondScrapeTxId = 0;

        var serverTask = Task.Run(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);

            // Handle Connect
            var connectReq = server.Receive(ref ep);
            var connectResp = new byte[16];
            WriteInt32BigEndian(connectResp, 0, 0);
            Array.Copy(connectReq, 12, connectResp, 4, 4);
            WriteInt64BigEndian(connectResp, 8, 100L);
            server.Send(connectResp, connectResp.Length, ep);

            // Scrape attempt 0: drop it
            var scrapeReq1 = server.Receive(ref ep);
            firstScrapeTxId = ReadInt32BigEndian(scrapeReq1, 12);
            scrapeAttempts++;

            // Scrape attempt 1 (retransmitted): respond successfully
            var scrapeReq2 = server.Receive(ref ep);
            secondScrapeTxId = ReadInt32BigEndian(scrapeReq2, 12);
            scrapeAttempts++;

            var scrapeResp = new byte[20];
            WriteInt32BigEndian(scrapeResp, 0, 2);
            Array.Copy(scrapeReq2, 12, scrapeResp, 4, 4);
            WriteInt32BigEndian(scrapeResp, 8, 25);
            WriteInt32BigEndian(scrapeResp, 12, 150);
            WriteInt32BigEndian(scrapeResp, 16, 5);
            server.Send(scrapeResp, scrapeResp.Length, ep);
        });

        var result = provider.Scrape(
            "AABBCCDDEE112233445566778899AABBCCDDEEFF",
            $"udp://127.0.0.1:{serverPort}/announce");

        serverTask.Wait(TimeSpan.FromSeconds(5));

        Assert.That(result.Success, Is.True);
        Assert.That(scrapeAttempts, Is.EqualTo(2));
        Assert.That(secondScrapeTxId, Is.EqualTo(firstScrapeTxId));
        Assert.That(result.Complete, Is.EqualTo(25));
        Assert.That(result.Downloaded, Is.EqualTo(150));
        Assert.That(result.Incomplete, Is.EqualTo(5));
    }

    private sealed class FastTimeoutUdpTrackerProvider : UdpTrackerProvider
    {
        public TimeSpan BaseTimeout { get; set; } = TimeSpan.FromMilliseconds(50);

        public FastTimeoutUdpTrackerProvider(IConfigService configService)
            : base(configService)
        {
        }

        internal override TimeSpan GetTimeout(int attempt)
        {
            var multiplier = 1 << Math.Min(attempt, 30);
            return BaseTimeout * multiplier;
        }
    }
}
