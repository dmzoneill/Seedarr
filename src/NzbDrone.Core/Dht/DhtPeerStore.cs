using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NzbDrone.Core.Dht;

public class DhtPeerEntry
{
    public IPAddress Address { get; set; }
    public int Port { get; set; }
    public DateTime LastSeen { get; set; }
}

public class DhtPeerStore : IDisposable
{
    public const int MaxPeersPerInfoHash = 100;
    public const int MaxInfoHashes = 5000;

    private readonly int _peerTtlMinutes;
    private readonly int _maxPeersPerInfoHash;
    private readonly int _maxInfoHashes;
    private readonly Func<DateTime> _timeProvider;
    private readonly Dictionary<string, SwarmEntry> _peers = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lruList = new();
    private readonly object _lock = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public DhtPeerStore(
        int peerTtlMinutes,
        int maxPeersPerInfoHash = MaxPeersPerInfoHash,
        int maxInfoHashes = MaxInfoHashes,
        TimeSpan? cleanupInterval = null,
        Func<DateTime> timeProvider = null)
    {
        _peerTtlMinutes = peerTtlMinutes;
        _maxPeersPerInfoHash = maxPeersPerInfoHash;
        _maxInfoHashes = maxInfoHashes;
        _timeProvider = timeProvider ?? (() => DateTime.UtcNow);

        if (_peerTtlMinutes > 0)
        {
            var interval = cleanupInterval ?? TimeSpan.FromMinutes(Math.Min(Math.Max(1, _peerTtlMinutes), 5));
            _cleanupTimer = new Timer(_ => CleanupExpiredPeers(), null, interval, interval);
        }
    }

    public int MaxPeersPerInfoHashLimit => _maxPeersPerInfoHash;

    public int MaxInfoHashesLimit => _maxInfoHashes;

    public int InfoHashCount
    {
        get
        {
            lock (_lock)
            {
                return _peers.Count;
            }
        }
    }

    public int SwarmCount => InfoHashCount;

    public void AddPeer(byte[] infoHash, IPAddress address, int port)
    {
        if (infoHash == null || infoHash.Length == 0 || address == null)
        {
            return;
        }

        var key = Convert.ToHexString(infoHash);
        var now = _timeProvider();

        lock (_lock)
        {
            if (_peers.TryGetValue(key, out var swarm))
            {
                if (swarm.LruNode != null && swarm.LruNode.List != null)
                {
                    _lruList.Remove(swarm.LruNode);
                    _lruList.AddLast(swarm.LruNode);
                }

                swarm.LastAccessed = now;
            }
            else
            {
                while (_peers.Count >= _maxInfoHashes && _lruList.First != null)
                {
                    var oldestKey = _lruList.First.Value;
                    _lruList.RemoveFirst();
                    _peers.Remove(oldestKey);
                }

                var node = new LinkedListNode<string>(key);
                _lruList.AddLast(node);
                swarm = new SwarmEntry
                {
                    LruNode = node,
                    LastAccessed = now
                };
                _peers[key] = swarm;
            }

            var existing = swarm.Peers.FirstOrDefault(p =>
                p.Address.Equals(address) && p.Port == port);

            if (existing != null)
            {
                existing.LastSeen = now;
            }
            else
            {
                if (swarm.Peers.Count >= _maxPeersPerInfoHash)
                {
                    var oldestIndex = 0;
                    var oldestSeen = swarm.Peers[0].LastSeen;
                    for (var i = 1; i < swarm.Peers.Count; i++)
                    {
                        if (swarm.Peers[i].LastSeen < oldestSeen)
                        {
                            oldestSeen = swarm.Peers[i].LastSeen;
                            oldestIndex = i;
                        }
                    }

                    swarm.Peers.RemoveAt(oldestIndex);
                }

                swarm.Peers.Add(new DhtPeerEntry
                {
                    Address = address,
                    Port = port,
                    LastSeen = now
                });
            }

            if (_peerTtlMinutes <= 0)
            {
                swarm.Peers.Clear();
                _peers.Remove(key);
                if (swarm.LruNode != null && swarm.LruNode.List != null)
                {
                    _lruList.Remove(swarm.LruNode);
                }
            }
        }
    }

    public List<byte[]> GetPeers(byte[] infoHash)
    {
        return GetPeers4(infoHash);
    }

