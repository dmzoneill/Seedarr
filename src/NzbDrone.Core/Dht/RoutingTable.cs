using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Dht;

public class RoutingTable
{
    private const int K = 8;
    private const int IdBits = 160;
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
    private readonly List<List<DhtNode>> _buckets;

    public RoutingTable()
    {
        _buckets = new List<List<DhtNode>>();
        for (var i = 0; i < IdBits; i++)
        {
            _buckets.Add(new List<DhtNode>());
        }
    }

    public void AddNode(DhtNode node)
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

        if (bucket.Count < K)
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

    public List<DhtNode> GetClosestNodes(byte[] targetId, int count = K)
    {
        return _buckets.SelectMany(b => b)
            .Where(n => n.IsGood)
            .OrderBy(n => Distance(n.NodeId, targetId), _byteComparer)
            .Take(count)
            .ToList();
    }

    public int NodeCount => _buckets.Sum(b => b.Count);

    private static int GetBucketIndex(byte[] nodeId)
    {
        // Find highest differing bit
        for (var i = 0; i < nodeId.Length; i++)
        {
            if (nodeId[i] != 0)
            {
                for (var bit = 7; bit >= 0; bit--)
                {
                    if ((nodeId[i] & (1 << bit)) != 0)
                    {
                        return (i * 8) + (7 - bit);
                    }
                }
            }
        }

        return IdBits - 1;
    }

    private static byte[] Distance(byte[] a, byte[] b)
    {
        var result = new byte[20];
        for (var i = 0; i < 20; i++)
        {
            result[i] = (byte)(a[i] ^ b[i]);
        }

        return result;
    }
}
