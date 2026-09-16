using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Transport;

public class UtpStream : Stream
{
    private readonly IUtpConnection _connection;
    private int _readTimeout = Timeout.Infinite;
    private int _writeTimeout = Timeout.Infinite;

    public UtpStream(IUtpConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public override bool CanRead => _connection.IsConnected;
    public override bool CanSeek => false;
    public override bool CanWrite => _connection.IsConnected;
    public override bool CanTimeout => true;

    public override int ReadTimeout
    {
        get => _readTimeout;
        set => _readTimeout = value;
    }

    public override int WriteTimeout
    {
        get => _writeTimeout;
        set => _writeTimeout = value;
    }

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        _connection.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => Flush(), cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset + count > buffer.Length)
        {
            throw new ArgumentException("Offset and count exceed buffer length.");
        }

        if (!_connection.IsConnected || count == 0)
        {
            return 0;
        }

        if (_readTimeout > 0)
        {
            var startTime = Environment.TickCount64;
            var readTask = Task.Run(() => _connection.Receive(buffer, offset, count));
            if (!readTask.Wait(_readTimeout))
            {
                throw new TimeoutException($"Read timed out after {_readTimeout} ms.");
            }

            var bytesRead = readTask.Result;
            if (bytesRead == 0 && _connection.IsConnected && (Environment.TickCount64 - startTime) >= _readTimeout)
            {
                throw new TimeoutException($"Read timed out after {_readTimeout} ms.");
            }

            return bytesRead;
        }

        return _connection.Receive(buffer, offset, count);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_connection.IsConnected || count == 0)
        {
            return Task.FromResult(0);
        }

        return Task.Run(() => Read(buffer, offset, count), cancellationToken);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_connection.IsConnected || buffer.IsEmpty)
        {
            return ValueTask.FromResult(0);
        }

        var array = System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
        return new ValueTask<int>(Task.Run(
            () =>
            {
                try
                {
                    var bytesRead = Read(array, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        new ReadOnlySpan<byte>(array, 0, bytesRead).CopyTo(buffer.Span);
                    }

                    return bytesRead;
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(array);
                }
            },
            cancellationToken));
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset + count > buffer.Length)
        {
            throw new ArgumentException("Offset and count exceed buffer length.");
        }

        if (count == 0)
        {
            return;
        }

        if (!_connection.IsConnected)
        {
            throw new IOException("uTP connection is not connected.");
        }

        var startTime = Environment.TickCount64;
        var remaining = count;
        var currentOffset = offset;

        while (remaining > 0)
        {
            if (!_connection.IsConnected)
            {
                throw new IOException($"uTP connection closed after writing {count - remaining} of {count} bytes.");
            }

            if (_writeTimeout > 0 && (Environment.TickCount64 - startTime) > _writeTimeout)
            {
                throw new TimeoutException($"Write timed out after {_writeTimeout} ms with {remaining} bytes remaining.");
            }

            var chunk = _connection.Send(buffer, currentOffset, remaining);
            if (chunk <= 0)
            {
                throw new IOException($"uTP connection closed after writing {count - remaining} of {count} bytes.");
            }

            remaining -= chunk;
            currentOffset += chunk;
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_connection.IsConnected)
        {
            throw new IOException("uTP connection is not connected.");
        }

        if (count == 0)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() => Write(buffer, offset, count), cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_connection.IsConnected)
        {
            throw new IOException("uTP connection is not connected.");
        }

        if (buffer.IsEmpty)
        {
            return ValueTask.CompletedTask;
        }

        var array = buffer.ToArray();
        return new ValueTask(Task.Run(() => Write(array, 0, array.Length), cancellationToken));
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection.Dispose();
        }

        base.Dispose(disposing);
    }
}
