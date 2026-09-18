using System;
using NUnit.Framework;
using NzbDrone.Core.Peers.Encryption;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;

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

    [Test]
    public void PrivateKey_should_be_configured_to_160_bits_per_bep8()
    {
        var kd = new MseKeyDerivation();

        Assert.That(kd.PrivateKey.Parameters.L, Is.EqualTo(160));
        Assert.That(kd.PrivateKey.X.BitLength, Is.LessThanOrEqualTo(160));
        Assert.That(kd.PrivateKey.X.BitLength, Is.GreaterThan(0));
    }

    [Test]
    public void NormalizeTo96Bytes_should_return_same_array_when_already_96_bytes()
    {
        var input = new byte[96];
        new Random(42).NextBytes(input);

        var result = MseKeyDerivation.NormalizeTo96Bytes(input);

        Assert.That(result, Is.SameAs(input));
        Assert.That(result, Has.Length.EqualTo(96));
    }

    [Test]
    public void NormalizeTo96Bytes_should_left_pad_with_zeros_when_less_than_96_bytes()
    {
        var input = new byte[94];
        Array.Fill<byte>(input, 0xAB);

        var result = MseKeyDerivation.NormalizeTo96Bytes(input);

        Assert.That(result, Has.Length.EqualTo(96));
        Assert.That(result[0], Is.EqualTo(0));
        Assert.That(result[1], Is.EqualTo(0));
        Assert.That(result[2], Is.EqualTo(0xAB));
        Assert.That(result[95], Is.EqualTo(0xAB));
    }

    [Test]
    public void NormalizeTo96Bytes_should_trim_to_trailing_96_bytes_when_greater_than_96_bytes()
    {
        var input = new byte[97];
        input[0] = 0x00;
        for (var i = 1; i < 97; i++)
        {
            input[i] = (byte)(i & 0xFF);
        }

        var result = MseKeyDerivation.NormalizeTo96Bytes(input);

        Assert.That(result, Has.Length.EqualTo(96));
        for (var i = 0; i < 96; i++)
        {
            Assert.That(result[i], Is.EqualTo(input[i + 1]));
        }
    }

    [Test]
    public void NormalizeTo96Bytes_should_throw_when_null()
    {
        Assert.That(() => MseKeyDerivation.NormalizeTo96Bytes(null), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void ComputeSharedSecret_should_throw_for_null_remote_key()
    {
        var kd = new MseKeyDerivation();

        Assert.That(() => kd.ComputeSharedSecret(null), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    [TestCase(0)]
    [TestCase(95)]
    [TestCase(97)]
    [TestCase(128)]
    public void ComputeSharedSecret_should_throw_for_invalid_remote_key_length(int length)
    {
        var kd = new MseKeyDerivation();
        var invalidKey = new byte[length];

        Assert.That(() => kd.ComputeSharedSecret(invalidKey), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ComputeSharedSecret_should_throw_for_p_minus_one_key()
    {
        var kd = new MseKeyDerivation();
        var pMinusOne = MseKeyDerivation.PrimeModulus.Subtract(BigInteger.One).ToByteArrayUnsigned();
        var pMinusOneKey = MseKeyDerivation.NormalizeTo96Bytes(pMinusOne);

        Assert.That(() => kd.ComputeSharedSecret(pMinusOneKey), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void ComputeSharedSecret_should_throw_for_key_greater_than_or_equal_to_p()
    {
        var kd = new MseKeyDerivation();
        var primeBytes = MseKeyDerivation.PrimeModulus.ToByteArrayUnsigned();
        var primeKey = MseKeyDerivation.NormalizeTo96Bytes(primeBytes);

        Assert.That(() => kd.ComputeSharedSecret(primeKey), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void ComputeSharedSecret_should_succeed_for_valid_boundary_keys()
    {
        var kd = new MseKeyDerivation();

        // Smallest valid public key: Y = 2
        var twoKey = new byte[96];
        twoKey[95] = 0x02;
        var secretTwo = kd.ComputeSharedSecret(twoKey);
        Assert.That(secretTwo, Has.Length.EqualTo(96));

        // Largest valid public key: Y = P - 2
        var pMinusTwo = MseKeyDerivation.PrimeModulus.Subtract(BigInteger.Two).ToByteArrayUnsigned();
        var pMinusTwoKey = MseKeyDerivation.NormalizeTo96Bytes(pMinusTwo);
        var secretPMinusTwo = kd.ComputeSharedSecret(pMinusTwoKey);
        Assert.That(secretPMinusTwo, Has.Length.EqualTo(96));
    }

    [Test]
    public void GetPublicKeyBytes_consistently_returns_strictly_96_bytes()
    {
        for (var i = 0; i < 20; i++)
        {
            var kd = new MseKeyDerivation();
            var pk = kd.GetPublicKeyBytes();
            Assert.That(pk, Has.Length.EqualTo(96));
        }
    }
}
