using System;
using NUnit.Framework;
using NzbDrone.Core.Peers.Encryption;

namespace NzbDrone.Core.Test.Peers.Encryption;

[TestFixture]
public class MseKeyDerivationTest
{
    [Test]
    public void GetPublicKeyBytes_should_return_96_bytes()
    {
        var kd = new MseKeyDerivation();

        var publicKey = kd.GetPublicKeyBytes();

        Assert.That(publicKey, Has.Length.EqualTo(96));
    }

    [Test]
    public void GetPublicKeyBytes_should_return_different_keys_on_different_instances()
    {
        var kd1 = new MseKeyDerivation();
        var kd2 = new MseKeyDerivation();

        var key1 = kd1.GetPublicKeyBytes();
        var key2 = kd2.GetPublicKeyBytes();

        Assert.That(key1, Is.Not.EqualTo(key2));
    }

    [Test]
    public void ComputeSharedSecret_should_return_96_bytes()
    {
        var kd1 = new MseKeyDerivation();
        var kd2 = new MseKeyDerivation();

        var secret = kd1.ComputeSharedSecret(kd2.GetPublicKeyBytes());

        Assert.That(secret, Has.Length.EqualTo(96));
    }

    [Test]
    public void ComputeSharedSecret_should_produce_same_secret_for_both_parties()
    {
        var kd1 = new MseKeyDerivation();
        var kd2 = new MseKeyDerivation();

        var secret1 = kd1.ComputeSharedSecret(kd2.GetPublicKeyBytes());
        var secret2 = kd2.ComputeSharedSecret(kd1.GetPublicKeyBytes());

        Assert.That(secret1, Is.EqualTo(secret2));
    }

    [Test]
    public void ComputeSharedSecret_should_throw_for_zero_key()
    {
        var kd = new MseKeyDerivation();
        var zeroKey = new byte[96];

        Assert.That(() => kd.ComputeSharedSecret(zeroKey), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void ComputeSharedSecret_should_throw_for_one_key()
    {
        var kd = new MseKeyDerivation();
        var oneKey = new byte[96];
        oneKey[95] = 0x01;

        Assert.That(() => kd.ComputeSharedSecret(oneKey), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void DeriveKey_should_return_20_bytes()
    {
        var secret = new byte[96];
        new Random(42).NextBytes(secret);
        var prefix = new byte[] { 0x01, 0x02, 0x03 };

        var key = MseKeyDerivation.DeriveKey(secret, prefix);

        Assert.That(key, Has.Length.EqualTo(20));
    }

    [Test]
    public void DeriveKey_should_return_deterministic_result()
    {
        var secret = new byte[96];
        new Random(42).NextBytes(secret);
        var prefix = new byte[] { 0x01, 0x02, 0x03 };

        var key1 = MseKeyDerivation.DeriveKey(secret, prefix);
        var key2 = MseKeyDerivation.DeriveKey(secret, prefix);

        Assert.That(key1, Is.EqualTo(key2));
    }

    [Test]
    public void DeriveKey_should_return_different_results_for_different_prefixes()
    {
        var secret = new byte[96];
        new Random(42).NextBytes(secret);
        var prefix1 = new byte[] { 0x01, 0x02, 0x03 };
        var prefix2 = new byte[] { 0x04, 0x05, 0x06 };

        var key1 = MseKeyDerivation.DeriveKey(secret, prefix1);
        var key2 = MseKeyDerivation.DeriveKey(secret, prefix2);

        Assert.That(key1, Is.Not.EqualTo(key2));
    }

    [Test]
    public void DeriveKey_should_return_different_results_for_different_secrets()
    {
        var secret1 = new byte[96];
        var secret2 = new byte[96];
        new Random(42).NextBytes(secret1);
        new Random(99).NextBytes(secret2);
        var prefix = new byte[] { 0x01, 0x02, 0x03 };

        var key1 = MseKeyDerivation.DeriveKey(secret1, prefix);
        var key2 = MseKeyDerivation.DeriveKey(secret2, prefix);

        Assert.That(key1, Is.Not.EqualTo(key2));
    }

    [Test]
    public void HashInfoHash_should_return_20_bytes()
    {
        var infoHash = new byte[]
        {
            0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C,
            0x0D, 0x0E, 0x0F, 0x10
        };

        var result = MseKeyDerivation.HashInfoHash(infoHash);

        Assert.That(result, Has.Length.EqualTo(20));
    }

    [Test]
    public void HashInfoHash_should_return_deterministic_result()
    {
        var infoHash = new byte[]
        {
            0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C,
            0x0D, 0x0E, 0x0F, 0x10
        };

        var result1 = MseKeyDerivation.HashInfoHash(infoHash);
        var result2 = MseKeyDerivation.HashInfoHash(infoHash);

        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void HashInfoHash_should_return_different_results_for_different_inputs()
    {
        var infoHash1 = new byte[]
        {
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
            0x11, 0x12, 0x13, 0x14
        };
        var infoHash2 = new byte[]
        {
            0xFF, 0xFE, 0xFD, 0xFC, 0xFB, 0xFA, 0xF9, 0xF8,
            0xF7, 0xF6, 0xF5, 0xF4, 0xF3, 0xF2, 0xF1, 0xF0,
            0xEF, 0xEE, 0xED, 0xEC
        };

        var result1 = MseKeyDerivation.HashInfoHash(infoHash1);
        var result2 = MseKeyDerivation.HashInfoHash(infoHash2);

        Assert.That(result1, Is.Not.EqualTo(result2));
    }

    [Test]
    public void ComputeSharedSecret_should_produce_different_secrets_with_different_peers()
    {
        var kd1 = new MseKeyDerivation();
        var kd2 = new MseKeyDerivation();
        var kd3 = new MseKeyDerivation();

        var secret12 = kd1.ComputeSharedSecret(kd2.GetPublicKeyBytes());
        var secret13 = kd1.ComputeSharedSecret(kd3.GetPublicKeyBytes());

        Assert.That(secret12, Is.Not.EqualTo(secret13));
    }
}
