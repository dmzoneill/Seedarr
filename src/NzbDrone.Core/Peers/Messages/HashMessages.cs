using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace NzbDrone.Core.Peers.Messages;

public class HashRequestMessage
{
    public const int HeaderSize = 48;
    public const int PiecesRootSize = 32;

    public byte[] PiecesRoot { get; set; } = new byte[PiecesRootSize];
    public int BaseLayer { get; set; }
    public int Index { get; set; }
    public int Length { get; set; }
    public int ProofLayers { get; set; }

    public short BaseLayerShort => (short)BaseLayer;
    public short ProofLayersShort => (short)ProofLayers;

    public HashRequestMessage()
    {
    }

    public HashRequestMessage(byte[] piecesRoot, int baseLayer, int index, int length, int proofLayers)
    {
        if (piecesRoot != null)
        {
            PiecesRoot = (byte[])piecesRoot.Clone();
        }

        BaseLayer = baseLayer;
        Index = index;
        Length = length;
        ProofLayers = proofLayers;
    }

    public HashRequestMessage(byte[] piecesRoot, short baseLayer, int index, int length, short proofLayers)
        : this(piecesRoot, (int)baseLayer, index, length, (int)proofLayers)
    {
    }

    public bool IsValid()
    {
        if (PiecesRoot == null || PiecesRoot.Length != PiecesRootSize)
        {
            return false;
        }

        if (BaseLayer < 0 || Index < 0 || Length < 2 || ProofLayers < 0)
        {
            return false;
        }

        // Length must be a power of two
        if ((Length & (Length - 1)) != 0)
        {
            return false;
        }

        // Index must be a multiple of length
        if ((Index % Length) != 0)
        {
            return false;
        }

        return true;
    }

    public byte[] ToBytes()
    {
        var buffer = new byte[HeaderSize];
        if (PiecesRoot != null)
        {
            Array.Copy(PiecesRoot, 0, buffer, 0, Math.Min(PiecesRoot.Length, PiecesRootSize));
        }

        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(32, 4), BaseLayer);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(36, 4), Index);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(40, 4), Length);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(44, 4), ProofLayers);

        return buffer;
    }

    public PeerMessage ToPeerMessage()
    {
        return new PeerMessage
        {
            Type = PeerMessageType.HashRequest,
            Payload = ToBytes()
        };
    }

    public static HashRequestMessage FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new ArgumentException($"Data length {data.Length} is less than required header size {HeaderSize}", nameof(data));
        }

        var piecesRoot = data.Slice(0, PiecesRootSize).ToArray();
        var baseLayer = BinaryPrimitives.ReadInt32BigEndian(data.Slice(32, 4));
        var index = BinaryPrimitives.ReadInt32BigEndian(data.Slice(36, 4));
        var length = BinaryPrimitives.ReadInt32BigEndian(data.Slice(40, 4));
        var proofLayers = BinaryPrimitives.ReadInt32BigEndian(data.Slice(44, 4));

        return new HashRequestMessage(piecesRoot, baseLayer, index, length, proofLayers);
    }

    public static HashRequestMessage FromPeerMessage(PeerMessage message)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        if (message.Type != PeerMessageType.HashRequest)
        {
            throw new ArgumentException($"Expected message type {PeerMessageType.HashRequest} but got {message.Type}", nameof(message));
        }

        return FromBytes(message.Payload ?? Array.Empty<byte>());
    }
}

public class HashesMessage
{
    public const int HeaderSize = 48;
    public const int PiecesRootSize = 32;
    public const int HashDigestSize = 32;

    public byte[] PiecesRoot { get; set; } = new byte[PiecesRootSize];
    public int BaseLayer { get; set; }
    public int Index { get; set; }
    public int Length { get; set; }
    public int ProofLayers { get; set; }
    public byte[] Hashes { get; set; } = Array.Empty<byte>();

    public int HashCount => (Hashes?.Length ?? 0) / HashDigestSize;
    public short BaseLayerShort => (short)BaseLayer;
    public short ProofLayersShort => (short)ProofLayers;

    public HashesMessage()
    {
    }

    public HashesMessage(byte[] piecesRoot, int baseLayer, int index, int length, int proofLayers, byte[] hashes)
    {
        if (piecesRoot != null)
        {
            PiecesRoot = (byte[])piecesRoot.Clone();
        }

        BaseLayer = baseLayer;
        Index = index;
        Length = length;
        ProofLayers = proofLayers;

        if (hashes != null)
        {
            Hashes = (byte[])hashes.Clone();
        }
    }

