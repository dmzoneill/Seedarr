using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Peers;

public class DiscoveredPeer
{
    public string Ip { get; set; }
    public int Port { get; set; }
    public string Source { get; set; }
    public DateTime DiscoveredAt { get; set; }
    public DateTime? LastAttempt { get; set; }
    public int FailCount { get; set; }
}

public interface IPeerDiscoveryService
{
    void AddPeers(string infoHash, IEnumerable<TrackerPeer> peers, string source);
    List<DiscoveredPeer> GetPeers(string infoHash, int maxCount = 10);
    void MarkAttempted(string infoHash, string ip, int port, bool success);
    int PeerCount(string infoHash);
}

public class PeerDiscoveryService : IPeerDiscoveryService
{
    private const int MaxPeersPerTorrent = 200;
    private const int MaxFailCount = 3;
    private const int RetryDelayMinutes = 10;

    private readonly ConcurrentDictionary<string, List<DiscoveredPeer>> _peers = new(StringComparer.OrdinalIgnoreCase);

    public void AddPeers(string infoHash, IEnumerable<TrackerPeer> peers, string source)
    {
        var list = _peers.GetOrAdd(infoHash, _ => new List<DiscoveredPeer>());

        lock (list)
        {
            foreach (var peer in peers)
            {
                if (string.IsNullOrEmpty(peer.Ip) || peer.Port <= 0)
                {
                    continue;
                }

                var existing = list.FirstOrDefault(p => p.Ip == peer.Ip && p.Port == peer.Port);
                if (existing != null)
                {
                    existing.DiscoveredAt = DateTime.UtcNow;
                    existing.Source = source;
                    continue;
                }

                list.Add(new DiscoveredPeer
                {
                    Ip = peer.Ip,
                    Port = peer.Port,
                    Source = source,
                    DiscoveredAt = DateTime.UtcNow
                });
            }

            if (list.Count > MaxPeersPerTorrent)
            {
                list.RemoveRange(0, list.Count - MaxPeersPerTorrent);
            }
        }
    }

    public List<DiscoveredPeer> GetPeers(string infoHash, int maxCount = 10)
    {
        if (!_peers.TryGetValue(infoHash, out var list))
        {
            return new List<DiscoveredPeer>();
        }

        var now = DateTime.UtcNow;

        lock (list)
        {
            return list
                .Where(p => p.FailCount < MaxFailCount)
                .Where(p => !p.LastAttempt.HasValue || (now - p.LastAttempt.Value).TotalMinutes >= RetryDelayMinutes)
                .OrderBy(p => p.FailCount)
                .ThenByDescending(p => p.DiscoveredAt)
                .Take(maxCount)
                .ToList();
        }
    }

    public void MarkAttempted(string infoHash, string ip, int port, bool success)
    {
        if (!_peers.TryGetValue(infoHash, out var list))
        {
            return;
        }

        lock (list)
        {
            var peer = list.FirstOrDefault(p => p.Ip == ip && p.Port == port);
            if (peer == null)
            {
                return;
            }

            peer.LastAttempt = DateTime.UtcNow;

            if (success)
            {
                peer.FailCount = 0;
            }
            else
            {
                peer.FailCount++;
            }
        }
    }

    public int PeerCount(string infoHash)
    {
        if (!_peers.TryGetValue(infoHash, out var list))
        {
            return 0;
        }

        lock (list)
        {
            return list.Count(p => p.FailCount < MaxFailCount);
        }
    }
}
