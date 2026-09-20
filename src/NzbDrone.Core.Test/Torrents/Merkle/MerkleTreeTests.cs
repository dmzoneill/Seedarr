using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using NzbDrone.Core.Torrents.Merkle;

namespace NzbDrone.Core.Test.Torrents.Merkle;

[TestFixture]
public class MerkleTreeTests
{
    private IMerkleTreeService _service;

    [SetUp]
    public void SetUp()
    {
        _service = new MerkleTree();
    }

    [Test]
    public void EmptyFile_ComputesAllZerosRoot()
    {
        var tree = new MerkleTree(Array.Empty<byte>());
        Assert.That(tree.RootHash, Is.EqualTo(new byte[32]));
        Assert.That(tree.BlockCount, Is.EqualTo(0));
        Assert.That(tree.LeafCount, Is.EqualTo(0));
        Assert.That(tree.FileLength, Is.EqualTo(0));

        using var ms = new MemoryStream();
        var rootFromStream = MerkleTree.ComputeRoot(ms, 0);
        Assert.That(rootFromStream, Is.EqualTo(new byte[32]));

        var rootFromService = _service.ComputeRootHash(ms, 0);
        Assert.That(rootFromService, Is.EqualTo(new byte[32]));
    }

    [Test]
    public void SingleBlockFile_LessThan16KiB_MatchesDirectSha256()
    {
        var data = Encoding.UTF8.GetBytes("Hello BitTorrent v2 BEP 52 Merkle Tree Verification");
        var expectedHash = SHA256.HashData(data);

        var tree = new MerkleTree(data);
        Assert.That(tree.RootHash, Is.EqualTo(expectedHash));
        Assert.That(tree.BlockCount, Is.EqualTo(1));
        Assert.That(tree.LeafCount, Is.EqualTo(1));

        using var ms = new MemoryStream(data);
        var root = MerkleTree.ComputeRoot(ms, data.Length);
        Assert.That(root, Is.EqualTo(expectedHash));

        var rootService = _service.ComputeRootHash(data);
        Assert.That(rootService, Is.EqualTo(expectedHash));
    }

    [Test]
    public void SingleBlockFile_Exactly16KiB_MatchesDirectSha256()
    {
        var data = new byte[16384];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        var expectedHash = SHA256.HashData(data);
        var tree = new MerkleTree(data);

        Assert.That(tree.RootHash, Is.EqualTo(expectedHash));
        Assert.That(tree.BlockCount, Is.EqualTo(1));
        Assert.That(tree.LeafCount, Is.EqualTo(1));
    }

    [Test]
    public void TwoBlocks_Exactly32KiB_ComputesParentOfBothBlocks()
    {
        var block0 = new byte[16384];
        Array.Fill(block0, (byte)0xAA);
        var block1 = new byte[16384];
        Array.Fill(block1, (byte)0xBB);

        var data = new byte[32768];
        block0.CopyTo(data, 0);
        block1.CopyTo(data, 16384);

        var hash0 = SHA256.HashData(block0);
        var hash1 = SHA256.HashData(block1);

        var concat = new byte[64];
        hash0.CopyTo(concat, 0);
        hash1.CopyTo(concat, 32);
        var expectedRoot = SHA256.HashData(concat);

        var tree = new MerkleTree(data);
        Assert.That(tree.RootHash, Is.EqualTo(expectedRoot));
        Assert.That(tree.BlockCount, Is.EqualTo(2));
        Assert.That(tree.LeafCount, Is.EqualTo(2));
    }

    [Test]
    public void NonPowerOfTwo_48000Bytes_PadsLeafWithZeroHash()
    {
        var data = new byte[48000];
        Array.Fill(data, (byte)'x');

        var b0 = SHA256.HashData(data.AsSpan(0, 16384));
        var b1 = SHA256.HashData(data.AsSpan(16384, 16384));
        var b2 = SHA256.HashData(data.AsSpan(32768, 48000 - 32768));
        var b3 = new byte[32]; // balanced tree padding

        var p0Concat = new byte[64];
        b0.CopyTo(p0Concat, 0);
        b1.CopyTo(p0Concat, 32);
        var p0 = SHA256.HashData(p0Concat);

        var p1Concat = new byte[64];
        b2.CopyTo(p1Concat, 0);
        b3.CopyTo(p1Concat, 32);
        var p1 = SHA256.HashData(p1Concat);

        var rootConcat = new byte[64];
        p0.CopyTo(rootConcat, 0);
        p1.CopyTo(rootConcat, 32);
        var expectedRoot = SHA256.HashData(rootConcat);

        var tree = new MerkleTree(data);
        Assert.That(tree.RootHash, Is.EqualTo(expectedRoot));
        Assert.That(tree.BlockCount, Is.EqualTo(3));
        Assert.That(tree.LeafCount, Is.EqualTo(4));
    }

