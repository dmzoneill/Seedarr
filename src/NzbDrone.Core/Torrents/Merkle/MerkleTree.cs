using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;

namespace NzbDrone.Core.Torrents.Merkle;

public class BlockVerificationResult
{
    public bool IsPieceValid { get; set; }
    public List<int> CorruptedBlockIndices { get; set; } = new();
    public List<int> ValidBlockIndices { get; set; } = new();
    public int TotalBlocks { get; set; }
}

public class PieceFileBoundary
{
    public long FileOffset { get; set; }
    public long FileSize { get; set; }
    public int PieceLength { get; set; }
    public int PieceIndex { get; set; }
    public long StartByteInFile { get; set; }
    public int BytesInPiece { get; set; }
    public bool SpansFileBoundary => false;
}

public class MerkleTree : IMerkleTreeService
{
    public const int BlockSize = 16384; // 16 KiB leaf blocks per BEP 52
    public const int HashSize = 32;     // SHA-256 digest size in bytes

    public byte[] RootHash { get; }
    public long FileLength { get; }
    public int BlockCount => LeafHashes.Count;
    public int LeafCount { get; }
    public IReadOnlyList<byte[]> LeafHashes { get; }

    public MerkleTree()
    {
        RootHash = new byte[HashSize];
        LeafHashes = Array.Empty<byte[]>();
        FileLength = 0;
        LeafCount = 0;
    }

    public MerkleTree(IReadOnlyList<byte[]> leafHashes)
    {
        LeafHashes = leafHashes ?? Array.Empty<byte[]>();
        LeafCount = RoundUpToPowerOfTwo(LeafHashes.Count);
        RootHash = ComputeRootHash(LeafHashes);
        FileLength = (long)LeafHashes.Count * BlockSize;
    }

    public MerkleTree(byte[] data, bool padLastBlockWithZeros = false)
        : this(data.AsSpan(), padLastBlockWithZeros)
    {
    }

    public MerkleTree(ReadOnlySpan<byte> data, bool padLastBlockWithZeros = false)
    {
        FileLength = data.Length;
        LeafHashes = ComputeLeafHashes(data, padLastBlockWithZeros);
        LeafCount = RoundUpToPowerOfTwo(LeafHashes.Count);
        RootHash = ComputeRootHash(LeafHashes);
    }

    public MerkleTree(Stream stream, long length, bool padLastBlockWithZeros = false)
    {
        FileLength = length;
        LeafHashes = ComputeLeafHashes(stream, length, padLastBlockWithZeros);
        LeafCount = RoundUpToPowerOfTwo(LeafHashes.Count);
        RootHash = ComputeRootHash(LeafHashes);
    }

    public static MerkleTree Create(byte[] data, bool padLastBlockWithZeros = false)
    {
        return new MerkleTree(data, padLastBlockWithZeros);
    }

    public static MerkleTree Create(ReadOnlySpan<byte> data, bool padLastBlockWithZeros = false)
    {
        return new MerkleTree(data, padLastBlockWithZeros);
    }

    public static MerkleTree Create(Stream stream, long length, bool padLastBlockWithZeros = false)
    {
        return new MerkleTree(stream, length, padLastBlockWithZeros);
    }

    public static MerkleTree FromLeafHashes(IReadOnlyList<byte[]> leafHashes)
    {
        return new MerkleTree(leafHashes);
    }

    public static int RoundUpToPowerOfTwo(int value)
    {
        if (value <= 0)
        {
            return 0;
        }

        if (value == 1)
        {
            return 1;
        }

        var power = 1;
        while (power < value)
        {
            power <<= 1;
        }

        return power;
    }

    public static List<byte[]> ComputeLeafHashes(Stream stream, long length, bool padLastBlockWithZeros = false)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (length < 0)
        {
            throw new ArgumentException("Length cannot be negative.", nameof(length));
        }

        var leafHashes = new List<byte[]>();
        if (length == 0)
        {
            return leafHashes;
        }

        var buffer = new byte[BlockSize];
        var remaining = length;

