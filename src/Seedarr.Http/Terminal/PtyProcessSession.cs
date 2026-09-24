// Copyright (c) PlaceholderCompany. All rights reserved.

#pragma warning disable SX1309

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Seedarr.Http.Terminal;

public sealed class PtyProcessSession : ITerminalSession
{
    private const string PythonPtyScript = @"import os, pty, struct, fcntl, termios, sys, select, signal

cwd = sys.argv[1] if len(sys.argv) > 1 else '/tmp'
cols = int(sys.argv[2]) if len(sys.argv) > 2 else 80
rows = int(sys.argv[3]) if len(sys.argv) > 3 else 24
ctrl_pipe = sys.argv[4] if len(sys.argv) > 4 else None

pid, master = pty.fork()
if pid == 0:
    try:
        os.chdir(cwd)
    except:
        pass
    os.environ['TERM'] = 'xterm-256color'
    os.environ['COLORTERM'] = 'truecolor'
    if 'LANG' not in os.environ:
        os.environ['LANG'] = 'en_US.UTF-8'
    os.environ['PS1'] = '\\[\\e[1;33m\\]\\u@seedarr\\[\\e[0m\\]:\\[\\e[1;34m\\]\\w\\[\\e[0m\\]\\$ '
    shell_bin = '/bin/bash' if os.path.exists('/bin/bash') else '/bin/sh'
    shell_name = os.path.basename(shell_bin)
    if os.path.exists('/bin/bash'):
        os.execlp('/bin/bash', 'bash', '--noprofile', '--norc', '-i')
    else:
        os.execlp('/bin/sh', 'sh', '-i')
else:
    winsize = struct.pack('HHHH', rows, cols, 0, 0)
    try:
        fcntl.ioctl(master, termios.TIOCSWINSZ, winsize)
    except:
        pass

    ctrl_fd = None
    if ctrl_pipe and os.path.exists(ctrl_pipe):
        try:
            ctrl_fd = os.open(ctrl_pipe, os.O_RDWR | os.O_NONBLOCK)
        except:
            ctrl_fd = None

    rfds = [0, master]
    if ctrl_fd is not None:
        rfds.append(ctrl_fd)

    try:
        while True:
            try:
                r, _, _ = select.select(rfds, [], [])
            except (InterruptedError, select.error):
                continue
            if 0 in r:
                try:
                    data = os.read(0, 4096)
                    if not data:
                        break
                    os.write(master, data)
                except OSError:
                    break
            if master in r:
                try:
                    data = os.read(master, 4096)
                    if not data:
                        break
                    os.write(1, data)
                except OSError:
                    break
            if ctrl_fd is not None and ctrl_fd in r:
                try:
                    ctrl_data = os.read(ctrl_fd, 512).decode('utf-8', errors='ignore')
                    if ctrl_data:
                        for line in ctrl_data.strip().split('\n'):
                            line = line.strip()
                            if ':' in line:
                                parts = line.split(':')
                                r_rows, r_cols = int(parts[0]), int(parts[1])
                                winsize = struct.pack('HHHH', r_rows, r_cols, 0, 0)
                                try:
                                    fcntl.ioctl(master, termios.TIOCSWINSZ, winsize)
                                except:
                                    pass
                                try:
                                    os.kill(pid, signal.SIGWINCH)
                                except:
                                    pass
                except:
                    pass
    finally:
        try:
            os.close(master)
        except OSError:
            pass
        if ctrl_fd is not None:
            try:
                os.close(ctrl_fd)
            except OSError:
                pass
        try:
            os.kill(pid, signal.SIGHUP)
        except OSError:
            pass
        try:
            os.waitpid(pid, 0)
        except OSError:
            pass
";

    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly Process _process;
    private readonly Stream _inputStream;
    private readonly Stream _outputStream;
    private readonly string _controlPipePath;
    private FileStream _controlPipeStream;
    private int _disposed;

    public int ProcessId => this._process.Id;

    public bool IsActive => this._disposed == 0 && !this._process.HasExited;

    private PtyProcessSession(Process process, string controlPipePath = null, FileStream controlPipeStream = null)
    {
        this._process = process;
        this._inputStream = process.StandardInput.BaseStream;
        this._outputStream = process.StandardOutput.BaseStream;
        this._controlPipePath = controlPipePath;
        this._controlPipeStream = controlPipeStream;
    }