    public List<byte[]> GetPeers4(byte[] infoHash)
    {
        if (infoHash == null || infoHash.Length == 0)
        {
            return new List<byte[]>();
        }

        var key = Convert.ToHexString(infoHash);
        var now = _timeProvider();

        lock (_lock)
        {
            if (!_peers.TryGetValue(key, out var swarm))
            {
                return new List<byte[]>();
            }

            if (swarm.LruNode != null && swarm.LruNode.List != null)
            {
                _lruList.Remove(swarm.LruNode);
                _lruList.AddLast(swarm.LruNode);
            }

            swarm.LastAccessed = now;

            return swarm.Peers
                .Where(p => _peerTtlMinutes > 0 && (now - p.LastSeen).TotalMinutes <= _peerTtlMinutes && p.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(p => EncodeCompactPeer(p.Address, p.Port))
                .Where(b => b != null)
                .ToList();
        }
    }

    public List<byte[]> GetPeers6(byte[] infoHash)
    {
        if (infoHash == null || infoHash.Length == 0)
        {
            return new List<byte[]>();
        }

        var key = Convert.ToHexString(infoHash);
        var now = _timeProvider();

        lock (_lock)
        {
            if (!_peers.TryGetValue(key, out var swarm))
            {
                return new List<byte[]>();
            }

            if (swarm.LruNode != null && swarm.LruNode.List != null)
            {
                _lruList.Remove(swarm.LruNode);
                _lruList.AddLast(swarm.LruNode);
            }

            swarm.LastAccessed = now;

            return swarm.Peers
                .Where(p => _peerTtlMinutes > 0 && (now - p.LastSeen).TotalMinutes <= _peerTtlMinutes && p.Address.AddressFamily == AddressFamily.InterNetworkV6)
                .Select(p => EncodeCompactPeer(p.Address, p.Port))
                .Where(b => b != null)
                .ToList();
        }
    }

    public List<byte[]> GetPeersV4(byte[] infoHash) => GetPeers4(infoHash);

    public List<byte[]> GetPeersValues(byte[] infoHash) => GetPeers4(infoHash);

    public List<byte[]> GetPeersV6(byte[] infoHash) => GetPeers6(infoHash);

    public List<byte[]> GetPeersIPv6(byte[] infoHash) => GetPeers6(infoHash);

    public List<byte[]> GetPeersValues6(byte[] infoHash) => GetPeers6(infoHash);

    public (List<byte[]> Values, List<byte[]> Values6) GetDualStackPeers(byte[] infoHash)
    {
        return (GetPeers4(infoHash), GetPeers6(infoHash));
    }

    public (List<byte[]> Values, List<byte[]> Values6) GetPeersDualStack(byte[] infoHash)
    {
        return (GetPeers4(infoHash), GetPeers6(infoHash));
    }

    public List<byte[]> GetAllPeers(byte[] infoHash)
    {
        if (infoHash == null || infoHash.Length == 0)
        {
            return new List<byte[]>();
        }

        var key = Convert.ToHexString(infoHash);
        var now = _timeProvider();

        lock (_lock)
        {
            if (!_peers.TryGetValue(key, out var swarm))
            {
                return new List<byte[]>();
            }

            if (swarm.LruNode != null && swarm.LruNode.List != null)
            {
                _lruList.Remove(swarm.LruNode);
                _lruList.AddLast(swarm.LruNode);
            }

            swarm.LastAccessed = now;

            return swarm.Peers
                .Where(p => _peerTtlMinutes > 0 && (now - p.LastSeen).TotalMinutes <= _peerTtlMinutes)
                .Select(p => EncodeCompactPeer(p.Address, p.Port))
                .Where(b => b != null)
                .ToList();
        }
    }

    public bool HasPeers(byte[] infoHash)
    {
        if (infoHash == null || infoHash.Length == 0)
        {
            return false;
        }

        var key = Convert.ToHexString(infoHash);
        var now = _timeProvider();

        lock (_lock)
        {
            if (!_peers.TryGetValue(key, out var swarm))
            {
                return false;
            }

            return _peerTtlMinutes > 0 && swarm.Peers.Any(p => (now - p.LastSeen).TotalMinutes <= _peerTtlMinutes);
        }
    }

    public int GetPeerCount(byte[] infoHash)
    {
        if (infoHash == null || infoHash.Length == 0)
        {
            return 0;
        }

        var key = Convert.ToHexString(infoHash);
        lock (_lock)
        {
            return _peers.TryGetValue(key, out var swarm) ? swarm.Peers.Count : 0;
        }
    }

    public void CleanupExpiredPeers()
    {
        if (_disposed)
        {
            return;
        }

        List<string> keys;
        lock (_lock)
        {
            keys = _peers.Keys.ToList();
        }

        var now = _timeProvider();
        var ttl = TimeSpan.FromMinutes(_peerTtlMinutes);

        foreach (var key in keys)
        {
            lock (_lock)
            {
                if (!_peers.TryGetValue(key, out var swarm))
                {
                    continue;
                }

                swarm.Peers.RemoveAll(p => _peerTtlMinutes <= 0 || (now - p.LastSeen) > ttl);

                if (swarm.Peers.Count == 0)
                {
                    _peers.Remove(key);
                    if (swarm.LruNode != null && swarm.LruNode.List != null)
                    {
                        _lruList.Remove(swarm.LruNode);
                    }
                }
            }
        }
    }

    public static byte[] EncodeCompactPeer(IPAddress address, int port)
    {
        if (address == null || port < 0 || port > 65535)
        {
            return null;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var ipBytes = address.GetAddressBytes();
            var result = new byte[6];
            Array.Copy(ipBytes, 0, result, 0, 4);
            result[4] = (byte)(port >> 8);
            result[5] = (byte)port;
            return result;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var ipBytes = address.GetAddressBytes();
            var result = new byte[18];
            Array.Copy(ipBytes, 0, result, 0, 16);
            result[16] = (byte)(port >> 8);
            result[17] = (byte)port;
            return result;
        }

        return null;
    }

    public static byte[] EncodeCompactPeerIPv4(IPAddress address, int port)
    {
        if (address?.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        return EncodeCompactPeer(address, port);
    }

    public static byte[] EncodeCompactPeerIPv6(IPAddress address, int port)
    {
        if (address?.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return null;
        }

        return EncodeCompactPeer(address, port);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _cleanupTimer?.Dispose();
        }

        _disposed = true;
    }

    private sealed class SwarmEntry
    {
        public LinkedListNode<string> LruNode { get; set; }
        public List<DhtPeerEntry> Peers { get; } = new();
        public DateTime LastAccessed { get; set; }
    }
}
