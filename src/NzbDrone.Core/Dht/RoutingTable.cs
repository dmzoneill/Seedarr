using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace NzbDrone.Core.Dht;

/// <summary>
/// Kademlia DHT routing table with bucket-based node management, replacement cache, and Sybil/Eclipse protection.
/// </summary>
public class RoutingTable
{
    private readonly int _bucketSize;
    private readonly int _idBits;
    private readonly int _maxNodes;
    private readonly int _replacementBucketSize;
    private readonly byte[] _localNodeId;
    private readonly List<List<DhtNode>> _buckets;
    private readonly List<List<DhtNode>> _replacementBuckets;
    private readonly DateTime[] _bucketLastChanged;
    private readonly object _lock = new();

    /// <summary>
    /// Event triggered when a candidate node arrives for a full bucket, signaling that the least-recently-seen node should be probed before eviction.
    /// Parameters: (int bucketIndex, DhtNode nodeToProbe).
    /// </summary>
    public event Action<int, DhtNode> PingBeforeEvict;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutingTable"/> class.
    /// </summary>
    /// <param name="localNodeId">The 20-byte local node ID.</param>
    /// <param name="bucketSize">The maximum number of nodes per bucket (default 8).</param>
    /// <param name="idBits">The bit length of node IDs (default 160).</param>
    /// <param name="maxNodes">The maximum total nodes across all buckets (0 for unlimited).</param>
    /// <param name="allowLocal">Whether to allow loopback and link-local IP addresses (default false).</param>
    /// <param name="replacementBucketSize">The maximum number of candidates per replacement bucket (default 8).</param>
    public RoutingTable(byte[] localNodeId, int bucketSize = 8, int idBits = 160, int maxNodes = 0, bool allowLocal = false, int replacementBucketSize = 8)
    {
        _localNodeId = localNodeId ?? throw new ArgumentNullException(nameof(localNodeId));
        _bucketSize = bucketSize;
        _idBits = idBits;
        _maxNodes = maxNodes;
        AllowLocal = allowLocal;
        _replacementBucketSize = replacementBucketSize;

        _buckets = new List<List<DhtNode>>();
        _replacementBuckets = new List<List<DhtNode>>();
        _bucketLastChanged = new DateTime[_idBits];

        var now = DateTime.UtcNow;
        for (var i = 0; i < _idBits; i++)
        {
            _buckets.Add(new List<DhtNode>());
            _replacementBuckets.Add(new List<DhtNode>());
            _bucketLastChanged[i] = now;
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
    /// Gets the total number of candidate nodes stored across all replacement buckets.
    /// </summary>
    public int ReplacementNodeCount
    {
        get
        {
            lock (_lock)
            {
                return _replacementBuckets.Sum(b => b.Count);
            }
        }
    }

    /// <summary>
    /// Adds a node to the routing table, enforcing IP uniqueness, subnet diversity per bucket, and replacement cache.
    /// </summary>
    /// <param name="node">The DHT node to add.</param>
    public void AddNode(DhtNode node)
    {
        if (node == null || node.NodeId == null)
        {
            return;
        }

        DhtNode pingCandidate = null;
        var pingBucketIndex = -1;

        lock (_lock)
        {
            var bucketIndex = GetBucketIndex(node.NodeId);
            var bucket = _buckets[bucketIndex];
            var replacement = _replacementBuckets[bucketIndex];

            var existing = bucket.FirstOrDefault(n => n.NodeId.SequenceEqual(node.NodeId));
            if (existing != null)
            {
                existing.LastSeen = DateTime.UtcNow;
                existing.FailCount = 0;
                if (node.EndPoint != null)
                {
                    existing.EndPoint = node.EndPoint;
                }

                _bucketLastChanged[bucketIndex] = DateTime.UtcNow;
                return;
            }

            var existingReplacement = replacement.FirstOrDefault(n => n.NodeId.SequenceEqual(node.NodeId));
            if (existingReplacement != null)
            {
                existingReplacement.LastSeen = DateTime.UtcNow;
                existingReplacement.FailCount = 0;
                if (node.EndPoint != null)
                {
                    existingReplacement.EndPoint = node.EndPoint;
                }

                return;
            }

            if (node.EndPoint != null && !AllowLocal)
            {
                if (IsLocalOrLinkLocal(node.EndPoint.Address))
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
                _bucketLastChanged[bucketIndex] = DateTime.UtcNow;
                return;
            }

            // Evict bad nodes
            var bad = bucket.FirstOrDefault(n => n.IsBad);
            if (bad != null)
            {
                bucket.Remove(bad);
                if (replacement.Count > 0)
                {
                    var promoted = replacement[0];
                    replacement.RemoveAt(0);
                    bucket.Add(promoted);
                    if (replacement.Count < _replacementBucketSize)
                    {
                        replacement.Add(node);
                    }
                }
                else
                {
                    bucket.Add(node);
                }

                _bucketLastChanged[bucketIndex] = DateTime.UtcNow;
                return;
            }

            // Bucket is full of good/questionable nodes. Add node to replacement cache if room.
            if (replacement.Count < _replacementBucketSize)
            {
                replacement.Add(node);
            }
            else
            {
                var badReplacement = replacement.FirstOrDefault(n => n.IsBad);
                if (badReplacement != null)
                {
                    replacement.Remove(badReplacement);
                    replacement.Add(node);
                }
            }

            // Identify least recently seen node in bucket for ping-before-evict
            var leastRecentlySeen = bucket.OrderBy(n => n.LastSeen).FirstOrDefault();
            if (leastRecentlySeen != null)
            {
                pingCandidate = leastRecentlySeen;
                pingBucketIndex = bucketIndex;
            }
        }

        if (pingCandidate != null && pingBucketIndex >= 0)
        {
            PingBeforeEvict?.Invoke(pingBucketIndex, pingCandidate);
        }
    }

    /// <summary>
    /// Gets the closest nodes (both good and questionable, excluding bad) to a target ID, ordered by XOR distance.
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
            var maxCandidates = Math.Max(take * 2, _bucketSize);
            var candidates = new List<DhtNode>(maxCandidates);

            var targetBucketIndex = GetBucketIndex(targetId);

            foreach (var node in _buckets[targetBucketIndex])
            {
                if (!node.IsBad)
                {
                    candidates.Add(node);
                }
            }

            // If target bucket alone does not have enough nodes to reach 'take', expand outward to adjacent buckets
            if (candidates.Count < take)
            {
                var left = targetBucketIndex - 1;
                var right = targetBucketIndex + 1;

                while (candidates.Count < maxCandidates && (left >= 0 || right < _buckets.Count))
                {
                    if (left >= 0)
                    {
                        foreach (var node in _buckets[left])
                        {
                            if (!node.IsBad)
                            {
                                candidates.Add(node);
                            }
                        }

                        left--;
                    }

                    if (candidates.Count >= maxCandidates)
                    {
                        break;
                    }

                    if (right < _buckets.Count)
                    {
                        foreach (var node in _buckets[right])
                        {
                            if (!node.IsBad)
                            {
                                candidates.Add(node);
                            }
                        }

                        right++;
                    }
                }
            }

            if (candidates.Count == 0)
            {
                return candidates;
            }

            CollectionsMarshal.AsSpan(candidates).Sort(new NodeDistanceComparer(targetId));

            if (candidates.Count > take)
            {
                candidates.RemoveRange(take, candidates.Count - take);
            }

            return candidates;
        }
    }

