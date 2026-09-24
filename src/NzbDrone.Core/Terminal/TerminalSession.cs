using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Terminal;

public class TerminalSession : IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private int _disposed;

    public TerminalSession(string connectionId, IPtyProcess process)
    {
        ConnectionId = connectionId;
        Process = process;
        CreatedAt = DateTime.UtcNow;
        CancellationTokenSource = new CancellationTokenSource();
    }

    public string ConnectionId { get; }
    public IPtyProcess Process { get; }
    public DateTime CreatedAt { get; }
    public CancellationTokenSource CancellationTokenSource { get; }
    public Task ReadLoopTask { get; set; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            CancellationTokenSource?.Cancel();
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to cancel cancellation token during terminal session disposal");
        }

        try
        {
            CancellationTokenSource?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to dispose cancellation token source in terminal session");
        }

        try
        {
            Process?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to dispose PTY process in terminal session");
        }
    }
}
