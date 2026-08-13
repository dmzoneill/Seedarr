using System;
using System.Collections.Generic;

namespace NzbDrone.Common.EnvironmentInfo;

public class StartupContext
{
    public StartupContext(params string[] args)
    {
        Flags = new HashSet<string>();
        Args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var arg in args)
        {
            var parts = arg.TrimStart('-', '/').Split('=', 2);
            if (parts.Length == 2)
            {
                Args[parts[0].ToLower()] = parts[1];
            }
            else
            {
                Flags.Add(parts[0].ToLower());
            }
        }
    }

    public HashSet<string> Flags { get; }
    public Dictionary<string, string> Args { get; }
}