    /// <summary>
    /// Evicts bad nodes (FailCount >= 3) from the bucket and promotes candidates from the replacement cache.
    /// </summary>
    public int EvictBadNodes(int bucketIndex)
    {
        if (bucketIndex < 0 || bucketIndex >= _idBits)
        {
            return 0;
        }

        lock (_lock)
        {
            return EvictBadNodesInternal(bucketIndex);
        }
    }

    /// <summary>
    /// Evicts bad nodes (FailCount >= 3) across all buckets and promotes replacement candidates.
    /// </summary>
    public int EvictBadNodes()
    {
        lock (_lock)
        {
            var count = 0;
            for (var i = 0; i < _idBits; i++)
            {
                count += EvictBadNodesInternal(i);
            }

            return count;
        }
    }

    private int EvictBadNodesInternal(int bucketIndex)
    {
        var bucket = _buckets[bucketIndex];
        var replacement = _replacementBuckets[bucketIndex];
        var evicted = 0;

        for (var i = bucket.Count - 1; i >= 0; i--)
        {
            if (bucket[i].IsBad)
            {
                bucket.RemoveAt(i);
                evicted++;

                while (replacement.Count > 0)
                {
                    var candidate = replacement[0];
                    replacement.RemoveAt(0);
                    if (!candidate.IsBad)
                    {
                        bucket.Add(candidate);
                        break;
                    }
                }

                _bucketLastChanged[bucketIndex] = DateTime.UtcNow;
            }
        }

        return evicted;
    }

