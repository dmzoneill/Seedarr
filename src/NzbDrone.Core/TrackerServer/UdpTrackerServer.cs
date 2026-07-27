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
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.TrackerServer;

public class UdpTrackerServer : BackgroundService
{
    private const int DefaultUdpPort = 6969;
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
    private const int ConnectionIdTtlMinutes = 2;
    private const int DefaultAnnounceInterval = 1800;
    private const int MaxScrapeHashes = 74;
    private const int ScrapeRequestHeaderSize = 16;

    private readonly PeerDatabase _peerDatabase;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<long, ConnectionEntry> _connectionIds = new();

    public UdpTrackerServer(PeerDatabase peerDatabase)
    {
        _peerDatabase = peerDatabase;
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        UdpClient client;

        try
        {
            client = new UdpClient(DefaultUdpPort);
        }
        catch (SocketException ex)
        {
            _logger.Warn(ex, "UDP tracker failed to bind port {0}, skipping", DefaultUdpPort);
            return;
        }

        _logger.Info("UDP tracker listening on port {0}", DefaultUdpPort);

        var cleanupTimer = new Timer(_ => PurgeExpiredConnections(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(stoppingToken);
                _ = Task.Run(() => HandleDatagram(client, result), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await cleanupTimer.DisposeAsync();
            client.Dispose();
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

            var response = action switch
            {
                ConnectAction => HandleConnect(connectionId, transactionId),
                AnnounceAction => HandleAnnounce(connectionId, transactionId, data, remote),
                ScrapeAction => HandleScrape(connectionId, transactionId, data),
                _ => BuildErrorResponse(transactionId, "Invalid action")
            };

            if (response != null)
            {
                client.Send(response, response.Length, remote);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "UDP tracker request error from {0}", result.RemoteEndPoint);
        }
    }

    private byte[] HandleConnect(long connectionId, int transactionId)
    {
        if (connectionId != ProtocolMagic)
        {
            return BuildErrorResponse(transactionId, "Invalid protocol magic");
        }

        var newConnectionId = GenerateConnectionId();
        _connectionIds[newConnectionId] = new ConnectionEntry { Created = DateTime.UtcNow };

        var response = new byte[16];
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(0, 4), ConnectAction);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(4, 4), transactionId);
        BinaryPrimitives.WriteInt64BigEndian(response.AsSpan(8, 8), newConnectionId);

        _logger.Debug("UDP connect from peer, issued connection_id {0}", newConnectionId);

        return response;
    }

    private byte[] HandleAnnounce(long connectionId, int transactionId, byte[] data, IPEndPoint remote)
    {
        if (!ValidateConnectionId(connectionId))
        {
            return BuildErrorResponse(transactionId, "Invalid connection_id");
        }

        if (data.Length < AnnounceRequestSize)
        {
            return BuildErrorResponse(transactionId, "Announce request too short");
        }

        var infoHash = ConvertInfoHashToHex(data, 16);
        var peerId = Encoding.Latin1.GetString(data, 16 + InfoHashLength, PeerIdLength);

        // Offsets 56-79: downloaded (8), left (8), uploaded (8) - tracked by PeerDatabase
        var eventId = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(80, 4));
        var ipAddress = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(84, 4));

        // Offset 88: key (4) - optional client identifier, not needed for peer tracking
        var numWant = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(92, 4));
        var port = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(96, 2));

        var peerIp = ipAddress != 0
            ? new IPAddress(BinaryPrimitives.ReverseEndianness(ipAddress)).ToString()
            : remote.Address.ToString();

        var peerPort = port > 0 ? port : remote.Port;

        if (numWant < 0)
        {
            numWant = 50;
        }

        var eventName = eventId switch
        {
            1 => "completed",
            2 => "started",
            3 => "stopped",
            _ => ""
        };

        if (eventName == "stopped")
        {
            _peerDatabase.RemovePeer(infoHash, peerIp, peerPort);
        }
        else
        {
            _peerDatabase.AddPeer(infoHash, peerIp, peerPort, peerId);
        }

        var peers = _peerDatabase.GetPeers(infoHash);
        var compactPeers = BuildCompactPeers(peers, peerIp, peerPort, numWant);
        var stats = _peerDatabase.GetStats(infoHash);

        var response = new byte[20 + compactPeers.Length];
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(0, 4), AnnounceAction);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(4, 4), transactionId);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(8, 4), DefaultAnnounceInterval);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(12, 4), stats.Incomplete);
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(16, 4), stats.Complete);
        Buffer.BlockCopy(compactPeers, 0, response, 20, compactPeers.Length);

        _logger.Debug(
            "UDP announce for {0} from {1}:{2}, event={3}, returning {4} peers",
            infoHash,
            peerIp,
            peerPort,
            eventName,
            compactPeers.Length / CompactPeerSize);

        return response;
    }

    private byte[] HandleScrape(long connectionId, int transactionId, byte[] data)
    {
        if (!ValidateConnectionId(connectionId))
        {
            return BuildErrorResponse(transactionId, "Invalid connection_id");
        }

        var payloadLength = data.Length - ScrapeRequestHeaderSize;

        if (payloadLength < InfoHashLength || payloadLength % InfoHashLength != 0)
        {
            return BuildErrorResponse(transactionId, "Invalid scrape request");
        }

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
        var filtered = peers
            .Where(p => p.Ip != excludeIp || p.Port != excludePort)
            .Take(maxPeers)
            .ToList();

        var data = new byte[filtered.Count * CompactPeerSize];

        for (var i = 0; i < filtered.Count; i++)
        {
            var ipParts = filtered[i].Ip.Split('.');
            var baseOffset = i * CompactPeerSize;
            data[baseOffset] = byte.Parse(ipParts[0]);
            data[baseOffset + 1] = byte.Parse(ipParts[1]);
            data[baseOffset + 2] = byte.Parse(ipParts[2]);
            data[baseOffset + 3] = byte.Parse(ipParts[3]);
            data[baseOffset + 4] = (byte)(filtered[i].Port >> 8);
            data[baseOffset + 5] = (byte)filtered[i].Port;
        }

        return data;
    }

    private static string ConvertInfoHashToHex(byte[] data, int offset)
    {
        return Convert.ToHexString(data, offset, InfoHashLength).ToLowerInvariant();
    }

    private long GenerateConnectionId()
    {
        var buffer = new byte[8];
        Random.Shared.NextBytes(buffer);
        var id = BinaryPrimitives.ReadInt64BigEndian(buffer);

        while (id == ProtocolMagic || _connectionIds.ContainsKey(id))
        {
            Random.Shared.NextBytes(buffer);
            id = BinaryPrimitives.ReadInt64BigEndian(buffer);
        }

        return id;
    }

    private bool ValidateConnectionId(long connectionId)
    {
        if (!_connectionIds.TryGetValue(connectionId, out var entry))
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

    private sealed class ConnectionEntry
    {
        public DateTime Created { get; init; }
    }
}
