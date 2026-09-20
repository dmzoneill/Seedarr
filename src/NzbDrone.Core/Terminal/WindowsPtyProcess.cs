#pragma warning disable SA1117
#pragma warning disable IDE0007
#pragma warning disable CA1844
#pragma warning disable CA1835

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using NLog;

namespace NzbDrone.Core.Terminal;

public class WindowsPtyProcess : IPtyProcess
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly int _pid;
    private readonly IntPtr _hProcess;
    private readonly IntPtr _hThread;
    private readonly IntPtr _hPC;
    private readonly Stream _masterStream;
    private readonly Process _fallbackProcess;
    private int _disposed;
    private bool _hasExited;
    private int _exitCode;

    public WindowsPtyProcess(int pid, IntPtr hProcess, IntPtr hThread, IntPtr hPC, Stream masterStream)
    {
        _pid = pid;
        _hProcess = hProcess;
        _hThread = hThread;
        _hPC = hPC;
        _masterStream = masterStream;
    }

    private WindowsPtyProcess(Process fallbackProcess)
    {
        _fallbackProcess = fallbackProcess;
        _pid = fallbackProcess.Id;
        _masterStream = new BidirectionalStream(fallbackProcess.StandardInput.BaseStream, fallbackProcess.StandardOutput.BaseStream);
    }

    public int Pid => _pid;
    public bool HasExited => _hasExited || (_fallbackProcess?.HasExited ?? false);
    public int ExitCode => _fallbackProcess?.ExitCode ?? _exitCode;
    public Stream MasterStream => _masterStream;

    public static WindowsPtyProcess Start(
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
            var comSpec = Environment.GetEnvironmentVariable("ComSpec");
            shellPath = !string.IsNullOrWhiteSpace(comSpec) && File.Exists(comSpec) ? comSpec : "powershell.exe";
        }

        var cmdLine = shellPath;
        if (args != null && args.Length > 0)
        {
            cmdLine = $"{shellPath} {string.Join(" ", args)}";
        }

        try
        {
            return StartWithConPty(cols, rows, shellPath, cmdLine, workingDirectory, environment);
        }
        catch (Exception)
        {
            return StartFallback(shellPath, args, workingDirectory, environment);
        }
    }

    private static WindowsPtyProcess StartWithConPty(
        int cols,
        int rows,
        string shellPath,
        string cmdLine,
        string workingDirectory,
        IDictionary<string, string> environment)
    {
        if (!WindowsNative.CreatePipe(out var hPipeInRead, out var hPipeInWrite, IntPtr.Zero, 0) ||
            !WindowsNative.CreatePipe(out var hPipeOutRead, out var hPipeOutWrite, IntPtr.Zero, 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to create I/O pipes for ConPTY");
        }

        var coord = new COORD
        {
            X = (short)Math.Clamp(cols, 1, 1000),
            Y = (short)Math.Clamp(rows, 1, 1000)
        };

        var hr = WindowsNative.CreatePseudoConsole(coord, hPipeInRead, hPipeOutWrite, 0, out var hPC);
        if (hr != 0)
        {
            WindowsNative.CloseHandle(hPipeInRead);
            WindowsNative.CloseHandle(hPipeInWrite);
            WindowsNative.CloseHandle(hPipeOutRead);
            WindowsNative.CloseHandle(hPipeOutWrite);
            throw new Win32Exception(hr, "CreatePseudoConsole failed");
        }

        WindowsNative.CloseHandle(hPipeInRead);
        WindowsNative.CloseHandle(hPipeOutWrite);

        var lpSize = IntPtr.Zero;
        WindowsNative.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize);
        var lpAttributeList = Marshal.AllocHGlobal(lpSize);

        try
        {
            if (!WindowsNative.InitializeProcThreadAttributeList(lpAttributeList, 1, 0, ref lpSize))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "InitializeProcThreadAttributeList failed");
            }

            if (!WindowsNative.UpdateProcThreadAttribute(
                lpAttributeList,
                0,
                WindowsNative.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                hPC,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "UpdateProcThreadAttribute failed");
            }

            var siEx = default(STARTUPINFOEX);
            siEx.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            siEx.lpAttributeList = lpAttributeList;

            var workDir = !string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory)
                ? workingDirectory
                : null;

            if (!WindowsNative.CreateProcess(
                null,
                cmdLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                WindowsNative.EXTENDED_STARTUPINFO_PRESENT,
                IntPtr.Zero,
                workDir,
                ref siEx,
                out var pi))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateProcess with ConPTY failed");
            }

            var inSafeHandle = new SafeFileHandle(hPipeInWrite, ownsHandle: true);
            var outSafeHandle = new SafeFileHandle(hPipeOutRead, ownsHandle: true);
            var stream = new WindowsPtyStream(inSafeHandle, outSafeHandle);

            return new WindowsPtyProcess(pi.dwProcessId, pi.hProcess, pi.hThread, hPC, stream);
        }
        finally
        {
            if (lpAttributeList != IntPtr.Zero)
            {
                WindowsNative.DeleteProcThreadAttributeList(lpAttributeList);
                Marshal.FreeHGlobal(lpAttributeList);
            }
        }
    }

    private static WindowsPtyProcess StartFallback(
        string shellPath,
        string[] args,
        string workingDirectory,
        IDictionary<string, string> environment)
    {
        var psi = new ProcessStartInfo
        {
            FileName = shellPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (args != null)
        {
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        if (environment != null)
        {
            foreach (var (k, v) in environment)
            {
                psi.Environment[k] = v;
            }
        }

        var process = Process.Start(psi);
        return new WindowsPtyProcess(process);
    }

    public void Resize(int cols, int rows)
    {
        if (_disposed != 0)
        {
            return;
        }

        if (_hPC != IntPtr.Zero)
        {
            var coord = new COORD
            {
                X = (short)Math.Clamp(cols, 1, 1000),
                Y = (short)Math.Clamp(rows, 1, 1000)
            };

            WindowsNative.ResizePseudoConsole(_hPC, coord);
        }
    }

    public void Kill()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (_fallbackProcess != null)
            {
                if (!_fallbackProcess.HasExited)
                {
                    _fallbackProcess.Kill(entireProcessTree: true);
                    _fallbackProcess.WaitForExit(1000);
                }

                _fallbackProcess.Dispose();
            }
            else
            {
                if (_hPC != IntPtr.Zero)
                {
                    WindowsNative.ClosePseudoConsole(_hPC);
                }

                if (_hProcess != IntPtr.Zero)
                {
                    WindowsNative.TerminateProcess(_hProcess, 1);
                    WindowsNative.WaitForSingleObject(_hProcess, 1000);

                    if (WindowsNative.GetExitCodeProcess(_hProcess, out var code))
                    {
                        _exitCode = (int)code;
                    }

                    WindowsNative.CloseHandle(_hProcess);
                }

                if (_hThread != IntPtr.Zero)
                {
                    WindowsNative.CloseHandle(_hThread);
                }

                _hasExited = true;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error while killing Windows PTY process {0}", _pid);
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

        if (_fallbackProcess != null)
        {
            await _fallbackProcess.WaitForExitAsync(cancellationToken);
            _hasExited = true;
            return;
        }

        await Task.Run(() =>
        {
            if (_hProcess != IntPtr.Zero)
            {
                WindowsNative.WaitForSingleObject(_hProcess, WindowsNative.INFINITE);
                if (WindowsNative.GetExitCodeProcess(_hProcess, out var code))
                {
                    _exitCode = (int)code;
                }

                _hasExited = true;
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        Kill();
        GC.SuppressFinalize(this);
    }

    private sealed class BidirectionalStream : Stream
    {
        private readonly Stream _input;
        private readonly Stream _output;

        public BidirectionalStream(Stream input, Stream output)
        {
            _input = input;
            _output = output;
        }

        public override bool CanRead => _output.CanRead;
        public override bool CanWrite => _input.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _input.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => _output.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _output.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _output.ReadAsync(buffer, offset, count, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => _input.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _input.WriteAsync(buffer, cancellationToken);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _input.WriteAsync(buffer, offset, count, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _input?.Dispose();
                _output?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
