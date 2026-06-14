using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace NzbDrone.Core.Peers;

public interface IConnectionManager
{
    void Add(PeerConnection connection);
    void Remove(PeerConnection connection);
    List<PeerConnection> GetConnections(string infoHash);
    int ActiveCount { get; }
}

public class ConnectionManager : IConnectionManager
{
    private const int MaxConnections = 200;

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

    public ConnectionManager()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Add(PeerConnection connection)
    {
        lock (_lock)
        {
            if (_connections.Count >= MaxConnections)
            {
                var oldest = _connections.OrderBy(c => c.LastActivity).First();
                _logger.Debug("Evicting peer {0} (LRU)", oldest.RemoteIp);
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
}
