using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.TrackerServer;

public interface IPeerDatabase
{
    void AddPeer(string infoHash, string ip, int port, string peerId);
    void RemovePeer(string infoHash, string ip, int port);
    List<TrackerPeerEntry> GetPeers(string infoHash);
    ScrapeStats GetStats(string infoHash);
    List<string> GetAllInfoHashes();
    int GetTotalPeerCount();
    int GetTotalTorrentCount();
}

public class TrackerPeerEntry
{
    public string Ip { get; set; }
    public int Port { get; set; }
    public string PeerId { get; set; }
    public DateTime LastAnnounce { get; set; }
}

public class ScrapeStats
{
    public int Complete { get; set; }
    public int Incomplete { get; set; }
    public int Downloaded { get; set; }
}

public class PeerDatabase : IPeerDatabase
{
    private const int PeerTtlMinutes = 45;

    private readonly Dictionary<string, List<TrackerPeerEntry>> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public void AddPeer(string infoHash, string ip, int port, string peerId)
    {
        lock (_lock)
        {
            if (!_peers.TryGetValue(infoHash, out var list))
            {
                list = new List<TrackerPeerEntry>();
                _peers[infoHash] = list;
            }

            var existing = list.FirstOrDefault(p => p.Ip == ip && p.Port == port);
            if (existing != null)
            {
                existing.LastAnnounce = DateTime.UtcNow;
                existing.PeerId = peerId;
            }
            else
            {
                list.Add(new TrackerPeerEntry
                {
                    Ip = ip,
                    Port = port,
                    PeerId = peerId,
                    LastAnnounce = DateTime.UtcNow
                });
            }

            list.RemoveAll(p => (DateTime.UtcNow - p.LastAnnounce).TotalMinutes > PeerTtlMinutes);

            if (list.Count == 0)
            {
                _peers.Remove(infoHash);
            }
        }
    }

    public void RemovePeer(string infoHash, string ip, int port)
    {
        lock (_lock)
        {
            if (_peers.TryGetValue(infoHash, out var list))
            {
                list.RemoveAll(p => p.Ip == ip && p.Port == port);

                if (list.Count == 0)
                {
                    _peers.Remove(infoHash);
                }
            }
        }
    }

    public List<TrackerPeerEntry> GetPeers(string infoHash)
    {
        lock (_lock)
        {
            if (_peers.TryGetValue(infoHash, out var list))
            {
                return list.Where(p => (DateTime.UtcNow - p.LastAnnounce).TotalMinutes <= PeerTtlMinutes).ToList();
            }

            return new List<TrackerPeerEntry>();
        }
    }

    public ScrapeStats GetStats(string infoHash)
    {
        lock (_lock)
        {
            if (!_peers.TryGetValue(infoHash, out var list))
            {
                return new ScrapeStats();
            }

            var active = list.Where(p => (DateTime.UtcNow - p.LastAnnounce).TotalMinutes <= PeerTtlMinutes).ToList();
            return new ScrapeStats
            {
                Complete = active.Count,
                Incomplete = 0,
                Downloaded = active.Count
            };
        }
    }

    public List<string> GetAllInfoHashes()
    {
        lock (_lock)
        {
            return _peers
                .Where(kvp => kvp.Value.Any(p => (DateTime.UtcNow - p.LastAnnounce).TotalMinutes <= PeerTtlMinutes))
                .Select(kvp => kvp.Key)
                .ToList();
        }
    }

    public int GetTotalPeerCount()
    {
        lock (_lock)
        {
            return _peers.Values
                .SelectMany(list => list)
                .Count(p => (DateTime.UtcNow - p.LastAnnounce).TotalMinutes <= PeerTtlMinutes);
        }
    }

    public int GetTotalTorrentCount()
    {
        lock (_lock)
        {
            return _peers.Count(kvp => kvp.Value.Any(p => (DateTime.UtcNow - p.LastAnnounce).TotalMinutes <= PeerTtlMinutes));
        }
    }
}
