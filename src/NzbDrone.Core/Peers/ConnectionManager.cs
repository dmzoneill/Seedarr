using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Peers;

public interface IConnectionManager
{
    void Add(PeerConnection connection);
    void Remove(PeerConnection connection);
    List<PeerConnection> GetConnections(string infoHash);
    int ActiveCount { get; }
    bool CanAddConnectionForTorrent(string infoHash);
    int GetUploadSlotCount();
    void ProcessDropouts();
    void RotateConnections();
}

public class ConnectionManager : IConnectionManager
{
    private readonly IConfigService _configService;
    private readonly List<PeerConnection> _connections = new();
    private readonly object _lock = new();
    private readonly Logger _logger;

    public int ActiveCount
    {
        get
        {
            lock (_lock)
            {
                return _connections.Count;
            }
        }
    }

    public ConnectionManager(IConfigService configService)
    {
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Add(PeerConnection connection)
    {
        lock (_lock)
        {
            var maxGlobal = _configService.MaxGlobalConnections;

            if (_connections.Count >= maxGlobal)
            {
                var oldest = _connections.OrderBy(c => c.LastActivity).First();
                _logger.Debug("Evicting peer {0} (LRU, global limit {1})", oldest.RemoteIp, maxGlobal);
                oldest.Dispose();
                _connections.Remove(oldest);
            }

            _connections.Add(connection);
        }
    }

    public void Remove(PeerConnection connection)
    {
        lock (_lock)
        {
            _connections.Remove(connection);
        }
    }

    public List<PeerConnection> GetConnections(string infoHash)
    {
        lock (_lock)
        {
            return _connections
                .Where(c => string.Equals(c.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public bool CanAddConnectionForTorrent(string infoHash)
    {
        lock (_lock)
        {
            var maxPerTorrent = _configService.MaxPerTorrentConnections;
            var torrentCount = _connections.Count(c =>
                string.Equals(c.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));

            return torrentCount < maxPerTorrent;
        }
    }

    public int GetUploadSlotCount()
    {
        return _configService.MaxUploadSlots;
    }

    public void ProcessDropouts()
    {
        lock (_lock)
        {
            var dropoutProbability = _configService.PeerDropoutProbability;

            if (dropoutProbability <= 0 || _connections.Count == 0)
            {
                return;
            }

            var toRemove = _connections
                .Where(_ => Random.Shared.NextDouble() < dropoutProbability)
                .ToList();

            foreach (var conn in toRemove)
            {
                _logger.Debug(
                    "Peer {0} dropped out (probability: {1:F2})",
                    conn.RemoteIp,
                    dropoutProbability);
                conn.Dispose();
                _connections.Remove(conn);
            }
        }
    }

    public void RotateConnections()
    {
        lock (_lock)
        {
            var rotationPct = _configService.ConnectionRotationPercentage;
            var rotateCount = (int)Math.Ceiling(_connections.Count * rotationPct);

            if (rotateCount <= 0 || _connections.Count == 0)
            {
                return;
            }

            rotateCount = Math.Min(rotateCount, _connections.Count);

            var oldest = _connections
                .OrderBy(c => c.ConnectedAt)
                .Take(rotateCount)
                .ToList();

            foreach (var conn in oldest)
            {
                _logger.Debug(
                    "Rotating out peer {0} (oldest, rotation: {1:P0})",
                    conn.RemoteIp,
                    rotationPct);
                conn.Dispose();
                _connections.Remove(conn);
            }
        }
    }
}
