#pragma warning disable SA1117
#pragma warning disable IDE0007
#pragma warning disable CA1844
#pragma warning disable CA1835

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Terminal;

public class PosixPtyStream : Stream
{
    private readonly int _fd;
    private int _disposed;

    public PosixPtyStream(int fd)
    {
        _fd = fd;
    }

    public int FileDescriptor => _fd;

    public override bool CanRead => _disposed == 0;
    public override bool CanWrite => _disposed == 0;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
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
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset + count > buffer.Length)
        {
            throw new ArgumentException("Offset and length were out of bounds for the array.");
        }

        if (count == 0)
        {
            return 0;
        }

        var temp = (offset == 0 && count == buffer.Length) ? buffer : new byte[count];
        var bytesRead = PosixNative.read(_fd, temp, (nuint)count);
        if (bytesRead < 0)
        {
            var err = Marshal.GetLastPInvokeError();
            if (err == PosixNative.EIO)
            {
                return 0;
            }

            throw new IOException($"PTY read failed with errno {err}");
        }

        if (bytesRead > 0 && temp != buffer)
        {
            Array.Copy(temp, 0, buffer, offset, (int)bytesRead);
        }

        return (int)bytesRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (buffer.Length == 0)
        {
            return 0;
        }

        return await Task.Run(() =>
        {
            var pfd = new PollFd
            {
                fd = _fd,
                events = PosixNative.POLLIN | PosixNative.POLLHUP | PosixNative.POLLERR,
                revents = 0
            };

            while (!cancellationToken.IsCancellationRequested && _disposed == 0)
            {
                var pollRes = PosixNative.poll(ref pfd, 1, 150);
                if (pollRes < 0)
                {
                    var err = Marshal.GetLastPInvokeError();
                    if (err == PosixNative.EINTR)
                    {
                        continue;
                    }

                    throw new IOException($"PTY poll failed with errno {err}");
                }

                if (pollRes > 0)
                {
                    var temp = new byte[buffer.Length];
                    var bytesRead = PosixNative.read(_fd, temp, (nuint)temp.Length);
                    if (bytesRead < 0)
                    {
                        var err = Marshal.GetLastPInvokeError();
                        if (err == PosixNative.EIO)
                        {
                            return 0;
                        }

                        if (err == PosixNative.EINTR)
                        {
                            continue;
                        }

                        throw new IOException($"PTY read failed with errno {err}");
                    }

                    if (bytesRead == 0)
                    {
                        return 0;
                    }

                    temp.AsSpan(0, (int)bytesRead).CopyTo(buffer.Span);
                    return (int)bytesRead;
                }
            }

            return 0;
        }, cancellationToken);
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset + count > buffer.Length)
        {
            throw new ArgumentException("Offset and length were out of bounds for the array.");
        }

        return await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset + count > buffer.Length)
        {
            throw new ArgumentException("Offset and length were out of bounds for the array.");
        }

        if (count == 0)
        {
            return;
        }

        var temp = (offset == 0 && count == buffer.Length) ? buffer : new byte[count];
        if (temp != buffer)
        {
            Array.Copy(buffer, offset, temp, 0, count);
        }

        var written = 0;
        while (written < count)
        {
            var chunk = (written == 0 && count == temp.Length) ? temp : new byte[count - written];
            if (chunk != temp)
            {
                Array.Copy(temp, written, chunk, 0, count - written);
            }

            var res = PosixNative.write(_fd, chunk, (nuint)chunk.Length);
            if (res < 0)
            {
                var err = Marshal.GetLastPInvokeError();
                if (err == PosixNative.EINTR)
                {
                    continue;
                }

                if (err == PosixNative.EPIPE || err == PosixNative.EIO)
                {
                    return;
                }

                throw new IOException($"PTY write failed with errno {err}");
            }

            written += (int)res;
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            var arr = buffer.ToArray();
            Write(arr, 0, arr.Length);
        }, cancellationToken);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset + count > buffer.Length)
        {
            throw new ArgumentException("Offset and length were out of bounds for the array.");
        }

        await WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            PosixNative.close(_fd);
        }

        base.Dispose(disposing);
    }
}