    /// <summary>
    /// Records a failed query for a node by endpoint, incrementing FailCount.
    /// If FailCount reaches 3, the node is evicted and a replacement is promoted.
    /// </summary>
    public void RecordFailure(IPEndPoint endPoint)
    {
        if (endPoint == null)
        {
            return;
        }

        lock (_lock)
        {
            for (var i = 0; i < _idBits; i++)
            {
                var node = _buckets[i].FirstOrDefault(n => n.EndPoint != null && n.EndPoint.Equals(endPoint));
                if (node != null)
                {
                    node.FailCount++;
                    if (node.IsBad)
                    {
                        EvictBadNodesInternal(i);
                    }

                    return;
                }

                var replacementNode = _replacementBuckets[i].FirstOrDefault(n => n.EndPoint != null && n.EndPoint.Equals(endPoint));
                if (replacementNode != null)
                {
                    replacementNode.FailCount++;
                    if (replacementNode.IsBad)
                    {
                        _replacementBuckets[i].Remove(replacementNode);
                    }

                    return;
                }
            }
        }
    }

    /// <summary>
    /// Records a failed query for a node by node ID, incrementing FailCount.
    /// If FailCount reaches 3, the node is evicted and a replacement is promoted.
    /// </summary>
    public void RecordFailure(byte[] nodeId)
    {
        if (nodeId == null)
        {
            return;
        }

        lock (_lock)
        {
            var bucketIndex = GetBucketIndex(nodeId);
            var node = _buckets[bucketIndex].FirstOrDefault(n => n.NodeId.SequenceEqual(nodeId));
            if (node != null)
            {
                node.FailCount++;
                if (node.IsBad)
                {
                    EvictBadNodesInternal(bucketIndex);
                }

                return;
            }

            var replacementNode = _replacementBuckets[bucketIndex].FirstOrDefault(n => n.NodeId.SequenceEqual(nodeId));
            if (replacementNode != null)
            {
                replacementNode.FailCount++;
                if (replacementNode.IsBad)
                {
                    _replacementBuckets[bucketIndex].Remove(replacementNode);
                }
            }
        }
    }

    /// <summary>
    /// Gets the least recently seen node in the specified bucket, or null if the bucket is empty.
    /// </summary>
    public DhtNode GetLeastRecentlySeenNode(int bucketIndex)
    {
        if (bucketIndex < 0 || bucketIndex >= _idBits)
        {
            return null;
        }

        lock (_lock)
        {
            var bucket = _buckets[bucketIndex];
            if (bucket.Count == 0)
            {
                return null;
            }

            return bucket.OrderBy(n => n.LastSeen).FirstOrDefault();
        }
    }

    /// <summary>
    /// Gets replacement nodes for the specified bucket.
    /// </summary>
    public List<DhtNode> GetReplacementNodes(int bucketIndex)
    {
        if (bucketIndex < 0 || bucketIndex >= _idBits)
        {
            return new List<DhtNode>();
        }

        lock (_lock)
        {
            return _replacementBuckets[bucketIndex].ToList();
        }
    }

    /// <summary>
    /// Gets all candidate nodes stored across all replacement buckets.
    /// </summary>
    public List<DhtNode> GetAllReplacementNodes()
    {
        lock (_lock)
        {
            return _replacementBuckets.SelectMany(b => b).ToList();
        }
    }

    /// <summary>
    /// Gets the last changed timestamp for a bucket.
    /// </summary>
    public DateTime GetBucketLastChanged(int bucketIndex)
    {
        if (bucketIndex < 0 || bucketIndex >= _idBits)
        {
            return DateTime.MinValue;
        }

        lock (_lock)
        {
            return _bucketLastChanged[bucketIndex];
        }
    }

