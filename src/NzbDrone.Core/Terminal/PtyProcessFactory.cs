using System.Collections.Generic;

namespace NzbDrone.Core.Terminal;

public class PtyProcessFactory : IPtyProcessFactory
{
    public IPtyProcess Create(
        int cols = 80,
        int rows = 24,
        string command = null,
        string[] args = null,
        string workingDirectory = null,
        IDictionary<string, string> environment = null)
    {
        return PtyProcess.Create(cols, rows, command, args, workingDirectory, environment);
    }
}
