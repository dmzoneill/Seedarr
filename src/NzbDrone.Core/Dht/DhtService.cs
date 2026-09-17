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
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Dht;

public class DhtService : BackgroundService, IDhtService, IHandle<ConfigSavedEvent>
{
    private const int DhtPort = 6882;
    private const int PeerTtlMinutes = 30;
    private const int SecretRotationMinutes = 10;

    public static readonly string[] DefaultBootstrapRouters = new[]
    {
        "router.bittorrent.com:6881",
        "router.utorrent.com:6881",
        "dht.transmissionbt.com:6881",
        "dht.aelitis.com:6881",
        "dht.libtorrent.org:25401"
    };

    private readonly IConfigService _configService;
    private readonly IPeerDiscoveryService _peerDiscovery;
    private readonly ITorrentService _torrentService;
    private readonly IDhtStateService _dhtStateService;
    private readonly int? _customPort;
    private readonly RoutingTable _routingTable;
    private readonly Logger _logger;
    private readonly byte[] _nodeId;
    private readonly DhtPeerStore _peerStore;
    private readonly object _secretLock = new();
    private readonly object _stateLock = new();
    private readonly ConcurrentDictionary<string, PendingDhtQuery> _pendingQueries = new();
    private readonly ConcurrentDictionary<IPAddress, TokenBucket> _rateLimiters = new();
    private UdpClient _udpClient;
    private int _boundPort;
    private CancellationTokenSource _workerCts;
    private Task _workerTask;
    private bool _wasEnabled;
    private CancellationToken _stoppingToken;

    private byte[] _tokenSecret;
    private byte[] _previousTokenSecret;
    private DateTime _lastSecretRotation;
    private DateTime _lastRateLimitCleanup;
    private DateTime _nextRefresh;
    private SemaphoreSlim _querySemaphore;

    public event EventHandler<PeersDiscoveredEventArgs> PeersDiscovered;

    public DhtService(
        IConfigService configService,
        IPeerDiscoveryService peerDiscovery = null,
        ITorrentService torrentService = null,
        int? port = null,
        IDhtStateService dhtStateService = null)
    {
        _configService = configService;
        _peerDiscovery = peerDiscovery;
        _torrentService = torrentService;
        _customPort = port;
        _dhtStateService = dhtStateService;

        var persistedHex = configService.DhtNodeIdHex;
        if (!string.IsNullOrWhiteSpace(persistedHex) && persistedHex.Length == 40)
        {
            try
            {
                _nodeId = Convert.FromHexString(persistedHex);
            }
            catch
            {
                _nodeId = null;
            }
        }

        if (_nodeId == null || _nodeId.Length != 20)
        {
            _nodeId = DhtSecurity.GenerateNodeId(IPAddress.Loopback);
            configService.DhtNodeIdHex = Convert.ToHexString(_nodeId);
        }

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
        _lastRateLimitCleanup = DateTime.UtcNow;

        var maxConcurrent = configService.DhtConcurrentQueries;
        _querySemaphore = new SemaphoreSlim(maxConcurrent > 0 ? maxConcurrent : 3, maxConcurrent > 0 ? maxConcurrent : 3);
    }

    public override void Dispose()
    {
        SaveRoutingTableState();
        StopDht();
        _querySemaphore?.Dispose();
        _peerStore?.Dispose();
        base.Dispose();
    }

    public RoutingTable RoutingTable => _routingTable;

    public DhtPeerStore PeerStore => _peerStore;

    public byte[] NodeId => _nodeId;

    public int BoundPort => _boundPort;

    public bool IsRunning => _udpClient != null;

    public IPEndPoint LocalEndPoint => _udpClient?.Client?.LocalEndPoint as IPEndPoint;

