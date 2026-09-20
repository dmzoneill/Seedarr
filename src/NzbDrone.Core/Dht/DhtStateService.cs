using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Dht;

public class Node
{
    public string NodeId { get; set; }
    public string Ip { get; set; }
    public int Port { get; set; }

    public Node()
    {
    }

    public Node(string nodeId, string ip, int port)
    {
        NodeId = nodeId;
        Ip = ip;
        Port = port;
    }

    public Node(byte[] nodeId, string ip, int port)
    {
        if (nodeId != null)
        {
            NodeId = Convert.ToHexString(nodeId);
        }

        Ip = ip;
        Port = port;
    }

    public Node(DhtNode dhtNode)
    {
        if (dhtNode?.NodeId != null)
        {
            NodeId = Convert.ToHexString(dhtNode.NodeId);
        }

        if (dhtNode?.EndPoint != null)
        {
            Ip = dhtNode.EndPoint.Address.ToString();
            Port = dhtNode.EndPoint.Port;
        }
    }

    public DhtNode ToDhtNode()
    {
        if (string.IsNullOrWhiteSpace(Ip) || Port <= 0 || Port > 65535)
        {
            return null;
        }

        if (!IPAddress.TryParse(Ip, out var ipAddress))
        {
            return null;
        }

        var idBytes = ParseNodeId(NodeId);
        if (idBytes == null || idBytes.Length != 20)
        {
            return null;
        }

        return new DhtNode
        {
            NodeId = idBytes,
            EndPoint = new IPEndPoint(ipAddress, Port),
            LastSeen = DateTime.UtcNow
        };
    }

    private static byte[] ParseNodeId(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return null;
        }

        if (nodeId.Length == 40)
        {
            try
            {
                return Convert.FromHexString(nodeId);
            }
            catch
            {
            }
        }

        try
        {
            var fromBase64 = Convert.FromBase64String(nodeId);
            if (fromBase64.Length == 20)
            {
                return fromBase64;
            }
        }
        catch
        {
        }

        if (nodeId.Length == 20)
        {
            return System.Text.Encoding.UTF8.GetBytes(nodeId);
        }

        return null;
    }
}

public class DhtStateService : IDhtStateService
{
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly Logger _logger;
    private readonly string _customFilePath;

    public DhtStateService(IAppFolderInfo appFolderInfo = null, string customFilePath = null)
    {
        _appFolderInfo = appFolderInfo;
        _customFilePath = customFilePath;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public string StateFilePath => _customFilePath ?? Path.Combine(_appFolderInfo?.AppDataFolder ?? AppContext.BaseDirectory, "dht.dat");

    public void SaveRoutingTable(IEnumerable<DhtNode> nodes)
    {
        if (nodes == null)
        {
            return;
        }

        var list = nodes.Where(n => n != null && n.NodeId != null && n.EndPoint != null)
                        .Select(n => new Node(n))
                        .ToList();

        SaveRoutingTable(list);
    }

    public void SaveRoutingTable(IEnumerable<Node> nodes)
    {
        if (nodes == null)
        {
            return;
        }

        try
        {
            var filePath = StateFilePath;
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var list = nodes.Where(n => n != null && !string.IsNullOrWhiteSpace(n.Ip) && n.Port > 0).ToList();
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
            _logger.Debug("Saved {0} DHT nodes to {1}", list.Count, filePath);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to save DHT routing table state to dht.dat");
        }
    }

    public List<DhtNode> LoadRoutingTable()
    {
        var filePath = StateFilePath;
        if (!File.Exists(filePath))
        {
            _logger.Debug("DHT state file {0} does not exist", filePath);
            return new List<DhtNode>();
        }

        try
        {
            var content = File.ReadAllBytes(filePath);
            if (content == null || content.Length == 0)
            {
                return new List<DhtNode>();
            }

            var firstChar = (char)content[0];
            if (firstChar == '[' || firstChar == '{')
            {
                try
                {
                    var json = System.Text.Encoding.UTF8.GetString(content);
                    var nodes = JsonSerializer.Deserialize<List<Node>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (nodes != null)
                    {
                        var dhtNodes = new List<DhtNode>();
                        foreach (var n in nodes)
                        {
                            var dhtNode = n.ToDhtNode();
                            if (dhtNode != null)
                            {
                                dhtNodes.Add(dhtNode);
                            }
                        }

                        _logger.Debug("Loaded {0} DHT nodes from {1}", dhtNodes.Count, filePath);
                        return dhtNodes;
                    }
                }
                catch (JsonException)
                {
                    // Random binary data may start with '[' or '{'; fall through to binary compact format
                }
            }

            if (content.Length % 26 == 0)
            {
                var dhtNodes = new List<DhtNode>();
                for (var i = 0; i + 25 < content.Length; i += 26)
                {
                    var id = new byte[20];
                    Array.Copy(content, i, id, 0, 20);
                    var ip = new IPAddress(content.AsSpan(i + 20, 4));
                    var port = (content[i + 24] << 8) | content[i + 25];

                    dhtNodes.Add(new DhtNode
                    {
                        NodeId = id,
                        EndPoint = new IPEndPoint(ip, port),
                        LastSeen = DateTime.UtcNow
                    });
                }

                return dhtNodes;
            }

            _logger.Warn("Unrecognized format in DHT state file {0}", filePath);
            return new List<DhtNode>();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to load DHT state from {0}", filePath);
            return new List<DhtNode>();
        }
    }

    public List<Node> LoadRoutingTableNodes()
    {
        return LoadRoutingTable().Select(n => new Node(n)).ToList();
    }
}
