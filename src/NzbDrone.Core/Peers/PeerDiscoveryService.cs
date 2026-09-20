using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Blocklist;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Torrents;
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
    public bool IsSeeder { get; set; }
}

public interface IPeerDiscoveryService
{
    void AddPeers(string infoHash, IEnumerable<TrackerPeer> peers, string source);
    void AddPeers(string infoHash, IEnumerable<PeerInfo> peers, string source);
    List<DiscoveredPeer> GetPeers(string infoHash, int maxCount = 10);
    void MarkAttempted(string infoHash, string ip, int port, bool success);
    int PeerCount(string infoHash);
    void RemoveTorrent(string infoHash);
    void PruneStalePeers();
}

public class PeerDiscoveryService : IPeerDiscoveryService, IHandle<TorrentDeletedEvent>
{
    private const int MaxPeersPerTorrent = 200;
    private const int MaxFailCount = 3;
    private const int RetryDelayMinutes = 10;
    private const int LocalRetryDelayMinutes = 1;

    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StaleFailedCandidateAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan StaleCandidateAge = TimeSpan.FromHours(48);

    private readonly IConfigService _configService;
    private readonly IPeerBlocklistSyncService _blocklistService;
    private readonly ConcurrentDictionary<string, List<DiscoveredPeer>> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pruneLock = new();
    private DateTime _lastPruneTime = DateTime.UtcNow;

    public PeerDiscoveryService(
        IConfigService configService = null,
        IPeerBlocklistSyncService blocklistService = null)
    {
        _configService = configService;
        _blocklistService = blocklistService;
    }

    public void AddPeers(string infoHash, IEnumerable<PeerInfo> peers, string source)
    {
        if (peers == null)
        {
            return;
        }

        AddPeers(
            infoHash,
            peers.Select(p => new TrackerPeer
            {
                Ip = p.Ip,
                Port = p.Port,
                IsSeeder = p.IsSeeder
            }),
            source);
    }

    public void AddPeers(string infoHash, IEnumerable<TrackerPeer> peers, string source)
    {
        if (peers == null)
        {
            return;
        }

        PruneIfDue();

        var list = _peers.GetOrAdd(infoHash, _ => new List<DiscoveredPeer>());

        lock (list)
        {
            foreach (var peer in peers)
            {
                if (string.IsNullOrEmpty(peer.Ip) || peer.Port <= 0)
                {
                    continue;
                }

                if (IsBlocked(peer.Ip))
                {
                    continue;
                }

                var existing = list.FirstOrDefault(p => p.Ip == peer.Ip && p.Port == peer.Port);
                if (existing != null)
                {
                    existing.DiscoveredAt = DateTime.UtcNow;
                    if (peer.IsSeeder)
                    {
                        existing.IsSeeder = true;
                    }

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
                    DiscoveredAt = DateTime.UtcNow,
                    IsSeeder = peer.IsSeeder
                });
            }

            if (list.Count > MaxPeersPerTorrent)
            {
                var toRemoveCount = list.Count - MaxPeersPerTorrent;
                var peersToRemove = list
                    .OrderByDescending(p => p.FailCount >= MaxFailCount ? 1 : 0)
                    .ThenBy(p => GetSourcePriority(p.Source))
                    .ThenBy(p => p.IsSeeder ? 1 : 0)
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
                .Where(p => !IsBlocked(p.Ip))
                .Where(p => p.FailCount < MaxFailCount)
                .Where(p => !p.LastAttempt.HasValue || (now - p.LastAttempt.Value).TotalMinutes >= GetRetryDelayMinutes(p.Source))
                .OrderBy(p => p.FailCount)
                .ThenByDescending(p => GetSourcePriority(p.Source))
                .ThenByDescending(p => p.IsSeeder ? 1 : 0)
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
            return list.Count(p => p.FailCount < MaxFailCount && !IsBlocked(p.Ip));
        }
    }

    private bool IsBlocked(string ip)
    {
        if (_blocklistService == null)
        {
            return false;
        }

        if (_configService != null && !_configService.BlocklistEnabled)
        {
            return false;
        }

        return _blocklistService.IsBlocked(ip);
    }

    public void RemoveTorrent(string infoHash)
    {
        if (!string.IsNullOrWhiteSpace(infoHash))
        {
            _peers.TryRemove(infoHash, out _);
        }
    }

    public void PruneStalePeers()
    {
        var now = DateTime.UtcNow;
        var cutoff24H = now - StaleFailedCandidateAge;
        var cutoff48H = now - StaleCandidateAge;

        foreach (var (infoHash, list) in _peers)
        {
            lock (list)
            {
                list.RemoveAll(p =>
                    (p.FailCount >= MaxFailCount && (p.LastAttempt ?? p.DiscoveredAt) < cutoff24H) ||
                    (p.DiscoveredAt < cutoff48H && (p.FailCount > 0 || !p.LastAttempt.HasValue)));

                if (list.Count == 0)
                {
                    _peers.TryRemove(infoHash, out _);
                }
            }
        }
    }

    public void Handle(TorrentDeletedEvent message)
    {
        if (message?.Torrent?.InfoHash != null)
        {
            RemoveTorrent(message.Torrent.InfoHash);
        }
    }

    private void PruneIfDue()
    {
        if (DateTime.UtcNow - _lastPruneTime < PruneInterval)
        {
            return;
        }

        lock (_pruneLock)
        {
            if (DateTime.UtcNow - _lastPruneTime < PruneInterval)
            {
                return;
            }

            _lastPruneTime = DateTime.UtcNow;
            PruneStalePeers();
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
