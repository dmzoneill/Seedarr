using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Trackers.Udp;

public class UdpTrackerProvider : ITrackerProvider
{
    private const long ProtocolMagic = 0x41727101980;
    private const int ActionConnect = 0;
    private const int ActionAnnounce = 1;
    private const int ActionScrape = 2;

    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public string Name => "UDP";

    public UdpTrackerProvider(IConfigService configService)
    {
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public TrackerAnnounceResponse Announce(TrackerAnnounceRequest request)
    {
        try
        {
            var timeoutMs = _configService.UdpTrackerTimeoutSeconds * 1000;
            var uri = new Uri(request.TrackerUrl);
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = timeoutMs;
            client.Client.SendTimeout = timeoutMs;

            var endpoint = new IPEndPoint(IPAddress.Any, 0);
            client.Connect(uri.Host, uri.Port);

            var connectionId = Connect(client, ref endpoint);

            var transactionId = GenerateTransactionId();
            var announceRequest = BuildAnnouncePacket(connectionId, transactionId, request);
            client.Send(announceRequest, announceRequest.Length);

            var response = client.Receive(ref endpoint);
            return ParseAnnounceResponse(response, transactionId);
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
            var timeoutMs = _configService.UdpTrackerTimeoutSeconds * 1000;
            var uri = new Uri(trackerUrl);
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = timeoutMs;
            client.Client.SendTimeout = timeoutMs;

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

            var scrapeResponseAction = ReadInt32BigEndian(response, 0);
            var scrapeResponseTxId = ReadInt32BigEndian(response, 4);

            if (scrapeResponseAction != ActionScrape)
            {
                return new TrackerScrapeResponse { Success = false, FailureReason = $"Unexpected scrape response action: {scrapeResponseAction}" };
            }

            if (scrapeResponseTxId != transactionId)
            {
                return new TrackerScrapeResponse { Success = false, FailureReason = "Scrape response transaction ID mismatch" };
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

        var responseAction = ReadInt32BigEndian(response, 0);
        var responseTxId = ReadInt32BigEndian(response, 4);

        if (responseAction != ActionConnect)
        {
            throw new InvalidOperationException($"UDP connect response has unexpected action: {responseAction}");
        }

        if (responseTxId != transactionId)
        {
            throw new InvalidOperationException("UDP connect response transaction ID mismatch");
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

    private static TrackerAnnounceResponse ParseAnnounceResponse(byte[] response, int transactionId)
    {
        if (response.Length < 20)
        {
            return new TrackerAnnounceResponse { Success = false, FailureReason = "Response too short" };
        }

        var responseAction = ReadInt32BigEndian(response, 0);
        var responseTxId = ReadInt32BigEndian(response, 4);

        if (responseAction != ActionAnnounce)
        {
            return new TrackerAnnounceResponse { Success = false, FailureReason = $"Unexpected announce response action: {responseAction}" };
        }

        if (responseTxId != transactionId)
        {
            return new TrackerAnnounceResponse { Success = false, FailureReason = "Announce response transaction ID mismatch" };
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
        BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(offset, 8), value);
    }

    private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset, 4), value);
    }

    private static void WriteInt16BigEndian(byte[] buffer, int offset, short value)
    {
        BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(offset, 2), value);
    }

    private static long ReadInt64BigEndian(byte[] buffer, int offset)
    {
        return BinaryPrimitives.ReadInt64BigEndian(buffer.AsSpan(offset, 8));
    }

    private static int ReadInt32BigEndian(byte[] buffer, int offset)
    {
        return BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(offset, 4));
    }
}