    [Test]
    public void PadLastBlockWithZeros_WhenRequested_PadsLastBlockTo16KiB()
    {
        var data = new byte[1000];
        Array.Fill(data, (byte)0x42);

        var paddedData = new byte[16384];
        data.CopyTo(paddedData, 0);
        var expectedPaddedHash = SHA256.HashData(paddedData);

        var standardTree = new MerkleTree(data, padLastBlockWithZeros: false);
        var paddedTree = new MerkleTree(data, padLastBlockWithZeros: true);

        Assert.That(standardTree.RootHash, Is.EqualTo(SHA256.HashData(data)));
        Assert.That(paddedTree.RootHash, Is.EqualTo(expectedPaddedHash));
        Assert.That(paddedTree.RootHash, Is.Not.EqualTo(standardTree.RootHash));
    }

    [Test]
    public void StreamAndSpanParity_ProducesIdenticalRoots()
    {
        var data = new byte[100000];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        using var ms = new MemoryStream(data);
        var rootFromStream = MerkleTree.ComputeRoot(ms, data.Length);
        var rootFromSpan = MerkleTree.ComputeRoot(data.AsSpan());
        var rootFromBytes = MerkleTree.ComputeRoot(data);

        Assert.That(rootFromStream, Is.EqualTo(rootFromSpan));
        Assert.That(rootFromSpan, Is.EqualTo(rootFromBytes));
    }

    [Test]
    public void GetPieceLayer_ThrowsOnInvalidPieceLength()
    {
        var data = new byte[32768];
        var tree = new MerkleTree(data);

        // Piece length < 16 KiB
        Assert.Throws<ArgumentException>(() => tree.GetPieceLayer(8192));
        // Piece length not power of 2
        Assert.Throws<ArgumentException>(() => tree.GetPieceLayer(20000));
    }

    [Test]
    public void GetPieceLayer_ReturnsEmpty_WhenFileSmallerOrEqualToPieceLength()
    {
        var data = new byte[32768];
        var tree = new MerkleTree(data);

        var layer = tree.GetPieceLayer(32768);
        Assert.That(layer, Is.Empty);

        var largerLayer = tree.GetPieceLayer(65536);
        Assert.That(largerLayer, Is.Empty);

        var serviceLayer = _service.GetPieceLayer(data, 65536);
        Assert.That(serviceLayer, Is.Empty);
    }

    [Test]
    public void GetPieceLayer_DerivesCorrectPieceRoots_ForMultiPieceFile()
    {
        // 100,000 bytes with 65,536 piece length = 2 pieces
        var data = new byte[100000];
        Array.Fill(data, (byte)'x');

        var tree = new MerkleTree(data);
        var pieceLayer = tree.GetPieceLayer(65536);

        Assert.That(pieceLayer.Length, Is.EqualTo(64)); // 2 pieces * 32 bytes

        // Piece 0 covers 4 blocks (0, 1, 2, 3)
        var piece0Data = data.AsSpan(0, 65536);
        var piece0Tree = new MerkleTree(piece0Data);
        var expectedPiece0Root = piece0Tree.RootHash;

        Assert.That(pieceLayer.AsSpan(0, 32).ToArray(), Is.EqualTo(expectedPiece0Root));

        // Piece 1 covers blocks 4, 5, 6 (with block 7 padded to 32 zero bytes)
        var b4 = SHA256.HashData(data.AsSpan(65536, 16384));
        var b5 = SHA256.HashData(data.AsSpan(65536 + 16384, 16384));
        var b6 = SHA256.HashData(data.AsSpan(65536 + 32768, 100000 - 98304));
        var b7 = new byte[32];

        var p1_0 = SHA256.HashData(b4.Concat(b5).ToArray());
        var p1_1 = SHA256.HashData(b6.Concat(b7).ToArray());
        var expectedPiece1Root = SHA256.HashData(p1_0.Concat(p1_1).ToArray());

        Assert.That(pieceLayer.AsSpan(32, 32).ToArray(), Is.EqualTo(expectedPiece1Root));

        // Balancing piece layer [Piece0, Piece1] yields the file Merkle root
        var rootFromPieces = SHA256.HashData(expectedPiece0Root.Concat(expectedPiece1Root).ToArray());
        Assert.That(tree.RootHash, Is.EqualTo(rootFromPieces));
    }

