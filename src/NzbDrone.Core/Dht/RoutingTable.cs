using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace NzbDrone.Core.Dht;

/// <summary>
/// Kademlia DHT routing table with bucket-based node management and Sybil/Eclipse protection.
/// </summary>
public class RoutingTable
{
    private static readonly IComparer<byte[]> _byteComparer = Comparer<byte[]>.Create((a, b) =>
    {
        for (var i = 0; i < a.Length && i < b.Length; i++)
        {
            var cmp = a[i].CompareTo(b[i]);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return a.Length.CompareTo(b.Length);
    });

    private readonly int _bucketSize;
    private readonly int _idBits;
    private readonly int _maxNodes;
    private readonly byte[] _localNodeId;
    private readonly List<List<DhtNode>> _buckets;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutingTable"/> class.
    /// </summary>
    /// <param name="localNodeId">The 20-byte local node ID.</param>
    /// <param name="bucketSize">The maximum number of nodes per bucket (default 8).</param>
    /// <param name="idBits">The bit length of node IDs (default 160).</param>
    /// <param name="maxNodes">The maximum total nodes across all buckets (0 for unlimited).</param>
    /// <param name="allowLocal">Whether to allow loopback and link-local IP addresses (default false).</param>
    public RoutingTable(byte[] localNodeId, int bucketSize = 8, int idBits = 160, int maxNodes = 0, bool allowLocal = false)
    {
        _localNodeId = localNodeId ?? throw new ArgumentNullException(nameof(localNodeId));
        _bucketSize = bucketSize;
        _idBits = idBits;
        _maxNodes = maxNodes;
        AllowLocal = allowLocal;

        _buckets = new List<List<DhtNode>>();
        for (var i = 0; i < _idBits; i++)
        {
            _buckets.Add(new List<DhtNode>());
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether local (loopback and link-local) IP addresses are accepted.
    /// </summary>
    public bool AllowLocal { get; set; }

    /// <summary>
    /// Gets the total number of nodes stored across all buckets.
    /// </summary>
    public int NodeCount
    {
        get
        {
            lock (_lock)
            {
                return _buckets.Sum(b => b.Count);
            }
        }
    }

    /// <summary>
    /// Adds a node to the routing table, enforcing IP uniqueness and subnet diversity per bucket.
    /// </summary>
    /// <param name="node">The DHT node to add.</param>
    public void AddNode(DhtNode node)
    {
        if (node == null || node.NodeId == null)
        {
            return;
        }

        lock (_lock)
        {
            var bucketIndex = GetBucketIndex(node.NodeId);
            var bucket = _buckets[bucketIndex];

            var existing = bucket.FirstOrDefault(n => n.NodeId.SequenceEqual(node.NodeId));
            if (existing != null)
            {
                existing.LastSeen = DateTime.UtcNow;
                existing.FailCount = 0;
                if (node.EndPoint != null)
                {
                    existing.EndPoint = node.EndPoint;
                }

                return;
            }

            if (node.EndPoint != null)
            {
                if (!AllowLocal && IsLocalOrLinkLocal(node.EndPoint.Address))
                {
                    return;
                }

                if (bucket.Any(n => n.EndPoint != null &&
                                    NormalizeAddress(n.EndPoint.Address).Equals(NormalizeAddress(node.EndPoint.Address)) &&
                                    !n.NodeId.SequenceEqual(node.NodeId)))
                {
                    return;
                }

                if (bucket.Count(n => n.EndPoint != null && AreInSameSubnet(n.EndPoint.Address, node.EndPoint.Address)) >= 2)
                {
                    return;
                }
            }

            // Check max nodes cap
            if (_maxNodes > 0 && _buckets.Sum(b => b.Count) >= _maxNodes)
            {
                return;
            }

            if (bucket.Count < _bucketSize)
            {
                bucket.Add(node);
                return;
            }

            // Evict bad nodes
            var bad = bucket.FirstOrDefault(n => !n.IsGood);
            if (bad != null)
            {
                bucket.Remove(bad);
                bucket.Add(node);
            }
        }
    }

    /// <summary>
    /// Gets the closest good nodes to a target ID, ordered by XOR distance.
    /// </summary>
    /// <param name="targetId">The target 20-byte ID.</param>
    /// <param name="count">The maximum number of nodes to return (0 for bucket size).</param>
    /// <returns>A list of closest nodes.</returns>
    public List<DhtNode> GetClosestNodes(byte[] targetId, int count = 0)
    {
        if (targetId == null)
        {
            return new List<DhtNode>();
        }

        lock (_lock)
        {
            var take = count > 0 ? count : _bucketSize;
            return _buckets.SelectMany(b => b)
                .Where(n => n.IsGood)
                .OrderBy(n => Distance(n.NodeId, targetId), _byteComparer)
                .Take(take)
                .ToList();
        }
    }

    /// <summary>
    /// Determines whether two IP addresses share the same subnet prefix (/24 for IPv4, /48 for IPv6).
    /// </summary>
    /// <param name="a">The first IP address.</param>
    /// <param name="b">The second IP address.</param>
    /// <returns>True if both addresses are in the same subnet; otherwise, false.</returns>
    public static bool AreInSameSubnet(IPAddress a, IPAddress b)
    {
        if (a == null || b == null)
        {
            return false;
        }

        a = NormalizeAddress(a);
        b = NormalizeAddress(b);

        if (a.AddressFamily != b.AddressFamily)
        {
            return false;
        }

        var bytesA = a.GetAddressBytes();
        var bytesB = b.GetAddressBytes();

        // IPv4 /24 prefix: first 3 bytes (24 bits)
        if (bytesA.Length == 4 && bytesB.Length == 4)
        {
            return bytesA[0] == bytesB[0] &&
                   bytesA[1] == bytesB[1] &&
                   bytesA[2] == bytesB[2];
        }

        // IPv6 /48 prefix: first 6 bytes (48 bits)
        if (bytesA.Length == 16 && bytesB.Length == 16)
        {
            for (var i = 0; i < 6; i++)
            {
                if (bytesA[i] != bytesB[i])
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether an IP address is a loopback or link-local address.
    /// </summary>
    /// <param name="address">The IP address to check.</param>
    /// <returns>True if the address is loopback or link-local; otherwise, false.</returns>
    public static bool IsLocalOrLinkLocal(IPAddress address)
    {
        if (address == null)
        {
            return false;
        }

        address = NormalizeAddress(address);

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None))
        {
            return true;
        }

        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            // Loopback 127.0.0.0/8
            if (bytes[0] == 127)
            {
                return true;
            }

            // IPv4 Link-local 169.254.0.0/16
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            // Current network 0.0.0.0/8
            if (bytes[0] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static IPAddress NormalizeAddress(IPAddress address)
    {
        if (address == null)
        {
            return null;
        }

        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private int GetBucketIndex(byte[] nodeId)
    {
        // Compute XOR distance from local node
        for (var i = 0; i < nodeId.Length && i < _localNodeId.Length && i < _idBits / 8; i++)
        {
            var xorByte = (byte)(nodeId[i] ^ _localNodeId[i]);
            if (xorByte != 0)
            {
                var bit = (i * 8) + (7 - (int)Math.Floor(Math.Log2(xorByte)));
                var bucketIndex = (_idBits - 1) - bit;
                return Math.Min(bucketIndex, _idBits - 1);
            }
        }

        return 0; // Same as local node
    }

    private static byte[] Distance(byte[] a, byte[] b)
    {
        var length = Math.Min(a.Length, b.Length);
        var result = new byte[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = (byte)(a[i] ^ b[i]);
        }

        return result;
    }
}
