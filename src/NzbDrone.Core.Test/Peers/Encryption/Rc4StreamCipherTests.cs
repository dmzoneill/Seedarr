using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Peers.Encryption;

namespace NzbDrone.Core.Test.Peers.Encryption;

[TestFixture]
public class Rc4StreamCipherTests
{
    private static readonly byte[] TestKey = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };

    [Test]
    public void Rfc_test_vector_1()
    {
        var key = Encoding.ASCII.GetBytes("Key");
        var plaintext = Encoding.ASCII.GetBytes("Plaintext");
        var expectedCiphertext = new byte[] { 0xBB, 0xF3, 0x16, 0xE8, 0xD9, 0x40, 0xAF, 0x0A, 0xD3 };

        var cipher = new Rc4StreamCipher(key, discard1024: false);
        var actual = cipher.Process(plaintext);

        Assert.That(actual, Is.EqualTo(expectedCiphertext));
    }

    [Test]
    public void Rfc_test_vector_2()
    {
        var key = Encoding.ASCII.GetBytes("Wiki");
        var plaintext = Encoding.ASCII.GetBytes("pedia");
        var expectedCiphertext = new byte[] { 0x10, 0x21, 0xBF, 0x04, 0x20 };

        var cipher = new Rc4StreamCipher(key, discard1024: false);
        var actual = cipher.Process(plaintext);

        Assert.That(actual, Is.EqualTo(expectedCiphertext));
    }

    [Test]
    public void Rfc_test_vector_3()
    {
        var key = Encoding.ASCII.GetBytes("Secret");
        var plaintext = Encoding.ASCII.GetBytes("Attack at dawn");
        var expectedCiphertext = new byte[]
        {
            0x45, 0xA0, 0x1F, 0x64, 0x5F, 0xC3, 0x5B, 0x38, 0x35, 0x52, 0x54, 0x4B, 0x9B, 0xF5
        };

        var cipher = new Rc4StreamCipher(key, discard1024: false);
        var actual = cipher.Process(plaintext);

        Assert.That(actual, Is.EqualTo(expectedCiphertext));
    }

    [Test]
    public void Rfc_test_vector_4_eight_zero_bytes()
    {
        var key = new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF };
        var plaintext = new byte[8];
        var expectedCiphertext = new byte[] { 0x74, 0x94, 0xC2, 0xE7, 0x10, 0x4B, 0x08, 0x79 };

        var cipher = new Rc4StreamCipher(key, discard1024: false);
        var actual = cipher.Process(plaintext);

        Assert.That(actual, Is.EqualTo(expectedCiphertext));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(15)]
    [TestCase(16)]
    [TestCase(17)]
    [TestCase(1024)]
    [TestCase(16384)]
    public void ProcessInPlace_matches_Process_across_various_lengths(int length)
    {
        var key = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
        var input = new byte[length];
        new Random(length + 1).NextBytes(input);

        var cipherOut = new Rc4StreamCipher(key, discard1024: false);
        var outExpected = new byte[length];
        cipherOut.Process(input.AsSpan(), outExpected.AsSpan());

        var cipherInPlace = new Rc4StreamCipher(key, discard1024: false);
        var bufferInPlace = (byte[])input.Clone();
        cipherInPlace.ProcessInPlace(bufferInPlace.AsSpan());

        Assert.That(bufferInPlace, Is.EqualTo(outExpected));
    }

    [Test]
    public void Encryption_decryption_roundtrip_restores_exact_plaintext()
    {
        var key = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE, 0x01, 0x02, 0x03, 0x04 };
        var plaintext = new byte[8192];
        new Random(12345).NextBytes(plaintext);

        var encryptor = new Rc4StreamCipher(key, discard1024: true);
        var decryptor = new Rc4StreamCipher(key, discard1024: true);

        var ciphertext = new byte[plaintext.Length];
        encryptor.Process(plaintext.AsSpan(), ciphertext.AsSpan());

        Assert.That(ciphertext, Is.Not.EqualTo(plaintext));

        var decrypted = new byte[plaintext.Length];
        decryptor.Process(ciphertext.AsSpan(), decrypted.AsSpan());

        Assert.That(decrypted, Is.EqualTo(plaintext));
    }

    [Test]
    public void ProcessInPlace_roundtrip_restores_exact_plaintext()
    {
        var key = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x55, 0xAA, 0x77, 0x88 };
        var buffer = new byte[4096];
        new Random(6789).NextBytes(buffer);
        var original = (byte[])buffer.Clone();

        var encryptor = new Rc4StreamCipher(key, discard1024: true);
        var decryptor = new Rc4StreamCipher(key, discard1024: true);

        encryptor.ProcessInPlace(buffer.AsSpan());
        Assert.That(buffer, Is.Not.EqualTo(original));

        decryptor.ProcessInPlace(buffer.AsSpan());
        Assert.That(buffer, Is.EqualTo(original));
    }

    [Test]
    public void Discard_1024_produces_expected_keystream_offset()
    {
        var key = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var plaintext = Encoding.ASCII.GetBytes("BitTorrent Encryption Protocol Message Stream Encryption");

        var cipherDiscardMethod = new Rc4StreamCipher(key, discard1024: false);
        cipherDiscardMethod.Discard(1024);

        var cipherConstructorDiscard = new Rc4StreamCipher(key, discard1024: true);

        var cipherProcessDummy = new Rc4StreamCipher(key, discard1024: false);
        var dummy = new byte[1024];
        cipherProcessDummy.ProcessInPlace(dummy.AsSpan());

        var ciphertext1 = cipherDiscardMethod.Process(plaintext);
        var ciphertext2 = cipherConstructorDiscard.Process(plaintext);
        var ciphertext3 = cipherProcessDummy.Process(plaintext);

        Assert.That(ciphertext1, Is.EqualTo(ciphertext2));
        Assert.That(ciphertext1, Is.EqualTo(ciphertext3));
    }

    [Test]
    public void EncryptedStream_sync_read_write_roundtrip()
    {
        var inner = new MemoryStream();
        var enc = new Rc4StreamCipher(TestKey, discard1024: true);
        var dec = new Rc4StreamCipher(TestKey, discard1024: true);

        var plaintext = new byte[2048];
        new Random(42).NextBytes(plaintext);

        using (var writeStream = new EncryptedStream(inner, enc, dec, ownsStream: false))
        {
            writeStream.Write(plaintext.AsSpan());
            writeStream.Flush();
        }

        inner.Position = 0;

        var readEnc = new Rc4StreamCipher(TestKey, discard1024: true);
        var readDec = new Rc4StreamCipher(TestKey, discard1024: true);
        using (var readStream = new EncryptedStream(inner, readEnc, readDec, ownsStream: false))
        {
            var readBuffer = new byte[plaintext.Length];
            var bytesRead = readStream.Read(readBuffer.AsSpan());

            Assert.That(bytesRead, Is.EqualTo(plaintext.Length));
            Assert.That(readBuffer, Is.EqualTo(plaintext));
        }
    }

    [Test]
    public async Task EncryptedStream_async_read_write_roundtrip()
    {
        var inner = new MemoryStream();
        var enc = new Rc4StreamCipher(TestKey, discard1024: true);
        var dec = new Rc4StreamCipher(TestKey, discard1024: true);

        var plaintext = new byte[4096];
        new Random(99).NextBytes(plaintext);

        using (var writeStream = new EncryptedStream(inner, enc, dec, ownsStream: false))
        {
            await writeStream.WriteAsync(plaintext.AsMemory());
            await writeStream.FlushAsync();
        }

        inner.Position = 0;

        var readEnc = new Rc4StreamCipher(TestKey, discard1024: true);
        var readDec = new Rc4StreamCipher(TestKey, discard1024: true);
        using (var readStream = new EncryptedStream(inner, readEnc, readDec, ownsStream: false))
        {
            var readBuffer = new byte[plaintext.Length];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory());

            Assert.That(bytesRead, Is.EqualTo(plaintext.Length));
            Assert.That(readBuffer, Is.EqualTo(plaintext));
        }
    }

    [Test]
    public void EncryptedStream_write_in_place_roundtrip()
    {
        var inner = new MemoryStream();
        var enc = new Rc4StreamCipher(TestKey, discard1024: true);
        var dec = new Rc4StreamCipher(TestKey, discard1024: true);

        var plaintext = new byte[1024];
        new Random(77).NextBytes(plaintext);
        var writeBuffer = (byte[])plaintext.Clone();

        using (var writeStream = new EncryptedStream(inner, enc, dec, ownsStream: false))
        {
            writeStream.WriteInPlace(writeBuffer.AsSpan());
            writeStream.Flush();
        }

        inner.Position = 0;

        var readEnc = new Rc4StreamCipher(TestKey, discard1024: true);
        var readDec = new Rc4StreamCipher(TestKey, discard1024: true);
        using (var readStream = new EncryptedStream(inner, readEnc, readDec, ownsStream: false))
        {
            var readBuffer = new byte[plaintext.Length];
            var bytesRead = readStream.Read(readBuffer.AsSpan());

            Assert.That(bytesRead, Is.EqualTo(plaintext.Length));
            Assert.That(readBuffer, Is.EqualTo(plaintext));
        }
    }
}
