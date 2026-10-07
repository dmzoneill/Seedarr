// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;

namespace Seedarr.Http.Terminal;

public static class TerminalShellArgv
{
    public static string[] BuildInteractiveExecArgv(string shellPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellPath);

        if (IsBash(shellPath))
        {
            return [shellPath, "--noprofile", "--norc", "-i"];
        }

        return [shellPath, "-i"];
    }

    public static string BuildInteractiveProcessArguments(string shellPath)
    {
        var argv = BuildInteractiveExecArgv(shellPath);
        return string.Join(' ', argv.AsSpan(1));
    }

    private static bool IsBash(string shellPath) =>
        string.Equals(shellPath, "/bin/bash", StringComparison.Ordinal)
        || string.Equals(Path.GetFileName(shellPath), "bash", StringComparison.Ordinal);
}
