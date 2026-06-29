using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace NzbDrone.Core.Peers.Encryption;

public class MseKeyDerivation
{
    // MSE/PE uses a 768-bit prime from BEP specification
    private static readonly byte[] PrimeBytes =
    [
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xC9, 0x0F, 0xDA, 0xA2, 0x21, 0x68, 0xC2, 0x34,
        0xC4, 0xC6, 0x62, 0x8B, 0x80, 0xDC, 0x1C, 0xD1,
        0x29, 0x02, 0x4E, 0x08, 0x8A, 0x67, 0xCC, 0x74,
        0x02, 0x0B, 0xBE, 0xA6, 0x3B, 0x13, 0x9B, 0x22,
        0x51, 0x4A, 0x08, 0x79, 0x8E, 0x34, 0x04, 0xDD,
        0xEF, 0x95, 0x19, 0xB3, 0xCD, 0x3A, 0x43, 0x1B,
        0x30, 0x2B, 0x0A, 0x6D, 0xF2, 0x5F, 0x14, 0x37,
        0x4F, 0xE1, 0x35, 0x6D, 0x6D, 0x51, 0xC2, 0x45,
        0xE4, 0x85, 0xB5, 0x76, 0x62, 0x5E, 0x7E, 0xC6,
        0xF4, 0x4C, 0x42, 0xE9, 0xA6, 0x3A, 0x36, 0x21,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x09, 0x05, 0x63
    ];

    private static readonly Org.BouncyCastle.Math.BigInteger Prime = new(1, PrimeBytes);
    private static readonly Org.BouncyCastle.Math.BigInteger Generator = Org.BouncyCastle.Math.BigInteger.Two;

    private readonly DHPrivateKeyParameters _privateKey;
    private readonly DHPublicKeyParameters _publicKey;

    public MseKeyDerivation()
    {
        var dhParams = new DHParameters(Prime, Generator);
        var keyGen = new DHKeyPairGenerator();
        keyGen.Init(new DHKeyGenerationParameters(new SecureRandom(), dhParams));
        var keyPair = keyGen.GenerateKeyPair();
        _privateKey = (DHPrivateKeyParameters)keyPair.Private;
        _publicKey = (DHPublicKeyParameters)keyPair.Public;
    }

    public byte[] GetPublicKeyBytes()
    {
        var bytes = _publicKey.Y.ToByteArrayUnsigned();
        if (bytes.Length < 96)
        {
            var padded = new byte[96];
            Array.Copy(bytes, 0, padded, 96 - bytes.Length, bytes.Length);
            return padded;
        }

        return bytes;
    }

    public byte[] ComputeSharedSecret(byte[] remotePublicKeyBytes)
    {
        var remoteY = new Org.BouncyCastle.Math.BigInteger(1, remotePublicKeyBytes);

        // Validate DH public key to prevent small subgroup attacks
        if (remoteY.CompareTo(Org.BouncyCastle.Math.BigInteger.One) <= 0 ||
            remoteY.CompareTo(Prime.Subtract(Org.BouncyCastle.Math.BigInteger.One)) >= 0)
        {
            throw new InvalidOperationException("Invalid DH public key");
        }

        var remotePublicKey = new DHPublicKeyParameters(remoteY, new DHParameters(Prime, Generator));

        var agreement = new DHBasicAgreement();
        agreement.Init(_privateKey);
        var sharedSecret = agreement.CalculateAgreement(remotePublicKey);
        var secretBytes = sharedSecret.ToByteArrayUnsigned();

        if (secretBytes.Length < 96)
        {
            var padded = new byte[96];
            Array.Copy(secretBytes, 0, padded, 96 - secretBytes.Length, secretBytes.Length);
            return padded;
        }

        return secretBytes;
    }

    public static byte[] DeriveKey(byte[] sharedSecret, byte[] prefix)
    {
        using var sha1 = SHA1.Create();
        var combined = new byte[prefix.Length + sharedSecret.Length];
        Array.Copy(prefix, 0, combined, 0, prefix.Length);
        Array.Copy(sharedSecret, 0, combined, prefix.Length, sharedSecret.Length);
        return sha1.ComputeHash(combined);
    }

    public static byte[] HashInfoHash(byte[] infoHash)
    {
        using var sha1 = SHA1.Create();
        return sha1.ComputeHash(infoHash);
    }
}
