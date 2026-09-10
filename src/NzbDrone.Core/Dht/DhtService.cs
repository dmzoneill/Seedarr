using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Dht;

public class DhtService : BackgroundService, IDhtService
{
    private const int DhtPort = 6882;
    private const int PeerTtlMinutes = 30;
    private const int SecretRotationMinutes = 10;

    private readonly IConfigService _configService;
    private readonly IPeerDiscoveryService _peerDiscovery;
    private readonly ITorrentService _torrentService;
    private readonly int? _customPort;
    private readonly RoutingTable _routingTable;
    private readonly Logger _logger;
    private readonly byte[] _nodeId;
    private readonly DhtPeerStore _peerStore;
    private readonly object _secretLock = new();
    private readonly ConcurrentDictionary<string, PendingDhtQuery> _pendingQueries = new();
    private UdpClient _udpClient;
    private int _boundPort;

    private byte[] _tokenSecret;
    private byte[] _previousTokenSecret;
    private DateTime _lastSecretRotation;

    private int _queryCount;
    private DateTime _rateLimitWindowStart;
    private DateTime _nextRefresh;
    private SemaphoreSlim _querySemaphore;

    public event EventHandler<PeersDiscoveredEventArgs> PeersDiscovered;

    public DhtService(
        IConfigService configService,
        IPeerDiscoveryService peerDiscovery = null,
        ITorrentService torrentService = null,
        int? port = null)
    {
        _configService = configService;
        _peerDiscovery = peerDiscovery;
        _torrentService = torrentService;
        _customPort = port;
        _nodeId = RandomNumberGenerator.GetBytes(20);
        _routingTable = new RoutingTable(
            _nodeId,
            configService.DhtBucketSize,
            configService.DhtRoutingTableSize,
            configService.DhtMaxNodes);
        _logger = LogManager.GetCurrentClassLogger();
        _peerStore = new DhtPeerStore(PeerTtlMinutes);

        _tokenSecret = RandomNumberGenerator.GetBytes(16);
        _previousTokenSecret = RandomNumberGenerator.GetBytes(16);
        _lastSecretRotation = DateTime.UtcNow;
        _rateLimitWindowStart = DateTime.UtcNow;

        var maxConcurrent = configService.DhtConcurrentQueries;
        _querySemaphore = new SemaphoreSlim(maxConcurrent > 0 ? maxConcurrent : 3, maxConcurrent > 0 ? maxConcurrent : 3);
    }

    public override void Dispose()
    {
        _querySemaphore?.Dispose();
        base.Dispose();
    }

    public RoutingTable RoutingTable => _routingTable;

    public DhtPeerStore PeerStore => _peerStore;

    public int BoundPort => _boundPort;

