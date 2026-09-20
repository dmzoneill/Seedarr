using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Peers.Encryption;

public class PrefixedStream : Stream
{
    private readonly byte[] _prefix;
    private readonly Stream _inner;
    private readonly bool _ownsStream;
    private int _prefixOffset;

    public PrefixedStream(byte[] prefix, Stream inner, bool ownsStream = true)
    {
        _prefix = prefix ?? Array.Empty<byte>();
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _ownsStream = ownsStream;
        _prefixOffset = 0;
    }

    public PrefixedStream(Stream inner, byte[] prefix, bool ownsStream = true)
        : this(prefix, inner, ownsStream)
    {
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;

    public bool DataAvailable => (_prefix != null && _prefixOffset < _prefix.Length) ||
        (_inner is NetworkStream ns && ns.DataAvailable) ||
        (_inner is PrefixedStream ps && ps.DataAvailable);

    public override long Length => CanSeek
        ? (_inner.Length + Math.Max(0, _prefix.Length - _prefixOffset))
        : throw new NotSupportedException("PrefixedStream does not support seeking");

    public override long Position
    {
        get => CanSeek
            ? Math.Max(0, _inner.Position - (_prefix.Length - _prefixOffset))
            : throw new NotSupportedException("PrefixedStream does not support seeking");
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var totalRead = 0;

        // Drain prefix first
        if (_prefixOffset < _prefix.Length)
        {
            var prefixAvailable = _prefix.Length - _prefixOffset;
            var toCopy = Math.Min(count, prefixAvailable);
            Array.Copy(_prefix, _prefixOffset, buffer, offset, toCopy);
            _prefixOffset += toCopy;
            totalRead += toCopy;
            offset += toCopy;
            count -= toCopy;
        }

        if (count > 0)
        {
            var innerRead = _inner.Read(buffer, offset, count);
            totalRead += innerRead;
        }

        return totalRead;
    }

    public override int Read(Span<byte> buffer)
    {
        var totalRead = 0;

        if (_prefixOffset < _prefix.Length)
        {
            var prefixAvailable = _prefix.Length - _prefixOffset;
            var toCopy = Math.Min(buffer.Length, prefixAvailable);
            _prefix.AsSpan(_prefixOffset, toCopy).CopyTo(buffer);
            _prefixOffset += toCopy;
            totalRead += toCopy;
            buffer = buffer[toCopy..];
        }

        if (!buffer.IsEmpty)
        {
            var innerRead = _inner.Read(buffer);
            totalRead += innerRead;
        }

        return totalRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var totalRead = 0;

        if (_prefixOffset < _prefix.Length)
        {
            var available = _prefix.Length - _prefixOffset;
            var toCopy = Math.Min(buffer.Length, available);
            _prefix.AsSpan(_prefixOffset, toCopy).CopyTo(buffer.Span);
            _prefixOffset += toCopy;
            totalRead += toCopy;
            buffer = buffer[toCopy..];
        }

        if (!buffer.IsEmpty)
        {
            var innerRead = await _inner.ReadAsync(buffer, cancellationToken);
            totalRead += innerRead;
        }

        return totalRead;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _inner.Write(buffer);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return _inner.WriteAsync(buffer, cancellationToken);
    }

    public override void Flush()
    {
        _inner.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _inner.FlushAsync(cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsStream)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_ownsStream)
        {
            await _inner.DisposeAsync();
        }

        await base.DisposeAsync();
    }
}
