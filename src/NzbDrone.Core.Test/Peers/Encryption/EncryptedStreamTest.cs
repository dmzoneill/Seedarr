using System;
using System.IO;
using NUnit.Framework;
using NzbDrone.Core.Peers.Encryption;

namespace NzbDrone.Core.Test.Peers.Encryption;

[TestFixture]
public class EncryptedStreamTest
{
    private static readonly byte[] TestKey = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };

    [Test]
    public void CanRead_should_reflect_inner_stream()
    {
        var inner = new MemoryStream();
        var enc = new Rc4StreamCipher(TestKey);
        var dec = new Rc4StreamCipher(TestKey);

        using var stream = new EncryptedStream(inner, enc, dec);

        Assert.That(stream.CanRead, Is.EqualTo(inner.CanRead));
    }

    [Test]
    public void CanSeek_should_return_false()
    {
        var inner = new MemoryStream();
        var enc = new Rc4StreamCipher(TestKey);
        var dec = new Rc4StreamCipher(TestKey);

        using var stream = new EncryptedStream(inner, enc, dec);

        Assert.That(stream.CanSeek, Is.False);
    }

    [Test]
    public void CanWrite_should_reflect_inner_stream()
    {
        var inner = new MemoryStream();
        var enc = new Rc4StreamCipher(TestKey);
        var dec = new Rc4StreamCipher(TestKey);

        using var stream = new EncryptedStream(inner, enc, dec);

        Assert.That(stream.CanWrite, Is.EqualTo(inner.CanWrite));
    }

    [Test]
    public void Seek_should_throw_not_supported()
    {
        var inner = new MemoryStream();
        var enc = new Rc4StreamCipher(TestKey);
        var dec = new Rc4StreamCipher(TestKey);

        using var stream = new EncryptedStream(inner, enc, dec);

        Assert.That(() => stream.Seek(0, SeekOrigin.Begin), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void SetLength_should_throw_not_supported()
    {
        var inner = new MemoryStream();
        var enc = new Rc4StreamCipher(TestKey);
        var dec = new Rc4StreamCipher(TestKey);

        using var stream = new EncryptedStream(inner, enc, dec);

        Assert.That(() => stream.SetLength(100), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Position_set_should_throw_not_supported()
    {
        var inner = new MemoryStream();
        var enc = new Rc4StreamCipher(TestKey);
        var dec = new Rc4StreamCipher(TestKey);

        using var stream = new EncryptedStream(inner, enc, dec);

        Assert.That(() => stream.Position = 10, Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Write_then_Read_should_roundtrip_data()
    {
        var plaintext = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0x57, 0x6F, 0x72, 0x6C, 0x64 };
        var inner = new MemoryStream();

        var writeEnc = new Rc4StreamCipher(TestKey, discard1024: true);
        var writeDec = new Rc4StreamCipher(TestKey, discard1024: true);
        using (var writeStream = new EncryptedStream(inner, writeEnc, writeDec, ownsStream: false))
        {
            writeStream.Write(plaintext, 0, plaintext.Length);
            writeStream.Flush();
        }

        inner.Position = 0;

        var readEnc = new Rc4StreamCipher(TestKey, discard1024: true);
        var readDec = new Rc4StreamCipher(TestKey, discard1024: true);
        using var readStream = new EncryptedStream(inner, readDec, readEnc, ownsStream: false);

        var buffer = new byte[plaintext.Length];
        var bytesRead = readStream.Read(buffer, 0, buffer.Length);

        Assert.That(bytesRead, Is.EqualTo(plaintext.Length));
        Assert.That(buffer, Is.EqualTo(plaintext));
    }

    [Test]
    public void Read_should_return_zero_for_empty_stream()
    {
        var inner = new MemoryStream();
        var enc = new Rc4StreamCipher(TestKey);
        var dec = new Rc4StreamCipher(TestKey);

        using var stream = new EncryptedStream(inner, enc, dec);
        var buffer = new byte[10];
        var bytesRead = stream.Read(buffer, 0, buffer.Length);

        Assert.That(bytesRead, Is.EqualTo(0));
    }

    [Test]
    public void Flush_should_flush_inner_stream()
    {
        var inner = new MemoryStream();
        var enc = new Rc4StreamCipher(TestKey);
        var dec = new Rc4StreamCipher(TestKey);

        using var stream = new EncryptedStream(inner, enc, dec);
        stream.Write(new byte[] { 0x01, 0x02 }, 0, 2);

        Assert.That(() => stream.Flush(), Throws.Nothing);
    }

    [Test]
    public void Dispose_should_dispose_inner_when_ownsStream_true()
    {
        var inner = new MemoryStream();
        var enc = new Rc4StreamCipher(TestKey);
        var dec = new Rc4StreamCipher(TestKey);

        var stream = new EncryptedStream(inner, enc, dec, ownsStream: true);
        stream.Dispose();

        Assert.That(() => inner.ReadByte(), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void Dispose_should_not_dispose_inner_when_ownsStream_false()
    {
        var inner = new MemoryStream(new byte[] { 0x01, 0x02, 0x03 });
        var enc = new Rc4StreamCipher(TestKey);
        var dec = new Rc4StreamCipher(TestKey);

        var stream = new EncryptedStream(inner, enc, dec, ownsStream: false);
        stream.Dispose();

        Assert.That(() => inner.ReadByte(), Throws.Nothing);
    }

    [Test]
    public void Write_should_encrypt_data_in_inner_stream()
    {
        var plaintext = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };
        var inner = new MemoryStream();
        var enc = new Rc4StreamCipher(TestKey);
        var dec = new Rc4StreamCipher(TestKey);

        using var stream = new EncryptedStream(inner, enc, dec, ownsStream: false);
        stream.Write(plaintext, 0, plaintext.Length);
        stream.Flush();

        var innerBytes = inner.ToArray();
        Assert.That(innerBytes, Has.Length.EqualTo(plaintext.Length));
        Assert.That(innerBytes, Is.Not.EqualTo(plaintext));
    }

    [Test]
    public void Length_should_reflect_inner_stream_length()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var inner = new MemoryStream();
        var enc = new Rc4StreamCipher(TestKey);
        var dec = new Rc4StreamCipher(TestKey);

        using var stream = new EncryptedStream(inner, enc, dec);
        stream.Write(data, 0, data.Length);

        Assert.That(stream.Length, Is.EqualTo(data.Length));
    }
}
