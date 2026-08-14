using System;
using System.IO;

namespace NzbDrone.Core.Peers.Encryption;

public class PrefixedStream : Stream
{
    private readonly byte[] _prefix;
    private readonly Stream _inner;
    private int _prefixOffset;

    public PrefixedStream(byte[] prefix, Stream inner)
    {
        _prefix = prefix;
        _inner = inner;
        _prefixOffset = 0;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
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

    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
    }

    public override void Flush()
    {
        _inner.Flush();
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
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
