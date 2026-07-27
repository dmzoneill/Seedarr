using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.Dht;

public class DhtService : BackgroundService
{
    private const int DhtPort = 6882;

    private readonly RoutingTable _routingTable;
    private readonly Logger _logger;
    private readonly byte[] _nodeId;
    private UdpClient _udpClient;

    public DhtService()
    {
        _routingTable = new RoutingTable();
        _logger = LogManager.GetCurrentClassLogger();
        _nodeId = RandomNumberGenerator.GetBytes(20);
    }

    public RoutingTable RoutingTable => _routingTable;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

        // Bootstrap with well-known nodes
        await Bootstrap(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(stoppingToken);
                HandleMessage(result.Buffer, result.RemoteEndPoint);
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

        // Parse compact node info from find_node responses
        if (response.ContainsKey("nodes"))
        {
            var nodesData = ((BString)response["nodes"]).Value;
            ParseCompactNodes(nodesData.Span);
        }
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
        var compactNodes = new byte[closest.Count * 26];
        for (var i = 0; i < closest.Count; i++)
        {
            var node = closest[i];
            Array.Copy(node.NodeId, 0, compactNodes, i * 26, 20);
            var ipBytes = node.EndPoint.Address.GetAddressBytes();
            Array.Copy(ipBytes, 0, compactNodes, (i * 26) + 20, 4);
            compactNodes[(i * 26) + 24] = (byte)(node.EndPoint.Port >> 8);
            compactNodes[(i * 26) + 25] = (byte)node.EndPoint.Port;
        }

        var response = new BDictionary
        {
            ["t"] = transactionId,
            ["y"] = new BString("r"),
            ["r"] = new BDictionary
            {
                ["id"] = new BString(_nodeId),
                ["nodes"] = new BString(compactNodes)
            }
        };

        var bytes = response.EncodeAsBytes();
        _udpClient.Send(bytes, bytes.Length, target);
    }

    private void SendFindNode(IPEndPoint target, byte[] targetId)
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
}
