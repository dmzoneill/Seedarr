using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Dht;

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

    public RoutingTable(byte[] localNodeId, int bucketSize = 8, int idBits = 160, int maxNodes = 0)
    {
        _localNodeId = localNodeId ?? throw new ArgumentNullException(nameof(localNodeId));
        _bucketSize = bucketSize;
        _idBits = idBits;
        _maxNodes = maxNodes;

        _buckets = new List<List<DhtNode>>();
        for (var i = 0; i < _idBits; i++)
        {
            _buckets.Add(new List<DhtNode>());
        }
    }

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
                return;
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
