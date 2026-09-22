// Copyright (c) PlaceholderCompany. All rights reserved.

#pragma warning disable SX1309
#pragma warning disable CA1835

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Seedarr.Http.Terminal;

public sealed class FallbackProcessSession : ITerminalSession
{
    private readonly Process _process;
    private readonly Stream _inputStream;
    private readonly Channel<byte[]> _outputChannel;
    private readonly CancellationTokenSource _sessionCts = new();
    private int _activePumps = 2;
    private byte[] _pendingChunk;
    private int _pendingOffset;
    private int _disposed;

    public int ProcessId => this._process.Id;

    public bool IsActive => this._disposed == 0 && !this._process.HasExited;

    private FallbackProcessSession(Process process)
    {
        this._process = process;
        this._inputStream = process.StandardInput.BaseStream;
        this._outputChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true,
        });

        _ = this.PumpStreamAsync(process.StandardOutput.BaseStream, this._sessionCts.Token);
        _ = this.PumpStreamAsync(process.StandardError.BaseStream, this._sessionCts.Token);
    }

    public static FallbackProcessSession Start(string cwd, int cols, int rows)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var shell = isWindows ? "powershell.exe" : (File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh");
        var args = isWindows ? "-NoLogo" : (File.Exists("/bin/bash") ? "--noprofile --norc -i" : "-i");

        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = args,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(cwd) && Directory.Exists(cwd))
        {
            startInfo.WorkingDirectory = cwd;
        }

        TerminalEnvironmentSanitizer.Sanitize(startInfo);

        var proc = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch fallback terminal process");

        return new FallbackProcessSession(proc);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (this._disposed != 0)
        {
            return 0;
        }

        try
        {
            while (this._pendingChunk == null || this._pendingOffset >= this._pendingChunk.Length)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this._sessionCts.Token);
                if (!await this._outputChannel.Reader.WaitToReadAsync(linkedCts.Token).ConfigureAwait(false))
                {
                    return 0;
                }

                if (this._outputChannel.Reader.TryRead(out var chunk))
                {
                    this._pendingChunk = chunk;
                    this._pendingOffset = 0;
                }
            }

            int toCopy = Math.Min(buffer.Length, this._pendingChunk.Length - this._pendingOffset);
            this._pendingChunk.AsMemory(this._pendingOffset, toCopy).CopyTo(buffer);
            this._pendingOffset += toCopy;
            return toCopy;
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
            // Shell pipe broken
        }
    }

    public void Resize(int cols, int rows)
    {
        // Standard process streams do not support TIOCSWINSZ
    }

    public void Kill()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) != 0)
        {
            return;
        }

        try
        {
            this._sessionCts.Cancel();
            this._outputChannel.Writer.TryComplete();

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

    private async Task PumpStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    break;
                }

                var chunk = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRead);
                await this._outputChannel.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Stream closed or cancelled
        }
        finally
        {
            if (Interlocked.Decrement(ref this._activePumps) == 0)
            {
                this._outputChannel.Writer.TryComplete();
            }
        }
    }
}
