using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NLog;

namespace NzbDrone.Core.Peers.Encryption;

public class MseHandshake
{
    private const int MaxPadLength = 512;
    private const int DhKeyLength = 96;

    private static readonly byte[] KeyAPrefix = Encoding.ASCII.GetBytes("keyA");
    private static readonly byte[] KeyBPrefix = Encoding.ASCII.GetBytes("keyB");
    private static readonly byte[] Req1Prefix = Encoding.ASCII.GetBytes("req1");
    private static readonly byte[] Req2Prefix = Encoding.ASCII.GetBytes("req2");
    private static readonly byte[] Req3Prefix = Encoding.ASCII.GetBytes("req3");

    private readonly Logger _logger;
    private readonly EncryptionMode _preferredMode;
    private readonly byte[] _infoHash;

    private MseKeyDerivation _keyDerivation;
    private byte[] _sharedSecret;
    private Rc4StreamCipher _outCipher;
    private Rc4StreamCipher _inCipher;
    private CryptoMethod _negotiatedMethod;

    public CryptoMethod NegotiatedMethod => _negotiatedMethod;

    public MseHandshake(byte[] infoHash, EncryptionMode preferredMode)
    {
        _infoHash = infoHash;
        _preferredMode = preferredMode;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public Stream NegotiateOutgoing(Stream stream)
    {
        _keyDerivation = new MseKeyDerivation();

        // Step 1: A -> B: Ya + PadA
        var ya = _keyDerivation.GetPublicKeyBytes();
        var padA = GeneratePadding();
        stream.Write(ya, 0, ya.Length);
        stream.Write(padA, 0, padA.Length);
        stream.Flush();

        // Step 2: A <- B: Yb (96 bytes, PadB follows but length is unknown)
        var yb = ReadExact(stream, DhKeyLength);
        _sharedSecret = _keyDerivation.ComputeSharedSecret(yb);

        // Initialize the RC4 ciphers
        var encKey = MseKeyDerivation.DeriveKey(_sharedSecret, KeyAPrefix);
        var decKey = MseKeyDerivation.DeriveKey(_sharedSecret, KeyBPrefix);
        _outCipher = new Rc4StreamCipher(encKey);
        _inCipher = new Rc4StreamCipher(decKey);

        // Step 3: A -> B: HASH('req1', S) + HASH('req2', SKEY) XOR HASH('req3', S) + ENCRYPT(VC, crypto_provide, len(PadC), PadC, len(IA))
        var req1Hash = MseKeyDerivation.DeriveKey(_sharedSecret, Req1Prefix);
        var req2Hash = MseKeyDerivation.DeriveKey(_infoHash, Req2Prefix);
        var req3Hash = MseKeyDerivation.DeriveKey(_sharedSecret, Req3Prefix);

        var obfuscatedHash = new byte[20];
        for (var i = 0; i < 20; i++)
        {
            obfuscatedHash[i] = (byte)(req2Hash[i] ^ req3Hash[i]);
        }

        stream.Write(req1Hash, 0, req1Hash.Length);
        stream.Write(obfuscatedHash, 0, obfuscatedHash.Length);

        var encryptedPayload = BuildEncryptedPayload();
        stream.Write(encryptedPayload, 0, encryptedPayload.Length);
        stream.Flush();

        // Step 4: A <- B: ENCRYPT(VC, crypto_select, len(PadD), PadD)
        // B's stream contains PadB (unknown length) followed by the encrypted response.
        // Synchronize by computing what ENCRYPT(VC) looks like and scanning for it.
        var vcMarker = ComputeEncryptedVcMarker(decKey);
        ScanForMarker(stream, vcMarker);

        // Found the VC marker. The real decryption cipher has already been initialized
        // and had 1024 bytes discarded. Advance it past the 8 VC bytes we just found.
        var vcDummy = new byte[8];
        _inCipher.ProcessInPlace(vcDummy, 0, 8);

        // Read crypto_select (4 bytes)
        var cryptoSelectBytes = ReadExact(stream, 4);
        _inCipher.ProcessInPlace(cryptoSelectBytes, 0, 4);
        var cryptoSelect = (CryptoMethod)ReadUint32(cryptoSelectBytes);

        if ((cryptoSelect & GetSupportedMethods()) == CryptoMethod.None)
        {
            throw new InvalidOperationException("Peer selected unsupported crypto method");
        }

        _negotiatedMethod = cryptoSelect;

        // Read PadD length and PadD
        var padDLenBytes = ReadExact(stream, 2);
        _inCipher.ProcessInPlace(padDLenBytes, 0, 2);
        var padDLen = ReadUint16(padDLenBytes);

        if (padDLen > MaxPadLength)
        {
            throw new InvalidOperationException("MSE padding length exceeds maximum");
        }

        if (padDLen > 0)
        {
            var padD = ReadExact(stream, padDLen);
            _inCipher.ProcessInPlace(padD, 0, padDLen);
        }

        _logger.Debug("MSE/PE outgoing negotiation complete: {0}", _negotiatedMethod);
        return WrapStream(stream);
    }

    public Stream NegotiateIncoming(Stream stream, Func<byte[], bool> infoHashValidator)
    {
        _keyDerivation = new MseKeyDerivation();

        // Step 1: B <- A: Ya (96 bytes, PadA follows but length is unknown)
        var ya = ReadExact(stream, DhKeyLength);
        _sharedSecret = _keyDerivation.ComputeSharedSecret(ya);

        // Step 2: B -> A: Yb + PadB
        var yb = _keyDerivation.GetPublicKeyBytes();
        var padB = GeneratePadding();
        stream.Write(yb, 0, yb.Length);
        stream.Write(padB, 0, padB.Length);
        stream.Flush();

        // Step 3: B <- A: HASH('req1', S), HASH('req2', SKEY) XOR HASH('req3', S), ENCRYPT(...)
        // Synchronize by scanning for HASH('req1', S) to skip past PadA
        var req1Hash = MseKeyDerivation.DeriveKey(_sharedSecret, Req1Prefix);
        ScanForMarker(stream, req1Hash);

        // Read the obfuscated SKEY hash (20 bytes)
        var obfuscatedHash = ReadExact(stream, 20);

        // Recover SKEY hash: obfuscatedHash XOR HASH('req3', S)
        var req3Hash = MseKeyDerivation.DeriveKey(_sharedSecret, Req3Prefix);
        var skeyHash = new byte[20];
        for (var i = 0; i < 20; i++)
        {
            skeyHash[i] = (byte)(obfuscatedHash[i] ^ req3Hash[i]);
        }

        if (!infoHashValidator(skeyHash))
        {
            throw new InvalidOperationException("Unknown info hash in MSE/PE handshake");
        }

        // Initialize RC4 ciphers (reversed roles for incoming side)
        var decKey = MseKeyDerivation.DeriveKey(_sharedSecret, KeyAPrefix);
        var encKey = MseKeyDerivation.DeriveKey(_sharedSecret, KeyBPrefix);
        _inCipher = new Rc4StreamCipher(decKey);
        _outCipher = new Rc4StreamCipher(encKey);

        // Read ENCRYPT(VC, crypto_provide, len(PadC), PadC, len(IA), IA)
        var encryptedVc = ReadExact(stream, 8);
        _inCipher.ProcessInPlace(encryptedVc, 0, 8);

        for (var i = 0; i < 8; i++)
        {
            if (encryptedVc[i] != 0)
            {
                throw new InvalidOperationException("MSE/PE verification constant mismatch");
            }
        }

        var cryptoProvideBytes = ReadExact(stream, 4);
        _inCipher.ProcessInPlace(cryptoProvideBytes, 0, 4);
        var cryptoProvide = (CryptoMethod)ReadUint32(cryptoProvideBytes);

        var padCLenBytes = ReadExact(stream, 2);
        _inCipher.ProcessInPlace(padCLenBytes, 0, 2);
        var padCLen = ReadUint16(padCLenBytes);

        if (padCLen > MaxPadLength)
        {
            throw new InvalidOperationException("MSE padding length exceeds maximum");
        }

        if (padCLen > 0)
        {
            var padC = ReadExact(stream, padCLen);
            _inCipher.ProcessInPlace(padC, 0, padCLen);
        }

        var iaLenBytes = ReadExact(stream, 2);
        _inCipher.ProcessInPlace(iaLenBytes, 0, 2);
        var iaLen = ReadUint16(iaLenBytes);

        if (iaLen > MaxPadLength)
        {
            throw new InvalidOperationException("MSE padding length exceeds maximum");
        }

        byte[] initialPayload = null;
        if (iaLen > 0)
        {
            initialPayload = ReadExact(stream, iaLen);
            _inCipher.ProcessInPlace(initialPayload, 0, iaLen);
        }

        // Select crypto method and send response
        _negotiatedMethod = SelectCryptoMethod(cryptoProvide);

        // Step 4: B -> A: ENCRYPT(VC, crypto_select, len(PadD), PadD)
        var response = BuildCryptoSelectResponse(_negotiatedMethod);
        stream.Write(response, 0, response.Length);
        stream.Flush();

        _logger.Debug("MSE/PE incoming negotiation complete: {0}", _negotiatedMethod);

        var wrappedStream = WrapStream(stream);

        if (initialPayload != null && initialPayload.Length > 0)
        {
            return new PrefixedStream(initialPayload, wrappedStream);
        }

        return wrappedStream;
    }

    private CryptoMethod GetSupportedMethods()
    {
        return _preferredMode switch
        {
            EncryptionMode.RequireEncrypted => CryptoMethod.Rc4,
            EncryptionMode.PreferEncrypted => CryptoMethod.Rc4 | CryptoMethod.PlainText,
            EncryptionMode.PreferPlainText => CryptoMethod.PlainText | CryptoMethod.Rc4,
            _ => CryptoMethod.Rc4 | CryptoMethod.PlainText
        };
    }

    private CryptoMethod SelectCryptoMethod(CryptoMethod peerProvides)
    {
        var supported = GetSupportedMethods();
        var common = peerProvides & supported;

        if (common == CryptoMethod.None)
        {
            throw new InvalidOperationException("No common crypto method available");
        }

        if (_preferredMode == EncryptionMode.RequireEncrypted || _preferredMode == EncryptionMode.PreferEncrypted)
        {
            if ((common & CryptoMethod.Rc4) != CryptoMethod.None)
            {
                return CryptoMethod.Rc4;
            }
        }

        if ((common & CryptoMethod.PlainText) != CryptoMethod.None)
        {
            return CryptoMethod.PlainText;
        }

        return CryptoMethod.Rc4;
    }

    private byte[] BuildEncryptedPayload()
    {
        var padC = GeneratePadding();
        var payloadLen = 8 + 4 + 2 + padC.Length + 2;
        var payload = new byte[payloadLen];
        var offset = 0;

        // VC (8 zero bytes, already zero-initialized)
        offset += 8;

        // crypto_provide (4 bytes, big-endian)
        var methods = (uint)GetSupportedMethods();
        payload[offset++] = (byte)(methods >> 24);
        payload[offset++] = (byte)(methods >> 16);
        payload[offset++] = (byte)(methods >> 8);
        payload[offset++] = (byte)methods;

        // len(PadC) (2 bytes, big-endian)
        payload[offset++] = (byte)(padC.Length >> 8);
        payload[offset++] = (byte)padC.Length;

        // PadC
        if (padC.Length > 0)
        {
            Array.Copy(padC, 0, payload, offset, padC.Length);
            offset += padC.Length;
        }

        // len(IA) = 0 (no initial application data)
        // Already zero from initialization

        _outCipher.ProcessInPlace(payload, 0, payload.Length);
        return payload;
    }

    private byte[] BuildCryptoSelectResponse(CryptoMethod selected)
    {
        var padD = GeneratePadding();
        var responseLen = 8 + 4 + 2 + padD.Length;
        var response = new byte[responseLen];
        var offset = 0;

        // VC (8 zero bytes, already zero-initialized)
        offset += 8;

        // crypto_select (4 bytes, big-endian)
        var method = (uint)selected;
        response[offset++] = (byte)(method >> 24);
        response[offset++] = (byte)(method >> 16);
        response[offset++] = (byte)(method >> 8);
        response[offset++] = (byte)method;

        // len(PadD) (2 bytes, big-endian)
        response[offset++] = (byte)(padD.Length >> 8);
        response[offset++] = (byte)padD.Length;

        // PadD
        if (padD.Length > 0)
        {
            Array.Copy(padD, 0, response, offset, padD.Length);
        }

        _outCipher.ProcessInPlace(response, 0, response.Length);
        return response;
    }

    private Stream WrapStream(Stream inner)
    {
        if (_negotiatedMethod == CryptoMethod.PlainText)
        {
            return inner;
        }

        return new EncryptedStream(inner, _outCipher, _inCipher, ownsStream: false);
    }

    private static byte[] ComputeEncryptedVcMarker(byte[] decryptionKey)
    {
        var tempCipher = new Rc4StreamCipher(decryptionKey);
        var vc = new byte[8];
        tempCipher.ProcessInPlace(vc, 0, 8);
        return vc;
    }

    private static void ScanForMarker(Stream stream, byte[] marker)
    {
        var maxSearch = DhKeyLength + MaxPadLength + marker.Length;
        var window = new byte[marker.Length];
        var filled = 0;

        for (var i = 0; i < maxSearch; i++)
        {
            var b = stream.ReadByte();
            if (b == -1)
            {
                throw new InvalidOperationException("Stream ended while searching for MSE/PE sync marker");
            }

            if (filled < marker.Length)
            {
                window[filled++] = (byte)b;
            }
            else
            {
                Array.Copy(window, 1, window, 0, marker.Length - 1);
                window[marker.Length - 1] = (byte)b;
            }

            if (filled == marker.Length && BytesEqual(window, marker))
            {
                return;
            }
        }

        throw new InvalidOperationException("MSE/PE sync marker not found within search limit");
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static byte[] ReadExact(Stream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read == 0)
            {
                throw new InvalidOperationException("Unexpected end of stream during MSE/PE handshake");
            }

            offset += read;
        }

        return buffer;
    }

    private static uint ReadUint32(byte[] bytes)
    {
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) |
            ((uint)bytes[2] << 8) | bytes[3];
    }

    private static int ReadUint16(byte[] bytes)
    {
        return (bytes[0] << 8) | bytes[1];
    }

    private static byte[] GeneratePadding()
    {
        var length = RandomNumberGenerator.GetInt32(0, MaxPadLength + 1);
        if (length == 0)
        {
            return Array.Empty<byte>();
        }

        var padding = new byte[length];
        RandomNumberGenerator.Fill(padding);
        return padding;
    }
}
