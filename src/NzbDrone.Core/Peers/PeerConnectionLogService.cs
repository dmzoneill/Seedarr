using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Peers;

public interface IPeerConnectionLogService
{
    void LogConnected(PeerConnection connection, string torrentName);
    void LogDisconnected(PeerConnection connection, string torrentName);
    List<PeerConnectionLog> GetByTimeRange(DateTime start, DateTime end, int limit = 1000, int offset = 0);
    List<PeerConnectionLog> GetByInfoHash(string infoHash, DateTime start, DateTime end, int limit = 1000, int offset = 0);
    List<PeerConnectionLog> GetLogs(DateTime start, DateTime end, int limit = 1000, int offset = 0);
    List<PeerConnectionLog> GetLogsByInfoHash(string infoHash, DateTime start, DateTime end, int limit = 1000, int offset = 0);
    (int EncryptedCount, int PlaintextCount) GetConnectionCounts(DateTime start, DateTime end);
    void Purge(DateTime before);
    void Purge(DateTime before, int maxLogs);
    void Purge(DateTime before, int maxLogs, int batchSize);
    void Flush();
    Task FlushAsync();
}

public class PeerConnectionLogService : IPeerConnectionLogService, IDisposable, IAsyncDisposable
{
    private readonly IPeerConnectionLogRepository _repository;
    private readonly Logger _logger;
    private readonly Timer _purgeTimer;
    private readonly Channel<PeerConnectionLog> _logChannel = Channel.CreateBounded<PeerConnectionLog>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumerTask;
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly object _flushGate = new();
    private CancellationTokenSource _flushSignalCts = new();
    private volatile bool _isProcessingBatch;
    private bool _disposed;

