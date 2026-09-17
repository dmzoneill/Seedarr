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
    private const int LocalRetryDelayMinutes = 1;

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
                    if (GetSourcePriority(source) >= GetSourcePriority(existing.Source))
                    {
                        existing.Source = source;
                    }

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
                var toRemoveCount = list.Count - MaxPeersPerTorrent;
                var peersToRemove = list
                    .OrderByDescending(p => p.FailCount >= MaxFailCount ? 1 : 0)
                    .ThenBy(p => GetSourcePriority(p.Source))
                    .ThenByDescending(p => p.FailCount)
                    .ThenBy(p => p.DiscoveredAt)
                    .Take(toRemoveCount)
                    .ToHashSet();

                list.RemoveAll(peersToRemove.Contains);
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
            var selected = list
                .Where(p => p.FailCount < MaxFailCount)
                .Where(p => !p.LastAttempt.HasValue || (now - p.LastAttempt.Value).TotalMinutes >= GetRetryDelayMinutes(p.Source))
                .OrderBy(p => p.FailCount)
                .ThenByDescending(p => GetSourcePriority(p.Source))
                .ThenByDescending(p => p.DiscoveredAt)
                .Take(maxCount)
                .ToList();

            foreach (var peer in selected)
            {
                peer.LastAttempt = now;
            }

            return selected;
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

    private static int GetSourcePriority(string source) => source?.ToLowerInvariant() switch
    {
        "lpd" => 4,     // Local Peer Discovery: 1st priority
        "pex" => 3,     // Peer Exchange: 2nd priority
        "tracker" => 2, // Trackers: 3rd priority
        "dht" => 1,     // DHT: 4th priority
        _ => 0
    };

    private static int GetRetryDelayMinutes(string source) =>
        string.Equals(source, "lpd", StringComparison.OrdinalIgnoreCase)
            ? LocalRetryDelayMinutes
            : RetryDelayMinutes;
}
