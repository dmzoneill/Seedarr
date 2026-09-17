using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
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
    void Purge(DateTime before, int maxLogsPerTorrent);
    Task FlushAsync();
}

public class TorrentEventLogService : ITorrentEventLogService, IDisposable, IAsyncDisposable
{
    private readonly ITorrentEventLogRepository _repository;
    private readonly Logger _logger;
    private readonly Timer _purgeTimer;
    private readonly Channel<TorrentEventLog> _logChannel = Channel.CreateBounded<TorrentEventLog>(
        new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processTask;
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private bool _disposed;

    public TorrentEventLogService(ITorrentEventLogRepository repository)
    {
        _repository = repository;
        _logger = LogManager.GetCurrentClassLogger();
        _processTask = Task.Run(ProcessLogsAsync);
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
        Enqueue("Debug", torrentId, source, message);
        _logger.Debug("[{0}] {1}", source, message);
    }

    public void Info(int torrentId, string source, string message)
    {
        Enqueue("Info", torrentId, source, message);
        _logger.Info("[{0}] {1}", source, message);
    }

    public void Warn(int torrentId, string source, string message)
    {
        Enqueue("Warn", torrentId, source, message);
        _logger.Warn("[{0}] {1}", source, message);
    }

    public void Error(int torrentId, string source, string message)
    {
        Enqueue("Error", torrentId, source, message);
        _logger.Error("[{0}] {1}", source, message);
    }

    public List<TorrentEventLog> GetByTorrentId(int torrentId, int count)
    {
        DrainRemainingLogs();
        return _repository.GetByTorrentId(torrentId, count);
    }

    public void Purge(DateTime before)
    {
        Purge(before, 1000);
    }

    public void Purge(DateTime before, int maxLogsPerTorrent)
    {
        _repository.Purge(before, maxLogsPerTorrent);
        _logger.Trace("Purged torrent event logs before {0} (max {1} per torrent)", before, maxLogsPerTorrent);
    }

    public async Task FlushAsync()
    {
        DrainRemainingLogs();
        await _flushLock.WaitAsync().ConfigureAwait(false);
        _flushLock.Release();
    }

    private void Enqueue(string level, int torrentId, string source, string message)
    {
        if (torrentId <= 0 || string.IsNullOrEmpty(message) || _disposed)
        {
            return;
        }

        var log = new TorrentEventLog
        {
            TorrentId = torrentId,
            TimeStamp = DateTime.UtcNow,
            Level = level,
            Source = source ?? "System",
            Message = message
        };

        _logChannel.Writer.TryWrite(log);
    }

    private async Task ProcessLogsAsync()
    {
        var batch = new List<TorrentEventLog>(100);

        try
        {
            while (await _logChannel.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                while (batch.Count < 100 && _logChannel.Reader.TryRead(out var log))
                {
                    batch.Add(log);
                }

                if (batch.Count > 0)
                {
                    FlushBatch(batch);
                    batch.Clear();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown / cancellation
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error in torrent event log processor");
        }
        finally
        {
            DrainRemainingLogs();
        }
    }

    private void FlushBatch(List<TorrentEventLog> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        _flushLock.Wait();
        try
        {
            _repository.InsertMany((IEnumerable<TorrentEventLog>)batch.ToList());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to persist batch of {0} torrent event logs", batch.Count);
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private void DrainRemainingLogs()
    {
        var batch = new List<TorrentEventLog>();
        while (_logChannel.Reader.TryRead(out var log))
        {
            batch.Add(log);
            if (batch.Count >= 100)
            {
                FlushBatch(batch);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            FlushBatch(batch);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _purgeTimer?.Dispose();
        _logChannel.Writer.TryComplete();

        if (_processTask != null)
        {
            try
            {
                await _processTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error while waiting for torrent event log processor shutdown");
            }
        }

        _cts.Dispose();
        _flushLock.Dispose();
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            _purgeTimer?.Dispose();

            if (disposing)
            {
                _logChannel.Writer.TryComplete();
                if (_processTask != null)
                {
                    try
                    {
                        if (!_processTask.Wait(TimeSpan.FromSeconds(5)))
                        {
                            _cts.Cancel();
                            _processTask.Wait(TimeSpan.FromSeconds(1));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error while waiting for torrent event log processor shutdown");
                    }
                }

                _cts.Dispose();
                _flushLock.Dispose();
            }
        }
    }
}
