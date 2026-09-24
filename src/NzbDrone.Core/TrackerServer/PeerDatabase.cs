using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;

namespace NzbDrone.Core.TrackerServer;

public interface IPeerDatabase : IDisposable
{
    void AddPeer(string infoHash, string ip, int port, string peerId, bool isSeeder = true);
    void AddPeer(string infoHash, string ip, int port, string peerId, long left, string announceEvent = null);
    void RecordCompleted(string infoHash);
    void RemovePeer(string infoHash, string ip, int port);
    List<TrackerPeerEntry> GetPeers(string infoHash);
    ScrapeStats GetStats(string infoHash);
    Dictionary<string, ScrapeStats> GetAllStats();
    List<string> GetAllInfoHashes();
    int GetTotalPeerCount();
    int GetTotalTorrentCount();
    long TotalAnnounces { get; }
    long TotalScrapes { get; }
    void IncrementAnnounces();
    void IncrementScrapes();
    int PruneStalePeers();
    bool ContainsSwarm(string infoHash);
}

public class TrackerPeerEntry
{
    public string Ip { get; set; }
    public int Port { get; set; }
    public string PeerId { get; set; }
    public DateTime LastAnnounce { get; set; }
    public bool IsSeeder { get; set; }
    public long Left { get; set; }
}

public class ScrapeStats
{
    public int Complete { get; set; }
    public int Incomplete { get; set; }
    public int Downloaded { get; set; }
}

public class PeerDatabase : IPeerDatabase, IDisposable
{
    private const int PeerTtlMinutes = 45;
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<string, List<TrackerPeerEntry>> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _completed = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly Timer _evictionTimer;
    private bool _disposed;
    private long _totalAnnounces;
    private long _totalScrapes;

    public PeerDatabase()
        : this(TimeSpan.FromMinutes(5))
    {
    }

    public PeerDatabase(TimeSpan evictionInterval)
    {
        if (evictionInterval > TimeSpan.Zero)
        {
            _evictionTimer = new Timer(OnEvictionTimer, null, evictionInterval, evictionInterval);
        }
    }

    private void OnEvictionTimer(object state)
    {
        try
        {
            PruneStalePeers();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Unexpected error occurred during periodic peer eviction");
        }
    }

    public long TotalAnnounces => Interlocked.Read(ref _totalAnnounces);
    public long TotalScrapes => Interlocked.Read(ref _totalScrapes);

    public void IncrementAnnounces()
    {
        Interlocked.Increment(ref _totalAnnounces);
    }

    public void IncrementScrapes()
    {
        Interlocked.Increment(ref _totalScrapes);
    }

    public int PruneStalePeers()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return 0;
            }

            var now = DateTime.UtcNow;
            var totalEvicted = 0;
            var emptyHashes = new List<string>();

            foreach (var kvp in _peers)
            {
                var list = kvp.Value;
                var initialCount = list.Count;
                list.RemoveAll(p => (now - p.LastAnnounce).TotalMinutes > PeerTtlMinutes);
                totalEvicted += initialCount - list.Count;

                if (list.Count == 0)
                {
                    emptyHashes.Add(kvp.Key);
                }
            }

            foreach (var emptyHash in emptyHashes)
            {
                _peers.Remove(emptyHash);
            }

            return totalEvicted;
        }
    }

    public bool ContainsSwarm(string infoHash)
    {
        lock (_lock)
        {
            return _peers.ContainsKey(infoHash);
        }
    }

    public void RecordCompleted(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return;
        }

        lock (_lock)
        {
            _completed[infoHash] = _completed.GetValueOrDefault(infoHash, 0) + 1;
        }
    }

    public void AddPeer(string infoHash, string ip, int port, string peerId, bool isSeeder = true)
    {
        AddPeer(infoHash, ip, port, peerId, isSeeder ? 0L : 1L, null);
    }

    public void AddPeer(string infoHash, string ip, int port, string peerId, long left, string announceEvent = null)
    {
        lock (_lock)
        {
            if (string.Equals(announceEvent, "completed", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(infoHash))
                {
                    _completed[infoHash] = _completed.GetValueOrDefault(infoHash, 0) + 1;
                }
            }

            if (!_peers.TryGetValue(infoHash, out var list))
            {
                list = new List<TrackerPeerEntry>();
                _peers[infoHash] = list;
            }

            var isSeeder = left == 0;
            var existing = list.FirstOrDefault(p => p.Ip == ip && p.Port == port);
            if (existing != null)
            {
                existing.LastAnnounce = DateTime.UtcNow;
                existing.PeerId = peerId;
                existing.IsSeeder = isSeeder;
                existing.Left = left;
            }
            else
            {
                list.Add(new TrackerPeerEntry
                {
                    Ip = ip,
                    Port = port,
                    PeerId = peerId,
                    LastAnnounce = DateTime.UtcNow,
                    IsSeeder = isSeeder,
                    Left = left
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
            var downloaded = _completed.GetValueOrDefault(infoHash, 0);

            if (!_peers.TryGetValue(infoHash, out var list))
            {
                return new ScrapeStats
                {
                    Complete = 0,
                    Incomplete = 0,
                    Downloaded = downloaded
                };
            }

            var active = list.Where(p => (DateTime.UtcNow - p.LastAnnounce).TotalMinutes <= PeerTtlMinutes).ToList();
            var complete = active.Count(p => p.IsSeeder);
            var incomplete = active.Count - complete;

            return new ScrapeStats
            {
                Complete = complete,
                Incomplete = incomplete,
                Downloaded = downloaded
            };
        }
    }

    public Dictionary<string, ScrapeStats> GetAllStats()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var result = new Dictionary<string, ScrapeStats>(_peers.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in _peers)
            {
                var active = kvp.Value.Where(p => (now - p.LastAnnounce).TotalMinutes <= PeerTtlMinutes).ToList();
                if (active.Count > 0)
                {
                    var complete = active.Count(p => p.IsSeeder);
                    var incomplete = active.Count - complete;
                    result[kvp.Key] = new ScrapeStats
                    {
                        Complete = complete,
                        Incomplete = incomplete,
                        Downloaded = _completed.GetValueOrDefault(kvp.Key, 0)
                    };
                }
            }

            return result;
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
            _evictionTimer?.Dispose();
        }

        _disposed = true;
    }
}