    public HashesMessage(byte[] piecesRoot, short baseLayer, int index, int length, short proofLayers, byte[] hashes)
        : this(piecesRoot, (int)baseLayer, index, length, (int)proofLayers, hashes)
    {
    }

    public HashesMessage(byte[] piecesRoot, int baseLayer, int index, int length, int proofLayers, IEnumerable<byte[]> hashes)
    {
        if (piecesRoot != null)
        {
            PiecesRoot = (byte[])piecesRoot.Clone();
        }

        BaseLayer = baseLayer;
        Index = index;
        Length = length;
        ProofLayers = proofLayers;

        if (hashes != null)
        {
            var list = new List<byte[]>(hashes);
            var buffer = new byte[list.Count * HashDigestSize];
            for (var i = 0; i < list.Count; i++)
            {
                Array.Copy(list[i], 0, buffer, i * HashDigestSize, Math.Min(list[i].Length, HashDigestSize));
            }

            Hashes = buffer;
        }
    }

    public bool IsValid()
    {
        if (PiecesRoot == null || PiecesRoot.Length != PiecesRootSize)
        {
            return false;
        }

        if (BaseLayer < 0 || Index < 0 || Length < 2 || ProofLayers < 0)
        {
            return false;
        }

        if ((Length & (Length - 1)) != 0)
        {
            return false;
        }

        if ((Index % Length) != 0)
        {
            return false;
        }

        if (Hashes == null || (Hashes.Length % HashDigestSize) != 0)
        {
            return false;
        }

        if (HashCount < Length)
        {
            return false;
        }

        return true;
    }

    public List<byte[]> GetHashesList()
    {
        var list = new List<byte[]>(HashCount);
        if (Hashes == null)
        {
            return list;
        }

        for (var i = 0; i < HashCount; i++)
        {
            var hash = new byte[HashDigestSize];
            Array.Copy(Hashes, i * HashDigestSize, hash, 0, HashDigestSize);
            list.Add(hash);
        }

        return list;
    }

    public byte[] ToBytes()
    {
        var hashesLength = Hashes?.Length ?? 0;
        var buffer = new byte[HeaderSize + hashesLength];
        if (PiecesRoot != null)
        {
            Array.Copy(PiecesRoot, 0, buffer, 0, Math.Min(PiecesRoot.Length, PiecesRootSize));
        }

        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(32, 4), BaseLayer);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(36, 4), Index);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(40, 4), Length);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(44, 4), ProofLayers);

        if (Hashes != null && hashesLength > 0)
        {
            Array.Copy(Hashes, 0, buffer, HeaderSize, hashesLength);
        }

        return buffer;
    }

    public PeerMessage ToPeerMessage()
    {
        return new PeerMessage
        {
            Type = PeerMessageType.Hashes,
            Payload = ToBytes()
        };
    }

    public static HashesMessage FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new ArgumentException($"Data length {data.Length} is less than required header size {HeaderSize}", nameof(data));
        }

        var hashesLength = data.Length - HeaderSize;
        if ((hashesLength % HashDigestSize) != 0)
        {
            throw new ArgumentException($"Hashes payload length {hashesLength} is not a multiple of {HashDigestSize}", nameof(data));
        }

        var piecesRoot = data.Slice(0, PiecesRootSize).ToArray();
        var baseLayer = BinaryPrimitives.ReadInt32BigEndian(data.Slice(32, 4));
        var index = BinaryPrimitives.ReadInt32BigEndian(data.Slice(36, 4));
        var length = BinaryPrimitives.ReadInt32BigEndian(data.Slice(40, 4));
        var proofLayers = BinaryPrimitives.ReadInt32BigEndian(data.Slice(44, 4));
        var hashes = data.Slice(HeaderSize).ToArray();

        return new HashesMessage(piecesRoot, baseLayer, index, length, proofLayers, hashes);
    }

    public static HashesMessage FromPeerMessage(PeerMessage message)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        if (message.Type != PeerMessageType.Hashes)
        {
            throw new ArgumentException($"Expected message type {PeerMessageType.Hashes} but got {message.Type}", nameof(message));
        }

        return FromBytes(message.Payload ?? Array.Empty<byte>());
    }

    public bool VerifyAgainstRoot(ReadOnlySpan<byte> expectedRoot)
    {
        if (expectedRoot.Length != PiecesRootSize || !IsValid())
        {
            return false;
        }

        if (HashCount < Length)
        {
            return false;
        }

        var hashesList = GetHashesList();
        var currentLevel = new List<byte[]>(Length);
        for (var i = 0; i < Length; i++)
        {
            currentLevel.Add(hashesList[i]);
        }

        Span<byte> combined = stackalloc byte[64];
        while (currentLevel.Count > 1)
        {
            var nextLevel = new List<byte[]>(currentLevel.Count / 2);
            for (var i = 0; i < currentLevel.Count; i += 2)
            {
                currentLevel[i].CopyTo(combined[..32]);
                currentLevel[i + 1].CopyTo(combined[32..]);
                nextLevel.Add(SHA256.HashData(combined));
            }

            currentLevel = nextLevel;
        }

        var currentHash = currentLevel[0];
        var impliedLayers = System.Numerics.BitOperations.TrailingZeroCount(Length);
        var uncleIdx = Length;

        for (var p = impliedLayers; p <= ProofLayers && uncleIdx < hashesList.Count; p++)
        {
            var uncle = hashesList[uncleIdx++];
            var nodeIdx = Index >> p;
            if ((nodeIdx & 1) == 0)
            {
                currentHash.CopyTo(combined[..32]);
                uncle.CopyTo(combined[32..]);
            }
            else
            {
                uncle.CopyTo(combined[..32]);
                currentHash.CopyTo(combined[32..]);
            }

            currentHash = SHA256.HashData(combined);
        }

        return CryptographicOperations.FixedTimeEquals(currentHash, expectedRoot);
    }
}

