using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.TrackerServer;

public class UdpTrackerServer : BackgroundService, IHandle<ConfigSavedEvent>
{
    private const long ProtocolMagic = 0x41727101980;
    private const int ConnectAction = 0;
    private const int AnnounceAction = 1;
    private const int ScrapeAction = 2;
    private const int ErrorAction = 3;
    private const int MinConnectRequestSize = 16;
    private const int AnnounceRequestSize = 98;
    private const int InfoHashLength = 20;
    private const int PeerIdLength = 20;
    private const int CompactPeerSize = 6;
    private const int CompactPeer6Size = 18;
    private const int ConnectionIdTtlMinutes = 2;
    private const int MaxScrapeHashes = 74;
    private const int ScrapeRequestHeaderSize = 16;

    private readonly IPeerDatabase _peerDatabase;
    private readonly IConfigService _configService;
    private readonly ITorrentService _torrentService;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<long, ConnectionEntry> _connectionIds = new();
    private readonly ConcurrentDictionary<string, RateLimitEntry> _rateLimits = new();
    private readonly object _sendLock = new();
    private readonly object _listenerLock = new();

    private UdpClient _client;
    private CancellationTokenSource _listenerCts;
    private Timer _cleanupTimer;
    private bool _wasEnabled;
    private int _boundPort;
    private string _boundAddress;

    public UdpTrackerServer(IPeerDatabase peerDatabase, IConfigService configService)
        : this(peerDatabase, configService, null)
    {
    }

    public UdpTrackerServer(IPeerDatabase peerDatabase, IConfigService configService, ITorrentService torrentService)
    {
        _peerDatabase = peerDatabase;
        _configService = configService;
        _torrentService = torrentService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(ConfigSavedEvent message)
    {
        var isEnabled = _configService.TrackerServerEnabled && _configService.TrackerUdpEnabled;

        lock (_listenerLock)
        {
            if (isEnabled)
            {
                var currentPort = (_client?.Client?.LocalEndPoint as IPEndPoint)?.Port;
                var portChanged = _client != null && (_boundPort != _configService.TrackerUdpPort || (currentPort.HasValue && currentPort != _configService.TrackerUdpPort && _configService.TrackerUdpPort != 0));
                var addressChanged = _client != null && !string.Equals(_boundAddress ?? string.Empty, _configService.TrackerBindAddress ?? string.Empty, StringComparison.OrdinalIgnoreCase);

                if (portChanged || addressChanged)
                {
                    StopListener();
                }

                if (_client == null)
                {
                    _logger.Info("UDP tracker starting listener");
                    StartListener();
                }
            }
            else if (_wasEnabled || _client != null)
            {
                _logger.Info("UDP tracker disabled via config change, stopping listener");
                StopListener();
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var isEnabled = _configService.TrackerServerEnabled && _configService.TrackerUdpEnabled;

        lock (_listenerLock)
        {
            _wasEnabled = isEnabled;
        }

        if (isEnabled)
        {
            StartListener();
        }
        else
        {
            _logger.Debug("Built-in tracker is disabled, waiting for config change");
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            StopListener();
        }
    }

    private void StartListener()
    {
        lock (_listenerLock)
        {
            if (_client != null)
            {
                return;
            }

            var port = _configService.TrackerUdpPort;
            var bindAddressStr = _configService.TrackerBindAddress;
            var bindAddress = !string.IsNullOrWhiteSpace(bindAddressStr) && IPAddress.TryParse(bindAddressStr, out var addr) ? addr : IPAddress.Any;
            UdpClient client;

            try
            {
                if (bindAddress.Equals(IPAddress.IPv6Any))
                {
                    client = new UdpClient(AddressFamily.InterNetworkV6);
                    client.Client.DualMode = true;
                    client.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
                }
                else
                {
                    client = new UdpClient(new IPEndPoint(bindAddress, port));
                }
            }
            catch (SocketException ex)
            {
                _logger.Warn(ex, "UDP tracker failed to bind {0}:{1}, skipping", bindAddress, port);
                return;
            }

            _client = client;
            _boundPort = port;
            _boundAddress = bindAddressStr;
            _wasEnabled = true;
            _listenerCts = new CancellationTokenSource();
            _logger.Info("UDP tracker listening on {0}:{1}", bindAddress, port);

            _cleanupTimer = new Timer(
                _ =>
                {
                    PurgeExpiredConnections();
                    PurgeExpiredRateLimits();
                },
                null,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1));

            _ = Task.Run(() => ReceiveLoop(_client, _listenerCts.Token));
        }
    }

    private void StopListener()
    {
        lock (_listenerLock)
        {
            _wasEnabled = false;

            if (_cleanupTimer != null)
            {
                _cleanupTimer.Dispose();
                _cleanupTimer = null;
            }

            if (_listenerCts != null)
            {
                _listenerCts.Cancel();
                _listenerCts.Dispose();
                _listenerCts = null;
            }

            if (_client != null)
            {
                try
                {
                    _client.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Error disposing UDP client");
                }

                _client = null;
                _logger.Info("UDP tracker stopped");
            }
        }
    }

    private async Task ReceiveLoop(UdpClient client, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(ct);
                _ = Task.Run(() => HandleDatagram(client, result), ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                _logger.Debug(ex, "UDP tracker receive error");
            }
        }
    }

    private void HandleDatagram(UdpClient client, UdpReceiveResult result)
    {
        try
        {
            var data = result.Buffer;
            var remote = result.RemoteEndPoint;

            if (data.Length < MinConnectRequestSize)
            {
                return;
            }

            var connectionId = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(0, 8));
            var action = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(8, 4));
            var transactionId = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(12, 4));
            var clientAddress = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address;
            var clientIp = clientAddress.ToString();

