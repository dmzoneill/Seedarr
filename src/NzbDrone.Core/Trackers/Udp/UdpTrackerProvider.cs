using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network;

namespace NzbDrone.Core.Trackers.Udp;

public class UdpTrackerProvider : ITrackerProvider
{
    private const long ProtocolMagic = 0x41727101980;
    private const int ActionConnect = 0;
    private const int ActionAnnounce = 1;
    private const int ActionScrape = 2;
    public const int DefaultMaxRetries = 4;

    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public string Name => "UDP";

    internal int MaxRetries { get; set; } = DefaultMaxRetries;

    public UdpTrackerProvider(IConfigService configService)
    {
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    internal virtual UdpClient CreateClient(int timeoutMs)
    {
        var client = new UdpClient();
        client.Client.ReceiveTimeout = timeoutMs;
        client.Client.SendTimeout = timeoutMs;
        client.Client.BindToNetworkInterface(_configService.BindInterface);
        return client;
    }

    internal virtual TimeSpan GetTimeout(int attempt)
    {
        var baseSeconds = _configService.UdpTrackerTimeoutSeconds > 0
            ? _configService.UdpTrackerTimeoutSeconds
            : 5;

        var multiplier = 1 << Math.Min(attempt, 30);
        return TimeSpan.FromSeconds(baseSeconds * multiplier);
    }

    internal virtual async Task<UdpReceiveResult> SendAndReceiveAsync(
        UdpClient client,
        byte[] packet,
        int transactionId,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var timeout = GetTimeout(attempt);
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(timeout);

            _logger.Debug(
                "Sending UDP tracker packet (transactionId: {0}, attempt {1}/{2})",
                transactionId,
                attempt + 1,
                MaxRetries + 1);

            await client.SendAsync(packet.AsMemory(), attemptCts.Token);

            while (!attemptCts.IsCancellationRequested)
            {
                UdpReceiveResult receiveResult;
                try
                {
                    receiveResult = await client.ReceiveAsync(attemptCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.Debug(
                        "UDP tracker request timed out after {0}s (transactionId: {1}, attempt {2}/{3})",
                        timeout.TotalSeconds,
                        transactionId,
                        attempt + 1,
                        MaxRetries + 1);
                    break;
                }

                var response = receiveResult.Buffer;

                if (response.Length >= 8)
                {
                    var responseTxId = ReadInt32BigEndian(response, 4);
                    if (responseTxId != transactionId)
                    {
                        _logger.Debug(
                            "Discarding UDP packet with mismatched transaction ID {0} (expected {1})",
                            responseTxId,
                            transactionId);
                        continue;
                    }
                }

                return receiveResult;
            }
        }

        _logger.Warn(
            "UDP tracker request timed out after {0} retries (transactionId: {1})",
            MaxRetries,
            transactionId);

        throw new TimeoutException($"UDP tracker request timed out after {MaxRetries} retries");
    }

    public TrackerAnnounceResponse Announce(TrackerAnnounceRequest request)
    {
        return AnnounceAsync(request).GetAwaiter().GetResult();
    }

    public async Task<TrackerAnnounceResponse> AnnounceAsync(TrackerAnnounceRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var timeoutMs = _configService.UdpTrackerTimeoutSeconds * 1000;
            var uri = new Uri(request.TrackerUrl);
            using var client = CreateClient(timeoutMs);

            client.Connect(uri.Host, uri.Port);

            var connectionId = await ConnectAsync(client, cancellationToken);

            var transactionId = GenerateTransactionId();
            var announceRequest = BuildAnnouncePacket(connectionId, transactionId, request);

            var receiveResult = await SendAndReceiveAsync(client, announceRequest, transactionId, cancellationToken);
            var response = receiveResult.Buffer;

            var addressFamily = receiveResult.RemoteEndPoint.AddressFamily;
            if (addressFamily != AddressFamily.InterNetworkV6 && uri.HostNameType == UriHostNameType.IPv6)
            {
                addressFamily = AddressFamily.InterNetworkV6;
            }

            return ParseAnnounceResponse(response, transactionId, addressFamily);
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
        return ScrapeAsync(infoHash, trackerUrl).GetAwaiter().GetResult();
    }

    public async Task<TrackerScrapeResponse> ScrapeAsync(string infoHash, string trackerUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var timeoutMs = _configService.UdpTrackerTimeoutSeconds * 1000;
            var uri = new Uri(trackerUrl);
            using var client = CreateClient(timeoutMs);

            client.Connect(uri.Host, uri.Port);

            var connectionId = await ConnectAsync(client, cancellationToken);
            var transactionId = GenerateTransactionId();

            var hashBytes = Convert.FromHexString(infoHash);
            var packet = new byte[36];
            WriteInt64BigEndian(packet, 0, connectionId);
            WriteInt32BigEndian(packet, 8, ActionScrape);
            WriteInt32BigEndian(packet, 12, transactionId);
            Array.Copy(hashBytes, 0, packet, 16, 20);

            var receiveResult = await SendAndReceiveAsync(client, packet, transactionId, cancellationToken);
            var response = receiveResult.Buffer;

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

    private async Task<long> ConnectAsync(UdpClient client, CancellationToken cancellationToken = default)
    {
        var transactionId = GenerateTransactionId();
        var packet = new byte[16];
        WriteInt64BigEndian(packet, 0, ProtocolMagic);
        WriteInt32BigEndian(packet, 8, ActionConnect);
        WriteInt32BigEndian(packet, 12, transactionId);

        var receiveResult = await SendAndReceiveAsync(client, packet, transactionId, cancellationToken);
        var response = receiveResult.Buffer;

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
            AnnounceEvent.Completed => 1,
            AnnounceEvent.Started => 2,
            AnnounceEvent.Stopped => 3,
            _ => 0
        };
        WriteInt32BigEndian(packet, 80, eventValue);
        WriteInt32BigEndian(packet, 84, 0);
        WriteInt32BigEndian(packet, 88, 0);
        WriteInt32BigEndian(packet, 92, request.NumWant);
        WriteInt16BigEndian(packet, 96, (short)request.Port);

        return packet;
    }

    internal static TrackerAnnounceResponse ParseAnnounceResponse(byte[] response, int transactionId, AddressFamily addressFamily = AddressFamily.Unspecified)
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

        var remaining = response.Length - 20;
        var isIPv6 = addressFamily == AddressFamily.InterNetworkV6 ||
            (remaining > 0 && remaining % 18 == 0 && remaining % 6 != 0);

        if (isIPv6)
        {
            for (var i = 20; i + 17 < response.Length; i += 18)
            {
                var ip = new IPAddress(response.AsSpan(i, 16)).ToString();
                var port = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(i + 16, 2));
                result.Peers.Add(new TrackerPeer { Ip = ip, Port = port });
            }
        }
        else
        {
            for (var i = 20; i + 5 < response.Length; i += 6)
            {
                var ip = new IPAddress(response.AsSpan(i, 4)).ToString();
                var port = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(i + 4, 2));
                result.Peers.Add(new TrackerPeer { Ip = ip, Port = port });
            }
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
