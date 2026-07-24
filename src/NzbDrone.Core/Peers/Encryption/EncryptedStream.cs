using System;
using System.IO;

namespace NzbDrone.Core.Peers.Encryption;

public class EncryptedStream : Stream
{
    private readonly Stream _inner;
    private readonly Rc4StreamCipher _encryptor;
    private readonly Rc4StreamCipher _decryptor;
    private readonly bool _ownsStream;

    public EncryptedStream(Stream inner, Rc4StreamCipher encryptor, Rc4StreamCipher decryptor, bool ownsStream = true)
    {
        _inner = inner;
        _encryptor = encryptor;
        _decryptor = decryptor;
        _ownsStream = ownsStream;
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
        var bytesRead = _inner.Read(buffer, offset, count);
        if (bytesRead > 0)
        {
            _decryptor.ProcessInPlace(buffer, offset, bytesRead);
        }

        return bytesRead;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        var encrypted = new byte[count];
        _encryptor.Process(buffer, offset, count, encrypted, 0);
        _inner.Write(encrypted, 0, count);
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
        if (disposing && _ownsStream)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
