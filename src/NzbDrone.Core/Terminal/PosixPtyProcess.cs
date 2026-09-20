#pragma warning disable SA1117
#pragma warning disable IDE0007

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Terminal;

public class PosixPtyProcess : IPtyProcess
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly int _pid;
    private readonly int _masterFd;
    private readonly PosixPtyStream _masterStream;
    private int _disposed;
    private bool _hasExited;
    private int _exitCode;

    public PosixPtyProcess(int pid, int masterFd, int cols, int rows)
    {
        _pid = pid;
        _masterFd = masterFd;
        _masterStream = new PosixPtyStream(masterFd);
    }

    public int Pid => _pid;
    public bool HasExited => _hasExited;
    public int ExitCode => _exitCode;
    public Stream MasterStream => _masterStream;

    public static PosixPtyProcess Start(
        int cols = 80,
        int rows = 24,
        string command = null,
        string[] args = null,
        string workingDirectory = null,
        IDictionary<string, string> environment = null)
    {
        var shellPath = command;
        if (string.IsNullOrWhiteSpace(shellPath))
        {
            shellPath = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
        }

        string[] finalArgs;
        if (args != null && args.Length > 0)
        {
            finalArgs = new string[args.Length + 1];
            finalArgs[0] = shellPath;
            Array.Copy(args, 0, finalArgs, 1, args.Length);
        }
        else
        {
            finalArgs = new[] { shellPath };
        }

        var envDict = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            var val = entry.Value?.ToString();
            if (!string.IsNullOrEmpty(key) && val != null)
            {
                envDict[key] = val;
            }
        }

        envDict["TERM"] = "xterm-256color";
        envDict["COLORTERM"] = "truecolor";
        envDict["SHELL"] = shellPath;

        if (environment != null)
        {
            foreach (var kvp in environment)
            {
                envDict[kvp.Key] = kvp.Value;
            }
        }

        var unmanagedToFree = new List<IntPtr>();
        var shellPathPtr = IntPtr.Zero;
        var argvPtr = IntPtr.Zero;
        var envpPtr = IntPtr.Zero;
        var fileActions = Marshal.AllocHGlobal(1024);
        var attr = Marshal.AllocHGlobal(1024);
        unmanagedToFree.Add(fileActions);
        unmanagedToFree.Add(attr);

        var masterFd = -1;
        var slaveFd = -1;

        try
        {
            shellPathPtr = AllocUtf8String(shellPath, unmanagedToFree);
            argvPtr = AllocStringArray(finalArgs, unmanagedToFree);

            var envList = envDict.Select(kv => $"{kv.Key}={kv.Value}").ToList();
            envpPtr = AllocStringArray(envList, unmanagedToFree);

            var win = new Winsize
            {
                ws_col = (ushort)Math.Clamp(cols, 1, 1000),
                ws_row = (ushort)Math.Clamp(rows, 1, 1000),
                ws_xpixel = 0,
                ws_ypixel = 0
            };

            var ptyRes = PosixNative.OpenPty(out masterFd, out slaveFd, ref win);
            if (ptyRes != 0)
            {
                var err = Marshal.GetLastPInvokeError();
                throw new Win32Exception(err, $"openpty failed with errno {err}");
            }

            PosixNative.posix_spawn_file_actions_init(fileActions);
            PosixNative.posix_spawn_file_actions_adddup2(fileActions, slaveFd, 0);
            PosixNative.posix_spawn_file_actions_adddup2(fileActions, slaveFd, 1);
            PosixNative.posix_spawn_file_actions_adddup2(fileActions, slaveFd, 2);
            PosixNative.posix_spawn_file_actions_addclose(fileActions, slaveFd);
            PosixNative.posix_spawn_file_actions_addclose(fileActions, masterFd);

            PosixNative.posix_spawnattr_init(attr);
            PosixNative.posix_spawnattr_setflags(attr, PosixNative.POSIX_SPAWN_SETPGROUP);
            PosixNative.posix_spawnattr_setpgroup(attr, 0);

            var spawnRet = PosixNative.posix_spawn(
                out var pid,
                shellPathPtr,
                fileActions,
                attr,
                argvPtr,
                envpPtr);

            PosixNative.posix_spawn_file_actions_destroy(fileActions);
            PosixNative.posix_spawnattr_destroy(attr);
            PosixNative.close(slaveFd);
            slaveFd = -1;

            if (spawnRet != 0)
            {
                PosixNative.close(masterFd);
                masterFd = -1;
                throw new Win32Exception(spawnRet, $"posix_spawn failed with error {spawnRet}");
            }

            return new PosixPtyProcess(pid, masterFd, cols, rows);
        }
        finally
        {
            if (slaveFd >= 0)
            {
                PosixNative.close(slaveFd);
            }

            foreach (var ptr in unmanagedToFree)
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }

    public void Resize(int cols, int rows)
    {
        if (_disposed != 0 || _masterFd < 0)
        {
            return;
        }

        cols = Math.Clamp(cols, 1, 1000);
        rows = Math.Clamp(rows, 1, 1000);

        var win = new Winsize
        {
            ws_col = (ushort)cols,
            ws_row = (ushort)rows,
            ws_xpixel = 0,
            ws_ypixel = 0
        };

        var request = OperatingSystem.IsMacOS() ? PosixNative.TIOCSWINSZ_OSX : PosixNative.TIOCSWINSZ_LINUX;
        PosixNative.ioctl(_masterFd, request, ref win);
    }

    public void Kill()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (!_hasExited && _pid > 1)
            {
                // Send SIGHUP and SIGTERM to child process group
                try
                {
                    PosixNative.kill(-_pid, PosixNative.SIGHUP);
                    PosixNative.kill(-_pid, PosixNative.SIGTERM);
                }
                catch
                {
                    PosixNative.kill(_pid, PosixNative.SIGHUP);
                    PosixNative.kill(_pid, PosixNative.SIGTERM);
                }

                var sw = Stopwatch.StartNew();
                var exited = false;
                while (sw.ElapsedMilliseconds < 2000)
                {
                    var res = PosixNative.waitpid(_pid, out var status, PosixNative.WNOHANG);
                    if (res == _pid || res < 0)
                    {
                        exited = true;
                        _exitCode = (status >> 8) & 0xFF;
                        _hasExited = true;
                        break;
                    }

                    Thread.Sleep(50);
                }

                if (!exited)
                {
                    // Escalate to SIGKILL if still alive after grace period
                    try
                    {
                        PosixNative.kill(-_pid, PosixNative.SIGKILL);
                    }
                    catch
                    {
                        PosixNative.kill(_pid, PosixNative.SIGKILL);
                    }

                    PosixNative.waitpid(_pid, out var status, 0);
                    _exitCode = (status >> 8) & 0xFF;
                    _hasExited = true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error killing POSIX PTY process {0}", _pid);
        }
        finally
        {
            _masterStream?.Dispose();
        }
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        if (_hasExited)
        {
            return;
        }

        await Task.Run(() =>
        {
            while (!_hasExited && !cancellationToken.IsCancellationRequested)
            {
                var res = PosixNative.waitpid(_pid, out var status, PosixNative.WNOHANG);
                if (res == _pid || res < 0)
                {
                    _exitCode = (status >> 8) & 0xFF;
                    _hasExited = true;
                    break;
                }

                Thread.Sleep(100);
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        Kill();
        GC.SuppressFinalize(this);
    }

    private static IntPtr AllocUtf8String(string str, List<IntPtr> toFree)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
        toFree.Add(ptr);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Marshal.WriteByte(ptr + bytes.Length, 0);
        return ptr;
    }

    private static IntPtr AllocStringArray(IEnumerable<string> strings, List<IntPtr> toFree)
    {
        var list = strings.ToList();
        var arrayPtr = Marshal.AllocHGlobal(IntPtr.Size * (list.Count + 1));
        toFree.Add(arrayPtr);

        for (var i = 0; i < list.Count; i++)
        {
            var strPtr = AllocUtf8String(list[i], toFree);
            Marshal.WriteIntPtr(arrayPtr + (i * IntPtr.Size), strPtr);
        }

        Marshal.WriteIntPtr(arrayPtr + (list.Count * IntPtr.Size), IntPtr.Zero);
        return arrayPtr;
    }
}
