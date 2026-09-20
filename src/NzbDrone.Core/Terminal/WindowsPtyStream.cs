#pragma warning disable SA1117
#pragma warning disable IDE0007
#pragma warning disable CA1844
#pragma warning disable CA1835

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace NzbDrone.Core.Terminal;

public class WindowsPtyStream : Stream
{
    private readonly FileStream _inputStream;
    private readonly FileStream _outputStream;
    private int _disposed;

    public WindowsPtyStream(SafeFileHandle inputHandle, SafeFileHandle outputHandle)
    {
        _inputStream = new FileStream(inputHandle, FileAccess.Write, 4096, isAsync: false);
        _outputStream = new FileStream(outputHandle, FileAccess.Read, 4096, isAsync: false);
    }

    public override bool CanRead => _disposed == 0 && _outputStream.CanRead;
    public override bool CanWrite => _disposed == 0 && _inputStream.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        _inputStream.Flush();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        try
        {
            return _outputStream.Read(buffer, offset, count);
        }
        catch (IOException)
        {
            return 0; // Pipe broken / closed
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        try
        {
            return await _outputStream.ReadAsync(buffer, cancellationToken);
        }
        catch (IOException)
        {
            return 0;
        }
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        try
        {
            _inputStream.Write(buffer, offset, count);
            _inputStream.Flush();
        }
        catch (IOException)
        {
            // Ignore if child closed
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        try
        {
            await _inputStream.WriteAsync(buffer, cancellationToken);
            await _inputStream.FlushAsync(cancellationToken);
        }
        catch (IOException)
        {
        }
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _inputStream?.Dispose();
            _outputStream?.Dispose();
        }

        base.Dispose(disposing);
    }
}
