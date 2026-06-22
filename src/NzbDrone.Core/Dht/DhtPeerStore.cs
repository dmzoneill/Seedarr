using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace NzbDrone.Core.Dht;

public class DhtPeerEntry
{
    public IPAddress Address { get; set; }
    public int Port { get; set; }
    public DateTime LastSeen { get; set; }
}

public class DhtPeerStore
{
    private readonly int _peerTtlMinutes;
    private readonly Dictionary<string, List<DhtPeerEntry>> _peers = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public DhtPeerStore(int peerTtlMinutes)
    {
        _peerTtlMinutes = peerTtlMinutes;
    }

    public void AddPeer(byte[] infoHash, IPAddress address, int port)
    {
        var key = Convert.ToHexString(infoHash);

        lock (_lock)
        {
            if (!_peers.TryGetValue(key, out var list))
            {
                list = new List<DhtPeerEntry>();
                _peers[key] = list;
            }

            var ipBytes = address.GetAddressBytes();
            var existing = list.FirstOrDefault(p =>
                p.Address.GetAddressBytes().SequenceEqual(ipBytes) && p.Port == port);

            if (existing != null)
            {
                existing.LastSeen = DateTime.UtcNow;
            }
            else
            {
                list.Add(new DhtPeerEntry
                {
                    Address = address,
                    Port = port,
                    LastSeen = DateTime.UtcNow
                });
            }

            list.RemoveAll(p => (DateTime.UtcNow - p.LastSeen).TotalMinutes > _peerTtlMinutes);
        }
    }

    public List<byte[]> GetPeers(byte[] infoHash)
    {
        var key = Convert.ToHexString(infoHash);

        lock (_lock)
        {
            if (!_peers.TryGetValue(key, out var list))
            {
                return new List<byte[]>();
            }

            var now = DateTime.UtcNow;
            return list
                .Where(p => (now - p.LastSeen).TotalMinutes <= _peerTtlMinutes)
                .Select(p => EncodeCompactPeer(p.Address, p.Port))
                .ToList();
        }
    }

    public bool HasPeers(byte[] infoHash)
    {
        var key = Convert.ToHexString(infoHash);

        lock (_lock)
        {
            if (!_peers.TryGetValue(key, out var list))
            {
                return false;
            }

            var now = DateTime.UtcNow;
            return list.Any(p => (now - p.LastSeen).TotalMinutes <= _peerTtlMinutes);
        }
    }

    private static byte[] EncodeCompactPeer(IPAddress address, int port)
    {
        // Compact peer info: 4 bytes IP + 2 bytes port (big-endian)
        var ipBytes = address.GetAddressBytes();
        var result = new byte[6];
        Array.Copy(ipBytes, 0, result, 0, 4);
        result[4] = (byte)(port >> 8);
        result[5] = (byte)port;
        return result;
    }
}
