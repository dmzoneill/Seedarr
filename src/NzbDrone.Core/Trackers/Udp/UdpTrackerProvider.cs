using System;
using System.Net;
using System.Net.Sockets;
using NLog;

namespace NzbDrone.Core.Trackers.Udp;

public class UdpTrackerProvider : ITrackerProvider
{
    private readonly Logger _logger;
    private const long ProtocolMagic = 0x41727101980;
    private const int ActionConnect = 0;
    private const int ActionAnnounce = 1;
    private const int ActionScrape = 2;

    public string Name => "UDP";

    public UdpTrackerProvider()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public TrackerAnnounceResponse Announce(TrackerAnnounceRequest request)
    {
        try
        {
            var uri = new Uri(request.TrackerUrl);
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 15000;
            client.Client.SendTimeout = 15000;

            var endpoint = new IPEndPoint(IPAddress.Any, 0);
            client.Connect(uri.Host, uri.Port);

            var connectionId = Connect(client, ref endpoint);

            var transactionId = GenerateTransactionId();
            var announceRequest = BuildAnnouncePacket(connectionId, transactionId, request);
            client.Send(announceRequest, announceRequest.Length);

            var response = client.Receive(ref endpoint);
            return ParseAnnounceResponse(response);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "UDP announce failed for {0}", request.TrackerUrl);
            return new TrackerAnnounceResponse
            {
                Success = false,
                FailureReason = ex.Message
            };
        }
    }

    public TrackerScrapeResponse Scrape(string infoHash, string trackerUrl)
    {
        try
        {
            var uri = new Uri(trackerUrl);
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 15000;
            client.Client.SendTimeout = 15000;

            var endpoint = new IPEndPoint(IPAddress.Any, 0);
            client.Connect(uri.Host, uri.Port);

            var connectionId = Connect(client, ref endpoint);
            var transactionId = GenerateTransactionId();

            var hashBytes = Convert.FromHexString(infoHash);
            var packet = new byte[36];
            WriteInt64BigEndian(packet, 0, connectionId);
            WriteInt32BigEndian(packet, 8, ActionScrape);
            WriteInt32BigEndian(packet, 12, transactionId);
            Array.Copy(hashBytes, 0, packet, 16, 20);

            client.Send(packet, packet.Length);
            var response = client.Receive(ref endpoint);

            if (response.Length < 20)
            {
                return new TrackerScrapeResponse { Success = false, FailureReason = "Response too short" };
            }

            return new TrackerScrapeResponse
            {
                Success = true,
                Complete = ReadInt32BigEndian(response, 8),
                Downloaded = ReadInt32BigEndian(response, 12),
                Incomplete = ReadInt32BigEndian(response, 16)
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "UDP scrape failed for {0}", trackerUrl);
            return new TrackerScrapeResponse { Success = false, FailureReason = ex.Message };
        }
    }

    private long Connect(UdpClient client, ref IPEndPoint endpoint)
    {
        var transactionId = GenerateTransactionId();
        var packet = new byte[16];
        WriteInt64BigEndian(packet, 0, ProtocolMagic);
        WriteInt32BigEndian(packet, 8, ActionConnect);
        WriteInt32BigEndian(packet, 12, transactionId);

        client.Send(packet, packet.Length);
        var response = client.Receive(ref endpoint);

        if (response.Length < 16)
        {
            throw new InvalidOperationException("UDP connect response too short");
        }

        return ReadInt64BigEndian(response, 8);
    }

    private static byte[] BuildAnnouncePacket(long connectionId, int transactionId, TrackerAnnounceRequest request)
    {
        var packet = new byte[98];
        WriteInt64BigEndian(packet, 0, connectionId);
        WriteInt32BigEndian(packet, 8, ActionAnnounce);
        WriteInt32BigEndian(packet, 12, transactionId);

        var hashBytes = Convert.FromHexString(request.InfoHash);
        Array.Copy(hashBytes, 0, packet, 16, 20);

        var peerIdBytes = System.Text.Encoding.ASCII.GetBytes(request.PeerId.PadRight(20)[..20]);
        Array.Copy(peerIdBytes, 0, packet, 36, 20);

        WriteInt64BigEndian(packet, 56, request.Downloaded);
        WriteInt64BigEndian(packet, 64, request.Left);
        WriteInt64BigEndian(packet, 72, request.Uploaded);

        var eventValue = request.Event switch
        {
            "completed" => 1,
            "started" => 2,
            "stopped" => 3,
            _ => 0
        };
        WriteInt32BigEndian(packet, 80, eventValue);
        WriteInt32BigEndian(packet, 84, 0);
        WriteInt32BigEndian(packet, 88, 0);
        WriteInt32BigEndian(packet, 92, request.NumWant);
        WriteInt16BigEndian(packet, 96, (short)request.Port);

        return packet;
    }

    private static TrackerAnnounceResponse ParseAnnounceResponse(byte[] response)
    {
        if (response.Length < 20)
        {
            return new TrackerAnnounceResponse { Success = false, FailureReason = "Response too short" };
        }

        var result = new TrackerAnnounceResponse
        {
            Success = true,
            Interval = ReadInt32BigEndian(response, 8),
            Incomplete = ReadInt32BigEndian(response, 12),
            Complete = ReadInt32BigEndian(response, 16)
        };

        for (var i = 20; i + 5 < response.Length; i += 6)
        {
            var ip = $"{response[i]}.{response[i + 1]}.{response[i + 2]}.{response[i + 3]}";
            var port = (response[i + 4] << 8) | response[i + 5];
            result.Peers.Add(new TrackerPeer { Ip = ip, Port = port });
        }

        return result;
    }

    private static int GenerateTransactionId()
    {
        return System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue);
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

    private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteInt16BigEndian(byte[] buffer, int offset, short value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
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
        return (buffer[offset] << 24) |
               (buffer[offset + 1] << 16) |
               (buffer[offset + 2] << 8) |
               buffer[offset + 3];
    }
}