    public static PtyProcessSession Start(string cwd, int cols, int rows)
    {
        var safeCwd = !string.IsNullOrWhiteSpace(cwd) && Directory.Exists(cwd) ? cwd : "/tmp";
        string controlPipePath = null;
        FileStream controlPipeStream = null;
        Process proc = null;

        try
        {
            ProcessStartInfo startInfo;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && (File.Exists("/usr/bin/python3") || File.Exists("/bin/python3")))
            {
                var pyBinary = File.Exists("/usr/bin/python3") ? "/usr/bin/python3" : "/bin/python3";
                controlPipePath = Path.Combine(Path.GetTempPath(), $"seedarr_pty_ctrl_{Guid.NewGuid():N}.pipe");
                CreateFifo(controlPipePath);

                var b64Script = Convert.ToBase64String(Encoding.UTF8.GetBytes(PythonPtyScript));
                var pyCommand = $"import base64; exec(base64.b64decode('{b64Script}'))";

                startInfo = new ProcessStartInfo
                {
                    FileName = pyBinary,
                    Arguments = $"-u -c \"{pyCommand}\" \"{safeCwd}\" {cols} {rows} \"{controlPipePath}\"",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = safeCwd,
                };
            }
            else
            {
                var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                var shell = isWindows ? "powershell.exe" : (File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh");
                var args = isWindows ? "-NoLogo" : "-i";

                startInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = args,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = safeCwd,
                };
            }

            TerminalEnvironmentSanitizer.Sanitize(startInfo);

            proc = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to launch terminal process");

            if (controlPipePath != null && File.Exists(controlPipePath))
            {
                try
                {
                    controlPipeStream = new FileStream(controlPipePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
                }
                catch
                {
                    controlPipeStream = null;
                }
            }

            return new PtyProcessSession(proc, controlPipePath, controlPipeStream);
        }
        catch
        {
            if (controlPipeStream != null)
            {
                try
                {
                    controlPipeStream.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to dispose control pipe stream during terminal session cleanup");
                }
            }

            if (controlPipePath != null && File.Exists(controlPipePath))
            {
                try
                {
                    File.Delete(controlPipePath);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to delete control pipe path {0} during terminal session cleanup", controlPipePath);
                }
            }

            if (proc != null)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                    }

                    proc.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to terminate process during terminal session cleanup");
                }
            }

            throw;
        }
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (this._disposed != 0 || this._process.HasExited)
        {
            return 0;
        }

        try
        {
            return await this._outputStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return 0;
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (this._disposed != 0 || this._process.HasExited)
        {
            return;
        }

        try
        {
            await this._inputStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            await this._inputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Shell closed
        }
    }

    public void Resize(int cols, int rows)
    {
        if (this._disposed != 0 || this._process.HasExited)
        {
            return;
        }

        if (this._controlPipeStream != null)
        {
            try
            {
                var msg = Encoding.UTF8.GetBytes($"{rows}:{cols}\n");
                this._controlPipeStream.Write(msg, 0, msg.Length);
                this._controlPipeStream.Flush();
            }
            catch
            {
                // Shell or control channel closed
            }
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
            if (this._controlPipeStream != null)
            {
                try
                {
                    this._controlPipeStream.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to dispose control pipe stream for PID {0}", this.ProcessId);
                }

                this._controlPipeStream = null;
            }

            if (this._controlPipePath != null && File.Exists(this._controlPipePath))
            {
                try
                {
                    File.Delete(this._controlPipePath);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to delete control pipe path {0}", this._controlPipePath);
                }
            }

            if (!this._process.HasExited)
            {
                this._process.Kill(entireProcessTree: true);
            }

            this._process.Dispose();
        }
        catch
        {
            // Ignored on teardown
        }
    }

    public ValueTask DisposeAsync()
    {
        this.Kill();
        return ValueTask.CompletedTask;
    }

    internal static void CreateFifo(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (MkFifo(path, 384 /* 0600 */) == 0)
                {
                    return;
                }
            }
        }
        catch
        {
            // Fallback to mkfifo process
        }

        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "mkfifo",
                Arguments = $"-m 0600 \"{path}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            proc?.WaitForExit(1000);
        }
        catch
        {
            // Ignored
        }
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string path, uint mode);
}
