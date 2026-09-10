using System;
using System.IO;
using System.Threading;

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
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (!_connection.IsConnected)
        {
            return 0;
        }

        return _connection.Receive(buffer, offset, count);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!_connection.IsConnected)
        {
            return;
        }

        var sent = _connection.Send(buffer, offset, count);
        var remaining = count - sent;
        var currentOffset = offset + sent;

        while (remaining > 0 && _connection.IsConnected)
        {
            var chunk = _connection.Send(buffer, currentOffset, remaining);
            if (chunk <= 0)
            {
                break;
            }

            remaining -= chunk;
            currentOffset += chunk;
        }
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
