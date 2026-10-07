// Copyright (c) FeedItOut. All rights reserved.

#pragma warning disable SX1309

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Terminal;

namespace Seedarr.Http.Terminal;

public sealed class LinuxPtySession : ITerminalSession
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly int _masterFd;
    private readonly int _pid;
    private int _disposed;

    public int ProcessId => this._pid;

    public bool IsActive => this._disposed == 0;

    private LinuxPtySession(int masterFd, int pid)
    {
        this._masterFd = masterFd;
        this._pid = pid;
        this.StartWatcher();
    }

    public static LinuxPtySession Start(string cwd, int cols, int rows)
    {
        var ws = new NativePty.Winsize
        {
            WsCol = (ushort)TerminalGeometry.Clamp(cols, rows).Cols,
            WsRow = (ushort)TerminalGeometry.Clamp(cols, rows).Rows,
        };

        string safeCwd = !string.IsNullOrWhiteSpace(cwd) ? Path.GetFullPath(cwd) : null;
        string shell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
        string[] argv = [shell, "-i"];

        var envVars = TerminalEnvironmentSanitizer.BuildSanitizedEnvironment(safeCwd);
        var envStrings = envVars.Select(kv => $"{kv.Key}={kv.Value}").ToArray();

        IntPtr cwdPtr = safeCwd != null ? Marshal.StringToCoTaskMemUTF8(safeCwd) : IntPtr.Zero;
        IntPtr shellPtr = Marshal.StringToCoTaskMemUTF8(shell);
        IntPtr argvArrayPtr = AllocateNativeStringArray(argv, out var argvPointers);
        IntPtr envArrayPtr = AllocateNativeStringArray(envStrings, out var envPointers);

        int masterFd = -1;
        int pid = -1;

        System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(typeof(NativePty).GetMethod(nameof(NativePty.Chdir)).MethodHandle);
        System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(typeof(NativePty).GetMethod(nameof(NativePty.ExecveRaw)).MethodHandle);
        System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(typeof(NativePty).GetMethod(nameof(NativePty.ExecvpRaw)).MethodHandle);
        System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(typeof(NativePty).GetMethod(nameof(NativePty.Exit)).MethodHandle);

        try
        {
            pid = NativePty.Forkpty(out masterFd, IntPtr.Zero, IntPtr.Zero, ref ws);
            if (pid < 0)
            {
                throw new InvalidOperationException($"Failed to fork pseudo-terminal (errno: {Marshal.GetLastWin32Error()})");
            }

            if (pid == 0)
            {
                // Child process: Only invoke async-signal-safe functions (chdir, execve, _exit).
                // Zero managed memory allocations, zero runtime locks, zero setenv/malloc calls.
                if (cwdPtr != IntPtr.Zero)
                {
                    if (NativePty.Chdir(cwdPtr) != 0)
                    {
                        // Ignored in child process
                    }
                }

                if (NativePty.ExecveRaw(shellPtr, argvArrayPtr, envArrayPtr) != 0)
                {
                    // Fallback if execve fails
                    if (NativePty.ExecvpRaw(shellPtr, argvArrayPtr) != 0)
                    {
                        // Ignored
                    }
                }

                NativePty.Exit(1);
            }
        }
        finally
        {
            if (cwdPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(cwdPtr);
            }

            if (shellPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(shellPtr);
            }

            FreeNativeStringArray(argvArrayPtr, argvPointers);
            FreeNativeStringArray(envArrayPtr, envPointers);
        }

        return new LinuxPtySession(masterFd, pid);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (this._disposed != 0)
        {
            return 0;
        }

        var temp = new byte[buffer.Length];
        return await Task.Run(
            () =>
            {
                nint bytesRead = NativePty.Read(this._masterFd, temp, (nuint)temp.Length);
                if (bytesRead <= 0)
                {
                    return 0;
                }

                temp.AsSpan(0, (int)bytesRead).CopyTo(buffer.Span);
                return (int)bytesRead;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (this._disposed != 0 || buffer.IsEmpty)
        {
            return;
        }

        var temp = buffer.ToArray();
        await Task.Run(
            () =>
            {
                NativePty.Write(this._masterFd, temp, (nuint)temp.Length);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public void Resize(int cols, int rows)
    {
        if (this._disposed != 0)
        {
            return;
        }

        var ws = new NativePty.Winsize
        {
            WsCol = (ushort)TerminalGeometry.Clamp(cols, rows).Cols,
            WsRow = (ushort)TerminalGeometry.Clamp(cols, rows).Rows,
        };

        if (NativePty.Ioctl(this._masterFd, NativePty.TIOCSWINSZ, ref ws) != 0)
        {
            _logger.Debug("ioctl TIOCSWINSZ failed");
        }
    }

    public void Kill()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (NativePty.Close(this._masterFd) != 0)
            {
                // Ignored on teardown
            }
        }
        catch
        {
            // Ignored on teardown
        }

        if (this._pid <= 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (NativePty.Kill(this._pid, 15) != 0) // SIGTERM
                {
                    // Ignored
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to send SIGTERM to Linux PTY process {0}", this._pid);
            }

            var sw = Stopwatch.StartNew();
            bool reaped = false;

            while (sw.ElapsedMilliseconds < 1500)
            {
                int res = NativePty.Waitpid(this._pid, out _, 1); // WNOHANG
                if (res > 0 || res < 0)
                {
                    reaped = true;
                    break;
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            if (!reaped)
            {
                try
                {
                    if (NativePty.Kill(this._pid, 9) != 0) // SIGKILL
                    {
                        // Ignored
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to send SIGKILL to Linux PTY process {0}", this._pid);
                }

                var killSw = Stopwatch.StartNew();
                while (killSw.ElapsedMilliseconds < 2000)
                {
                    int res = NativePty.Waitpid(this._pid, out _, 1); // WNOHANG
                    if (res > 0 || res < 0)
                    {
                        break;
                    }

                    await Task.Delay(50).ConfigureAwait(false);
                }
            }
        });
    }

    public ValueTask DisposeAsync()
    {
        this.Kill();
        return ValueTask.CompletedTask;
    }

    private static IntPtr AllocateNativeStringArray(string[] array, out IntPtr[] elementPointers)
    {
        elementPointers = new IntPtr[array.Length + 1];
        for (int i = 0; i < array.Length; i++)
        {
            elementPointers[i] = Marshal.StringToCoTaskMemUTF8(array[i]);
        }

        elementPointers[^1] = IntPtr.Zero;

        IntPtr arrayPtr = Marshal.AllocHGlobal(IntPtr.Size * elementPointers.Length);
        Marshal.Copy(elementPointers, 0, arrayPtr, elementPointers.Length);
        return arrayPtr;
    }

    private static void FreeNativeStringArray(IntPtr arrayPtr, IntPtr[] elementPointers)
    {
        if (elementPointers != null)
        {
            foreach (var ptr in elementPointers)
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(ptr);
                }
            }
        }

        if (arrayPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(arrayPtr);
        }
    }

    private void StartWatcher()
    {
        if (this._pid <= 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            while (this._disposed == 0)
            {
                int res = NativePty.Waitpid(this._pid, out _, 1); // WNOHANG
                if (res > 0 || res < 0)
                {
                    this.Kill();
                    return;
                }

                await Task.Delay(200).ConfigureAwait(false);
            }
        });
    }
}