    public IPEndPoint LocalEndPoint => _udpClient?.Client?.LocalEndPoint as IPEndPoint;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configService.EnableDht)
        {
            _logger.Info("DHT service disabled via configuration");
            return;
        }

        var portToBind = _customPort ?? DhtPort;
        try
        {
            _udpClient = new UdpClient(portToBind);
            _boundPort = ((IPEndPoint)_udpClient.Client.LocalEndPoint).Port;
        }
        catch (SocketException ex)
        {
            _logger.Warn(ex, "DHT service failed to bind port {0}, skipping", portToBind);
            return;
        }

        _logger.Info("DHT service started on port {0}, node ID: {1}", _boundPort, Convert.ToHexString(_nodeId));

        // Bootstrap with well-known nodes if enabled
        if (_configService.DhtAutoBootstrap)
        {
            using var bootstrapCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            bootstrapCts.CancelAfter(TimeSpan.FromSeconds(_configService.DhtBootstrapTimeout));
            try
            {
                await Bootstrap(bootstrapCts.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.Warn("DHT bootstrap timed out after {0}s", _configService.DhtBootstrapTimeout);
            }
        }

        _nextRefresh = DateTime.UtcNow.AddSeconds(_configService.DhtAnnouncementInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                RotateSecretIfNeeded();
                CleanupExpiredQueries();

                // Use query timeout so the loop wakes up periodically for maintenance
                using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                receiveCts.CancelAfter(TimeSpan.FromSeconds(_configService.DhtQueryTimeout));

                try
                {
                    var result = await _udpClient.ReceiveAsync(receiveCts.Token);

                    // Rate limiting
                    if (_configService.DhtRateLimitEnabled)
                    {
                        var now = DateTime.UtcNow;
                        if ((now - _rateLimitWindowStart).TotalSeconds >= 1.0)
                        {
                            _rateLimitWindowStart = now;
                            _queryCount = 0;
                        }

                        if (_queryCount >= _configService.DhtMaxQueriesPerSecond)
                        {
                            continue;
                        }

                        _queryCount++;
                    }

                    HandleMessage(result.Buffer, result.RemoteEndPoint);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Query timeout — no messages received, continue to maintenance check
                }

                // Periodic routing table refresh and torrent announce at the configured announcement interval
                if (DateTime.UtcNow >= _nextRefresh)
                {
                    if (_configService.DhtAutoBootstrap)
                    {
                        using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        refreshCts.CancelAfter(TimeSpan.FromSeconds(_configService.DhtBootstrapTimeout));
                        try
                        {
                            await Bootstrap(refreshCts.Token);
                        }
                        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                        {
                            _logger.Debug("DHT periodic refresh timed out");
                        }
                    }

                    await AnnounceTorrentsAsync(stoppingToken);

                    _nextRefresh = DateTime.UtcNow.AddSeconds(_configService.DhtAnnouncementInterval);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "DHT receive error");
            }
        }

        _udpClient?.Dispose();
    }

    private async Task AnnounceTorrentsAsync(CancellationToken ct)
    {
        if (!_configService.EnableDht || _torrentService == null)
        {
            return;
        }

        List<Torrent> torrents;
        try
        {
            torrents = _torrentService.GetAll();
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "DHT: failed to retrieve torrents for announce");
            return;
        }

        var publicTorrents = torrents
            .Where(t => !t.IsPrivate && !string.IsNullOrEmpty(t.InfoHash) && t.Status != TorrentStatus.Stopped && t.Status != TorrentStatus.Paused)
            .ToList();

        var port = _configService.ListeningPort > 0 ? _configService.ListeningPort : 6881;

        foreach (var torrent in publicTorrents)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await AnnounceTorrent(torrent.InfoHash, port, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "DHT announce failed for torrent {0}", torrent.InfoHash);
            }
        }
    }

    private async Task Bootstrap(CancellationToken stoppingToken)
    {
        // Send find_node to bootstrap nodes
        var bootstrapNodes = new[]
        {
            "router.bittorrent.com:6881",
            "dht.transmissionbt.com:6881"
        };

        foreach (var node in bootstrapNodes)
        {
            try
            {
                var parts = node.Split(':');
                var addresses = await Dns.GetHostAddressesAsync(parts[0], stoppingToken);
                if (addresses.Length > 0)
                {
                    var endpoint = new IPEndPoint(addresses[0], int.Parse(parts[1]));
                    await SendFindNode(endpoint, _nodeId, stoppingToken);
                    _logger.Debug("DHT bootstrap: sent find_node to {0}", node);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "DHT bootstrap failed for {0}", node);
            }
        }
    }

    public async Task Bootstrap(IPEndPoint endpoint, CancellationToken ct = default)
    {
        if (_udpClient == null || endpoint == null)
        {
            return;
        }

        await SendFindNode(endpoint, _nodeId, ct);
    }

    private void HandleMessage(byte[] data, IPEndPoint sender)
    {
        try
        {
            var parser = new BencodeParser();
            var message = parser.Parse<BDictionary>(data);

            var messageType = ((BString)message["y"]).ToString();

            switch (messageType)
            {
                case "q":
                    HandleQuery(message, sender);
                    break;
                case "r":
                    HandleResponse(message, sender);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Trace(ex, "DHT parse error from {0}", sender);
        }
    }

    private void HandleQuery(BDictionary message, IPEndPoint sender)
    {
        var queryType = ((BString)message["q"]).ToString();
        var args = (BDictionary)message["a"];
        var transactionId = (BString)message["t"];

        switch (queryType)
        {
            case "ping":
                SendPingResponse(sender, transactionId);
                break;
            case "find_node":
                HandleFindNodeQuery(args, sender, transactionId);
                break;
            case "get_peers":
                HandleGetPeersQuery(args, sender, transactionId);
                break;
            case "announce_peer":
                HandleAnnouncePeerQuery(args, sender, transactionId);
                break;
        }

        // Add querying node to routing table
        if (args.ContainsKey("id"))
        {
            var nodeId = ((BString)args["id"]).Value.ToArray();
            _routingTable.AddNode(new DhtNode
            {
                NodeId = nodeId,
                EndPoint = sender,
                LastSeen = DateTime.UtcNow
            });
        }
    }

    private void HandleResponse(BDictionary message, IPEndPoint sender)
    {
        if (!message.ContainsKey("r"))
        {
            return;
        }

        var response = (BDictionary)message["r"];

        if (response.ContainsKey("id"))
        {
            var nodeId = ((BString)response["id"]).Value.ToArray();
            _routingTable.AddNode(new DhtNode
            {
                NodeId = nodeId,
                EndPoint = sender,
                LastSeen = DateTime.UtcNow
            });
        }

        // Parse compact node info from find_node / get_peers responses
        if (response.ContainsKey("nodes"))
        {
            var nodesData = ((BString)response["nodes"]).Value;
            ParseCompactNodes(nodesData.Span);
        }

        // Match pending query by transaction ID
        PendingDhtQuery pending = null;
        if (message.ContainsKey("t") && message["t"] is BString tStr)
        {
            var txKey = Convert.ToHexString(tStr.Value.ToArray());
            _pendingQueries.TryRemove(txKey, out pending);
        }

        // Parse peer values from get_peers responses
        var discoveredPeers = new List<TrackerPeer>();
        if (response.ContainsKey("values"))
        {
            var values = (BList)response["values"];
            foreach (var value in values)
            {
                var peerData = ((BString)value).Value;
                if (peerData.Length == 6)
                {
                    var ip = new IPAddress(peerData.Slice(0, 4).Span);
                    var port = (peerData.Span[4] << 8) | peerData.Span[5];
                    discoveredPeers.Add(new TrackerPeer { Ip = ip.ToString(), Port = port });
                    _logger.Debug("DHT get_peers response: peer {0}:{1}", ip, port);
                }
            }
        }

        if (discoveredPeers.Count > 0)
        {
            var infoHashHex = pending?.InfoHash != null ? Convert.ToHexString(pending.InfoHash) : null;
            if (!string.IsNullOrEmpty(infoHashHex))
            {
                _peerDiscovery?.AddPeers(infoHashHex, discoveredPeers, "dht");
            }

            PeersDiscovered?.Invoke(this, new PeersDiscoveredEventArgs(infoHashHex, discoveredPeers));
        }

        // If this was an announce query and we received a token, send announce_peer
        if (pending?.IsAnnounce == true && response.ContainsKey("token") && pending.InfoHash != null)
        {
            var token = ((BString)response["token"]).Value.ToArray();
            var announcePort = pending.Port > 0 ? pending.Port : (_configService.ListeningPort > 0 ? _configService.ListeningPort : 6881);
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendAnnouncePeer(sender, pending.InfoHash, announcePort, token, false, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to send announce_peer after get_peers token to {0}", sender);
                }
            });
        }
    }

    private void HandleGetPeersQuery(BDictionary args, IPEndPoint sender, BString transactionId)
    {
        if (!args.ContainsKey("info_hash"))
        {
            return;
        }

        var infoHash = ((BString)args["info_hash"]).Value.ToArray();
        var token = GenerateToken(sender.Address);

        var responseDict = new BDictionary
        {
            ["id"] = new BString(_nodeId),
            ["token"] = new BString(token)
        };

        var peers = _peerStore.GetPeers(infoHash);
        if (peers.Count > 0)
        {
            var values = new BList();
            foreach (var peer in peers)
            {
                values.Add(new BString(peer));
            }

            responseDict["values"] = values;
            _logger.Debug("DHT get_peers from {0}: returning {1} peers for {2}", sender, peers.Count, Convert.ToHexString(infoHash));
        }
        else
        {
            var closest = _routingTable.GetClosestNodes(infoHash);
            responseDict["nodes"] = new BString(EncodeCompactNodes(closest));
            _logger.Debug("DHT get_peers from {0}: no peers for {1}, returning {2} closest nodes", sender, Convert.ToHexString(infoHash), closest.Count);
        }

        var response = new BDictionary
        {
            ["t"] = transactionId,
            ["y"] = new BString("r"),
            ["r"] = responseDict
        };

        var bytes = response.EncodeAsBytes();
        _udpClient.Send(bytes, bytes.Length, sender);
    }

    private void HandleAnnouncePeerQuery(BDictionary args, IPEndPoint sender, BString transactionId)
    {
        if (!args.ContainsKey("info_hash") || !args.ContainsKey("token"))
        {
            return;
        }

        var infoHash = ((BString)args["info_hash"]).Value.ToArray();
        var receivedToken = ((BString)args["token"]).Value.ToArray();

        if (!ValidateToken(receivedToken, sender.Address))
        {
            _logger.Debug("DHT announce_peer from {0}: invalid token", sender);
            SendErrorResponse(sender, transactionId, 203, "Invalid token");
            return;
        }

        // BEP 5: if implied_port is set and non-zero, use the UDP source port
        var port = sender.Port;
        if (args.ContainsKey("implied_port"))
        {
            var impliedPort = ((BNumber)args["implied_port"]).Value;
            if (impliedPort == 0 && args.ContainsKey("port"))
            {
                port = (int)((BNumber)args["port"]).Value;
            }
        }
        else if (args.ContainsKey("port"))
        {
            port = (int)((BNumber)args["port"]).Value;
        }

        _peerStore.AddPeer(infoHash, sender.Address, port);
        _logger.Debug("DHT announce_peer from {0}: stored peer for {1} at port {2}", sender, Convert.ToHexString(infoHash), port);

        SendPingResponse(sender, transactionId);
    }

    private void ParseCompactNodes(ReadOnlySpan<byte> data)
    {
        // 26 bytes per node: 20 bytes node ID + 4 bytes IP + 2 bytes port
        for (var i = 0; i + 25 < data.Length; i += 26)
        {
            var nodeId = data.Slice(i, 20).ToArray();
            var ip = new IPAddress(data.Slice(i + 20, 4));
            var port = (data[i + 24] << 8) | data[i + 25];

            _routingTable.AddNode(new DhtNode
            {
                NodeId = nodeId,
                EndPoint = new IPEndPoint(ip, port),
                LastSeen = DateTime.UtcNow
            });
        }
    }

    private byte[] EncodeCompactNodes(List<DhtNode> nodes)
    {
        var compactNodes = new byte[nodes.Count * 26];
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            Array.Copy(node.NodeId, 0, compactNodes, i * 26, 20);
            var ipBytes = node.EndPoint.Address.GetAddressBytes();
            Array.Copy(ipBytes, 0, compactNodes, (i * 26) + 20, 4);
            compactNodes[(i * 26) + 24] = (byte)(node.EndPoint.Port >> 8);
            compactNodes[(i * 26) + 25] = (byte)node.EndPoint.Port;
        }

        return compactNodes;
    }

    private void SendPingResponse(IPEndPoint target, BString transactionId)
    {
        var response = new BDictionary
        {
            ["t"] = transactionId,
            ["y"] = new BString("r"),
            ["r"] = new BDictionary
            {
                ["id"] = new BString(_nodeId)
            }
        };

        var bytes = response.EncodeAsBytes();
        _udpClient.Send(bytes, bytes.Length, target);
    }

    private void HandleFindNodeQuery(BDictionary args, IPEndPoint sender, BString transactionId)
    {
        var targetId = args.ContainsKey("target") && args["target"] is BString targetStr
            ? targetStr.Value.ToArray()
            : _nodeId;

        var closest = _routingTable.GetClosestNodes(targetId);

        var response = new BDictionary
        {
            ["t"] = transactionId,
            ["y"] = new BString("r"),
            ["r"] = new BDictionary
            {
                ["id"] = new BString(_nodeId),
                ["nodes"] = new BString(EncodeCompactNodes(closest))
            }
        };

        var bytes = response.EncodeAsBytes();
        _udpClient.Send(bytes, bytes.Length, sender);
    }

    private void SendErrorResponse(IPEndPoint target, BString transactionId, int code, string message)
    {
        var error = new BDictionary
        {
            ["t"] = transactionId,
            ["y"] = new BString("e"),
            ["e"] = new BList
            {
                (IBObject)new BNumber(code),
                (IBObject)new BString(message)
            }
        };

        var bytes = error.EncodeAsBytes();
        _udpClient.Send(bytes, bytes.Length, target);
    }

    private async Task SendFindNode(IPEndPoint target, byte[] targetId, CancellationToken ct = default)
    {
        await _querySemaphore.WaitAsync(ct);
        try
        {
            var transactionId = RandomNumberGenerator.GetBytes(2);
            var query = new BDictionary
            {
                ["t"] = new BString(transactionId),
                ["y"] = new BString("q"),
                ["q"] = new BString("find_node"),
                ["a"] = new BDictionary
                {
                    ["id"] = new BString(_nodeId),
                    ["target"] = new BString(targetId)
                }
            };

            var bytes = query.EncodeAsBytes();
            await _udpClient.SendAsync(bytes, bytes.Length, target);
        }
        finally
        {
            _querySemaphore.Release();
        }
    }

    public Task SendGetPeers(IPEndPoint target, byte[] infoHash, CancellationToken ct = default)
    {
        return SendGetPeersInternal(target, infoHash, isAnnounce: false, port: 0, ct: ct);
    }

    public Task SendGetPeers(IPEndPoint target, string infoHash, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return Task.CompletedTask;
        }

        return SendGetPeers(target, Convert.FromHexString(infoHash), ct);
    }

    private async Task SendGetPeersInternal(IPEndPoint target, byte[] infoHash, bool isAnnounce, int port, CancellationToken ct = default)
    {
        if (_udpClient == null || infoHash == null)
        {
            return;
        }

        await _querySemaphore.WaitAsync(ct);
        try
        {
            var transactionId = RandomNumberGenerator.GetBytes(2);
            var txKey = Convert.ToHexString(transactionId);

            _pendingQueries[txKey] = new PendingDhtQuery
            {
                QueryType = "get_peers",
                InfoHash = infoHash,
                Target = target,
                IsAnnounce = isAnnounce,
                Port = port,
                SentAt = DateTime.UtcNow
            };

            var query = new BDictionary
            {
                ["t"] = new BString(transactionId),
                ["y"] = new BString("q"),
                ["q"] = new BString("get_peers"),
                ["a"] = new BDictionary
                {
                    ["id"] = new BString(_nodeId),
                    ["info_hash"] = new BString(infoHash)
                }
            };

            var bytes = query.EncodeAsBytes();
            await _udpClient.SendAsync(bytes, bytes.Length, target);
            _logger.Debug("DHT sent get_peers to {0} for {1}", target, Convert.ToHexString(infoHash));
        }
        finally
        {
            _querySemaphore.Release();
        }
    }

    public Task SendAnnouncePeer(IPEndPoint target, string infoHash, int port, byte[] token, bool impliedPort = false, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return Task.CompletedTask;
        }

        return SendAnnouncePeer(target, Convert.FromHexString(infoHash), port, token, impliedPort, ct);
    }

    public async Task SendAnnouncePeer(IPEndPoint target, byte[] infoHash, int port, byte[] token, bool impliedPort = false, CancellationToken ct = default)
    {
        if (_udpClient == null || infoHash == null || token == null)
        {
            return;
        }

        await _querySemaphore.WaitAsync(ct);
        try
        {
            var transactionId = RandomNumberGenerator.GetBytes(2);
            var args = new BDictionary
            {
                ["id"] = new BString(_nodeId),
                ["info_hash"] = new BString(infoHash),
                ["port"] = new BNumber(port),
                ["token"] = new BString(token)
            };

            if (impliedPort)
            {
                args["implied_port"] = new BNumber(1);
            }

            var query = new BDictionary
            {
                ["t"] = new BString(transactionId),
                ["y"] = new BString("q"),
                ["q"] = new BString("announce_peer"),
                ["a"] = args
            };

            var bytes = query.EncodeAsBytes();
            await _udpClient.SendAsync(bytes, bytes.Length, target);
            _logger.Debug("DHT sent announce_peer to {0} for {1} port {2}", target, Convert.ToHexString(infoHash), port);
        }
        finally
        {
            _querySemaphore.Release();
        }
    }

    public async Task AnnounceTorrent(string infoHash, int port, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(infoHash) || _udpClient == null)
        {
            return;
        }

        byte[] infoHashBytes;
        try
        {
            infoHashBytes = Convert.FromHexString(infoHash);
        }
        catch (Exception)
        {
            _logger.Debug("Invalid hex infoHash: {0}", infoHash);
            return;
        }

        await AnnounceTorrent(infoHashBytes, port, ct);
    }

    public async Task AnnounceTorrent(byte[] infoHash, int port, CancellationToken ct = default)
    {
        if (infoHash == null || infoHash.Length != 20 || _udpClient == null)
        {
            return;
        }

        var closest = _routingTable.GetClosestNodes(infoHash);
        if (closest.Count == 0)
        {
            _logger.Debug("DHT: no nodes in routing table to announce {0}", Convert.ToHexString(infoHash));
            return;
        }

        foreach (var node in closest)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await SendGetPeersInternal(node.EndPoint, infoHash, isAnnounce: true, port: port, ct: ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "DHT: error sending get_peers for announce to {0}", node.EndPoint);
            }
        }
    }

    private void CleanupExpiredQueries()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        foreach (var kvp in _pendingQueries)
        {
            if (kvp.Value.SentAt < cutoff)
            {
                _pendingQueries.TryRemove(kvp.Key, out _);
            }
        }
    }

    private byte[] GenerateToken(IPAddress address)
    {
        byte[] secret;
        lock (_secretLock)
        {
            secret = _tokenSecret;
        }

        return GenerateTokenWithSecret(address, secret);
    }

    private bool ValidateToken(byte[] token, IPAddress address)
    {
        if (token == null || token.Length == 0)
        {
            return false;
        }

        byte[] currentSecret;
        byte[] previousSecret;

        lock (_secretLock)
        {
            currentSecret = _tokenSecret;
            previousSecret = _previousTokenSecret;
        }

        var currentToken = GenerateTokenWithSecret(address, currentSecret);
        if (CryptographicOperations.FixedTimeEquals(token, currentToken))
        {
            return true;
        }

        var previousToken = GenerateTokenWithSecret(address, previousSecret);
        return CryptographicOperations.FixedTimeEquals(token, previousToken);
    }

    private byte[] GenerateTokenWithSecret(IPAddress address, byte[] secret)
    {
        var ipBytes = address.GetAddressBytes();
        var input = new byte[ipBytes.Length + secret.Length];
        Array.Copy(ipBytes, 0, input, 0, ipBytes.Length);
        Array.Copy(secret, 0, input, ipBytes.Length, secret.Length);
        return SHA1.HashData(input);
    }

    private void RotateSecretIfNeeded()
    {
        lock (_secretLock)
        {
            if ((DateTime.UtcNow - _lastSecretRotation).TotalMinutes < SecretRotationMinutes)
            {
                return;
            }

            _previousTokenSecret = _tokenSecret;
            _tokenSecret = RandomNumberGenerator.GetBytes(16);
            _lastSecretRotation = DateTime.UtcNow;
            _logger.Debug("DHT token secret rotated");
        }
    }

    private class PendingDhtQuery
    {
        public string QueryType { get; set; }
        public byte[] InfoHash { get; set; }
        public IPEndPoint Target { get; set; }
        public bool IsAnnounce { get; set; }
        public int Port { get; set; }
        public DateTime SentAt { get; set; }
    }
}