    public void UpdateExternalAddress(IPAddress address)
    {
        if (address == null || RoutingTable.IsLocalOrLinkLocal(address))
        {
            return;
        }

        if (!DhtSecurity.IsNodeIdValid(_nodeId, address))
        {
            var newNodeId = DhtSecurity.GenerateNodeId(address);
            Array.Copy(newNodeId, _nodeId, 20);
            _configService.DhtNodeIdHex = Convert.ToHexString(_nodeId);
            _logger.Info("Updated DHT Node ID to BEP 42 compliant ID {0} for external IP {1}", Convert.ToHexString(_nodeId), address);
        }
    }

    private static bool IsNodeIdValidForEndpoint(byte[] nodeId, IPEndPoint endpoint)
    {
        if (nodeId == null || nodeId.Length != 20 || endpoint == null)
        {
            return false;
        }

        if (RoutingTable.IsLocalOrLinkLocal(endpoint.Address))
        {
            return true;
        }

        return DhtSecurity.IsNodeIdValid(nodeId, endpoint.Address);
    }

    public void Handle(ConfigSavedEvent message)
    {
        lock (_stateLock)
        {
            var isEnabled = _configService.EnableDht;

            if (isEnabled)
            {
                if (_workerTask == null || _workerTask.IsCompleted)
                {
                    _logger.Info("DHT enabled via configuration change, starting service");
                    StartDht();
                }
            }
            else if (_wasEnabled || _workerTask != null)
            {
                _logger.Info("DHT disabled via configuration change, stopping service");
                StopDht();
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        lock (_stateLock)
        {
            _wasEnabled = _configService.EnableDht;
        }

        if (_configService.EnableDht)
        {
            StartDht();
        }
        else
        {
            _logger.Info("DHT service disabled via configuration");
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
            StopDht();
            if (_workerTask != null)
            {
                try
                {
                    await _workerTask.ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    private void StartDht()
    {
        lock (_stateLock)
        {
            if (_workerTask != null && !_workerTask.IsCompleted)
            {
                return;
            }

            LoadRoutingTableState();

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

            _wasEnabled = true;
            _logger.Info("DHT service started on port {0}, node ID: {1}", _boundPort, Convert.ToHexString(_nodeId));

            _workerCts = _stoppingToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken)
                : new CancellationTokenSource();

            var token = _workerCts.Token;
            _workerTask = Task.Run(() => RunWorkerAsync(token), token);
        }
    }

    private void StopDht()
    {
        lock (_stateLock)
        {
            _wasEnabled = false;

            SaveRoutingTableState();

            if (_workerCts != null)
            {
                try
                {
                    _workerCts.Cancel();
                }
                catch (Exception)
                {
                }

                _workerCts.Dispose();
                _workerCts = null;
            }

            if (_udpClient != null)
            {
                try
                {
                    _udpClient.Close();
                    _udpClient.Dispose();
                }
                catch (Exception)
                {
                }

                _udpClient = null;
                _boundPort = 0;
                _rateLimiters.Clear();
                _logger.Info("DHT service stopped");
            }

            _workerTask = null;
        }
    }

    public void LoadRoutingTableState()
    {
        if (_dhtStateService == null)
        {
            return;
        }

        try
        {
            var cachedNodes = _dhtStateService.LoadRoutingTable();
            if (cachedNodes != null && cachedNodes.Count > 0)
            {
                foreach (var node in cachedNodes)
                {
                    _routingTable.AddNode(node);
                }

                _logger.Info("DHT loaded {0} cached nodes from state", cachedNodes.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to load DHT state on startup");
        }
    }

    public void SaveRoutingTableState()
    {
        if (_dhtStateService == null)
        {
            return;
        }

        try
        {
            var goodNodes = _routingTable.GetGoodNodes();
            if (goodNodes.Count == 0)
            {
                goodNodes = _routingTable.GetAllNodes();
            }

            _dhtStateService.SaveRoutingTable(goodNodes);
            _logger.Debug("DHT saved {0} nodes to state", goodNodes.Count);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to save DHT routing table state");
        }
    }

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
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
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }

        _nextRefresh = DateTime.UtcNow.AddSeconds(_configService.DhtAnnouncementInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                RotateSecretIfNeeded();
                CleanupExpiredQueries();
                CleanupExpiredRateLimitersIfNeeded();

                // Use query timeout so the loop wakes up periodically for maintenance
                using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                receiveCts.CancelAfter(TimeSpan.FromSeconds(_configService.DhtQueryTimeout));

                try
                {
                    var client = _udpClient;
                    if (client == null)
                    {
                        break;
                    }

                    var result = await client.ReceiveAsync(receiveCts.Token);
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
                    SaveRoutingTableState();

                    _nextRefresh = DateTime.UtcNow.AddSeconds(_configService.DhtAnnouncementInterval);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                _logger.Debug(ex, "DHT receive error");
            }
        }
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

    public IEnumerable<string> GetBootstrapNodes()
    {
        var nodes = new List<string>();
        if (!string.IsNullOrWhiteSpace(_configService?.DhtBootstrapNodes))
        {
            var userNodes = _configService.DhtBootstrapNodes
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s) && s.Contains(':'));

            nodes.AddRange(userNodes);
        }

        foreach (var def in DefaultBootstrapRouters)
        {
            if (!nodes.Contains(def, StringComparer.OrdinalIgnoreCase))
            {
                nodes.Add(def);
            }
        }

        return nodes;
    }

    private async Task Bootstrap(CancellationToken stoppingToken)
    {
        var bootstrapNodes = GetBootstrapNodes();

        foreach (var node in bootstrapNodes)
        {
            try
            {
                var lastColon = node.LastIndexOf(':');
                if (lastColon <= 0 || !int.TryParse(node.AsSpan(lastColon + 1), out var port))
                {
                    continue;
                }

                var host = node.Substring(0, lastColon).Trim('[', ']');
                if (IPAddress.TryParse(host, out var ip))
                {
                    var endpoint = new IPEndPoint(ip, port);
                    await SendFindNode(endpoint, _nodeId, stoppingToken);
                    _logger.Debug("DHT bootstrap: sent find_node to {0}", node);
                }
                else
                {
                    var addresses = await Dns.GetHostAddressesAsync(host, stoppingToken);
                    if (addresses.Length > 0)
                    {
                        var endpoint = new IPEndPoint(addresses[0], port);
                        await SendFindNode(endpoint, _nodeId, stoppingToken);
                        _logger.Debug("DHT bootstrap: sent find_node to {0}", node);
                    }
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

            if (!message.ContainsKey("y") || message["y"] is not BString yStr)
            {
                return;
            }

            var messageType = yStr.ToString();

            switch (messageType)
            {
                case "q":
                    if (IsRateLimited(sender.Address))
                    {
                        _logger.Debug("DHT query rate limit exceeded for {0}, dropping query", sender.Address);
                        return;
                    }

                    HandleQuery(message, sender);
                    break;
                case "r":
                    HandleResponse(message, sender);
                    break;
                case "e":
                    HandleErrorResponse(message, sender);
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
        var transactionId = message.ContainsKey("t") && message["t"] is BString tStr
            ? tStr
            : new BString(Array.Empty<byte>());

        if (!message.ContainsKey("q") || message["q"] is not BString qStr ||
            !message.ContainsKey("a") || message["a"] is not BDictionary args)
        {
            SendErrorResponse(sender, transactionId, 203, "Protocol Error");
            return;
        }

        if (args.ContainsKey("id") && args["id"] is BString queryIdStr && queryIdStr.Value.Length == 20)
        {
            var queryingNodeId = queryIdStr.Value.ToArray();
            if (!IsNodeIdValidForEndpoint(queryingNodeId, sender))
            {
                _logger.Debug("DHT query from {0} rejected: invalid BEP 42 node ID", sender);
                SendErrorResponse(sender, transactionId, 203, "Invalid Node ID");
                return;
            }
        }

        var queryType = qStr.ToString();

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
            default:
                SendErrorResponse(sender, transactionId, 204, "Method Unknown");
                break;
        }

        // Add querying node to routing table
        if (args.ContainsKey("id") && args["id"] is BString idStr && idStr.Value.Length == 20)
        {
            var nodeId = idStr.Value.ToArray();
            if (IsNodeIdValidForEndpoint(nodeId, sender))
            {
                _routingTable.AddNode(new DhtNode
                {
                    NodeId = nodeId,
                    EndPoint = sender,
                    LastSeen = DateTime.UtcNow
                });
            }
        }
    }

    private void HandleResponse(BDictionary message, IPEndPoint sender)
    {
        if (!message.ContainsKey("r"))
        {
            return;
        }

        // Match pending query by transaction ID
        PendingDhtQuery pending = null;
        if (message.ContainsKey("t") && message["t"] is BString tStr)
        {
            var txKey = Convert.ToHexString(tStr.Value.ToArray());
            if (_pendingQueries.TryGetValue(txKey, out var query) && sender.Equals(query.Target))
            {
                _pendingQueries.TryRemove(txKey, out pending);
            }
            else
            {
                pending = query;
            }
        }

        if (pending == null || !sender.Equals(pending.Target))
        {
            _logger.Debug("DHT response rejected: sender {0} does not match query target {1}", sender, pending?.Target);
            return;
        }

        if (message.ContainsKey("ip") && message["ip"] is BString ipBStr)
        {
            var raw = ipBStr.Value.Span;
            if (raw.Length == 6)
            {
                var extIp = new IPAddress(raw.Slice(0, 4));
                UpdateExternalAddress(extIp);
            }
            else if (raw.Length == 18)
            {
                var extIp = new IPAddress(raw.Slice(0, 16));
                UpdateExternalAddress(extIp);
            }
        }

        var response = (BDictionary)message["r"];

        if (response.ContainsKey("id"))
        {
            var nodeId = ((BString)response["id"]).Value.ToArray();
            if (IsNodeIdValidForEndpoint(nodeId, sender))
            {
                _routingTable.AddNode(new DhtNode
                {
                    NodeId = nodeId,
                    EndPoint = sender,
                    LastSeen = DateTime.UtcNow
                });
            }
        }

        // Parse compact node info from find_node / get_peers responses
        if (response.ContainsKey("nodes"))
        {
            var nodesData = ((BString)response["nodes"]).Value;
            ParseCompactNodes(nodesData.Span);
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

        if (response.ContainsKey("values6"))
        {
            var values6 = (BList)response["values6"];
            foreach (var value in values6)
            {
                var peerData = ((BString)value).Value;
                if (peerData.Length == 18)
                {
                    var ip = new IPAddress(peerData.Slice(0, 16).Span);
                    var port = (peerData.Span[16] << 8) | peerData.Span[17];
                    discoveredPeers.Add(new TrackerPeer { Ip = ip.ToString(), Port = port });
                    _logger.Debug("DHT get_peers response (IPv6): peer {0}:{1}", ip, port);
                }
            }
        }

        if (discoveredPeers.Count > 0)
        {
            var infoHashHex = pending.InfoHash != null ? Convert.ToHexString(pending.InfoHash) : null;
            if (!string.IsNullOrEmpty(infoHashHex))
            {
                _peerDiscovery?.AddPeers(infoHashHex, discoveredPeers, "dht");
            }

            PeersDiscovered?.Invoke(this, new PeersDiscoveredEventArgs(infoHashHex, discoveredPeers));
        }

        // If this was an announce query and we received a token, send announce_peer
        if (pending.IsAnnounce && response.ContainsKey("token") && pending.InfoHash != null)
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
        if (!args.ContainsKey("info_hash") || args["info_hash"] is not BString hashStr || hashStr.Value.Length != 20)
        {
            SendErrorResponse(sender, transactionId, 203, "Protocol Error");
            return;
        }

        var infoHash = hashStr.Value.ToArray();
        var token = GenerateToken(sender.Address);

        var responseDict = new BDictionary
        {
            ["id"] = new BString(_nodeId),
            ["token"] = new BString(token)
        };

        var peers = _peerStore.GetPeers(infoHash);
        var peers6 = _peerStore.GetPeers6(infoHash);
        if (peers.Count > 0 || peers6.Count > 0)
        {
            if (peers.Count > 0)
            {
                var values = new BList();
                foreach (var peer in peers.Take(50))
                {
                    values.Add(new BString(peer));
                }

                responseDict["values"] = values;
            }

            if (peers6.Count > 0)
            {
                var values6 = new BList();
                foreach (var peer in peers6.Take(50))
                {
                    values6.Add(new BString(peer));
                }

                responseDict["values6"] = values6;
            }

            _logger.Debug("DHT get_peers from {0}: returning {1} IPv4 peers, {2} IPv6 peers for {3}", sender, peers.Count, peers6.Count, Convert.ToHexString(infoHash));
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
            ["r"] = responseDict,
            ["ip"] = new BString(EncodeCompactAddress(sender))
        };

        var bytes = response.EncodeAsBytes();
        _udpClient?.Send(bytes, bytes.Length, sender);
    }

    private void HandleAnnouncePeerQuery(BDictionary args, IPEndPoint sender, BString transactionId)
    {
        if (!args.ContainsKey("info_hash") || args["info_hash"] is not BString hashStr || hashStr.Value.Length != 20)
        {
            SendErrorResponse(sender, transactionId, 203, "Protocol Error");
            return;
        }

        if (!args.ContainsKey("token") || args["token"] is not BString tokenStr)
        {
            SendErrorResponse(sender, transactionId, 203, "Protocol Error");
            return;
        }

        var infoHash = hashStr.Value.ToArray();
        var receivedToken = tokenStr.Value.ToArray();

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
            if (args["implied_port"] is not BNumber impliedNumber)
            {
                SendErrorResponse(sender, transactionId, 203, "Protocol Error");
                return;
            }

            var impliedPort = impliedNumber.Value;
            if (impliedPort == 0)
            {
                if (args.ContainsKey("port"))
                {
                    if (args["port"] is not BNumber portNumber)
                    {
                        SendErrorResponse(sender, transactionId, 203, "Protocol Error");
                        return;
                    }

                    port = (int)portNumber.Value;
                }
            }
        }
        else if (args.ContainsKey("port"))
        {
            if (args["port"] is not BNumber portNumber)
            {
                SendErrorResponse(sender, transactionId, 203, "Protocol Error");
                return;
            }

            port = (int)portNumber.Value;
        }

        if (port < 1 || port > 65535)
        {
            _logger.Debug("DHT announce_peer from {0}: invalid port {1}", sender, port);
            SendErrorResponse(sender, transactionId, 203, "Protocol Error");
            return;
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
            var ep = new IPEndPoint(ip, port);

            if (IsNodeIdValidForEndpoint(nodeId, ep))
            {
                _routingTable.AddNode(new DhtNode
                {
                    NodeId = nodeId,
                    EndPoint = ep,
                    LastSeen = DateTime.UtcNow
                });
            }
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

    private static byte[] EncodeCompactAddress(IPEndPoint endpoint)
    {
        if (endpoint == null)
        {
            return Array.Empty<byte>();
        }

        var ipBytes = endpoint.Address.GetAddressBytes();
        var result = new byte[ipBytes.Length + 2];
        Array.Copy(ipBytes, 0, result, 0, ipBytes.Length);
        result[ipBytes.Length] = (byte)(endpoint.Port >> 8);
        result[ipBytes.Length + 1] = (byte)(endpoint.Port & 0xFF);
        return result;
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
            },
            ["ip"] = new BString(EncodeCompactAddress(target))
        };

        var bytes = response.EncodeAsBytes();
        _udpClient?.Send(bytes, bytes.Length, target);
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
            },
            ["ip"] = new BString(EncodeCompactAddress(sender))
        };

        var bytes = response.EncodeAsBytes();
        _udpClient?.Send(bytes, bytes.Length, sender);
    }

    private void SendErrorResponse(IPEndPoint target, BString transactionId, int errorCode, string errorMessage)
    {
        var error = new BDictionary
        {
            ["t"] = transactionId ?? new BString(Array.Empty<byte>()),
            ["y"] = new BString("e"),
            ["e"] = new BList
            {
                (IBObject)new BNumber(errorCode),
                (IBObject)new BString(errorMessage)
            }
        };

        var bytes = error.EncodeAsBytes();
        _udpClient?.Send(bytes, bytes.Length, target);
    }

    private async Task SendFindNode(IPEndPoint target, byte[] targetId, CancellationToken ct = default)
    {
        await _querySemaphore.WaitAsync(ct);
        try
        {
            var transactionId = RandomNumberGenerator.GetBytes(4);
            var txKey = Convert.ToHexString(transactionId);

            _pendingQueries[txKey] = new PendingDhtQuery
            {
                QueryType = "find_node",
                Target = target,
                SentAt = DateTime.UtcNow
            };

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
            var client = _udpClient;
            if (client != null)
            {
                await client.SendAsync(bytes, bytes.Length, target);
            }
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
            var transactionId = RandomNumberGenerator.GetBytes(4);
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
            var transactionId = RandomNumberGenerator.GetBytes(4);
            var txKey = Convert.ToHexString(transactionId);

            _pendingQueries[txKey] = new PendingDhtQuery
            {
                QueryType = "announce_peer",
                InfoHash = infoHash,
                Target = target,
                SentAt = DateTime.UtcNow
            };

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

    private bool IsRateLimited(IPAddress address)
    {
        if (!_configService.DhtRateLimitEnabled)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var maxPerSecond = _configService.DhtMaxQueriesPerSecond;

        if (_rateLimiters.Count > 10000)
        {
            CleanupExpiredRateLimiters(now);
        }

        var bucket = _rateLimiters.GetOrAdd(address, _ => new TokenBucket(maxPerSecond, now));
        return !bucket.TryConsume(maxPerSecond, now);
    }

    private void CleanupExpiredRateLimitersIfNeeded()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastRateLimitCleanup).TotalSeconds < 30)
        {
            return;
        }

        CleanupExpiredRateLimiters(now);
    }

    private void CleanupExpiredRateLimiters(DateTime now)
    {
        _lastRateLimitCleanup = now;
        var cutoff = now.AddMinutes(-2);
        foreach (var (ip, bucket) in _rateLimiters)
        {
            if (bucket.LastSeen < cutoff)
            {
                _rateLimiters.TryRemove(ip, out _);
            }
        }
    }

    private void HandleErrorResponse(BDictionary message, IPEndPoint sender)
    {
        if (message.ContainsKey("t") && message["t"] is BString tStr)
        {
            var txKey = Convert.ToHexString(tStr.Value.ToArray());
            _pendingQueries.TryRemove(txKey, out _);
        }

        _logger.Debug("DHT received error response from {0}", sender);
    }

    private sealed class TokenBucket
    {
        private readonly object _lock = new();
        private double _tokens;
        private DateTime _lastRefill;

        public DateTime LastSeen { get; private set; }

        public TokenBucket(double capacity, DateTime now)
        {
            _tokens = capacity;
            _lastRefill = now;
            LastSeen = now;
        }

        public bool TryConsume(int maxPerSecond, DateTime now)
        {
            if (maxPerSecond <= 0)
            {
                return false;
            }

            lock (_lock)
            {
                LastSeen = now;
                var elapsed = (now - _lastRefill).TotalSeconds;
                if (elapsed > 0)
                {
                    _tokens = Math.Min(maxPerSecond, _tokens + (elapsed * maxPerSecond));
                    _lastRefill = now;
                }

                if (_tokens >= 1.0)
                {
                    _tokens -= 1.0;
                    return true;
                }

                return false;
            }
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
