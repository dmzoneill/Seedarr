using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Terminal;

public interface ITerminalService : IDisposable
{
    IReadOnlyCollection<string> ActiveSessionIds { get; }
    bool HasSession(string connectionId);
    TerminalSession GetSession(string connectionId);
    Task<TerminalSession> StartSessionAsync(
        string connectionId,
        int cols,
        int rows,
        Func<string, Task> onOutput,
        Action onExit = null,
        CancellationToken cancellationToken = default);
    Task WriteInputAsync(string connectionId, string data, CancellationToken cancellationToken = default);
    void WriteInput(string connectionId, string data);
    void Resize(string connectionId, int cols, int rows);
    Task CloseSessionAsync(string connectionId);
    void CloseSession(string connectionId);
}
