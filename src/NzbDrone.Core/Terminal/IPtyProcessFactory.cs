using System.Collections.Generic;

namespace NzbDrone.Core.Terminal;

public interface IPtyProcessFactory
{
    IPtyProcess Create(
        int cols = 80,
        int rows = 24,
        string command = null,
        string[] args = null,
        string workingDirectory = null,
        IDictionary<string, string> environment = null);
}
