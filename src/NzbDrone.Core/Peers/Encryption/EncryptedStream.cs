using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Peers.Encryption;

public class EncryptedStream : Stream
{
    private readonly Stream _inner;
    private readonly Rc4StreamCipher _encryptor;
    private readonly Rc4StreamCipher _decryptor;
    private readonly bool _ownsStream;

    public EncryptedStream(Stream inner, Rc4StreamCipher encryptor, Rc4StreamCipher decryptor, bool ownsStream = true)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _encryptor = encryptor ?? throw new ArgumentNullException(nameof(encryptor));
        _decryptor = decryptor ?? throw new ArgumentNullException(nameof(decryptor));
        _ownsStream = ownsStream;
    }

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => _inner.CanWrite;

    public bool DataAvailable => (_inner is NetworkStream ns && ns.DataAvailable) ||
        (_inner is PrefixedStream ps && ps.DataAvailable) ||
        (_inner is EncryptedStream es && es.DataAvailable);

    public override long Length => _inner.CanSeek ? _inner.Length : throw new NotSupportedException("EncryptedStream does not support seeking");

    public override long Position
    {
        get => _inner.CanSeek ? _inner.Position : throw new NotSupportedException("EncryptedStream does not support seeking");
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        var bytesRead = _inner.Read(buffer);
        if (bytesRead > 0)
        {
            _decryptor.ProcessInPlace(buffer[..bytesRead]);
        }

        return bytesRead;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var bytesRead = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (bytesRead > 0)
        {
            _decryptor.ProcessInPlace(buffer.Span[..bytesRead]);
        }

        return bytesRead;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            _encryptor.Process(buffer, rented.AsSpan(0, buffer.Length));
            _inner.Write(rented.AsSpan(0, buffer.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public void WriteInPlace(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        _encryptor.ProcessInPlace(buffer);
        _inner.Write(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            _encryptor.Process(buffer.Span, rented.AsSpan(0, buffer.Length));
            await _inner.WriteAsync(rented.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public async ValueTask WriteInPlaceAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        _encryptor.ProcessInPlace(buffer.Span);
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
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
            await _inner.DisposeAsync().ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }
}
