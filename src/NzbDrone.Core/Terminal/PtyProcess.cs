using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Terminal;

public static class PtyProcess
{
    public static IPtyProcess Create(
        int cols = 80,
        int rows = 24,
        string command = null,
        string[] args = null,
        string workingDirectory = null,
        IDictionary<string, string> environment = null)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsPtyProcess.Start(cols, rows, command, args, workingDirectory, environment);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return PosixPtyProcess.Start(cols, rows, command, args, workingDirectory, environment);
        }

        throw new PlatformNotSupportedException("PTY is only supported on Linux, macOS, and Windows.");
    }
}
