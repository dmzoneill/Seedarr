using System;
using System.IO;
using NUnit.Framework;
using NzbDrone.Core.Peers.Encryption;

namespace NzbDrone.Core.Test.Peers.Encryption;

[TestFixture]
public class PrefixedStreamTest
{
    [Test]
    public void Read_should_return_prefix_bytes_first()
    {
        var prefix = new byte[] { 0xAA, 0xBB, 0xCC };
        using var inner = new MemoryStream(new byte[] { 0x01, 0x02 });
        using var stream = new PrefixedStream(prefix, inner);

        var buffer = new byte[3];
        var read = stream.Read(buffer, 0, 3);

        Assert.That(read, Is.EqualTo(3));
        Assert.That(buffer, Is.EqualTo(new byte[] { 0xAA, 0xBB, 0xCC }));
    }

    [Test]
    public void Read_should_return_inner_stream_bytes_after_prefix()
    {
        var prefix = new byte[] { 0xAA };
        var innerData = new byte[] { 0x01, 0x02, 0x03 };
        using var inner = new MemoryStream(innerData);
        using var stream = new PrefixedStream(prefix, inner);

        var buffer = new byte[1];
        stream.Read(buffer, 0, 1);

        var buffer2 = new byte[3];
        var read = stream.Read(buffer2, 0, 3);

        Assert.That(read, Is.EqualTo(3));
        Assert.That(buffer2, Is.EqualTo(new byte[] { 0x01, 0x02, 0x03 }));
    }

    [Test]
    public void Read_should_combine_prefix_and_inner_in_single_read()
    {
        var prefix = new byte[] { 0xAA, 0xBB };
        var innerData = new byte[] { 0x01, 0x02, 0x03 };
        using var inner = new MemoryStream(innerData);
        using var stream = new PrefixedStream(prefix, inner);

        var buffer = new byte[10];
        var read = stream.Read(buffer, 0, 10);

        Assert.That(read, Is.EqualTo(5));
        Assert.That(buffer[0], Is.EqualTo(0xAA));
        Assert.That(buffer[1], Is.EqualTo(0xBB));
        Assert.That(buffer[2], Is.EqualTo(0x01));
        Assert.That(buffer[3], Is.EqualTo(0x02));
        Assert.That(buffer[4], Is.EqualTo(0x03));
    }

    [Test]
    public void Read_should_return_only_prefix_when_count_is_less_than_prefix_length()
    {
        var prefix = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        using var inner = new MemoryStream(new byte[] { 0x01 });
        using var stream = new PrefixedStream(prefix, inner);

        var buffer = new byte[2];
        var read = stream.Read(buffer, 0, 2);

        Assert.That(read, Is.EqualTo(2));
        Assert.That(buffer, Is.EqualTo(new byte[] { 0xAA, 0xBB }));
    }

    [Test]
    public void Read_should_drain_prefix_across_multiple_reads()
    {
        var prefix = new byte[] { 0xAA, 0xBB, 0xCC };
        using var inner = new MemoryStream(new byte[] { 0x01 });
        using var stream = new PrefixedStream(prefix, inner);

        var buffer = new byte[1];
        var result = new byte[4];

        for (var i = 0; i < 4; i++)
        {
            stream.Read(buffer, 0, 1);
            result[i] = buffer[0];
        }

        Assert.That(result[0], Is.EqualTo(0xAA));
        Assert.That(result[1], Is.EqualTo(0xBB));
        Assert.That(result[2], Is.EqualTo(0xCC));
        Assert.That(result[3], Is.EqualTo(0x01));
    }

    [Test]
    public void Read_should_return_zero_when_both_prefix_and_inner_exhausted()
    {
        var prefix = new byte[] { 0xAA };
        using var inner = new MemoryStream(new byte[] { 0x01 });
        using var stream = new PrefixedStream(prefix, inner);

        var buffer = new byte[10];
        stream.Read(buffer, 0, 10);

        var read = stream.Read(buffer, 0, 10);

        Assert.That(read, Is.EqualTo(0));
    }

    [Test]
    public void Write_should_pass_through_to_inner_stream()
    {
        using var inner = new MemoryStream();
        using var stream = new PrefixedStream(new byte[] { 0xAA }, inner);

        var data = new byte[] { 0x01, 0x02, 0x03 };
        stream.Write(data, 0, data.Length);

        Assert.That(inner.ToArray(), Is.EqualTo(new byte[] { 0x01, 0x02, 0x03 }));
    }

    [Test]
    public void Flush_should_flush_inner_stream()
    {
        using var inner = new MemoryStream();
        using var stream = new PrefixedStream(new byte[] { 0xAA }, inner);

        stream.Write(new byte[] { 0x01 }, 0, 1);

        Assert.DoesNotThrow(() => stream.Flush());
    }

    [Test]
    public void CanSeek_should_return_false()
    {
        using var inner = new MemoryStream();
        using var stream = new PrefixedStream(Array.Empty<byte>(), inner);

        Assert.That(stream.CanSeek, Is.False);
    }

    [Test]
    public void Seek_should_throw_not_supported()
    {
        using var inner = new MemoryStream();
        using var stream = new PrefixedStream(Array.Empty<byte>(), inner);

        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
    }

    [Test]
    public void SetLength_should_throw_not_supported()
    {
        using var inner = new MemoryStream();
        using var stream = new PrefixedStream(Array.Empty<byte>(), inner);

        Assert.Throws<NotSupportedException>(() => stream.SetLength(100));
    }

    [Test]
    public void Position_set_should_throw_not_supported()
    {
        using var inner = new MemoryStream();
        using var stream = new PrefixedStream(Array.Empty<byte>(), inner);

        Assert.Throws<NotSupportedException>(() => stream.Position = 5);
    }

    [Test]
    public void CanRead_should_reflect_inner_stream()
    {
        using var inner = new MemoryStream(new byte[] { 0x01 });
        using var stream = new PrefixedStream(Array.Empty<byte>(), inner);

        Assert.That(stream.CanRead, Is.EqualTo(inner.CanRead));
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public void CanWrite_should_reflect_inner_stream()
    {
        using var inner = new MemoryStream();
        using var stream = new PrefixedStream(Array.Empty<byte>(), inner);

        Assert.That(stream.CanWrite, Is.EqualTo(inner.CanWrite));
        Assert.That(stream.CanWrite, Is.True);
    }

    [Test]
    public void Dispose_should_dispose_inner_stream()
    {
        var inner = new MemoryStream();
        var stream = new PrefixedStream(Array.Empty<byte>(), inner);

        stream.Dispose();

        Assert.Throws<ObjectDisposedException>(() => inner.Read(new byte[1], 0, 1));
    }
}
