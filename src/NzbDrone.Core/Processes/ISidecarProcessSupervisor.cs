using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NzbDrone.Core.Processes;

public interface ISidecarProcessSupervisor : IDisposable
{
    IReadOnlyCollection<int> ActiveProcessIds { get; }

    bool IsShuttingDown { get; }

    void RegisterProcess(Process process);

    void UnregisterProcess(int pid);

    void TerminateAll(TimeSpan? timeout = null);
}