public class HashRejectMessage
{
    public const int HeaderSize = 48;
    public const int PiecesRootSize = 32;

    public byte[] PiecesRoot { get; set; } = new byte[PiecesRootSize];
    public int BaseLayer { get; set; }
    public int Index { get; set; }
    public int Length { get; set; }
    public int ProofLayers { get; set; }

    public short BaseLayerShort => (short)BaseLayer;
    public short ProofLayersShort => (short)ProofLayers;

    public HashRejectMessage()
    {
    }

    public HashRejectMessage(byte[] piecesRoot, int baseLayer, int index, int length, int proofLayers)
    {
        if (piecesRoot != null)
        {
            PiecesRoot = (byte[])piecesRoot.Clone();
        }

        BaseLayer = baseLayer;
        Index = index;
        Length = length;
        ProofLayers = proofLayers;
    }

    public HashRejectMessage(byte[] piecesRoot, short baseLayer, int index, int length, short proofLayers)
        : this(piecesRoot, (int)baseLayer, index, length, (int)proofLayers)
    {
    }

    public HashRejectMessage(HashRequestMessage request)
    {
        if (request != null)
        {
            if (request.PiecesRoot != null)
            {
                PiecesRoot = (byte[])request.PiecesRoot.Clone();
            }

            BaseLayer = request.BaseLayer;
            Index = request.Index;
            Length = request.Length;
            ProofLayers = request.ProofLayers;
        }
    }

    public bool IsValid()
    {
        if (PiecesRoot == null || PiecesRoot.Length != PiecesRootSize)
        {
            return false;
        }

        if (BaseLayer < 0 || Index < 0 || Length < 2 || ProofLayers < 0)
        {
            return false;
        }

        return true;
    }

    public byte[] ToBytes()
    {
        var buffer = new byte[HeaderSize];
        if (PiecesRoot != null)
        {
            Array.Copy(PiecesRoot, 0, buffer, 0, Math.Min(PiecesRoot.Length, PiecesRootSize));
        }

        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(32, 4), BaseLayer);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(36, 4), Index);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(40, 4), Length);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(44, 4), ProofLayers);

        return buffer;
    }

    public PeerMessage ToPeerMessage()
    {
        return new PeerMessage
        {
            Type = PeerMessageType.HashReject,
            Payload = ToBytes()
        };
    }

    public static HashRejectMessage FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new ArgumentException($"Data length {data.Length} is less than required header size {HeaderSize}", nameof(data));
        }

        var piecesRoot = data.Slice(0, PiecesRootSize).ToArray();
        var baseLayer = BinaryPrimitives.ReadInt32BigEndian(data.Slice(32, 4));
        var index = BinaryPrimitives.ReadInt32BigEndian(data.Slice(36, 4));
        var length = BinaryPrimitives.ReadInt32BigEndian(data.Slice(40, 4));
        var proofLayers = BinaryPrimitives.ReadInt32BigEndian(data.Slice(44, 4));

        return new HashRejectMessage(piecesRoot, baseLayer, index, length, proofLayers);
    }

    public static HashRejectMessage FromPeerMessage(PeerMessage message)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        if (message.Type != PeerMessageType.HashReject)
        {
            throw new ArgumentException($"Expected message type {PeerMessageType.HashReject} but got {message.Type}", nameof(message));
        }

        return FromBytes(message.Payload ?? Array.Empty<byte>());
    }
}
