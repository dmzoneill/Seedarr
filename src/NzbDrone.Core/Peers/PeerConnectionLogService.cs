using System;
using System.Collections.Generic;
using System.Threading;
using NLog;

namespace NzbDrone.Core.Peers;

public interface IPeerConnectionLogService
{
    void LogConnected(PeerConnection connection, string torrentName);
    void LogDisconnected(PeerConnection connection, string torrentName);
    List<PeerConnectionLog> GetByTimeRange(DateTime start, DateTime end);
    List<PeerConnectionLog> GetByInfoHash(string infoHash, DateTime start, DateTime end);
    void Purge(DateTime before);
}

public class PeerConnectionLogService : IPeerConnectionLogService, IDisposable
{
    private readonly IPeerConnectionLogRepository _repository;
    private readonly Logger _logger;
    private readonly Timer _purgeTimer;

    public PeerConnectionLogService(IPeerConnectionLogRepository repository)
    {
        _repository = repository;
        _logger = LogManager.GetCurrentClassLogger();
        _purgeTimer = new Timer(
            _ =>
            {
                try
                {
                    Purge(DateTime.UtcNow.AddDays(-7));
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during automatic peer connection log purge");
                }
            },
            null,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(24));
    }

    public void LogConnected(PeerConnection connection, string torrentName)
    {
        LogEvent(connection, torrentName, "Connected");
    }

    public void LogDisconnected(PeerConnection connection, string torrentName)
    {
        LogEvent(connection, torrentName, "Disconnected");
    }

    private void LogEvent(PeerConnection connection, string torrentName, string eventType)
    {
        var log = new PeerConnectionLog
        {
            InfoHash = connection.InfoHash ?? string.Empty,
            TorrentName = torrentName,
            RemoteIp = connection.RemoteIp,
            RemotePort = connection.RemotePort,
            PeerId = connection.PeerId,
            IsEncrypted = connection.IsEncrypted,
            EventType = eventType,
            Timestamp = DateTime.UtcNow,
        };

        _repository.Insert(log);
        _logger.Trace("Logged peer {0}: {1}:{2} for {3}", eventType.ToLowerInvariant(), connection.RemoteIp, connection.RemotePort, connection.InfoHash);
    }

    public List<PeerConnectionLog> GetByTimeRange(DateTime start, DateTime end)
    {
        return _repository.GetByTimeRange(start, end);
    }

    public List<PeerConnectionLog> GetByInfoHash(string infoHash, DateTime start, DateTime end)
    {
        return _repository.GetByInfoHash(infoHash, start, end);
    }

    public void Purge(DateTime before)
    {
        _repository.Purge(before);
        _logger.Info("Purged peer connection logs before {0}", before);
    }

    public void Dispose()
    {
        _purgeTimer?.Dispose();
    }
}
