using System;
using System.Collections.Generic;
using System.Threading;
using NLog;

namespace NzbDrone.Core.Torrents;

public interface ITorrentEventLogService
{
    void Debug(int torrentId, string source, string message);
    void Info(int torrentId, string source, string message);
    void Warn(int torrentId, string source, string message);
    void Error(int torrentId, string source, string message);
    List<TorrentEventLog> GetByTorrentId(int torrentId, int count);
    void Purge(DateTime before);
}

public class TorrentEventLogService : ITorrentEventLogService, IDisposable
{
    private readonly ITorrentEventLogRepository _repository;
    private readonly Logger _logger;
    private readonly Timer _purgeTimer;

    public TorrentEventLogService(ITorrentEventLogRepository repository)
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
                    _logger.Error(ex, "Error during automatic torrent event log purge");
                }
            },
            null,
            TimeSpan.FromMinutes(15),
            TimeSpan.FromHours(6));
    }

    public void Debug(int torrentId, string source, string message)
    {
        Insert("Debug", torrentId, source, message);
        _logger.Debug("[{0}] {1}", source, message);
    }

    public void Info(int torrentId, string source, string message)
    {
        Insert("Info", torrentId, source, message);
        _logger.Info("[{0}] {1}", source, message);
    }

    public void Warn(int torrentId, string source, string message)
    {
        Insert("Warn", torrentId, source, message);
        _logger.Warn("[{0}] {1}", source, message);
    }

    public void Error(int torrentId, string source, string message)
    {
        Insert("Error", torrentId, source, message);
        _logger.Error("[{0}] {1}", source, message);
    }

    public List<TorrentEventLog> GetByTorrentId(int torrentId, int count)
    {
        return _repository.GetByTorrentId(torrentId, count);
    }

    public void Purge(DateTime before)
    {
        _repository.Purge(before);
        _logger.Trace("Purged torrent event logs before {0}", before);
    }

    private void Insert(string level, int torrentId, string source, string message)
    {
        if (torrentId <= 0 || string.IsNullOrEmpty(message))
        {
            return;
        }

        try
        {
            _repository.Insert(new TorrentEventLog
            {
                TorrentId = torrentId,
                TimeStamp = DateTime.UtcNow,
                Level = level,
                Source = source ?? "System",
                Message = message
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to persist torrent event log for torrent {0}", torrentId);
        }
    }

    public void Dispose()
    {
        _purgeTimer?.Dispose();
    }
}
