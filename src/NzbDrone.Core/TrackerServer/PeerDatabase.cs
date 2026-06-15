using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.TrackerServer;

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

public class PeerDatabase
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
        }
    }

    public void RemovePeer(string infoHash, string ip, int port)
    {
        lock (_lock)
        {
            if (_peers.TryGetValue(infoHash, out var list))
            {
                list.RemoveAll(p => p.Ip == ip && p.Port == port);
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
}