        while (remaining > 0)
        {
            var toRead = (int)Math.Min((long)BlockSize, remaining);
            var totalRead = 0;

            while (totalRead < toRead)
            {
                var bytesRead = stream.Read(buffer, totalRead, toRead - totalRead);
                if (bytesRead <= 0)
                {
                    break;
                }

                totalRead += bytesRead;
            }

            if (totalRead == 0)
            {
                break;
            }

            remaining -= totalRead;

            if (padLastBlockWithZeros && totalRead < BlockSize)
            {
                Array.Clear(buffer, totalRead, BlockSize - totalRead);
                leafHashes.Add(SHA256.HashData(buffer.AsSpan(0, BlockSize)));
            }
            else
            {
                leafHashes.Add(SHA256.HashData(buffer.AsSpan(0, totalRead)));
            }
        }

        return leafHashes;
    }

    public static List<byte[]> ComputeLeafHashes(ReadOnlySpan<byte> data, bool padLastBlockWithZeros = false)
    {
        var leafHashes = new List<byte[]>();
        if (data.IsEmpty)
        {
            return leafHashes;
        }

        var remaining = data.Length;
        var offset = 0;
        var padded = padLastBlockWithZeros ? stackalloc byte[BlockSize] : default;

        while (remaining > 0)
        {
            var chunkSize = Math.Min(BlockSize, remaining);
            var chunk = data.Slice(offset, chunkSize);

            if (padLastBlockWithZeros && chunkSize < BlockSize)
            {
                padded.Clear();
                chunk.CopyTo(padded);
                leafHashes.Add(SHA256.HashData(padded));
            }
            else
            {
                leafHashes.Add(SHA256.HashData(chunk));
            }

            offset += chunkSize;
            remaining -= chunkSize;
        }

        return leafHashes;
    }

    public static byte[] ComputeRootHash(IReadOnlyList<byte[]> leafHashes)
    {
        if (leafHashes == null || leafHashes.Count == 0)
        {
            return new byte[HashSize];
        }

        if (leafHashes.Count == 1)
        {
            return (byte[])leafHashes[0].Clone();
        }

        var leafCount = RoundUpToPowerOfTwo(leafHashes.Count);
        var currentLayer = new byte[leafCount][];

        for (var i = 0; i < leafCount; i++)
        {
            currentLayer[i] = i < leafHashes.Count ? leafHashes[i] : new byte[HashSize];
        }

        Span<byte> concatBuffer = stackalloc byte[64];
        while (currentLayer.Length > 1)
        {
            var nextLayer = new byte[currentLayer.Length / 2][];
            for (var i = 0; i < nextLayer.Length; i++)
            {
                currentLayer[i * 2].CopyTo(concatBuffer[..32]);
                currentLayer[(i * 2) + 1].CopyTo(concatBuffer[32..]);
                nextLayer[i] = SHA256.HashData(concatBuffer);
            }

            currentLayer = nextLayer;
        }

        return currentLayer[0];
    }

    public static byte[] ComputeRoot(Stream fileStream, long length, bool padLastBlockWithZeros = false)
    {
        var leaves = ComputeLeafHashes(fileStream, length, padLastBlockWithZeros);
        return ComputeRootHash(leaves);
    }

    public static byte[] ComputeRoot(ReadOnlySpan<byte> data, bool padLastBlockWithZeros = false)
    {
        var leaves = ComputeLeafHashes(data, padLastBlockWithZeros);
        return ComputeRootHash(leaves);
    }

    public static byte[] ComputeRoot(byte[] data, bool padLastBlockWithZeros = false)
    {
        return ComputeRoot(data.AsSpan(), padLastBlockWithZeros);
    }

    public static byte[] GetPieceLayer(IReadOnlyList<byte[]> leafHashes, int pieceLength)
    {
        if (pieceLength < BlockSize || (pieceLength & (pieceLength - 1)) != 0)
        {
            throw new ArgumentException($"Piece length must be a power of two >= {BlockSize} bytes.", nameof(pieceLength));
        }

        if (leafHashes == null || leafHashes.Count == 0)
        {
            return Array.Empty<byte>();
        }

        var blocksPerPiece = pieceLength / BlockSize;
        if (leafHashes.Count <= blocksPerPiece)
        {
            return Array.Empty<byte>();
        }

        var pieceCount = ((leafHashes.Count + blocksPerPiece) - 1) / blocksPerPiece;
        var result = new byte[pieceCount * HashSize];

        var pieceLeaves = new byte[blocksPerPiece][];
        for (var p = 0; p < pieceCount; p++)
        {
            var startBlock = p * blocksPerPiece;
            for (var b = 0; b < blocksPerPiece; b++)
            {
                var blockIdx = startBlock + b;
                pieceLeaves[b] = blockIdx < leafHashes.Count ? leafHashes[blockIdx] : new byte[HashSize];
            }

            var pieceRoot = ComputeRootHash(pieceLeaves);
            pieceRoot.CopyTo(result.AsSpan(p * HashSize, HashSize));
        }

        return result;
    }

    public static byte[] GetPieceLayer(Stream fileStream, long length, int pieceLength, bool padLastBlockWithZeros = false)
    {
        var leaves = ComputeLeafHashes(fileStream, length, padLastBlockWithZeros);
        return GetPieceLayer(leaves, pieceLength);
    }

    public static byte[] GetPieceLayer(ReadOnlySpan<byte> data, int pieceLength, bool padLastBlockWithZeros = false)
    {
        var leaves = ComputeLeafHashes(data, padLastBlockWithZeros);
        return GetPieceLayer(leaves, pieceLength);
    }

    public static byte[] GetPieceLayer(byte[] data, int pieceLength, bool padLastBlockWithZeros = false)
    {
        return GetPieceLayer(data.AsSpan(), pieceLength, padLastBlockWithZeros);
    }

    public static byte[] GenerateBlockProof(IReadOnlyList<byte[]> leafHashes, int blockIndex, int targetLayer = 0)
    {
        if (leafHashes == null || leafHashes.Count == 0)
        {
            return Array.Empty<byte>();
        }

        if (blockIndex < 0 || blockIndex >= leafHashes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(blockIndex), $"Block index {blockIndex} is out of range [0, {leafHashes.Count}).");
        }

        var leafCount = RoundUpToPowerOfTwo(leafHashes.Count);
        if (leafCount == 1)
        {
            return Array.Empty<byte>();
        }

        var layers = new List<byte[][]>();
        var currentLayer = new byte[leafCount][];
        for (var i = 0; i < leafCount; i++)
        {
            currentLayer[i] = i < leafHashes.Count ? leafHashes[i] : new byte[HashSize];
        }

        layers.Add(currentLayer);

        Span<byte> concatBuffer = stackalloc byte[64];
        while (currentLayer.Length > 1)
        {
            var nextLayer = new byte[currentLayer.Length / 2][];
            for (var i = 0; i < nextLayer.Length; i++)
            {
                currentLayer[i * 2].CopyTo(concatBuffer[..32]);
                currentLayer[(i * 2) + 1].CopyTo(concatBuffer[32..]);
                nextLayer[i] = SHA256.HashData(concatBuffer);
            }

            layers.Add(nextLayer);
            currentLayer = nextLayer;
        }

        var maxLayers = layers.Count - 1;
        var proofLayersCount = targetLayer > 0 ? Math.Min(targetLayer, maxLayers) : maxLayers;

        var proof = new byte[proofLayersCount * HashSize];
        var cursor = blockIndex;

        for (var level = 0; level < proofLayersCount; level++)
        {
            var siblingIndex = cursor ^ 1;
            var uncleHash = layers[level][siblingIndex];
            uncleHash.CopyTo(proof.AsSpan(level * HashSize, HashSize));
            cursor >>= 1;
        }

        return proof;
    }

    public byte[] GetHashesForRequest(int baseLayer, int index, int length, int proofLayers)
    {
        return GetHashesForRequest(LeafHashes, baseLayer, index, length, proofLayers);
    }

    public static byte[] GetHashesForRequest(IReadOnlyList<byte[]> leafHashes, int baseLayer, int index, int length, int proofLayers)
    {
        if (baseLayer < 0 || index < 0 || length < 2 || proofLayers < 0)
        {
            return null;
        }

        if ((length & (length - 1)) != 0 || (index % length) != 0)
        {
            return null;
        }

        if (leafHashes == null || leafHashes.Count == 0)
        {
            return null;
        }

        var leafCount = RoundUpToPowerOfTwo(leafHashes.Count);
        var layers = new List<byte[][]>();
        var currentLayer = new byte[leafCount][];
        for (var i = 0; i < leafCount; i++)
        {
            currentLayer[i] = i < leafHashes.Count ? leafHashes[i] : new byte[HashSize];
        }

        layers.Add(currentLayer);

        Span<byte> concatBuffer = stackalloc byte[64];
        while (currentLayer.Length > 1)
        {
            var nextLayer = new byte[currentLayer.Length / 2][];
            for (var i = 0; i < nextLayer.Length; i++)
            {
                currentLayer[i * 2].CopyTo(concatBuffer[..32]);
                currentLayer[(i * 2) + 1].CopyTo(concatBuffer[32..]);
                nextLayer[i] = SHA256.HashData(concatBuffer);
            }

            layers.Add(nextLayer);
            currentLayer = nextLayer;
        }

        if (baseLayer >= layers.Count)
        {
            return null;
        }

        var targetLayerNodes = layers[baseLayer];
        if (index + length > targetLayerNodes.Length)
        {
            return null;
        }

        var resultList = new List<byte[]>(length + proofLayers);
        for (var i = index; i < index + length; i++)
        {
            resultList.Add(targetLayerNodes[i]);
        }

        var impliedLayers = BitOperations.TrailingZeroCount(length);
        for (var p = impliedLayers; p <= proofLayers; p++)
        {
            var layerIdx = baseLayer + p;
            if (layerIdx >= layers.Count)
            {
                break;
            }

            var nodeIdx = index >> p;
            var siblingIdx = nodeIdx ^ 1;
            if (siblingIdx < layers[layerIdx].Length)
            {
                resultList.Add(layers[layerIdx][siblingIdx]);
            }
        }

        var totalBytes = new byte[resultList.Count * HashSize];
        for (var i = 0; i < resultList.Count; i++)
        {
            Array.Copy(resultList[i], 0, totalBytes, i * HashSize, HashSize);
        }

        return totalBytes;
    }

    public static byte[] GenerateBlockProofForPiece(IReadOnlyList<byte[]> leafHashes, int blockIndex, int pieceLength)
    {
        if (pieceLength < BlockSize || (pieceLength & (pieceLength - 1)) != 0)
        {
            throw new ArgumentException($"Piece length must be a power of two >= {BlockSize} bytes.", nameof(pieceLength));
        }

        var blocksPerPiece = pieceLength / BlockSize;
        var levelsInPiece = BitOperations.TrailingZeroCount(blocksPerPiece);
        if (levelsInPiece == 0)
        {
            return Array.Empty<byte>();
        }

        var pieceIndex = blockIndex / blocksPerPiece;
        var startBlock = pieceIndex * blocksPerPiece;

        var pieceLeaves = new byte[blocksPerPiece][];
        for (var b = 0; b < blocksPerPiece; b++)
        {
            var idx = startBlock + b;
            pieceLeaves[b] = (leafHashes != null && idx < leafHashes.Count) ? leafHashes[idx] : new byte[HashSize];
        }

        var blockOffsetInPiece = blockIndex % blocksPerPiece;
        return GenerateBlockProof(pieceLeaves, blockOffsetInPiece, levelsInPiece);
    }

    public static bool VerifyBlock(
        byte[] blockData,
        int blockIndex,
        ReadOnlySpan<byte> uncleHashes,
        ReadOnlySpan<byte> rootHash)
    {
        if (blockData == null)
        {
            return false;
        }

        return VerifyBlock(blockData.AsSpan(), blockIndex, uncleHashes, rootHash);
    }

    public static bool VerifyBlock(
        ReadOnlySpan<byte> blockData,
        int blockIndex,
        ReadOnlySpan<byte> uncleHashes,
        ReadOnlySpan<byte> expectedRoot,
        bool padBlockTo16KiB = false)
    {
        if (expectedRoot.Length != HashSize || uncleHashes.Length % HashSize != 0 || blockIndex < 0)
        {
            return false;
        }

        Span<byte> currentHash = stackalloc byte[HashSize];
        if (padBlockTo16KiB && blockData.Length < BlockSize)
        {
            Span<byte> padded = stackalloc byte[BlockSize];
            padded.Clear();
            blockData.CopyTo(padded);
            SHA256.HashData(padded, currentHash);
        }
        else
        {
            SHA256.HashData(blockData, currentHash);
        }

        if (uncleHashes.IsEmpty)
        {
            return CryptographicOperations.FixedTimeEquals(currentHash, expectedRoot);
        }

        Span<byte> parentBuffer = stackalloc byte[64];
        var uncleCount = uncleHashes.Length / HashSize;
        var cursor = blockIndex;

        for (var i = 0; i < uncleCount; i++)
        {
            var uncle = uncleHashes.Slice(i * HashSize, HashSize);
            if ((cursor & 1) == 0)
            {
                currentHash.CopyTo(parentBuffer[..32]);
                uncle.CopyTo(parentBuffer[32..]);
            }
            else
            {
                uncle.CopyTo(parentBuffer[..32]);
                currentHash.CopyTo(parentBuffer[32..]);
            }

            SHA256.HashData(parentBuffer, currentHash);
            cursor >>= 1;
        }

        return CryptographicOperations.FixedTimeEquals(currentHash, expectedRoot);
    }

    public static BlockVerificationResult VerifyPieceBlocks(
        byte[] pieceData,
        int pieceIndex,
        int pieceLength,
        ReadOnlySpan<byte> expectedPieceRoot,
        IReadOnlyList<byte[]> knownLeafHashes = null,
        bool padBlockTo16KiB = false)
    {
        if (pieceData == null || expectedPieceRoot.Length != HashSize || pieceLength < BlockSize)
        {
            return new BlockVerificationResult { IsPieceValid = false };
        }

        var blockCount = ((pieceData.Length + BlockSize) - 1) / BlockSize;
        var blocksPerPiece = pieceLength / BlockSize;
        var result = new BlockVerificationResult
        {
            TotalBlocks = blockCount
        };

        if (blockCount == 0)
        {
            result.IsPieceValid = false;
            return result;
        }

        var actualLeafHashes = new List<byte[]>(blockCount);
        var padded = padBlockTo16KiB ? stackalloc byte[BlockSize] : default;

        for (var k = 0; k < blockCount; k++)
        {
            var offset = k * BlockSize;
            var len = Math.Min(BlockSize, pieceData.Length - offset);
            var blockSlice = pieceData.AsSpan(offset, len);

            byte[] leafHash;
            if (padBlockTo16KiB && len < BlockSize)
            {
                padded.Clear();
                blockSlice.CopyTo(padded);
                leafHash = SHA256.HashData(padded);
            }
            else
            {
                leafHash = SHA256.HashData(blockSlice);
            }

            actualLeafHashes.Add(leafHash);
        }

        if (knownLeafHashes != null && knownLeafHashes.Count >= blockCount)
        {
            for (var k = 0; k < blockCount; k++)
            {
                if (CryptographicOperations.FixedTimeEquals(actualLeafHashes[k], knownLeafHashes[k]))
                {
                    result.ValidBlockIndices.Add(k);
                }
                else
                {
                    result.CorruptedBlockIndices.Add(k);
                }
            }

            result.IsPieceValid = result.CorruptedBlockIndices.Count == 0;
            return result;
        }

        var pieceLeaves = new byte[blocksPerPiece][];
        for (var b = 0; b < blocksPerPiece; b++)
        {
            pieceLeaves[b] = b < blockCount ? actualLeafHashes[b] : new byte[HashSize];
        }

        var computedPieceRoot = ComputeRootHash(pieceLeaves);
        var matches = CryptographicOperations.FixedTimeEquals(computedPieceRoot, expectedPieceRoot);

        if (matches)
        {
            result.IsPieceValid = true;
            result.ValidBlockIndices.AddRange(Enumerable.Range(0, blockCount));
        }
        else
        {
            result.IsPieceValid = false;
            if (blockCount == 1)
            {
                result.CorruptedBlockIndices.Add(0);
            }
            else
            {
                result.CorruptedBlockIndices.AddRange(Enumerable.Range(0, blockCount));
            }
        }

        return result;
    }

    public static PieceFileBoundary CalculatePieceFileBoundary(long fileOffset, long fileSize, int pieceLength, int pieceIndex)
    {
        if (pieceLength <= 0)
        {
            throw new ArgumentException("Piece length must be greater than 0.", nameof(pieceLength));
        }

        if (fileSize < 0)
        {
            throw new ArgumentException("File size cannot be negative.", nameof(fileSize));
        }

        var fileStartPiece = (int)(fileOffset / pieceLength);
        var pieceOffsetInFile = pieceIndex - fileStartPiece;
        var startByteInFile = (long)pieceOffsetInFile * pieceLength;

        if (pieceOffsetInFile < 0 || startByteInFile >= fileSize)
        {
            return new PieceFileBoundary
            {
                FileOffset = fileOffset,
                FileSize = fileSize,
                PieceLength = pieceLength,
                PieceIndex = pieceIndex,
                StartByteInFile = startByteInFile,
                BytesInPiece = 0
            };
        }

        var bytesRemainingInFile = fileSize - startByteInFile;
        var bytesInPiece = (int)Math.Min((long)pieceLength, bytesRemainingInFile);

        return new PieceFileBoundary
        {
            FileOffset = fileOffset,
            FileSize = fileSize,
            PieceLength = pieceLength,
            PieceIndex = pieceIndex,
            StartByteInFile = startByteInFile,
            BytesInPiece = bytesInPiece
        };
    }

    byte[] IMerkleTreeService.ComputeRootHash(Stream fileStream, long length, bool padLastBlockWithZeros)
    {
        return ComputeRoot(fileStream, length, padLastBlockWithZeros);
    }

    byte[] IMerkleTreeService.ComputeRootHash(ReadOnlySpan<byte> data, bool padLastBlockWithZeros)
    {
        return ComputeRoot(data, padLastBlockWithZeros);
    }

    byte[] IMerkleTreeService.ComputeRootHash(byte[] data, bool padLastBlockWithZeros)
    {
        return ComputeRoot(data, padLastBlockWithZeros);
    }

    public byte[] GetPieceLayer(int pieceLength)
    {
        return GetPieceLayer(LeafHashes, pieceLength);
    }

    byte[] IMerkleTreeService.GetPieceLayer(Stream fileStream, long length, int pieceLength, bool padLastBlockWithZeros)
    {
        return GetPieceLayer(fileStream, length, pieceLength, padLastBlockWithZeros);
    }

    byte[] IMerkleTreeService.GetPieceLayer(ReadOnlySpan<byte> data, int pieceLength, bool padLastBlockWithZeros)
    {
        return GetPieceLayer(data, pieceLength, padLastBlockWithZeros);
    }

    byte[] IMerkleTreeService.GetPieceLayer(byte[] data, int pieceLength, bool padLastBlockWithZeros)
    {
        return GetPieceLayer(data, pieceLength, padLastBlockWithZeros);
    }

    byte[] IMerkleTreeService.GenerateBlockProof(Stream fileStream, long length, int blockIndex, int targetLayer, bool padLastBlockWithZeros)
    {
        var leaves = ComputeLeafHashes(fileStream, length, padLastBlockWithZeros);
        return GenerateBlockProof(leaves, blockIndex, targetLayer);
    }

    byte[] IMerkleTreeService.GenerateBlockProof(byte[] data, int blockIndex, int targetLayer, bool padLastBlockWithZeros)
    {
        var leaves = ComputeLeafHashes(data.AsSpan(), padLastBlockWithZeros);
        return GenerateBlockProof(leaves, blockIndex, targetLayer);
    }

    public byte[] GenerateUncleProof(int blockIndex, int targetLayer = 0)
    {
        return GenerateBlockProof(LeafHashes, blockIndex, targetLayer);
    }

    bool IMerkleTreeService.VerifyBlock(byte[] blockData, int blockIndex, ReadOnlySpan<byte> uncleHashes, ReadOnlySpan<byte> rootHash)
    {
        return VerifyBlock(blockData, blockIndex, uncleHashes, rootHash);
    }

    bool IMerkleTreeService.VerifyBlock(ReadOnlySpan<byte> blockData, int blockIndex, ReadOnlySpan<byte> uncleHashes, ReadOnlySpan<byte> rootHash)
    {
        return VerifyBlock(blockData, blockIndex, uncleHashes, rootHash);
    }

    BlockVerificationResult IMerkleTreeService.VerifyPieceBlocks(byte[] pieceData, int pieceIndex, int pieceLength, ReadOnlySpan<byte> expectedPieceRoot, IReadOnlyList<byte[]> knownLeafHashes)
    {
        return VerifyPieceBlocks(pieceData, pieceIndex, pieceLength, expectedPieceRoot, knownLeafHashes);
    }

    PieceFileBoundary IMerkleTreeService.CalculatePieceFileBoundary(long fileOffset, long fileSize, int pieceLength, int pieceIndex)
    {
        return CalculatePieceFileBoundary(fileOffset, fileSize, pieceLength, pieceIndex);
    }
}
