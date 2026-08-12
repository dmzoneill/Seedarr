using System;
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

namespace NzbDrone.Core.Dht;

public class DhtService : BackgroundService
{
    private const int DhtPort = 6882;
    private const int PeerTtlMinutes = 30;
    private const int SecretRotationMinutes = 10;

    private readonly IConfigService _configService;
    private readonly RoutingTable _routingTable;
    private readonly Logger _logger;
    private readonly byte[] _nodeId;
    private readonly DhtPeerStore _peerStore;
    private UdpClient _udpClient;

    private byte[] _tokenSecret;
    private byte[] _previousTokenSecret;
    private DateTime _lastSecretRotation;

    private int _queryCount;
    private DateTime _rateLimitWindowStart;
    private DateTime _nextRefresh;
    private SemaphoreSlim _querySemaphore;

    public DhtService(IConfigService configService)
    {
        _configService = configService;
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configService.EnableDht)
        {
            _logger.Info("DHT service disabled via configuration");
            return;
        }

        try
        {
            _udpClient = new UdpClient(DhtPort);
        }
        catch (SocketException ex)
        {
            _logger.Warn(ex, "DHT service failed to bind port {0}, skipping", DhtPort);
            return;
        }

        _logger.Info("DHT service started on port {0}, node ID: {1}", DhtPort, Convert.ToHexString(_nodeId));

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

                // Periodic routing table refresh at the configured announcement interval
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
                    SendFindNode(endpoint, _nodeId);
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
                SendFindNodeResponse(sender, transactionId);
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

        // Parse peer values from get_peers responses
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
                    _logger.Debug("DHT get_peers response: peer {0}:{1}", ip, port);
                }
            }
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

    private void SendFindNodeResponse(IPEndPoint target, BString transactionId)
    {
        var closest = _routingTable.GetClosestNodes(_nodeId);

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
        _udpClient.Send(bytes, bytes.Length, target);
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

    private void SendFindNode(IPEndPoint target, byte[] targetId)
    {
        _querySemaphore.Wait();
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
            _udpClient.Send(bytes, bytes.Length, target);
        }
        finally
        {
            _querySemaphore.Release();
        }
    }

    public void SendGetPeers(IPEndPoint target, byte[] infoHash)
    {
        if (_udpClient == null)
        {
            return;
        }

        _querySemaphore.Wait();
        try
        {
            var transactionId = RandomNumberGenerator.GetBytes(2);
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
            _udpClient.Send(bytes, bytes.Length, target);
            _logger.Debug("DHT sent get_peers to {0} for {1}", target, Convert.ToHexString(infoHash));
        }
        finally
        {
            _querySemaphore.Release();
        }
    }

    public void SendAnnouncePeer(IPEndPoint target, byte[] infoHash, int port, byte[] token, bool impliedPort = false)
    {
        if (_udpClient == null)
        {
            return;
        }

        _querySemaphore.Wait();
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
            _udpClient.Send(bytes, bytes.Length, target);
            _logger.Debug("DHT sent announce_peer to {0} for {1} port {2}", target, Convert.ToHexString(infoHash), port);
        }
        finally
        {
            _querySemaphore.Release();
        }
    }

    private byte[] GenerateToken(IPAddress address)
    {
        var ipBytes = address.GetAddressBytes();
        var input = new byte[ipBytes.Length + _tokenSecret.Length];
        Array.Copy(ipBytes, 0, input, 0, ipBytes.Length);
        Array.Copy(_tokenSecret, 0, input, ipBytes.Length, _tokenSecret.Length);
        return SHA1.HashData(input);
    }

    private bool ValidateToken(byte[] token, IPAddress address)
    {
        var currentToken = GenerateTokenWithSecret(address, _tokenSecret);
        if (token.SequenceEqual(currentToken))
        {
            return true;
        }

        var previousToken = GenerateTokenWithSecret(address, _previousTokenSecret);
        return token.SequenceEqual(previousToken);
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
