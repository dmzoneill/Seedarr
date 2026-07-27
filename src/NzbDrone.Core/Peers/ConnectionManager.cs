using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Torrents;

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
    private readonly IPeerConnectionLogService _connectionLogService;
    private readonly ITorrentService _torrentService;
    private readonly IFastExtensionHandler _fastExtensionHandler;
    private readonly ITorrentEventLogService _eventLogService;
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

    public ConnectionManager(IConfigService configService, IPeerConnectionLogService connectionLogService, ITorrentService torrentService, IFastExtensionHandler fastExtensionHandler, ITorrentEventLogService eventLogService)
    {
        _configService = configService;
        _connectionLogService = connectionLogService;
        _torrentService = torrentService;
        _fastExtensionHandler = fastExtensionHandler;
        _eventLogService = eventLogService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Add(PeerConnection connection)
    {
        PeerConnection evicted = null;
        lock (_lock)
        {
            var maxGlobal = _configService.MaxGlobalConnections;

            if (_connections.Count >= maxGlobal)
            {
                evicted = _connections.OrderBy(c => c.LastActivity).First();
                _logger.Debug("Evicting peer {0} (LRU, global limit {1})", evicted.RemoteIp, maxGlobal);
                _connections.Remove(evicted);
            }

            _connections.Add(connection);
        }

        if (evicted != null)
        {
            LogDisconnect(evicted);
            _fastExtensionHandler.UnregisterPeer(evicted);
            evicted.Dispose();
        }

        LogConnect(connection);
    }

    public void Remove(PeerConnection connection)
    {
        lock (_lock)
        {
            _connections.Remove(connection);
        }

        _fastExtensionHandler.UnregisterPeer(connection);
        connection.Dispose();
        LogDisconnect(connection);
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
        List<PeerConnection> toRemove;
        double dropoutProbability;
        lock (_lock)
        {
            dropoutProbability = _configService.PeerDropoutProbability;

            if (dropoutProbability <= 0 || _connections.Count == 0)
            {
                return;
            }

            toRemove = _connections
                .Where(_ => Random.Shared.NextDouble() < dropoutProbability)
                .ToList();

            foreach (var conn in toRemove)
            {
                _connections.Remove(conn);
            }
        }

        foreach (var conn in toRemove)
        {
            _logger.Debug(
                "Peer {0} dropped out (probability: {1:F2})",
                conn.RemoteIp,
                dropoutProbability);
            LogDisconnect(conn);
            _fastExtensionHandler.UnregisterPeer(conn);
            conn.Dispose();
        }
    }

    public void RotateConnections()
    {
        List<PeerConnection> oldest;
        double rotationPct;
        lock (_lock)
        {
            rotationPct = _configService.ConnectionRotationPercentage;
            var rotateCount = (int)Math.Ceiling(_connections.Count * rotationPct);

            if (rotateCount <= 0 || _connections.Count == 0)
            {
                return;
            }

            rotateCount = Math.Min(rotateCount, _connections.Count);

            oldest = _connections
                .OrderBy(c => c.ConnectedAt)
                .Take(rotateCount)
                .ToList();

            foreach (var conn in oldest)
            {
                _connections.Remove(conn);
            }
        }

        foreach (var conn in oldest)
        {
            _logger.Debug(
                "Rotating out peer {0} (oldest, rotation: {1:P0})",
                conn.RemoteIp,
                rotationPct);
            LogDisconnect(conn);
            _fastExtensionHandler.UnregisterPeer(conn);
            conn.Dispose();
        }
    }

    private Torrent ResolveTorrent(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return null;
        }

        try
        {
            var torrents = _torrentService.GetAll();
            return torrents.FirstOrDefault(t => string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private void LogConnect(PeerConnection connection)
    {
        try
        {
            var torrent = ResolveTorrent(connection.InfoHash);
            _connectionLogService.LogConnected(connection, torrent?.Name);

            if (torrent != null)
            {
                _eventLogService.Debug(
                    torrent.Id,
                    "Peers",
                    $"Peer connected {connection.RemoteIp}:{connection.RemotePort}{(connection.IsEncrypted ? " (encrypted)" : string.Empty)}");
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to log peer connection event");
        }
    }

    private void LogDisconnect(PeerConnection connection)
    {
        try
        {
            var torrent = ResolveTorrent(connection.InfoHash);
            _connectionLogService.LogDisconnected(connection, torrent?.Name);

            if (torrent != null)
            {
                _eventLogService.Debug(
                    torrent.Id,
                    "Peers",
                    $"Peer disconnected {connection.RemoteIp}:{connection.RemotePort}");
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to log peer disconnection event");
        }
    }
}
