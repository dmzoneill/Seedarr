using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
    private const int ActionError = 3;
    public const int DefaultMaxRetries = 4;
    public const int MaxScrapeHashesPerBatch = 74;
    public const int ConnectionIdExpirySeconds = 60;

    private readonly ConcurrentDictionary<string, (long ConnectionId, DateTime ExpiresAtUtc)> _connectionCache = new();
    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public string Name => "UDP";

    internal int MaxRetries { get; set; } = DefaultMaxRetries;
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public UdpTrackerProvider(IConfigService configService)
    {
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void ClearConnectionCache()
    {
        _connectionCache.Clear();
    }

    public bool InvalidateConnection(string trackerUrl)
    {
        if (string.IsNullOrWhiteSpace(trackerUrl))
        {
            return false;
        }

        if (Uri.TryCreate(trackerUrl, UriKind.Absolute, out var uri))
        {
            return InvalidateConnection(uri);
        }

        return _connectionCache.TryRemove(trackerUrl.Trim().ToLowerInvariant(), out _);
    }

    public bool InvalidateConnection(Uri trackerUri)
    {
        if (trackerUri == null)
        {
            return false;
        }

        return _connectionCache.TryRemove(GetCacheKey(trackerUri), out _);
    }

    internal void EvictConnection(string cacheKey)
    {
        if (!string.IsNullOrEmpty(cacheKey))
        {
            _connectionCache.TryRemove(cacheKey, out _);
        }
    }

    internal bool TryGetCachedConnection(string trackerUrl, out (long ConnectionId, DateTime ExpiresAtUtc) entry)
    {
        if (Uri.TryCreate(trackerUrl, UriKind.Absolute, out var uri))
        {
            return _connectionCache.TryGetValue(GetCacheKey(uri), out entry);
        }

        return _connectionCache.TryGetValue(trackerUrl.Trim().ToLowerInvariant(), out entry);
    }

    internal void SetCachedConnection(string trackerUrl, long connectionId, DateTime expiresAtUtc)
    {
        var key = Uri.TryCreate(trackerUrl, UriKind.Absolute, out var uri)
            ? GetCacheKey(uri)
            : trackerUrl.Trim().ToLowerInvariant();

        _connectionCache[key] = (connectionId, expiresAtUtc);
    }

    private static string GetCacheKey(Uri uri)
    {
        return uri.Port > 0
            ? $"{uri.Host}:{uri.Port}".ToLowerInvariant()
            : uri.Host.ToLowerInvariant();
    }

    private async Task<long> GetConnectionIdAsync(UdpClient client, Uri uri, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(uri);
        var now = UtcNow();

        if (_connectionCache.TryGetValue(cacheKey, out var entry) && now < entry.ExpiresAtUtc)
        {
            _logger.Debug("Reusing cached UDP connection ID for {0}", cacheKey);
            return entry.ConnectionId;
        }

        var connectionId = await ConnectAsync(client, cancellationToken);
        var expiresAtUtc = UtcNow().AddSeconds(ConnectionIdExpirySeconds);
        _connectionCache[cacheKey] = (connectionId, expiresAtUtc);
        _logger.Debug("Cached new UDP connection ID for {0} (expires in {1}s)", cacheKey, ConnectionIdExpirySeconds);

        return connectionId;
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
        string cacheKey = null;
        try
        {
            var timeoutMs = _configService.UdpTrackerTimeoutSeconds * 1000;
            var uri = new Uri(request.TrackerUrl);
            cacheKey = GetCacheKey(uri);
            using var client = CreateClient(timeoutMs);

            client.Connect(uri.Host, uri.Port);

            var connectionId = await GetConnectionIdAsync(client, uri, cancellationToken);

            var transactionId = GenerateTransactionId();
            var announceRequest = BuildAnnouncePacket(connectionId, transactionId, request);

            var receiveResult = await SendAndReceiveAsync(client, announceRequest, transactionId, cancellationToken);
            var response = receiveResult.Buffer;

            var addressFamily = receiveResult.RemoteEndPoint.AddressFamily;
            if (addressFamily != AddressFamily.InterNetworkV6 && uri.HostNameType == UriHostNameType.IPv6)
            {
                addressFamily = AddressFamily.InterNetworkV6;
            }

            var announceResponse = ParseAnnounceResponse(response, transactionId, addressFamily);
            if (!announceResponse.Success)
            {
                EvictConnection(cacheKey);
            }

            return announceResponse;
        }
        catch (Exception ex)
        {
            EvictConnection(cacheKey);
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
        string cacheKey = null;
        try
        {
            var timeoutMs = _configService.UdpTrackerTimeoutSeconds * 1000;
            var uri = new Uri(trackerUrl);
            cacheKey = GetCacheKey(uri);
            using var client = CreateClient(timeoutMs);

            client.Connect(uri.Host, uri.Port);

            var connectionId = await GetConnectionIdAsync(client, uri, cancellationToken);
            var transactionId = GenerateTransactionId();

            var packet = BuildScrapePacket(connectionId, transactionId, new[] { infoHash });

            var receiveResult = await SendAndReceiveAsync(client, packet, transactionId, cancellationToken);
            var response = receiveResult.Buffer;

            var results = ParseScrapeResponse(response, transactionId, new[] { infoHash });
            if (!results.TryGetValue(infoHash, out var scrapeResponse) || !scrapeResponse.Success)
            {
                EvictConnection(cacheKey);
                return scrapeResponse ?? new TrackerScrapeResponse { Success = false, FailureReason = "Unknown scrape error" };
            }

            return scrapeResponse;
        }
        catch (Exception ex)
        {
            EvictConnection(cacheKey);
            _logger.Error(ex, "UDP scrape failed for {0}", trackerUrl);
            return new TrackerScrapeResponse { Success = false, FailureReason = ex.Message };
        }
    }

    public Dictionary<string, TrackerScrapeResponse> BatchScrape(IEnumerable<string> infoHashes, string trackerUrl)
    {
        return BatchScrapeAsync(infoHashes, trackerUrl).GetAwaiter().GetResult();
    }

    public async Task<Dictionary<string, TrackerScrapeResponse>> BatchScrapeAsync(
        IEnumerable<string> infoHashes,
        string trackerUrl,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, TrackerScrapeResponse>(StringComparer.OrdinalIgnoreCase);
        if (infoHashes == null)
        {
            return result;
        }

        var hashList = new List<string>();
        foreach (var hash in infoHashes)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                continue;
            }

            var trimmed = hash.Trim();
            if (trimmed.Length != 40 || !IsHexString(trimmed))
            {
                result[trimmed] = new TrackerScrapeResponse
                {
                    Success = false,
                    FailureReason = "Invalid info_hash"
                };
                continue;
            }

            if (!hashList.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                hashList.Add(trimmed);
            }
        }

        if (hashList.Count == 0)
        {
            return result;
        }

        string cacheKey = null;
        try
        {
            var timeoutMs = _configService.UdpTrackerTimeoutSeconds * 1000;
            var uri = new Uri(trackerUrl);
            cacheKey = GetCacheKey(uri);
            using var client = CreateClient(timeoutMs);

            client.Connect(uri.Host, uri.Port);

            var connectionId = await GetConnectionIdAsync(client, uri, cancellationToken);

            for (var i = 0; i < hashList.Count; i += MaxScrapeHashesPerBatch)
            {
                var batch = hashList.Skip(i).Take(MaxScrapeHashesPerBatch).ToList();
                try
                {
                    var transactionId = GenerateTransactionId();
                    var packet = BuildScrapePacket(connectionId, transactionId, batch);

                    var receiveResult = await SendAndReceiveAsync(client, packet, transactionId, cancellationToken);
                    var batchResponses = ParseScrapeResponse(receiveResult.Buffer, transactionId, batch);

                    var anyFailed = false;
                    foreach (var kvp in batchResponses)
                    {
                        result[kvp.Key] = kvp.Value;
                        if (!kvp.Value.Success)
                        {
                            anyFailed = true;
                        }
                    }

                    if (anyFailed)
                    {
                        EvictConnection(cacheKey);
                    }
                }
                catch (Exception ex)
                {
                    EvictConnection(cacheKey);
                    _logger.Error(ex, "UDP batch scrape chunk failed for {0}", trackerUrl);
                    foreach (var hash in batch)
                    {
                        result[hash] = new TrackerScrapeResponse
                        {
                            Success = false,
                            FailureReason = ex.Message
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            EvictConnection(cacheKey);
            _logger.Error(ex, "UDP batch scrape failed for {0}", trackerUrl);
            foreach (var hash in hashList)
            {
                if (!result.ContainsKey(hash))
                {
                    result[hash] = new TrackerScrapeResponse
                    {
                        Success = false,
                        FailureReason = ex.Message
                    };
                }
            }
        }

        return result;
    }

    internal virtual async Task<long> ConnectAsync(UdpClient client, CancellationToken cancellationToken = default)
    {
        var transactionId = GenerateTransactionId();
        var packet = new byte[16];
        WriteInt64BigEndian(packet, 0, ProtocolMagic);
        WriteInt32BigEndian(packet, 8, ActionConnect);
        WriteInt32BigEndian(packet, 12, transactionId);

        var receiveResult = await SendAndReceiveAsync(client, packet, transactionId, cancellationToken);
        var response = receiveResult.Buffer;

        if (response.Length >= 8)
        {
            var responseAction = ReadInt32BigEndian(response, 0);
            if (responseAction == ActionError)
            {
                var errorReason = ParseErrorMessage(response);
                throw new InvalidOperationException($"UDP connect failed: {errorReason}");
            }
        }

        if (response.Length < 16)
        {
            throw new InvalidOperationException("UDP connect response too short");
        }

        var responseActionValue = ReadInt32BigEndian(response, 0);
        var responseTxId = ReadInt32BigEndian(response, 4);

        if (responseActionValue != ActionConnect)
        {
            throw new InvalidOperationException($"UDP connect response has unexpected action: {responseActionValue}");
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
        if (response.Length >= 8)
        {
            var responseAction = ReadInt32BigEndian(response, 0);
            if (responseAction == ActionError)
            {
                var errorReason = ParseErrorMessage(response);
                return new TrackerAnnounceResponse { Success = false, FailureReason = errorReason };
            }
        }

        if (response.Length < 20)
        {
            return new TrackerAnnounceResponse { Success = false, FailureReason = "Response too short" };
        }

        var action = ReadInt32BigEndian(response, 0);
        var responseTxId = ReadInt32BigEndian(response, 4);

        if (action != ActionAnnounce)
        {
            return new TrackerAnnounceResponse { Success = false, FailureReason = $"Unexpected announce response action: {action}" };
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

    private static string ParseErrorMessage(byte[] response)
    {
        if (response.Length <= 8)
        {
            return "Unknown error";
        }

        string message;
        try
        {
            message = Encoding.UTF8.GetString(response, 8, response.Length - 8).Trim().Trim('\0').Trim();
        }
        catch
        {
            try
            {
                message = Encoding.ASCII.GetString(response, 8, response.Length - 8).Trim().Trim('\0').Trim();
            }
            catch
            {
                message = string.Empty;
            }
        }

        return string.IsNullOrWhiteSpace(message) ? "Unknown error" : message;
    }

    internal static byte[] BuildScrapePacket(long connectionId, int transactionId, IReadOnlyList<string> infoHashes)
    {
        if (infoHashes == null || infoHashes.Count == 0)
        {
            throw new ArgumentException("At least one info_hash is required to build a scrape packet", nameof(infoHashes));
        }

        var packet = new byte[16 + (20 * infoHashes.Count)];
        WriteInt64BigEndian(packet, 0, connectionId);
        WriteInt32BigEndian(packet, 8, ActionScrape);
        WriteInt32BigEndian(packet, 12, transactionId);

        for (var i = 0; i < infoHashes.Count; i++)
        {
            var hashBytes = Convert.FromHexString(infoHashes[i]);
            if (hashBytes.Length != 20)
            {
                throw new ArgumentException($"Info-hash at index {i} must be 20 bytes (40 hex characters)", nameof(infoHashes));
            }

            Array.Copy(hashBytes, 0, packet, 16 + (20 * i), 20);
        }

        return packet;
    }

    internal static Dictionary<string, TrackerScrapeResponse> ParseScrapeResponse(
        byte[] response,
        int transactionId,
        IReadOnlyList<string> infoHashes)
    {
        var result = new Dictionary<string, TrackerScrapeResponse>(StringComparer.OrdinalIgnoreCase);
        if (infoHashes == null || infoHashes.Count == 0)
        {
            return result;
        }

        if (response == null || response.Length < 8)
        {
            foreach (var hash in infoHashes)
            {
                result[hash] = new TrackerScrapeResponse { Success = false, FailureReason = "Response too short" };
            }

            return result;
        }

        var action = ReadInt32BigEndian(response, 0);
        if (action == ActionError)
        {
            var errorReason = ParseErrorMessage(response);
            foreach (var hash in infoHashes)
            {
                result[hash] = new TrackerScrapeResponse { Success = false, FailureReason = errorReason };
            }

            return result;
        }

        if (action != ActionScrape)
        {
            foreach (var hash in infoHashes)
            {
                result[hash] = new TrackerScrapeResponse { Success = false, FailureReason = $"Unexpected scrape response action: {action}" };
            }

            return result;
        }

        var responseTxId = ReadInt32BigEndian(response, 4);
        if (responseTxId != transactionId)
        {
            foreach (var hash in infoHashes)
            {
                result[hash] = new TrackerScrapeResponse { Success = false, FailureReason = "Scrape response transaction ID mismatch" };
            }

            return result;
        }

        var expectedMinLength = 8 + (12 * infoHashes.Count);
        if (response.Length < expectedMinLength)
        {
            foreach (var hash in infoHashes)
            {
                result[hash] = new TrackerScrapeResponse { Success = false, FailureReason = "Response too short" };
            }

            return result;
        }

        for (var i = 0; i < infoHashes.Count; i++)
        {
            var baseOffset = 8 + (12 * i);
            result[infoHashes[i]] = new TrackerScrapeResponse
            {
                Success = true,
                Complete = ReadInt32BigEndian(response, baseOffset),
                Downloaded = ReadInt32BigEndian(response, baseOffset + 4),
                Incomplete = ReadInt32BigEndian(response, baseOffset + 8)
            };
        }

        return result;
    }

    private static bool IsHexString(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }

        return true;
    }
}
