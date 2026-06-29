using System;
using NUnit.Framework;
using NzbDrone.Core.Peers.Encryption;

namespace NzbDrone.Core.Test.Peers.Encryption;

[TestFixture]
public class Rc4StreamCipherTest
{
    private static readonly byte[] TestKey = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };

    [Test]
    public void Process_should_return_different_bytes_from_input()
    {
        var cipher = new Rc4StreamCipher(TestKey);
        var plaintext = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0x57, 0x6F, 0x72, 0x6C, 0x64 };

        var encrypted = cipher.Process(plaintext);

        Assert.That(encrypted, Is.Not.EqualTo(plaintext));
    }

    [Test]
    public void Process_should_roundtrip_data_when_using_same_key()
    {
        var encryptor = new Rc4StreamCipher(TestKey, discard1024: true);
        var decryptor = new Rc4StreamCipher(TestKey, discard1024: true);
        var plaintext = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0x57, 0x6F, 0x72, 0x6C, 0x64 };

        var encrypted = encryptor.Process(plaintext);
        var decrypted = decryptor.Process(encrypted);

        Assert.That(decrypted, Is.EqualTo(plaintext));
    }

    [Test]
    public void ProcessInPlace_should_modify_buffer()
    {
        var cipher = new Rc4StreamCipher(TestKey);
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var original = (byte[])data.Clone();

        cipher.ProcessInPlace(data, 0, data.Length);

        Assert.That(data, Is.Not.EqualTo(original));
    }

    [Test]
    public void ProcessInPlace_should_roundtrip()
    {
        var encryptor = new Rc4StreamCipher(TestKey, discard1024: true);
        var decryptor = new Rc4StreamCipher(TestKey, discard1024: true);
        var data = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50 };
        var original = (byte[])data.Clone();

        encryptor.ProcessInPlace(data, 0, data.Length);
        Assert.That(data, Is.Not.EqualTo(original));

        decryptor.ProcessInPlace(data, 0, data.Length);
        Assert.That(data, Is.EqualTo(original));
    }

    [Test]
    public void Process_with_offsets_should_encrypt_correct_range()
    {
        var cipher = new Rc4StreamCipher(TestKey);
        var input = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
        var output = new byte[6];

        cipher.Process(input, 1, 3, output, 2);

        Assert.That(output[0], Is.EqualTo(0));
        Assert.That(output[1], Is.EqualTo(0));
        Assert.That(output[5], Is.EqualTo(0));
        Assert.That(output[2] != 0 || output[3] != 0 || output[4] != 0, Is.True);
    }

    [Test]
    public void Process_should_produce_different_output_with_different_keys()
    {
        var key1 = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var key2 = new byte[] { 0x05, 0x06, 0x07, 0x08 };
        var cipher1 = new Rc4StreamCipher(key1);
        var cipher2 = new Rc4StreamCipher(key2);
        var plaintext = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };

        var encrypted1 = cipher1.Process(plaintext);
        var encrypted2 = cipher2.Process(plaintext);

        Assert.That(encrypted1, Is.Not.EqualTo(encrypted2));
    }

    [Test]
    public void Constructor_should_produce_different_output_when_discard1024_is_false()
    {
        var cipherWithDiscard = new Rc4StreamCipher(TestKey, discard1024: true);
        var cipherWithoutDiscard = new Rc4StreamCipher(TestKey, discard1024: false);
        var plaintext = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };

        var encryptedWithDiscard = cipherWithDiscard.Process(plaintext);
        var encryptedWithoutDiscard = cipherWithoutDiscard.Process(plaintext);

        Assert.That(encryptedWithDiscard, Is.Not.EqualTo(encryptedWithoutDiscard));
    }

    [Test]
    public void Process_should_handle_empty_array()
    {
        var cipher = new Rc4StreamCipher(TestKey);
        var empty = Array.Empty<byte>();

        var result = cipher.Process(empty);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Process_should_handle_single_byte()
    {
        var encryptor = new Rc4StreamCipher(TestKey, discard1024: true);
        var decryptor = new Rc4StreamCipher(TestKey, discard1024: true);
        var plaintext = new byte[] { 0x42 };

        var encrypted = encryptor.Process(plaintext);
        Assert.That(encrypted, Has.Length.EqualTo(1));

        var decrypted = decryptor.Process(encrypted);
        Assert.That(decrypted, Is.EqualTo(plaintext));
    }

    [Test]
    public void Process_should_handle_large_data()
    {
        var encryptor = new Rc4StreamCipher(TestKey, discard1024: true);
        var decryptor = new Rc4StreamCipher(TestKey, discard1024: true);
        var plaintext = new byte[10240];
        new Random(42).NextBytes(plaintext);

        var encrypted = encryptor.Process(plaintext);
        Assert.That(encrypted, Has.Length.EqualTo(10240));
        Assert.That(encrypted, Is.Not.EqualTo(plaintext));

        var decrypted = decryptor.Process(encrypted);
        Assert.That(decrypted, Is.EqualTo(plaintext));
    }

    [Test]
    public void ProcessInPlace_should_only_modify_specified_range()
    {
        var cipher = new Rc4StreamCipher(TestKey);
        var data = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
        var originalFirst = data[0];
        var originalLast = data[5];

        cipher.ProcessInPlace(data, 1, 4);

        Assert.That(data[0], Is.EqualTo(originalFirst));
        Assert.That(data[5], Is.EqualTo(originalLast));
    }

    [Test]
    public void Process_with_offsets_should_match_full_process_at_same_position()
    {
        var cipherFull = new Rc4StreamCipher(TestKey, discard1024: true);
        var cipherOffset = new Rc4StreamCipher(TestKey, discard1024: true);

        var fullInput = new byte[] { 0x00, 0x00, 0x11, 0x22, 0x33, 0x00, 0x00 };
        var fullOutput = cipherFull.Process(fullInput);

        var offsetOutput = new byte[7];
        cipherOffset.Process(fullInput, 0, fullInput.Length, offsetOutput, 0);

        Assert.That(offsetOutput[2], Is.EqualTo(fullOutput[2]));
        Assert.That(offsetOutput[3], Is.EqualTo(fullOutput[3]));
        Assert.That(offsetOutput[4], Is.EqualTo(fullOutput[4]));
    }

    [Test]
    public void Process_should_return_output_of_same_length_as_input()
    {
        var cipher = new Rc4StreamCipher(TestKey);
        var plaintext = new byte[37];
        new Random(99).NextBytes(plaintext);

        var encrypted = cipher.Process(plaintext);

        Assert.That(encrypted, Has.Length.EqualTo(plaintext.Length));
    }
}
