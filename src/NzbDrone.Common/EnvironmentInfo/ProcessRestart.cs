using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace NzbDrone.Common.EnvironmentInfo;

public static class ProcessRestart
{
    public const string WaitForPidArgument = "restart-wait-pid";
    public const string LockFileName = "seedarr.instance.lock";
    public static readonly TimeSpan ShutdownWaitTimeout = TimeSpan.FromMinutes(5);

    private static readonly ConcurrentDictionary<string, FileStream> Locks = new(StringComparer.OrdinalIgnoreCase);

    public static string PrepareForStart(StartupContext startupContext)
    {
        if (startupContext != null &&
            startupContext.Args.TryGetValue(WaitForPidArgument, out var pidText) &&
            int.TryParse(pidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
        {
            WaitForPreviousProcess(pid, ShutdownWaitTimeout);
        }

        var dataFolder = new AppFolderInfo(startupContext).AppDataFolder;
        AcquireDataDirectoryLock(dataFolder, TimeSpan.FromSeconds(30));
        return dataFolder;
    }

    public static ProcessStartInfo CreateStartInfo(
        string processPath,
        IReadOnlyList<string> commandLineArgs,
        string workingDirectory,
        int waitForPid,
        string entryAssemblyPath = null)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new ArgumentException("Process path is required to restart.", nameof(processPath));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = processPath, /* cmd-safe: fixed/admin-configured binary, args via ProcessStartInfo, no shell */ // nosemgrep
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false
        };

        var args = commandLineArgs ?? Array.Empty<string>();
        var start = 0;
        if (IsDotnetHost(processPath))
        {
            var assembly = ResolveEntryAssembly(args, entryAssemblyPath, out start);
            startInfo.ArgumentList.Add(assembly);
        }
        else if (args.Count > 0 && IsSameExecutable(args[0], processPath))
        {
            start = 1;
        }

        for (var i = start; i < args.Count; i++)
        {
            if (IsWaitForPidArgument(args[i]))
            {
                continue;
            }

            startInfo.ArgumentList.Add(args[i]);
        }

        if (waitForPid > 0)
        {
            startInfo.ArgumentList.Add("--" + WaitForPidArgument + "=" + waitForPid.ToString(CultureInfo.InvariantCulture));
        }

        return startInfo;
    }

    public static void WaitForPreviousProcess(int pid, TimeSpan timeout)
    {
        if (pid <= 0 || pid == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                return;
            }

            if (!process.WaitForExit(timeout))
            {
                throw new TimeoutException($"Timed out waiting for process {pid} to exit before restart.");
            }
        }
        catch (ArgumentException)
        {
            // The previous process has already exited.
        }
    }

    public static void AcquireDataDirectoryLock(string dataFolder, TimeSpan retryFor)
    {
        if (string.IsNullOrWhiteSpace(dataFolder))
        {
            throw new ArgumentException("Data folder is required.", nameof(dataFolder));
        }

        var full = Path.GetFullPath(dataFolder);
        Directory.CreateDirectory(full);
        var path = Path.Combine(full, LockFileName);
        var deadline = DateTime.UtcNow + (retryFor < TimeSpan.Zero ? TimeSpan.Zero : retryFor);
        while (true)
        {
            try
            {
                Locks.GetOrAdd(full, _ => OpenLock(path));
                return;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException($"Another Seedarr instance is already using this data folder: {full}", ex);
            }
        }
    }

    public static void ReleaseDataDirectoryLock(string dataFolder)
    {
        if (string.IsNullOrWhiteSpace(dataFolder))
        {
            return;
        }

        var full = Path.GetFullPath(dataFolder);
        if (Locks.TryRemove(full, out var stream))
        {
            stream.Dispose();
        }
    }

    private static FileStream OpenLock(string path)
    {
        var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            stream.SetLength(0);
            var bytes = Encoding.UTF8.GetBytes(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static string ResolveEntryAssembly(IReadOnlyList<string> args, string entryAssemblyPath, out int start)
    {
        if (args.Count > 0 && !IsDotnetHost(args[0]) && IsManagedEntry(args[0]))
        {
            start = 1;
            return args[0];
        }

        if (args.Count > 1 && IsDotnetHost(args[0]) && IsManagedEntry(args[1]))
        {
            start = 2;
            return args[1];
        }

        if (!string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            start = args.Count > 0 && (IsDotnetHost(args[0]) || IsSameExecutable(args[0], entryAssemblyPath)) ? 1 : 0;
            return entryAssemblyPath;
        }

        throw new InvalidOperationException("Cannot restart a dotnet-hosted process without the entry assembly path.");
    }

    private static bool IsManagedEntry(string value)
    {
        return value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDotnetHost(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            || name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameExecutable(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return string.Equals(Path.GetFileName(left), Path.GetFileName(right), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool IsWaitForPidArgument(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.TrimStart('-', '/');
        return trimmed.Equals(WaitForPidArgument, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(WaitForPidArgument + "=", StringComparison.OrdinalIgnoreCase);
    }
}