    public PeerConnectionLogService(IPeerConnectionLogRepository repository)
    {
        _repository = repository;
        _logger = LogManager.GetCurrentClassLogger();
        _consumerTask = Task.Run(ProcessLogsAsync);
        _purgeTimer = new Timer(
            _ =>
            {
                try
                {
                    Purge(DateTime.UtcNow.AddDays(-7), 50000);
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
        if (_disposed)
        {
            return;
        }

        var log = new PeerConnectionLog
        {
            InfoHash = (connection?.InfoHash ?? string.Empty).Trim().ToLowerInvariant(),
            TorrentName = torrentName,
            RemoteIp = connection?.RemoteIp,
            RemotePort = connection?.RemotePort ?? 0,
            PeerId = connection?.PeerId,
            IsEncrypted = connection?.IsEncrypted ?? false,
            EventType = eventType,
            Timestamp = DateTime.UtcNow,
        };

        _logChannel.Writer.TryWrite(log);
        if (connection != null)
        {
            _logger.Trace("Logged peer {0}: {1}:{2} for {3}", eventType.ToLowerInvariant(), connection.RemoteIp, connection.RemotePort, connection.InfoHash);
        }
    }

    public List<PeerConnectionLog> GetByTimeRange(DateTime start, DateTime end, int limit = 1000, int offset = 0)
    {
        return _repository.GetByTimeRange(start, end, limit, offset);
    }

    public List<PeerConnectionLog> GetByInfoHash(string infoHash, DateTime start, DateTime end, int limit = 1000, int offset = 0)
    {
        return _repository.GetByInfoHash(infoHash, start, end, limit, offset);
    }

    public List<PeerConnectionLog> GetLogs(DateTime start, DateTime end, int limit = 1000, int offset = 0) =>
        GetByTimeRange(start, end, limit, offset);

    public List<PeerConnectionLog> GetLogsByInfoHash(string infoHash, DateTime start, DateTime end, int limit = 1000, int offset = 0) =>
        GetByInfoHash(infoHash, start, end, limit, offset);

    public (int EncryptedCount, int PlaintextCount) GetConnectionCounts(DateTime start, DateTime end)
    {
        return _repository.GetConnectionCounts(start, end);
    }

    public void Purge(DateTime before)
    {
        Purge(before, 50000, 1000);
    }

    public void Purge(DateTime before, int maxLogs)
    {
        Purge(before, maxLogs, 1000);
    }

    public void Purge(DateTime before, int maxLogs, int batchSize)
    {
        _repository.Purge(before, maxLogs, batchSize);
        _logger.Info("Purged peer connection logs before {0} (max {1} logs retained, batch size {2})", before, maxLogs, batchSize);
    }

    public void Flush()
    {
        TriggerFlush();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((_logChannel.Reader.Count > 0 || _isProcessingBatch) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }

        _flushLock.Wait(TimeSpan.FromSeconds(5));
        _flushLock.Release();
    }

    public async Task FlushAsync()
    {
        TriggerFlush();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((_logChannel.Reader.Count > 0 || _isProcessingBatch) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5).ConfigureAwait(false);
        }

        await _flushLock.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        _flushLock.Release();
    }

    private void TriggerFlush()
    {
        lock (_flushGate)
        {
            if (!_flushSignalCts.IsCancellationRequested)
            {
                _flushSignalCts.Cancel();
            }
        }
    }

    private async Task ProcessLogsAsync()
    {
        var batch = new List<PeerConnectionLog>(100);

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
                    _isProcessingBatch = true;
                }

                if (batch.Count < 100 && batch.Count > 0)
                {
                    var deadline = DateTime.UtcNow.AddSeconds(2);
                    while (batch.Count < 100)
                    {
                        var remaining = deadline - DateTime.UtcNow;
                        if (remaining <= TimeSpan.Zero)
                        {
                            break;
                        }

                        CancellationToken flushToken;
                        lock (_flushGate)
                        {
                            if (_flushSignalCts.IsCancellationRequested)
                            {
                                _flushSignalCts.Dispose();
                                _flushSignalCts = new CancellationTokenSource();
                            }

                            flushToken = _flushSignalCts.Token;
                        }

                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, flushToken);
                        timeoutCts.CancelAfter(remaining);

                        try
                        {
                            var available = await _logChannel.Reader.WaitToReadAsync(timeoutCts.Token).ConfigureAwait(false);
                            if (!available)
                            {
                                break;
                            }

                            while (batch.Count < 100 && _logChannel.Reader.TryRead(out var log))
                            {
                                batch.Add(log);
                            }
                        }
                        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
                        {
                            // 2 second timeout expired or flush signaled
                            break;
                        }
                    }
                }

                if (batch.Count > 0)
                {
                    FlushBatch(batch);
                    batch.Clear();
                    _isProcessingBatch = false;
                }

                lock (_flushGate)
                {
                    if (_flushSignalCts.IsCancellationRequested)
                    {
                        _flushSignalCts.Dispose();
                        _flushSignalCts = new CancellationTokenSource();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown / cancellation
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error in peer connection log processor");
        }
        finally
        {
            DrainRemainingLogs(batch);
            _isProcessingBatch = false;
        }
    }

    private void FlushBatch(List<PeerConnectionLog> batch)
    {
        if (batch == null || batch.Count == 0)
        {
            return;
        }

        _flushLock.Wait();
        try
        {
            _repository.InsertMany(batch.ToList());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to persist batch of {0} peer connection logs", batch.Count);
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private void DrainRemainingLogs(List<PeerConnectionLog> batch = null)
    {
        batch ??= new List<PeerConnectionLog>(100);

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
            batch.Clear();
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
        TriggerFlush();

        if (_consumerTask != null)
        {
            try
            {
                await _consumerTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error while waiting for peer connection log processor shutdown");
            }
        }

        _cts.Dispose();
        _flushSignalCts.Dispose();
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
                TriggerFlush();

                if (_consumerTask != null)
                {
                    try
                    {
                        if (!_consumerTask.Wait(TimeSpan.FromSeconds(5)))
                        {
                            _cts.Cancel();
                            _consumerTask.Wait(TimeSpan.FromSeconds(1));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error while waiting for peer connection log processor shutdown");
                    }
                }

                _cts.Dispose();
                _flushSignalCts.Dispose();
                _flushLock.Dispose();
            }
        }
    }
}
