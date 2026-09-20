#pragma warning disable SA1117
#pragma warning disable IDE0007
#pragma warning disable CA1835
#pragma warning disable CA1849

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Terminal;

public class TerminalService : ITerminalService, IHandle<ApplicationShutdownRequested>
{
    private readonly IPtyProcessFactory _ptyProcessFactory;
    private readonly ConcurrentDictionary<string, TerminalSession> _sessions = new();
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private bool _disposed;

    public TerminalService(IPtyProcessFactory ptyProcessFactory)
    {
        _ptyProcessFactory = ptyProcessFactory;
    }

    public IReadOnlyCollection<string> ActiveSessionIds => _sessions.Keys.ToArray();

    public bool HasSession(string connectionId)
    {
        return !string.IsNullOrWhiteSpace(connectionId) && _sessions.ContainsKey(connectionId);
    }

    public TerminalSession GetSession(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            return null;
        }

        _sessions.TryGetValue(connectionId, out var session);
        return session;
    }

    public async Task<TerminalSession> StartSessionAsync(
        string connectionId,
        int cols,
        int rows,
        Func<string, Task> onOutput,
        Action onExit = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        if (_sessions.TryRemove(connectionId, out var existingSession))
        {
            _logger.Info("Closing existing terminal session for connection {0}", connectionId);
            try
            {
                existingSession.Process?.Kill();
                existingSession.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error disposing existing session for {0}", connectionId);
            }
        }

        cols = cols <= 0 ? 80 : cols;
        rows = rows <= 0 ? 24 : rows;

        _logger.Info("Spawning PTY process for connection {0} ({1}x{2})", connectionId, cols, rows);
        var process = _ptyProcessFactory.Create(cols, rows);
        var session = new TerminalSession(connectionId, process);

        if (!_sessions.TryAdd(connectionId, session))
        {
            process.Kill();
            process.Dispose();
            throw new InvalidOperationException($"Could not register terminal session for {connectionId}");
        }

        session.ReadLoopTask = Task.Run(async () =>
        {
            var buffer = new byte[4096];
            try
            {
                while (!session.CancellationTokenSource.Token.IsCancellationRequested && !session.Process.HasExited)
                {
                    var bytesRead = await session.Process.MasterStream.ReadAsync(
                        buffer.AsMemory(),
                        session.CancellationTokenSource.Token);

                    if (bytesRead <= 0)
                    {
                        break;
                    }

                    var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    if (onOutput != null)
                    {
                        try
                        {
                            await onOutput(text);
                        }
                        catch (Exception ex)
                        {
                            _logger.Trace(ex, "Error executing onOutput callback for {0}", connectionId);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Terminal read loop ended with exception for {0}", connectionId);
            }
            finally
            {
                try
                {
                    onExit?.Invoke();
                }
                catch (Exception ex)
                {
                    _logger.Trace(ex, "Error executing onExit callback for {0}", connectionId);
                }

                await CloseSessionInternalAsync(connectionId, session);
            }
        }, cancellationToken);

        return session;
    }

    public async Task WriteInputAsync(string connectionId, string data, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(connectionId))
        {
            return;
        }

        if (_sessions.TryGetValue(connectionId, out var session))
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            await session.Process.MasterStream.WriteAsync(bytes.AsMemory(), cancellationToken);
            await session.Process.MasterStream.FlushAsync(cancellationToken);
        }
    }

    public void WriteInput(string connectionId, string data)
    {
        if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(connectionId))
        {
            return;
        }

        if (_sessions.TryGetValue(connectionId, out var session))
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            session.Process.MasterStream.Write(bytes, 0, bytes.Length);
            session.Process.MasterStream.Flush();
        }
    }

    public void Resize(string connectionId, int cols, int rows)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return;
        }

        if (_sessions.TryGetValue(connectionId, out var session))
        {
            cols = cols <= 0 ? 80 : cols;
            rows = rows <= 0 ? 24 : rows;
            session.Process.Resize(cols, rows);
        }
    }

    public async Task CloseSessionAsync(string connectionId)
    {
        await CloseSessionInternalAsync(connectionId, null);
    }

    public void CloseSession(string connectionId)
    {
        CloseSessionAsync(connectionId).GetAwaiter().GetResult();
    }

    private async Task CloseSessionInternalAsync(string connectionId, TerminalSession expectedSession = null)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return;
        }

        if (_sessions.TryGetValue(connectionId, out var currentSession))
        {
            if (expectedSession != null && !ReferenceEquals(currentSession, expectedSession))
            {
                return;
            }

            if (_sessions.TryRemove(connectionId, out var session))
            {
                _logger.Info("Closing terminal session for {0} (PID {1})", connectionId, session.Process.Pid);

                try
                {
                    await session.CancellationTokenSource.CancelAsync();
                }
                catch
                {
                }

                try
                {
                    session.Process.Kill();
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Error terminating terminal process for {0}", connectionId);
                }

                try
                {
                    session.Dispose();
                }
                catch
                {
                }
            }
        }
    }

    public void Handle(ApplicationShutdownRequested message)
    {
        _logger.Info("Application shutdown requested, terminating all active terminal sessions");
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var connectionIds = _sessions.Keys.ToArray();
        foreach (var id in connectionIds)
        {
            try
            {
                CloseSession(id);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error closing terminal session {0} during dispose", id);
            }
        }

        _sessions.Clear();
    }
}
