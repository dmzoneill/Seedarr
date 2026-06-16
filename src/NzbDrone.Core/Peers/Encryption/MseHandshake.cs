using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NLog;

namespace NzbDrone.Core.Peers.Encryption;

public class MseHandshake
{
    // MSE/PE key derivation prefixes
    private static readonly byte[] KeyA = Encoding.ASCII.GetBytes("keyA");
    private static readonly byte[] KeyB = Encoding.ASCII.GetBytes("keyB");
    private static readonly byte[] Req1Prefix = Encoding.ASCII.GetBytes("req1");
    private static readonly byte[] Req2Prefix = Encoding.ASCII.GetBytes("req2");
    private static readonly byte[] Req3Prefix = Encoding.ASCII.GetBytes("req3");

    // Verification constant: 8 zero bytes
    private static readonly byte[] VerificationConstant = new byte[8];

    // Padding length bounds
    private const int MaxPadLength = 512;
    private const int DhKeyLength = 96;

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

        // Step 1: A sends Ya + PadA
        var ya = _keyDerivation.GetPublicKeyBytes();
        var padA = GeneratePadding();
        stream.Write(ya, 0, ya.Length);
        stream.Write(padA, 0, padA.Length);
        stream.Flush();

        // Step 2: A receives Yb + PadB
        var yb = ReadExact(stream, DhKeyLength);
        _sharedSecret = _keyDerivation.ComputeSharedSecret(yb);

        // Consume any PadB by scanning for HASH('req1', S)
        var req1Hash = MseKeyDerivation.DeriveKey(_sharedSecret, Req1Prefix);
        var syncBuffer = ConsumeUntilMarker(stream, req1Hash);

        // Step 3: A builds crypto_provide message
        // HASH('req2', SKEY) XOR HASH('req3', S)
        var req2Hash = MseKeyDerivation.DeriveKey(_infoHash, Req2Prefix);
        var req3Hash = MseKeyDerivation.DeriveKey(_sharedSecret, Req3Prefix);
        var obfuscatedHash = new byte[20];
        for (var i = 0; i < 20; i++)
        {
            obfuscatedHash[i] = (byte)(req2Hash[i] ^ req3Hash[i]);
        }

        // Initialize RC4 ciphers for the negotiation phase
        var encKey = MseKeyDerivation.DeriveKey(_sharedSecret, KeyA);
        var decKey = MseKeyDerivation.DeriveKey(_sharedSecret, KeyB);
        _outCipher = new Rc4StreamCipher(encKey);
        _inCipher = new Rc4StreamCipher(decKey);

        // Send: HASH('req1', S) + HASH('req2', SKEY) XOR HASH('req3', S) + ENCRYPT(VC + crypto_provide + len(PadC) + PadC + len(IA))
        stream.Write(req1Hash, 0, req1Hash.Length);
        stream.Write(obfuscatedHash, 0, obfuscatedHash.Length);

        var cryptoProvide = BuildCryptoProvide();
        var encryptedPayload = BuildEncryptedPayload(cryptoProvide);
        stream.Write(encryptedPayload, 0, encryptedPayload.Length);
        stream.Flush();

        // Step 4: A receives ENCRYPT(VC + crypto_select + len(PadD) + PadD)
        var encryptedVc = ReadExact(stream, 8);
        _inCipher.ProcessInPlace(encryptedVc, 0, 8);

        // Validate VC (should be 8 zero bytes after decryption)
        for (var i = 0; i < 8; i++)
        {
            if (encryptedVc[i] != 0)
            {
                throw new InvalidOperationException("MSE/PE verification constant mismatch");
            }
        }

        // Read crypto_select (4 bytes)
        var cryptoSelectBytes = ReadExact(stream, 4);
        _inCipher.ProcessInPlace(cryptoSelectBytes, 0, 4);
        var cryptoSelect = (CryptoMethod)((cryptoSelectBytes[0] << 24) | (cryptoSelectBytes[1] << 16) |
                           (cryptoSelectBytes[2] << 8) | cryptoSelectBytes[3]);

        if ((cryptoSelect & GetSupportedMethods()) == CryptoMethod.None)
        {
            throw new InvalidOperationException("Peer selected unsupported crypto method");
        }

        _negotiatedMethod = cryptoSelect;

