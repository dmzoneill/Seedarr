using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class PieceCacheTests
{
    private IMultiFilePieceStorage _storage;
    private Torrent _torrent;
    private List<TorrentFile> _files;

    [SetUp]
    public void SetUp()
    {
        _storage = Substitute.For<IMultiFilePieceStorage>();
        _torrent = new Torrent
        {
            Id = 1,
            Name = "TestTorrent",
            PieceLength = 32768,
            TotalSize = 65536,
            SavePath = "/test/download"
        };
        _files = new List<TorrentFile>
        {
            new TorrentFile
            {
                TorrentId = 1,
                Path = "file1.bin",
                Size = 65536
            }
        };
    }

    [Test]
    public void AddBlock_buffers_blocks_until_piece_is_complete()
    {
        var cache = new PieceCache();
        var pieceLength = 32768;
        var block1 = new byte[16384];
        var block2 = new byte[16384];
        Array.Fill(block1, (byte)0xAA);
        Array.Fill(block2, (byte)0xBB);

        // Add first block
        var isComplete1 = cache.AddBlock(1, 0, 0, block1, pieceLength);
        Assert.That(isComplete1, Is.False);
        Assert.That(cache.IsPieceComplete(1, 0), Is.False);
        Assert.That(cache.ContainsPiece(1, 0), Is.True);
        Assert.That(cache.CurrentCacheSizeBytes, Is.EqualTo(pieceLength));
        Assert.That(cache.CachedPieceCount, Is.EqualTo(1));

        // Add second block completing the piece
        var isComplete2 = cache.AddBlock(1, 0, 16384, block2, pieceLength);
        Assert.That(isComplete2, Is.True);
        Assert.That(cache.IsPieceComplete(1, 0), Is.True);

        // Check assembled buffer
        var assembled = cache.GetPieceData(1, 0);
        Assert.That(assembled, Is.Not.Null);
        Assert.That(assembled.Length, Is.EqualTo(pieceLength));
        Assert.That(assembled[0], Is.EqualTo(0xAA));
        Assert.That(assembled[16383], Is.EqualTo(0xAA));
        Assert.That(assembled[16384], Is.EqualTo(0xBB));
        Assert.That(assembled[32767], Is.EqualTo(0xBB));
    }

    [Test]
    public void AddBlock_handles_out_of_order_blocks()
    {
        var cache = new PieceCache();
        var pieceLength = 32768;
        var block1 = new byte[16384];
        var block2 = new byte[16384];
        Array.Fill(block1, (byte)0x11);
        Array.Fill(block2, (byte)0x22);

        // Add second block first
        var isComplete1 = cache.AddBlock(1, 0, 16384, block2, pieceLength);
        Assert.That(isComplete1, Is.False);
        Assert.That(cache.IsPieceComplete(1, 0), Is.False);

        // Add first block second
        var isComplete2 = cache.AddBlock(1, 0, 0, block1, pieceLength);
        Assert.That(isComplete2, Is.True);
        Assert.That(cache.IsPieceComplete(1, 0), Is.True);

        var assembled = cache.GetPieceData(1, 0);
        Assert.That(assembled[0], Is.EqualTo(0x11));
        Assert.That(assembled[16384], Is.EqualTo(0x22));
    }

    [Test]
    public void AddBlock_handles_duplicate_blocks_without_premature_completion()
    {
        var cache = new PieceCache();
        var pieceLength = 32768;
        var block1 = new byte[16384];
        Array.Fill(block1, (byte)0x55);

        // Add first block twice
        cache.AddBlock(1, 0, 0, block1, pieceLength);
        var isComplete = cache.AddBlock(1, 0, 0, block1, pieceLength);

        Assert.That(isComplete, Is.False);
        Assert.That(cache.IsPieceComplete(1, 0), Is.False);
    }

    [Test]
    public void VerifyAndFlushPiece_incomplete_piece_returns_false_and_does_not_write_to_disk()
    {
        var cache = new PieceCache();
        var pieceLength = 32768;
        var block1 = new byte[16384];
        cache.AddBlock(1, 0, 0, block1, pieceLength);

        var dummyHash = new byte[20];
        var result = cache.VerifyAndFlushPiece(1, 0, dummyHash, _storage, _torrent, _files);

        Assert.That(result, Is.False);
        _storage.DidNotReceive().WritePiece(
            Arg.Any<Torrent>(),
            Arg.Any<IList<TorrentFile>>(),
            Arg.Any<int>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<string>());

        // Cache still retains the incomplete piece
        Assert.That(cache.ContainsPiece(1, 0), Is.True);
        Assert.That(cache.IsPieceComplete(1, 0), Is.False);
    }

    [Test]
    public void VerifyAndFlushPiece_successful_sha1_verification_writes_to_disk_and_marks_complete()
    {
        var cache = new PieceCache();
        var pieceLength = 32768;
        var pieceData = new byte[pieceLength];
        new Random(42).NextBytes(pieceData);

        var expectedHash = SHA1.HashData(pieceData);

        var block1 = new byte[16384];
        var block2 = new byte[16384];
        Array.Copy(pieceData, 0, block1, 0, 16384);
        Array.Copy(pieceData, 16384, block2, 0, 16384);

        cache.AddBlock(1, 0, 0, block1, pieceLength);
        cache.AddBlock(1, 0, 16384, block2, pieceLength);

        var result = cache.VerifyAndFlushPiece(1, 0, expectedHash, _storage, _torrent, _files);

        Assert.That(result, Is.True);
        _storage.Received(1).WritePiece(_torrent, _files, 0, Arg.Is<ReadOnlyMemory<byte>>(m => m.ToArray().SequenceEqual(pieceData)), null);
        Assert.That(cache.IsPieceFlushed(1, 0), Is.True);
        Assert.That(cache.IsPieceComplete(1, 0), Is.True);
    }

    [Test]
    public void VerifyAndFlushPiece_corrupted_piece_discards_buffer_immediately_without_disk_write()
    {
        var cache = new PieceCache();
        var pieceLength = 32768;
        var pieceData = new byte[pieceLength];
        Array.Fill(pieceData, (byte)0x42);

        // Expected hash for genuine data
        var expectedHash = SHA1.HashData(pieceData);

        // Poisoned block
        var poisonedBlock = new byte[16384];
        Array.Fill(poisonedBlock, (byte)0x99);

        cache.AddBlock(1, 0, 0, pieceData.AsSpan(0, 16384).ToArray(), pieceLength);
        cache.AddBlock(1, 0, 16384, poisonedBlock, pieceLength);

        var result = cache.VerifyAndFlushPiece(1, 0, expectedHash, _storage, _torrent, _files);

        Assert.That(result, Is.False);
        _storage.DidNotReceive().WritePiece(
            Arg.Any<Torrent>(),
            Arg.Any<IList<TorrentFile>>(),
            Arg.Any<int>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<string>());

        // In-memory buffer is discarded immediately
        Assert.That(cache.ContainsPiece(1, 0), Is.False);
        Assert.That(cache.CurrentCacheSizeBytes, Is.EqualTo(0));
        Assert.That(cache.CachedPieceCount, Is.EqualTo(0));
    }

    [Test]
    public void Memory_boundary_eviction_evicts_lru_piece_when_limit_exceeded()
    {
        // 32 KiB cache limit = holds two 16 KiB pieces
        var cache = new PieceCache(maxCacheSizeBytes: 32768);
        var pieceLength = 16384;
        var block = new byte[16384];

        // Add piece 0 and piece 1
        cache.AddBlock(1, 0, 0, block, pieceLength);
        cache.AddBlock(1, 1, 0, block, pieceLength);

        Assert.That(cache.CachedPieceCount, Is.EqualTo(2));
        Assert.That(cache.CurrentCacheSizeBytes, Is.EqualTo(32768));
        Assert.That(cache.ContainsPiece(1, 0), Is.True);
        Assert.That(cache.ContainsPiece(1, 1), Is.True);

        // Add piece 2, exceeding the 32 KiB limit
        cache.AddBlock(1, 2, 0, block, pieceLength);

        // Piece 0 (oldest / LRU) is evicted
        Assert.That(cache.ContainsPiece(1, 0), Is.False);
        Assert.That(cache.ContainsPiece(1, 1), Is.True);
        Assert.That(cache.ContainsPiece(1, 2), Is.True);
        Assert.That(cache.CurrentCacheSizeBytes, Is.EqualTo(32768));
        Assert.That(cache.CachedPieceCount, Is.EqualTo(2));
    }

    [Test]
    public void Memory_boundary_eviction_prefers_evicting_flushed_piece_first()
    {
        var cache = new PieceCache(maxCacheSizeBytes: 32768);
        var pieceLength = 16384;
        var piece0Data = new byte[16384];
        Array.Fill(piece0Data, (byte)0x11);
        var hash0 = SHA1.HashData(piece0Data);

        // Add and flush piece 0 (now clean/flushed)
        cache.AddBlock(1, 0, 0, piece0Data, pieceLength);
        cache.VerifyAndFlushPiece(1, 0, hash0, _storage, _torrent, _files);
        Assert.That(cache.IsPieceFlushed(1, 0), Is.True);

        // Add piece 1 (dirty/unflushed)
        var piece1Data = new byte[8192];
        cache.AddBlock(1, 1, 0, piece1Data, pieceLength);

        // Add piece 2, triggering eviction
        var piece2Data = new byte[16384];
        cache.AddBlock(1, 2, 0, piece2Data, pieceLength);

        // Flushed piece 0 is evicted to protect unverified piece 1
        Assert.That(cache.ContainsPiece(1, 0), Is.False);
        Assert.That(cache.ContainsPiece(1, 1), Is.True);
        Assert.That(cache.ContainsPiece(1, 2), Is.True);
    }

    [Test]
    public void Memory_boundary_eviction_updates_lru_order_on_access()
    {
        var cache = new PieceCache(maxCacheSizeBytes: 32768);
        var pieceLength = 16384;
        var block = new byte[8192];

        cache.AddBlock(1, 0, 0, block, pieceLength);
        cache.AddBlock(1, 1, 0, block, pieceLength);

        // Touch piece 0 by adding second block (makes piece 1 the LRU)
        cache.AddBlock(1, 0, 8192, block, pieceLength);

        // Add piece 2, requiring eviction
        cache.AddBlock(1, 2, 0, block, pieceLength);

        // Piece 1 is evicted because piece 0 was more recently used
        Assert.That(cache.ContainsPiece(1, 0), Is.True);
        Assert.That(cache.ContainsPiece(1, 1), Is.False);
        Assert.That(cache.ContainsPiece(1, 2), Is.True);
    }

    [Test]
    public void ClearTorrent_removes_only_specified_torrent_pieces()
    {
        var cache = new PieceCache();
        var block = new byte[16384];

        cache.AddBlock(1, 0, 0, block, 16384);
        cache.AddBlock(2, 0, 0, block, 16384);

        Assert.That(cache.CachedPieceCount, Is.EqualTo(2));

        cache.ClearTorrent(1);

        Assert.That(cache.ContainsPiece(1, 0), Is.False);
        Assert.That(cache.ContainsPiece(2, 0), Is.True);
        Assert.That(cache.CachedPieceCount, Is.EqualTo(1));
        Assert.That(cache.CurrentCacheSizeBytes, Is.EqualTo(16384));
    }

    [Test]
    public void Clear_removes_all_pieces_and_resets_memory()
    {
        var cache = new PieceCache();
        var block = new byte[16384];

        cache.AddBlock(1, 0, 0, block, 16384);
        cache.AddBlock(1, 1, 0, block, 16384);

        cache.Clear();

        Assert.That(cache.CachedPieceCount, Is.EqualTo(0));
        Assert.That(cache.CurrentCacheSizeBytes, Is.EqualTo(0));
        Assert.That(cache.ContainsPiece(1, 0), Is.False);
        Assert.That(cache.ContainsPiece(1, 1), Is.False);
    }

    [Test]
    public void AddBlock_validates_arguments()
    {
        var cache = new PieceCache();
        var data = new byte[100];

        Assert.Throws<ArgumentOutOfRangeException>(() => cache.AddBlock(-1, 0, 0, data, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.AddBlock(1, -1, 0, data, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.AddBlock(1, 0, 0, data, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.AddBlock(1, 0, -1, data, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.AddBlock(1, 0, 1000, data, 1000));
        Assert.Throws<ArgumentNullException>(() => cache.AddBlock(1, 0, 0, null, 1000));
    }

    [Test]
    public void VerifyAndFlushPiece_validates_arguments()
    {
        var cache = new PieceCache();
        var validHash = new byte[20];

        Assert.That(cache.VerifyAndFlushPiece(1, 0, null, _storage, _torrent, _files), Is.False);
        Assert.That(cache.VerifyAndFlushPiece(1, 0, new byte[19], _storage, _torrent, _files), Is.False);
        Assert.That(cache.VerifyAndFlushPiece(1, 0, validHash, null, _torrent, _files), Is.False);
    }

    [Test]
    public void VerifyAndFlushPiece_with_real_MultiFilePieceStorage_synchronous_fsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "seedarr_piece_cache_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var resolver = new PieceBoundaryResolver();
            using var realStorage = new MultiFilePieceStorage(resolver);

            var torrent = new Torrent
            {
                Id = 99,
                Name = "RealStorageTest",
                PieceLength = 16384,
                TotalSize = 16384,
                SavePath = tempDir
            };

            var files = new List<TorrentFile>
            {
                new TorrentFile
                {
                    TorrentId = 99,
                    Path = "payload.bin",
                    Size = 16384
                }
            };

            var cache = new PieceCache(synchronousFsync: true);
            var testData = new byte[16384];
            new Random(123).NextBytes(testData);
            var expectedHash = SHA1.HashData(testData);

            cache.AddBlock(99, 0, 0, testData, 16384);
            var flushed = cache.VerifyAndFlushPiece(99, 0, expectedHash, realStorage, torrent, files);

            Assert.That(flushed, Is.True);

            // Read file from disk to verify data was written and fsynced
            var filePath = Path.Combine(tempDir, "payload.bin");
            Assert.That(File.Exists(filePath), Is.True);
            var diskBytes = File.ReadAllBytes(filePath);
            Assert.That(diskBytes, Is.EqualTo(testData));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Best effort
                }
            }
        }
    }
}