    /// <summary>
    /// Marks the specified bucket as changed now.
    /// </summary>
    public void TouchBucket(int bucketIndex)
    {
        if (bucketIndex >= 0 && bucketIndex < _idBits)
        {
            lock (_lock)
            {
                _bucketLastChanged[bucketIndex] = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Returns the indices of buckets that have not changed within the specified threshold (default 15 minutes).
    /// </summary>
    public List<int> GetStaleBucketIndices(TimeSpan? threshold = null)
    {
        var limit = threshold ?? TimeSpan.FromMinutes(15);
        var now = DateTime.UtcNow;
        var stale = new List<int>();

        lock (_lock)
        {
            for (var i = 0; i < _idBits; i++)
            {
                if (now - _bucketLastChanged[i] >= limit)
                {
                    stale.Add(i);
                }
            }
        }

        return stale;
    }

    public List<int> GetStaleBucketIndices(TimeSpan threshold) => GetStaleBucketIndices((TimeSpan?)threshold);

    /// <summary>
    /// Generates a random 20-byte node ID falling into the prefix range of the specified bucket index.
    /// </summary>
    public byte[] GenerateRandomIdForBucket(int bucketIndex)
    {
        if (bucketIndex < 0 || bucketIndex >= _idBits)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketIndex));
        }

        var result = new byte[_idBits / 8];
        RandomNumberGenerator.Fill(result);

        var diffBit = (_idBits - 1) - bucketIndex;
        var byteIndex = diffBit / 8;
        var bitOffset = 7 - (diffBit % 8);

        for (var i = 0; i < byteIndex; i++)
        {
            result[i] = _localNodeId[i];
        }

        var prefixMask = (byte)(0xFF << (bitOffset + 1));
        var diffMask = (byte)(1 << bitOffset);
        var suffixMask = (byte)((1 << bitOffset) - 1);

        result[byteIndex] = (byte)((_localNodeId[byteIndex] & prefixMask) |
                                  ((_localNodeId[byteIndex] ^ diffMask) & diffMask) |
                                  (result[byteIndex] & suffixMask));

        return result;
    }

    public byte[] GetRandomNodeIdForBucket(int bucketIndex) => GenerateRandomIdForBucket(bucketIndex);

    /// <summary>
    /// Compares the XOR distance of two node IDs relative to a target ID without allocating memory.
    /// </summary>
    /// <param name="a">The first node ID.</param>
    /// <param name="b">The second node ID.</param>
    /// <param name="target">The target ID.</param>
    /// <returns>Negative if a is closer to target than b; positive if b is closer; 0 if equal distance.</returns>
    public static int CompareDistance(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, ReadOnlySpan<byte> target)
    {
        var length = Math.Min(a.Length, Math.Min(b.Length, target.Length));
        for (var i = 0; i < length; i++)
        {
            var da = (byte)(a[i] ^ target[i]);
            var db = (byte)(b[i] ^ target[i]);
            if (da != db)
            {
                return da.CompareTo(db);
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    /// <summary>
    /// Calculates the XOR distance between two byte arrays.
    /// </summary>
    /// <param name="a">The first byte array.</param>
    /// <param name="b">The second byte array.</param>
    /// <returns>The XOR distance array.</returns>
    public static byte[] Distance(byte[] a, byte[] b)
    {
        var length = Math.Min(a.Length, b.Length);
        var result = new byte[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = (byte)(a[i] ^ b[i]);
        }

        return result;
    }

    /// <summary>
    /// Gets all nodes currently stored across all buckets.
    /// </summary>
    /// <returns>A list of all nodes.</returns>
    public List<DhtNode> GetAllNodes()
    {
        lock (_lock)
        {
            return _buckets.SelectMany(b => b).ToList();
        }
    }

    /// <summary>
    /// Gets all good nodes currently in the routing table.
    /// </summary>
    /// <returns>A list of good nodes.</returns>
    public List<DhtNode> GetGoodNodes()
    {
        lock (_lock)
        {
            return _buckets.SelectMany(b => b).Where(n => n.IsGood).ToList();
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

    public int GetBucketIndex(byte[] nodeId)
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

    private readonly struct NodeDistanceComparer : IComparer<DhtNode>
    {
        private readonly byte[] _target;

        public NodeDistanceComparer(byte[] target)
        {
            _target = target;
        }

        public int Compare(DhtNode x, DhtNode y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x == null)
            {
                return 1;
            }

            if (y == null)
            {
                return -1;
            }

            return CompareDistance(x.NodeId, y.NodeId, _target);
        }
    }
}
