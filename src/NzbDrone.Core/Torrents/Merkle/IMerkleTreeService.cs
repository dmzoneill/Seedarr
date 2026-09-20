using System;
using System.Collections.Generic;
using System.IO;

namespace NzbDrone.Core.Torrents.Merkle;

public interface IMerkleTreeService
{
    byte[] ComputeRootHash(Stream fileStream, long length, bool padLastBlockWithZeros = false);

    byte[] ComputeRootHash(ReadOnlySpan<byte> data, bool padLastBlockWithZeros = false);

    byte[] ComputeRootHash(byte[] data, bool padLastBlockWithZeros = false);

    byte[] GetPieceLayer(Stream fileStream, long length, int pieceLength, bool padLastBlockWithZeros = false);

    byte[] GetPieceLayer(ReadOnlySpan<byte> data, int pieceLength, bool padLastBlockWithZeros = false);

    byte[] GetPieceLayer(byte[] data, int pieceLength, bool padLastBlockWithZeros = false);

    byte[] GenerateBlockProof(Stream fileStream, long length, int blockIndex, int targetLayer = 0, bool padLastBlockWithZeros = false);

    byte[] GenerateBlockProof(byte[] data, int blockIndex, int targetLayer = 0, bool padLastBlockWithZeros = false);

    bool VerifyBlock(byte[] blockData, int blockIndex, ReadOnlySpan<byte> uncleHashes, ReadOnlySpan<byte> rootHash);

    bool VerifyBlock(ReadOnlySpan<byte> blockData, int blockIndex, ReadOnlySpan<byte> uncleHashes, ReadOnlySpan<byte> rootHash);

    BlockVerificationResult VerifyPieceBlocks(byte[] pieceData, int pieceIndex, int pieceLength, ReadOnlySpan<byte> expectedPieceRoot, IReadOnlyList<byte[]> knownLeafHashes = null);

    PieceFileBoundary CalculatePieceFileBoundary(long fileOffset, long fileSize, int pieceLength, int pieceIndex);
}