        // Read PadD length and PadD
        var padDLenBytes = ReadExact(stream, 2);
        _inCipher.ProcessInPlace(padDLenBytes, 0, 2);
        var padDLen = (padDLenBytes[0] << 8) | padDLenBytes[1];
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

        // Step 1: B receives Ya (+ possible PadA, which we ignore since we only need 96 bytes)
        var ya = ReadExact(stream, DhKeyLength);
        _sharedSecret = _keyDerivation.ComputeSharedSecret(ya);

        // Step 2: B sends Yb + PadB
        var yb = _keyDerivation.GetPublicKeyBytes();
        var padB = GeneratePadding();
        stream.Write(yb, 0, yb.Length);
        stream.Write(padB, 0, padB.Length);
        stream.Flush();

        // Step 3: B receives HASH('req1', S) + HASH('req2', SKEY) XOR HASH('req3', S) + ENCRYPT(...)
        var req1Hash = MseKeyDerivation.DeriveKey(_sharedSecret, Req1Prefix);

        // We need to find req1Hash in the incoming stream (there may be trailing PadA bytes)
        ConsumeUntilMarker(stream, req1Hash);

        // Read the obfuscated SKEY hash
        var obfuscatedHash = ReadExact(stream, 20);

        // Recover SKEY hash: obfuscatedHash XOR HASH('req3', S)
        var req3Hash = MseKeyDerivation.DeriveKey(_sharedSecret, Req3Prefix);
        var skeyHash = new byte[20];
        for (var i = 0; i < 20; i++)
        {
            skeyHash[i] = (byte)(obfuscatedHash[i] ^ req3Hash[i]);
        }

        // Validate that we know the info hash
        // skeyHash == HASH('req2', SKEY) where SKEY is the info hash
        if (!infoHashValidator(skeyHash))
        {
            throw new InvalidOperationException("Unknown info hash in MSE/PE handshake");
        }

        // Initialize RC4 ciphers (reversed for incoming)
        var decKey = MseKeyDerivation.DeriveKey(_sharedSecret, KeyA);
        var encKey = MseKeyDerivation.DeriveKey(_sharedSecret, KeyB);
        _inCipher = new Rc4StreamCipher(decKey);
        _outCipher = new Rc4StreamCipher(encKey);

        // Read ENCRYPT(VC + crypto_provide + len(PadC) + PadC + len(IA))
        var encryptedVc = ReadExact(stream, 8);
        _inCipher.ProcessInPlace(encryptedVc, 0, 8);

        for (var i = 0; i < 8; i++)
        {
            if (encryptedVc[i] != 0)
            {
                throw new InvalidOperationException("MSE/PE verification constant mismatch");
            }
        }

        // Read crypto_provide
        var cryptoProvideBytes = ReadExact(stream, 4);
        _inCipher.ProcessInPlace(cryptoProvideBytes, 0, 4);
        var cryptoProvide = (CryptoMethod)((cryptoProvideBytes[0] << 24) | (cryptoProvideBytes[1] << 16) |
                            (cryptoProvideBytes[2] << 8) | cryptoProvideBytes[3]);

        // Read PadC length and PadC
        var padCLenBytes = ReadExact(stream, 2);
        _inCipher.ProcessInPlace(padCLenBytes, 0, 2);
        var padCLen = (padCLenBytes[0] << 8) | padCLenBytes[1];
        if (padCLen > 0)
        {
            var padC = ReadExact(stream, padCLen);
            _inCipher.ProcessInPlace(padC, 0, padCLen);
        }

        // Read IA length
        var iaLenBytes = ReadExact(stream, 2);
        _inCipher.ProcessInPlace(iaLenBytes, 0, 2);
        var iaLen = (iaLenBytes[0] << 8) | iaLenBytes[1];

        // Read and decrypt Initial Application data (IA)
        byte[] initialPayload = null;
        if (iaLen > 0)
        {
            initialPayload = ReadExact(stream, iaLen);
            _inCipher.ProcessInPlace(initialPayload, 0, iaLen);
        }

        // Select crypto method
        _negotiatedMethod = SelectCryptoMethod(cryptoProvide);

        // Step 4: B sends ENCRYPT(VC + crypto_select + len(PadD) + PadD)
        var response = BuildCryptoSelectResponse(_negotiatedMethod);
        stream.Write(response, 0, response.Length);
        stream.Flush();

        _logger.Debug("MSE/PE incoming negotiation complete: {0}", _negotiatedMethod);