            string infoHash = null;
            if (action == AnnounceAction && data.Length >= 16 + InfoHashLength)
            {
                infoHash = ConvertInfoHashToHex(data, 16);
            }

            if (IsRateLimited(clientIp, infoHash))
            {
                return;
            }

            var response = action switch
            {
                ConnectAction => HandleConnect(connectionId, transactionId, remote),
                AnnounceAction => HandleAnnounce(connectionId, transactionId, data, remote),
                ScrapeAction => HandleScrape(connectionId, transactionId, data, remote),
                _ => BuildErrorResponse(transactionId, "Invalid action")
            };

            if (response != null)
            {
                lock (_sendLock)
                {
                    client.Send(response, response.Length, remote);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "UDP tracker request error from {0}", result.RemoteEndPoint);
        }
    }

    private byte[] HandleConnect(long connectionId, int transactionId) =>
        HandleConnect(connectionId, transactionId, new IPEndPoint(IPAddress.Loopback, 0));

    private byte[] HandleConnect(long connectionId, int transactionId, IPEndPoint remote)
    {
        if (connectionId != ProtocolMagic)
        {
            return BuildErrorResponse(transactionId, "Invalid protocol magic");
        }

        var clientAddress = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address;

        var newConnectionId = GenerateConnectionId();
        _connectionIds[newConnectionId] = new ConnectionEntry
        {
            Created = DateTime.UtcNow,
            RemoteAddress = clientAddress
        };

        var response = new byte[16];
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(0, 4), ConnectAction);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(4, 4), transactionId);
        BinaryPrimitives.WriteInt64BigEndian(response.AsSpan(8, 8), newConnectionId);

        _logger.Debug("UDP connect from peer, issued connection_id {0}", newConnectionId);

        return response;
    }

    private byte[] HandleAnnounce(long connectionId, int transactionId, byte[] data, IPEndPoint remote)
    {
        var clientAddress = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address;

        if (!ValidateConnectionId(connectionId, clientAddress))
        {
            return BuildErrorResponse(transactionId, "Invalid connection_id");
        }

        if (data.Length < AnnounceRequestSize)
        {
            return BuildErrorResponse(transactionId, "Announce request too short");
        }

        var infoHash = ConvertInfoHashToHex(data, 16);
        var peerId = Encoding.Latin1.GetString(data, 16 + InfoHashLength, PeerIdLength);

        if (_configService.TrackerPrivateMode)
        {
            var isRegistered = _torrentService != null && _torrentService.ExistsByInfoHash(infoHash);
            if (!isRegistered)
            {
                return BuildErrorResponse(transactionId, "torrent not registered with tracker");
            }
        }

        var left = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(64, 8));
        var isSeeder = left == 0;
        var eventId = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(80, 4));

        var numWant = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(92, 4));
        var port = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(96, 2));

        // Always use the remote address; never trust client-specified IP
        var peerIp = clientAddress.ToString();

        var peerPort = port > 0 ? port : remote.Port;

        var configMaxPeers = _configService.TrackerMaxPeersPerAnnounce;

        if (numWant < 0 || numWant > configMaxPeers)
        {
            numWant = configMaxPeers;
        }

        var eventName = eventId switch
        {
            1 => "completed",
            2 => "started",
            3 => "stopped",
            _ => ""
        };

        if (eventName == "completed")
        {
            _peerDatabase.RecordCompleted(infoHash);
        }

        if (eventName == "stopped")
        {
            _peerDatabase.RemovePeer(infoHash, peerIp, peerPort);
        }
        else
        {
            _peerDatabase.AddPeer(infoHash, peerIp, peerPort, peerId, isSeeder);
        }

        _peerDatabase.IncrementAnnounces();

        var peers = _peerDatabase.GetPeers(infoHash);
        var isIpv6Client = clientAddress.AddressFamily == AddressFamily.InterNetworkV6;
        var compactPeers = isIpv6Client
            ? BuildCompactPeers6(peers, peerIp, peerPort, numWant)
            : BuildCompactPeers(peers, peerIp, peerPort, numWant);
        var stats = _peerDatabase.GetStats(infoHash) ?? new ScrapeStats();

        var announceInterval = _configService.TrackerAnnounceInterval;

        var response = new byte[20 + compactPeers.Length];
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(0, 4), AnnounceAction);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(4, 4), transactionId);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(8, 4), announceInterval);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(12, 4), stats.Incomplete);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(16, 4), stats.Complete);
        Buffer.BlockCopy(compactPeers, 0, response, 20, compactPeers.Length);

        if (_configService.TrackerLogAnnounces)
        {
            var peerSize = isIpv6Client ? CompactPeer6Size : CompactPeerSize;
            _logger.Info(
                "UDP announce for {0} from {1}:{2}, event={3}, returning {4} peers",
                infoHash,
                peerIp,
                peerPort,
                eventName,
                compactPeers.Length / peerSize);
        }

        return response;
    }

    private byte[] HandleScrape(long connectionId, int transactionId, byte[] data) =>
        HandleScrape(connectionId, transactionId, data, new IPEndPoint(IPAddress.Loopback, 0));

    private byte[] HandleScrape(long connectionId, int transactionId, byte[] data, IPEndPoint remote)
    {
        var clientAddress = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address;

        if (!ValidateConnectionId(connectionId, clientAddress))
        {
            return BuildErrorResponse(transactionId, "Invalid connection_id");
        }

        if (!_configService.TrackerEnableScrape)
        {
            return BuildErrorResponse(transactionId, "Scrape disabled");
        }

        var payloadLength = data.Length - ScrapeRequestHeaderSize;

        if (payloadLength < InfoHashLength || payloadLength % InfoHashLength != 0)
        {
            return BuildErrorResponse(transactionId, "Invalid scrape request");
        }

        _peerDatabase.IncrementScrapes();

        var hashCount = payloadLength / InfoHashLength;

        if (hashCount > MaxScrapeHashes)
        {
            hashCount = MaxScrapeHashes;
        }

        var response = new byte[8 + (hashCount * 12)];
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(0, 4), ScrapeAction);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(4, 4), transactionId);

        for (var i = 0; i < hashCount; i++)
        {
            var offset = ScrapeRequestHeaderSize + (i * InfoHashLength);
            var infoHash = ConvertInfoHashToHex(data, offset);
            var stats = _peerDatabase.GetStats(infoHash);
            var responseOffset = 8 + (i * 12);

            BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(responseOffset, 4), stats.Complete);
            BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(responseOffset + 4, 4), stats.Downloaded);
            BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(responseOffset + 8, 4), stats.Incomplete);
        }

        _logger.Debug("UDP scrape for {0} info_hashes", hashCount);

        return response;
    }

    private static byte[] BuildErrorResponse(int transactionId, string message)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var response = new byte[8 + messageBytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(0, 4), ErrorAction);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(4, 4), transactionId);
        Buffer.BlockCopy(messageBytes, 0, response, 8, messageBytes.Length);

        return response;
    }

    private static byte[] BuildCompactPeers(List<TrackerPeerEntry> peers, string excludeIp, int excludePort, int maxPeers)
    {
        if (maxPeers <= 0)
        {
            return Array.Empty<byte>();
        }

        IPAddress.TryParse(excludeIp, out var excludeAddr);
        if (excludeAddr != null && excludeAddr.IsIPv4MappedToIPv6)
        {
            excludeAddr = excludeAddr.MapToIPv4();
        }

        var chunks = new List<byte>();

        foreach (var peer in peers)
        {
            if (!IPAddress.TryParse(peer.Ip, out var addr))
            {
                continue;
            }

            if (addr.IsIPv4MappedToIPv6)
            {
                addr = addr.MapToIPv4();
            }

            if (addr.AddressFamily != AddressFamily.InterNetwork)
            {
                continue;
            }

            if (peer.Port == excludePort && (peer.Ip == excludeIp || (excludeAddr != null && addr.Equals(excludeAddr))))
            {
                continue;
            }

            var ipBytes = addr.GetAddressBytes();
            chunks.AddRange(ipBytes);
            chunks.Add((byte)(peer.Port >> 8));
            chunks.Add((byte)peer.Port);

            if (chunks.Count / CompactPeerSize >= maxPeers)
            {
                break;
            }
        }

        return chunks.ToArray();
    }

    private static byte[] BuildCompactPeers6(List<TrackerPeerEntry> peers, string excludeIp, int excludePort, int maxPeers)
    {
        if (maxPeers <= 0)
        {
            return Array.Empty<byte>();
        }

        IPAddress.TryParse(excludeIp, out var excludeAddr);

        var chunks = new List<byte>();

        foreach (var peer in peers)
        {
            if (!IPAddress.TryParse(peer.Ip, out var addr))
            {
                continue;
            }

            if (addr.IsIPv4MappedToIPv6 || addr.AddressFamily != AddressFamily.InterNetworkV6)
            {
                continue;
            }

            if (peer.Port == excludePort && (peer.Ip == excludeIp || (excludeAddr != null && addr.Equals(excludeAddr))))
            {
                continue;
            }

            var ipBytes = addr.GetAddressBytes();
            chunks.AddRange(ipBytes);
            chunks.Add((byte)(peer.Port >> 8));
            chunks.Add((byte)peer.Port);

            if (chunks.Count / CompactPeer6Size >= maxPeers)
            {
                break;
            }
        }

        return chunks.ToArray();
    }

    private static string ConvertInfoHashToHex(byte[] data, int offset)
    {
        return Convert.ToHexString(data, offset, InfoHashLength).ToLowerInvariant();
    }

    private long GenerateConnectionId()
    {
        var buffer = new byte[8];
        RandomNumberGenerator.Fill(buffer);
        var id = BinaryPrimitives.ReadInt64BigEndian(buffer);

        while (id == ProtocolMagic || _connectionIds.ContainsKey(id))
        {
            RandomNumberGenerator.Fill(buffer);
            id = BinaryPrimitives.ReadInt64BigEndian(buffer);
        }

        return id;
    }

    private bool ValidateConnectionId(long connectionId) =>
        ValidateConnectionId(connectionId, IPAddress.Loopback);

    private bool ValidateConnectionId(long connectionId, IPAddress remoteAddress)
    {
        if (!_connectionIds.TryGetValue(connectionId, out var entry))
        {
            return false;
        }

        if (remoteAddress != null && remoteAddress.IsIPv4MappedToIPv6)
        {
            remoteAddress = remoteAddress.MapToIPv4();
        }

        if (remoteAddress == null || entry.RemoteAddress == null || !entry.RemoteAddress.Equals(remoteAddress))
        {
            return false;
        }

        if ((DateTime.UtcNow - entry.Created).TotalMinutes > ConnectionIdTtlMinutes)
        {
            _connectionIds.TryRemove(connectionId, out _);
            return false;
        }

        return true;
    }

    private void PurgeExpiredConnections()
    {
        var expired = _connectionIds
            .Where(kvp => (DateTime.UtcNow - kvp.Value.Created).TotalMinutes > ConnectionIdTtlMinutes)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expired)
        {
            _connectionIds.TryRemove(key, out _);
        }

        if (expired.Count > 0)
        {
            _logger.Debug("Purged {0} expired UDP connection IDs", expired.Count);
        }
    }

    private bool IsRateLimited(string ip) => IsRateLimited(ip, null);

    private bool IsRateLimited(string ip, string infoHash)
    {
        var rateLimit = _configService.TrackerRateLimitPerMinute;

        if (rateLimit <= 0)
        {
            return false;
        }

        var key = string.IsNullOrEmpty(infoHash) ? ip : $"{ip}:{infoHash.ToLowerInvariant()}";
        var now = DateTime.UtcNow;
        var entry = _rateLimits.AddOrUpdate(
            key,
            _ => new RateLimitEntry { Count = 1, WindowStart = now },
            (_, existing) =>
            {
                if ((now - existing.WindowStart).TotalMinutes >= 1)
                {
                    return new RateLimitEntry { Count = 1, WindowStart = now };
                }

                return new RateLimitEntry { Count = existing.Count + 1, WindowStart = existing.WindowStart };
            });

        return entry.Count > rateLimit;
    }

    private void PurgeExpiredRateLimits()
    {
        var now = DateTime.UtcNow;
        var expired = _rateLimits
            .Where(kvp => (now - kvp.Value.WindowStart).TotalMinutes >= 2)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expired)
        {
            _rateLimits.TryRemove(key, out _);
        }
    }

    private sealed class ConnectionEntry
    {
        public DateTime Created { get; init; }
        public IPAddress RemoteAddress { get; init; }
    }

    private sealed class RateLimitEntry
    {
        public int Count { get; init; }
        public DateTime WindowStart { get; init; }
    }
}
