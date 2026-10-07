// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Seedarr.Http.Terminal;

public static class TerminalEnvironmentSanitizer
{
    private static readonly string[] SensitivePatterns =
    [
        "PASSWORD",
        "SECRET",
        "POSTGRES",
        "API_KEY",
        "TOKEN",
        "DATABASE_URL",
        "JWT",
        "AUTH",
        "CREDENTIAL",
        "PRIVATE_KEY",
        "SEEDARR_",
    ];

    public static bool IsSensitiveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        foreach (var pattern in SensitivePatterns)
        {
            if (key.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static void StripSensitiveKeys(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        var sensitiveKeys = startInfo.EnvironmentVariables.Keys.Cast<string>()
            .Where(IsSensitiveKey)
            .ToList();

        foreach (var key in sensitiveKeys)
        {
            startInfo.EnvironmentVariables.Remove(key);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S5443", Justification = "Private temp directory created with 0700 permissions")]
#pragma warning disable S5443
    public static string GetSafeTempDirectory()
    {
        var privateDir = Path.Combine(Path.GetTempPath(), "seedarr-" + Environment.ProcessId);
        if (!Directory.Exists(privateDir))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Directory.CreateDirectory(privateDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            else
            {
                Directory.CreateDirectory(privateDir);
            }
        }

        return privateDir;
    }

    public static Dictionary<string, string> BuildSanitizedEnvironment(string workingDirectory = null)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";
        var home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var user = Environment.GetEnvironmentVariable("USER") ?? Environment.GetEnvironmentVariable("USERNAME") ?? "seedarr";
        var shell = Environment.GetEnvironmentVariable("SHELL") ?? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell.exe" : (File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh"));
        var tmpdir = Environment.GetEnvironmentVariable("TMPDIR");
        if (string.IsNullOrWhiteSpace(tmpdir))
        {
            tmpdir = GetSafeTempDirectory();
        }

        var lang = Environment.GetEnvironmentVariable("LANG") ?? "en_US.UTF-8";
        var pwd = !string.IsNullOrWhiteSpace(workingDirectory)
            ? workingDirectory
            : (Environment.GetEnvironmentVariable("PWD") ?? Directory.GetCurrentDirectory());

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(path))
        {
            env["PATH"] = path;
        }

        if (!string.IsNullOrEmpty(home))
        {
            env["HOME"] = home;
        }

        if (!string.IsNullOrEmpty(user))
        {
            env["USER"] = user;
        }

        if (!string.IsNullOrEmpty(shell))
        {
            env["SHELL"] = shell;
        }

        if (!string.IsNullOrEmpty(tmpdir))
        {
            env["TMPDIR"] = tmpdir;
        }

        if (!string.IsNullOrEmpty(pwd))
        {
            env["PWD"] = pwd;
        }

        env["TERM"] = "xterm-256color";
        env["COLORTERM"] = "truecolor";
        env["LANG"] = lang;
        env["PS1"] = @"\[\e[1;33m\]\u@seedarr\[\e[0m\]:\[\e[1;34m\]\w\[\e[0m\]\$ ";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
            if (!string.IsNullOrEmpty(systemRoot))
            {
                env["SystemRoot"] = systemRoot;
            }

            var winDir = Environment.GetEnvironmentVariable("windir");
            if (!string.IsNullOrEmpty(winDir))
            {
                env["windir"] = winDir;
            }

            var pathext = Environment.GetEnvironmentVariable("PATHEXT");
            if (!string.IsNullOrEmpty(pathext))
            {
                env["PATHEXT"] = pathext;
            }

            var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(userProfile))
            {
                env["USERPROFILE"] = userProfile;
            }
        }

        foreach (var key in env.Keys.Where(IsSensitiveKey).ToList())
        {
            env.Remove(key);
        }

        return env;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S5443", Justification = "Private temp directory created with 0700 permissions")]
    public static void Sanitize(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        var env = BuildSanitizedEnvironment(startInfo.WorkingDirectory);
        startInfo.Environment.Clear();
        foreach (var kvp in env)
        {
            startInfo.Environment[kvp.Key] = kvp.Value;
        }
    }
}
