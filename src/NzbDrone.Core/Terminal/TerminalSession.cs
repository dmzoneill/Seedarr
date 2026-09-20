using System;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Terminal;

public class TerminalSession : IDisposable
{
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
        catch
        {
        }

        try
        {
            CancellationTokenSource?.Dispose();
        }
        catch
        {
        }

        try
        {
            Process?.Dispose();
        }
        catch
        {
        }
    }
}