    [Test]
    public void GenerateAndVerifyBlockProof_SucceedsForValidBlock()
    {
        var data = new byte[65536]; // 4 blocks
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 179);
        }

        var tree = new MerkleTree(data);
        for (var b = 0; b < 4; b++)
        {
            var proof = tree.GenerateUncleProof(b);
            Assert.That(proof.Length, Is.EqualTo(64)); // 2 levels * 32 bytes

            var blockData = data.AsSpan(b * 16384, 16384).ToArray();
            var isValid = MerkleTree.VerifyBlock(blockData, b, proof, tree.RootHash);
            Assert.That(isValid, Is.True, $"Block {b} proof verification failed");

            var isValidService = _service.VerifyBlock(blockData, b, proof, tree.RootHash);
            Assert.That(isValidService, Is.True);
        }
    }

    [Test]
    public void VerifyBlock_FailsWhenBlockDataIsCorrupted()
    {
        var data = new byte[65536]; // 4 blocks
        Array.Fill(data, (byte)0x55);

        var tree = new MerkleTree(data);
        var proof = tree.GenerateUncleProof(1);

        var block1 = data.AsSpan(16384, 16384).ToArray();
        block1[100] ^= 0xFF; // Corrupt 1 byte

        var isValid = MerkleTree.VerifyBlock(block1, 1, proof, tree.RootHash);
        Assert.That(isValid, Is.False);
    }

    [Test]
    public void VerifyBlock_FailsWhenBlockIndexIsMismatched()
    {
        var data = new byte[65536];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i / 16384) + 1); // Distinct contents for each 16 KiB block
        }

        var tree = new MerkleTree(data);
        var proof = tree.GenerateUncleProof(1);
        var block1 = data.AsSpan(16384, 16384).ToArray();

        // Pass index 2 instead of 1
        var isValid = MerkleTree.VerifyBlock(block1, 2, proof, tree.RootHash);
        Assert.That(isValid, Is.False);
    }

    [Test]
    public void VerifyBlock_FailsOnInvalidProofDimensions()
    {
        var blockData = new byte[16384];
        var rootHash = new byte[32];

        // Root hash not 32 bytes
        Assert.That(MerkleTree.VerifyBlock(blockData, 0, new byte[32], new byte[20]), Is.False);

        // Uncle proof not a multiple of 32 bytes
        Assert.That(MerkleTree.VerifyBlock(blockData, 0, new byte[31], rootHash), Is.False);

        // Negative block index
        Assert.That(MerkleTree.VerifyBlock(blockData, -1, new byte[32], rootHash), Is.False);

        // Null block data
        Assert.That(MerkleTree.VerifyBlock((byte[])null, 0, new byte[32], rootHash), Is.False);
    }

    [Test]
    public void VerifyBlock_AgainstPieceRoot_UsingGenerateBlockProofForPiece()
    {
        var data = new byte[131072]; // 8 blocks, 2 pieces of 64 KiB
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 253);
        }

        var tree = new MerkleTree(data);
        var pieceLayer = tree.GetPieceLayer(65536);
        var piece0Root = pieceLayer.AsSpan(0, 32).ToArray();
        var piece1Root = pieceLayer.AsSpan(32, 32).ToArray();

        // Block 5 is block offset 1 within Piece 1
        var proofForPiece1 = MerkleTree.GenerateBlockProofForPiece(tree.LeafHashes, 5, 65536);
        var block5Data = data.AsSpan(5 * 16384, 16384).ToArray();

        var isValid = MerkleTree.VerifyBlock(block5Data, 1, proofForPiece1, piece1Root);
        Assert.That(isValid, Is.True);

        // Verifying against Piece 0 root should fail
        var isInvalidPiece = MerkleTree.VerifyBlock(block5Data, 1, proofForPiece1, piece0Root);
        Assert.That(isInvalidPiece, Is.False);
    }

    [Test]
    public void VerifyPieceBlocks_IsolatesOnlyCorruptedBlock_WithKnownLeafHashes()
    {
        var pieceLength = 65536; // 4 blocks
        var pieceData = new byte[pieceLength];
        for (var i = 0; i < pieceData.Length; i++)
        {
            pieceData[i] = (byte)(i % 137);
        }

        var genuineTree = new MerkleTree(pieceData);
        var knownLeaves = genuineTree.LeafHashes;

        // Corrupt only block 2
        var corruptedPiece = (byte[])pieceData.Clone();
        corruptedPiece[(2 * 16384) + 42] ^= 0xEE;

        var result = MerkleTree.VerifyPieceBlocks(
            corruptedPiece,
            pieceIndex: 0,
            pieceLength: pieceLength,
            expectedPieceRoot: genuineTree.RootHash,
            knownLeafHashes: knownLeaves);

        Assert.That(result.IsPieceValid, Is.False);
        Assert.That(result.CorruptedBlockIndices, Is.EquivalentTo(new[] { 2 }));
        Assert.That(result.ValidBlockIndices, Is.EquivalentTo(new[] { 0, 1, 3 }));
        Assert.That(result.TotalBlocks, Is.EqualTo(4));
    }

    [Test]
    public void VerifyPieceBlocks_ValidPiece_MarksAllBlocksValid()
    {
        var pieceLength = 65536;
        var pieceData = new byte[pieceLength];
        Array.Fill(pieceData, (byte)0x77);

        var tree = new MerkleTree(pieceData);

        var result = MerkleTree.VerifyPieceBlocks(
            pieceData,
            pieceIndex: 0,
            pieceLength: pieceLength,
            expectedPieceRoot: tree.RootHash);

        Assert.That(result.IsPieceValid, Is.True);
        Assert.That(result.CorruptedBlockIndices, Is.Empty);
        Assert.That(result.ValidBlockIndices, Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
        Assert.That(result.TotalBlocks, Is.EqualTo(4));
    }

    [Test]
    public void VerifyPieceBlocks_InvalidPieceWithoutKnownHashes_MarksAllBlocksCorrupted()
    {
        var pieceLength = 65536;
        var pieceData = new byte[pieceLength];
        Array.Fill(pieceData, (byte)0x88);

        var falseRoot = new byte[32];
        Array.Fill(falseRoot, (byte)0xFF);

        var result = MerkleTree.VerifyPieceBlocks(
            pieceData,
            pieceIndex: 0,
            pieceLength: pieceLength,
            expectedPieceRoot: falseRoot);

        Assert.That(result.IsPieceValid, Is.False);
        Assert.That(result.CorruptedBlockIndices, Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
        Assert.That(result.ValidBlockIndices, Is.Empty);
    }

    [Test]
    public void CalculatePieceFileBoundary_RespectsPieceAlignmentAndAvoidsCrossFileSpillover()
    {
        // 100,000 byte file with 65,536 piece length
        const long fileSize = 100000;
        const int pieceLength = 65536;

        // Piece 0 (covers bytes 0 to 65535)
        var p0 = MerkleTree.CalculatePieceFileBoundary(0, fileSize, pieceLength, 0);
        Assert.That(p0.PieceIndex, Is.EqualTo(0));
        Assert.That(p0.StartByteInFile, Is.EqualTo(0));
        Assert.That(p0.BytesInPiece, Is.EqualTo(65536));
        Assert.That(p0.SpansFileBoundary, Is.False);

        // Piece 1 (covers remaining bytes 65536 to 99999 = 34464 bytes)
        var p1 = MerkleTree.CalculatePieceFileBoundary(0, fileSize, pieceLength, 1);
        Assert.That(p1.PieceIndex, Is.EqualTo(1));
        Assert.That(p1.StartByteInFile, Is.EqualTo(65536));
        Assert.That(p1.BytesInPiece, Is.EqualTo(34464)); // Strictly clamped to file end!
        Assert.That(p1.SpansFileBoundary, Is.False);

        // Piece 2 is past file end
        var p2 = MerkleTree.CalculatePieceFileBoundary(0, fileSize, pieceLength, 2);
        Assert.That(p2.BytesInPiece, Is.EqualTo(0));

        // Piece before file start
        var pBefore = MerkleTree.CalculatePieceFileBoundary(65536, fileSize, pieceLength, 0);
        Assert.That(pBefore.BytesInPiece, Is.EqualTo(0));
    }

    [TestCase(-1, ExpectedResult = 0)]
    [TestCase(0, ExpectedResult = 0)]
    [TestCase(1, ExpectedResult = 1)]
    [TestCase(2, ExpectedResult = 2)]
    [TestCase(3, ExpectedResult = 4)]
    [TestCase(4, ExpectedResult = 4)]
    [TestCase(5, ExpectedResult = 8)]
    [TestCase(7, ExpectedResult = 8)]
    [TestCase(8, ExpectedResult = 8)]
    [TestCase(9, ExpectedResult = 16)]
    [TestCase(17, ExpectedResult = 32)]
    public int RoundUpToPowerOfTwo_CalculatesCorrectPowers(int input)
    {
        return MerkleTree.RoundUpToPowerOfTwo(input);
    }
}