        var wrappedStream = WrapStream(stream);

        // If there was initial payload data, we need to present it before further reads
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

        // Prefer RC4 if we prefer encryption, or if both support it and preference is encrypted
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

    private byte[] BuildCryptoProvide()
    {
        var methods = (uint)GetSupportedMethods();
        return
        [
            (byte)(methods >> 24),
            (byte)(methods >> 16),
            (byte)(methods >> 8),
            (byte)methods
        ];
    }

    private byte[] BuildEncryptedPayload(byte[] cryptoProvide)
    {
        // VC (8 bytes) + crypto_provide (4 bytes) + len(PadC) (2 bytes) + PadC + len(IA) (2 bytes)
        var padC = GeneratePadding();
        var payloadLen = 8 + 4 + 2 + padC.Length + 2;
        var payload = new byte[payloadLen];
        var offset = 0;

        // VC (8 zero bytes)
        Array.Copy(VerificationConstant, 0, payload, offset, 8);
        offset += 8;

        // crypto_provide
        Array.Copy(cryptoProvide, 0, payload, offset, 4);
        offset += 4;

        // len(PadC)
        payload[offset++] = (byte)(padC.Length >> 8);
        payload[offset++] = (byte)padC.Length;

        // PadC
        if (padC.Length > 0)
        {
            Array.Copy(padC, 0, payload, offset, padC.Length);
            offset += padC.Length;
        }

        // len(IA) = 0 (no initial application data from initiator in this implementation)
        payload[offset++] = 0;
        payload[offset] = 0;

        // Encrypt the entire payload
        _outCipher.ProcessInPlace(payload, 0, payload.Length);

        return payload;
    }

    private byte[] BuildCryptoSelectResponse(CryptoMethod selected)
    {
        var padD = GeneratePadding();
        var responseLen = 8 + 4 + 2 + padD.Length;
        var response = new byte[responseLen];
        var offset = 0;

        // VC
        Array.Copy(VerificationConstant, 0, response, offset, 8);
        offset += 8;

        // crypto_select
        var method = (uint)selected;
        response[offset++] = (byte)(method >> 24);
        response[offset++] = (byte)(method >> 16);
        response[offset++] = (byte)(method >> 8);
        response[offset++] = (byte)method;

        // len(PadD)
        response[offset++] = (byte)(padD.Length >> 8);
        response[offset++] = (byte)padD.Length;

        // PadD
        if (padD.Length > 0)
        {
            Array.Copy(padD, 0, response, offset, padD.Length);
        }

        // Encrypt the entire response
        _outCipher.ProcessInPlace(response, 0, response.Length);

        return response;
    }

    private Stream WrapStream(Stream inner)
    {
        if (_negotiatedMethod == CryptoMethod.PlainText)
        {
            // PlainText mode: the handshake used encryption but data doesn't.
            // Return the raw stream.
            return inner;
        }

        return new EncryptedStream(inner, _outCipher, _inCipher, ownsStream: false);
    }

    private static byte[] ConsumeUntilMarker(Stream stream, byte[] marker)
    {
        // Read one byte at a time, accumulating into a window, looking for the marker.
        // Maximum search is DhKeyLength + MaxPadLength bytes to prevent infinite reads.
        var maxSearch = DhKeyLength + MaxPadLength + marker.Length;
        var window = new byte[marker.Length];
        var windowPos = 0;
        var consumed = new MemoryStream();

        for (var i = 0; i < maxSearch; i++)
        {
            var b = stream.ReadByte();
            if (b == -1)
            {
                throw new InvalidOperationException("Stream ended while searching for MSE/PE marker");
            }

            consumed.WriteByte((byte)b);

            if (windowPos < marker.Length)
            {
                window[windowPos++] = (byte)b;
            }
            else
            {
                // Shift window left
                Array.Copy(window, 1, window, 0, marker.Length - 1);
                window[marker.Length - 1] = (byte)b;
            }

            if (windowPos == marker.Length && MatchBytes(window, marker))
            {
                return consumed.ToArray();
            }
        }

        throw new InvalidOperationException("MSE/PE marker not found within search limit");
    }

    private static bool MatchBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
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

    private static byte[] GeneratePadding()
    {
        var length = RandomNumberGenerator.GetInt32(0, MaxPadLength + 1);
        var padding = new byte[length];
        RandomNumberGenerator.Fill(padding);
        return padding;
    }
}
