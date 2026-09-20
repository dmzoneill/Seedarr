using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Terminal;

public interface IPtyProcess : IDisposable
{
    int Pid { get; }
    bool HasExited { get; }
    int ExitCode { get; }
    Stream MasterStream { get; }
    void Resize(int cols, int rows);
    void Kill();
    Task WaitForExitAsync(CancellationToken cancellationToken = default);
}
