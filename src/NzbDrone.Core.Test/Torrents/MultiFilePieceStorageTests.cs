using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class MultiFilePieceStorageTests
{
    private string _tempDir;
    private PieceBoundaryResolver _resolver;
    private MultiFilePieceStorage _storage;
    private IPieceStorage _pieceStorage;
    private IPiecePicker _piecePicker;
    private PieceVerificationService _verificationService;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_storage_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _resolver = new PieceBoundaryResolver();
        _storage = new MultiFilePieceStorage(_resolver);
        _pieceStorage = Substitute.For<IPieceStorage>();
        _piecePicker = Substitute.For<IPiecePicker>();
        _verificationService = new PieceVerificationService(_pieceStorage, _piecePicker, null, _storage);
    }

    [TearDown]
    public void TearDown()
    {
        _storage?.Dispose();

        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Test]
    public void WritePiece_across_two_files_and_read_it_back()
    {
        var torrent = new Torrent
        {
            InfoHash = "hash123",
            PieceLength = 1000,
            TotalSize = 2000,
            SavePath = _tempDir
        };

        var fileA = new TorrentFile { Path = "folder/fileA.bin", Size = 600 };
        var fileB = new TorrentFile { Path = "folder/fileB.bin", Size = 1400 };
        var files = new List<TorrentFile> { fileA, fileB };

        // Piece 0 spans File A (600 bytes) and File B (400 bytes)
        var sourceBuffer = new byte[1000];
        for (var i = 0; i < sourceBuffer.Length; i++)
        {
            sourceBuffer[i] = (byte)(i % 256);
        }

        _storage.WritePiece(torrent, files, 0, sourceBuffer);

        var fileAPath = Path.Combine(_tempDir, "folder", "fileA.bin");
        var fileBPath = Path.Combine(_tempDir, "folder", "fileB.bin");

        Assert.That(File.Exists(fileAPath), Is.True);
        Assert.That(File.Exists(fileBPath), Is.True);
        Assert.That(new FileInfo(fileAPath).Length, Is.EqualTo(600));
        Assert.That(new FileInfo(fileBPath).Length, Is.EqualTo(400));

        var readBuffer = new byte[1000];
        var bytesRead = _storage.ReadPiece(torrent, files, 0, readBuffer);

        Assert.That(bytesRead, Is.EqualTo(1000));
        Assert.That(readBuffer, Is.EqualTo(sourceBuffer));
    }

    [Test]
    public void ReadPiece_when_file_missing_returns_partial_zeroed_buffer_gracefully()
    {
        var torrent = new Torrent
        {
            InfoHash = "hash456",
            PieceLength = 1000,
            TotalSize = 2000,
            SavePath = _tempDir
        };

        var fileA = new TorrentFile { Path = "present.bin", Size = 500 };
        var fileB = new TorrentFile { Path = "missing.bin", Size = 1500 };
        var files = new List<TorrentFile> { fileA, fileB };

        var presentData = new byte[500];
        Array.Fill(presentData, (byte)0xAB);
        File.WriteAllBytes(Path.Combine(_tempDir, "present.bin"), presentData);

        var destinationBuffer = new byte[1000];
        Array.Fill(destinationBuffer, (byte)0xFF);

        var bytesRead = _storage.ReadPiece(torrent, files, 0, destinationBuffer);

        Assert.That(bytesRead, Is.EqualTo(500));
        Assert.That(destinationBuffer.AsSpan(0, 500).ToArray(), Is.EqualTo(presentData));
        Assert.That(destinationBuffer.AsSpan(500, 500).ToArray(), Is.EqualTo(new byte[500]));
    }

    [Test]
    public void ReadPiece_when_file_is_shorter_than_expected_returns_partial_zeroed_buffer()
    {
        var torrent = new Torrent
        {
            InfoHash = "hash789",
            PieceLength = 1000,
            TotalSize = 1000,
            SavePath = _tempDir
        };

        var file = new TorrentFile { Path = "truncated.bin", Size = 1000 };
        var files = new List<TorrentFile> { file };

        var partialData = new byte[300];
        Array.Fill(partialData, (byte)0x42);
        File.WriteAllBytes(Path.Combine(_tempDir, "truncated.bin"), partialData);

        var destinationBuffer = new byte[1000];
        Array.Fill(destinationBuffer, (byte)0xFF);

        var bytesRead = _storage.ReadPiece(torrent, files, 0, destinationBuffer);

        Assert.That(bytesRead, Is.EqualTo(300));
        Assert.That(destinationBuffer.AsSpan(0, 300).ToArray(), Is.EqualTo(partialData));
        Assert.That(destinationBuffer.AsSpan(300, 700).ToArray(), Is.EqualTo(new byte[700]));
    }

    [Test]
    public void VerifyPieceFromStorage_end_to_end_verification_success()
    {
        var torrent = new Torrent
        {
            InfoHash = "info123",
            PieceLength = 800,
            TotalSize = 1500,
            SavePath = _tempDir
        };

        var fileA = new TorrentFile { Path = "sub/fileA.dat", Size = 500 };
        var fileB = new TorrentFile { Path = "sub/fileB.dat", Size = 1000 };
        var files = new List<TorrentFile> { fileA, fileB };

        var piece0Data = new byte[800];
        for (var i = 0; i < piece0Data.Length; i++)
        {
            piece0Data[i] = (byte)((i * 3) % 256);
        }

        _storage.WritePiece(torrent, files, 0, piece0Data);

        var expectedHash0 = SHA1.HashData(piece0Data);

        var result = _verificationService.VerifyPieceFromStorage(torrent, files, 0, expectedHash0);

        Assert.That(result, Is.True);
        _pieceStorage.Received(1).MarkPieceVerified("info123", 0, 800);
        _piecePicker.Received(1).MarkPieceInactive("info123", 0);
        _pieceStorage.DidNotReceive().MarkPieceCorrupted(Arg.Any<string>(), Arg.Any<int>());
    }

    [Test]
    public void VerifyPieceFromStorage_with_concatenated_hashes_buffer()
    {
        var torrent = new Torrent
        {
            InfoHash = "info456",
            PieceLength = 500,
            TotalSize = 1000,
            SavePath = _tempDir
        };

        var fileA = new TorrentFile { Path = "f1.dat", Size = 500 };
        var fileB = new TorrentFile { Path = "f2.dat", Size = 500 };
        var files = new List<TorrentFile> { fileA, fileB };

        var piece0 = Encoding.UTF8.GetBytes(new string('A', 500));
        var piece1 = Encoding.UTF8.GetBytes(new string('B', 500));

        _storage.WritePiece(torrent, files, 0, piece0);
        _storage.WritePiece(torrent, files, 1, piece1);

        var allHashes = new byte[40];
        SHA1.HashData(piece0).CopyTo(allHashes.AsSpan(0, 20));
        SHA1.HashData(piece1).CopyTo(allHashes.AsSpan(20, 20));

        var result0 = _verificationService.VerifyPieceFromStorage(torrent, files, 0, allHashes);
        var result1 = _verificationService.VerifyPieceFromStorage(torrent, files, 1, allHashes);

        Assert.That(result0, Is.True);
        Assert.That(result1, Is.True);
        _pieceStorage.Received(1).MarkPieceVerified("info456", 0, 500);
        _pieceStorage.Received(1).MarkPieceVerified("info456", 1, 500);
    }

    [Test]
    public void VerifyPieceFromStorage_fails_when_file_corrupted_or_mismatched()
    {
        var torrent = new Torrent
        {
            InfoHash = "info789",
            PieceLength = 500,
            TotalSize = 500,
            SavePath = _tempDir
        };

        var file = new TorrentFile { Path = "data.bin", Size = 500 };
        var files = new List<TorrentFile> { file };

        var correctData = Encoding.UTF8.GetBytes(new string('X', 500));
        var expectedHash = SHA1.HashData(correctData);

        var corruptData = Encoding.UTF8.GetBytes(new string('Z', 500));
        _storage.WritePiece(torrent, files, 0, corruptData);

        var result = _verificationService.VerifyPieceFromStorage(torrent, files, 0, expectedHash);

        Assert.That(result, Is.False);
        _pieceStorage.Received(1).MarkPieceCorrupted("info789", 0);
        _piecePicker.Received(1).MarkPieceInactive("info789", 0);
        _pieceStorage.DidNotReceive().MarkPieceVerified(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<long>());
    }

    [Test]
    public void FileHandlePool_reuses_open_handles_for_same_file()
    {
        using var pool = new FileHandlePool(maxCapacity: 10);
        var testFile = Path.Combine(_tempDir, "reuse_test.bin");
        File.WriteAllBytes(testFile, new byte[100]);

        var handle1 = pool.GetOrCreateHandle(testFile, writeAccess: true);
        var handle2 = pool.GetOrCreateHandle(testFile, writeAccess: false);

        Assert.That(pool.Count, Is.EqualTo(1));
        Assert.That(ReferenceEquals(handle1, handle2), Is.True);
        Assert.That(handle1.IsClosed, Is.False);
        Assert.That(handle1.IsInvalid, Is.False);
    }

    [Test]
    public void FileHandlePool_evicts_least_recently_used_handle_when_capacity_exceeded()
    {
        using var pool = new FileHandlePool(maxCapacity: 2);
        var f1 = Path.Combine(_tempDir, "lru1.bin");
        var f2 = Path.Combine(_tempDir, "lru2.bin");
        var f3 = Path.Combine(_tempDir, "lru3.bin");

        File.WriteAllBytes(f1, new byte[10]);
        File.WriteAllBytes(f2, new byte[10]);
        File.WriteAllBytes(f3, new byte[10]);

        var h1 = pool.GetOrCreateHandle(f1, writeAccess: true);
        var h2 = pool.GetOrCreateHandle(f2, writeAccess: true);

        Assert.That(pool.Count, Is.EqualTo(2));
        Assert.That(pool.Contains(f1), Is.True);
        Assert.That(pool.Contains(f2), Is.True);
        Assert.That(h1.IsClosed, Is.False);

        // Accessing f3 should evict f1 (least recently used)
        var h3 = pool.GetOrCreateHandle(f3, writeAccess: true);

        Assert.That(pool.Count, Is.EqualTo(2));
        Assert.That(pool.Contains(f1), Is.False);
        Assert.That(pool.Contains(f2), Is.True);
        Assert.That(pool.Contains(f3), Is.True);
        Assert.That(h1.IsClosed, Is.True);
        Assert.That(h2.IsClosed, Is.False);
        Assert.That(h3.IsClosed, Is.False);
    }

    [Test]
    public void FileHandlePool_promotes_recently_used_handle_preventing_its_eviction()
    {
        using var pool = new FileHandlePool(maxCapacity: 2);
        var f1 = Path.Combine(_tempDir, "promote1.bin");
        var f2 = Path.Combine(_tempDir, "promote2.bin");
        var f3 = Path.Combine(_tempDir, "promote3.bin");

        File.WriteAllBytes(f1, new byte[10]);
        File.WriteAllBytes(f2, new byte[10]);
        File.WriteAllBytes(f3, new byte[10]);

        var h1 = pool.GetOrCreateHandle(f1, writeAccess: true);
        var h2 = pool.GetOrCreateHandle(f2, writeAccess: true);

        // Re-accessing f1 moves it to MRU, so f2 becomes the LRU
        var h1Reaccessed = pool.GetOrCreateHandle(f1, writeAccess: false);
        Assert.That(ReferenceEquals(h1, h1Reaccessed), Is.True);

        // Accessing f3 should evict f2, not f1
        var h3 = pool.GetOrCreateHandle(f3, writeAccess: true);

        Assert.That(pool.Count, Is.EqualTo(2));
        Assert.That(pool.Contains(f1), Is.True);
        Assert.That(pool.Contains(f2), Is.False);
        Assert.That(pool.Contains(f3), Is.True);
        Assert.That(h2.IsClosed, Is.True);
        Assert.That(h1.IsClosed, Is.False);
        Assert.That(h3.IsClosed, Is.False);
    }

    [Test]
    public void MultiFilePieceStorage_with_limited_handle_pool_handles_LRU_eviction_seamlessly()
    {
        using var pool = new FileHandlePool(maxCapacity: 2);
        using var storage = new MultiFilePieceStorage(_resolver, pool);

        var torrent = new Torrent
        {
            InfoHash = "pool_lru_test",
            PieceLength = 100,
            TotalSize = 400,
            SavePath = _tempDir
        };

        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "f1.dat", Size = 100 },
            new TorrentFile { Path = "f2.dat", Size = 100 },
            new TorrentFile { Path = "f3.dat", Size = 100 },
            new TorrentFile { Path = "f4.dat", Size = 100 }
        };

        var data = new byte[400];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i & 0xFF);
        }

        // Write piece 0 (f1), piece 1 (f2), piece 2 (f3), piece 3 (f4) -> triggers LRU evictions in pool of size 2
        storage.WritePiece(torrent, files, 0, data.AsMemory(0, 100));
        storage.WritePiece(torrent, files, 1, data.AsMemory(100, 100));
        storage.WritePiece(torrent, files, 2, data.AsMemory(200, 100));
        storage.WritePiece(torrent, files, 3, data.AsMemory(300, 100));

        Assert.That(pool.Count, Is.EqualTo(2));

        // Read all pieces back and verify data integrity
        for (var p = 0; p < 4; p++)
        {
            var readBuf = new byte[100];
            var bytesRead = storage.ReadPiece(torrent, files, p, readBuf);
            Assert.That(bytesRead, Is.EqualTo(100));
            Assert.That(readBuf, Is.EqualTo(data.AsSpan(p * 100, 100).ToArray()));
        }

        Assert.That(pool.Count, Is.EqualTo(2));
    }

    [Test]
    public void MultiFilePieceStorage_concurrent_thread_safe_reads_and_writes()
    {
        using var pool = new FileHandlePool(maxCapacity: 4);
        using var storage = new MultiFilePieceStorage(_resolver, pool);

        var torrent = new Torrent
        {
            InfoHash = "concurrent_io_test",
            PieceLength = 256,
            TotalSize = 1024,
            SavePath = _tempDir
        };

        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "conc/f1.dat", Size = 256 },
            new TorrentFile { Path = "conc/f2.dat", Size = 256 },
            new TorrentFile { Path = "conc/f3.dat", Size = 256 },
            new TorrentFile { Path = "conc/f4.dat", Size = 256 }
        };

        // Write initial data for all 4 pieces
        for (var p = 0; p < 4; p++)
        {
            var buf = new byte[256];
            Array.Fill(buf, (byte)(p + 1));
            storage.WritePiece(torrent, files, p, buf);
        }

        // Run concurrent reads and writes across all 4 pieces from multiple threads
        Parallel.For(0, 50, i =>
        {
            var pieceIndex = i % 4;
            if (i % 2 == 0)
            {
                var writeBuf = new byte[256];
                Array.Fill(writeBuf, (byte)(pieceIndex + 1));
                storage.WritePiece(torrent, files, pieceIndex, writeBuf);
            }
            else
            {
                var readBuf = new byte[256];
                var read = storage.ReadPiece(torrent, files, pieceIndex, readBuf);
                Assert.That(read, Is.EqualTo(256));
                Assert.That(readBuf[0], Is.EqualTo((byte)(pieceIndex + 1)));
            }
        });
    }

    [Test]
    public void FileShare_ReadWrite_allows_concurrent_external_access()
    {
        var testFile = Path.Combine(_tempDir, "share_test.bin");
        var initialData = new byte[] { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(testFile, initialData);

        using var pool = new FileHandlePool(maxCapacity: 10);
        var pooledHandle = pool.GetOrCreateHandle(testFile, writeAccess: true);

        // Open externally with FileShare.ReadWrite concurrently
        using var externalHandle = File.OpenHandle(testFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var readBuffer = new byte[5];
        var read = RandomAccess.Read(externalHandle, readBuffer, 0);
        Assert.That(read, Is.EqualTo(5));
        Assert.That(readBuffer, Is.EqualTo(initialData));

        var updateBytes = new byte[] { 9, 9 };
        RandomAccess.Write(externalHandle, updateBytes, 0);

        var pooledReadBuffer = new byte[2];
        RandomAccess.Read(pooledHandle, pooledReadBuffer, 0);
        Assert.That(pooledReadBuffer, Is.EqualTo(updateBytes));
    }

    [Test]
    public void FileHandlePool_Dispose_closes_all_handles()
    {
        var pool = new FileHandlePool(maxCapacity: 5);
        var f1 = Path.Combine(_tempDir, "disp1.bin");
        var f2 = Path.Combine(_tempDir, "disp2.bin");
        File.WriteAllBytes(f1, new byte[10]);
        File.WriteAllBytes(f2, new byte[10]);

        var h1 = pool.GetOrCreateHandle(f1, writeAccess: true);
        var h2 = pool.GetOrCreateHandle(f2, writeAccess: true);

        Assert.That(h1.IsClosed, Is.False);
        Assert.That(h2.IsClosed, Is.False);
        Assert.That(pool.Count, Is.EqualTo(2));

        pool.Dispose();

        Assert.That(h1.IsClosed, Is.True);
        Assert.That(h2.IsClosed, Is.True);
        Assert.That(pool.Count, Is.EqualTo(0));
        Assert.Throws<ObjectDisposedException>(() => pool.GetOrCreateHandle(f1, writeAccess: false));
    }

    [Test]
    public void ReadBlock_and_WriteBlock_spanning_two_files_succeeds()
    {
        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "blk_a.bin", Size = 600 },
            new TorrentFile { Path = "blk_b.bin", Size = 600 }
        };

        using var storage = new MultiFilePieceStorage(_tempDir, files, pieceLength: 1200);

        var blockData = new byte[400];
        for (var i = 0; i < blockData.Length; i++)
        {
            blockData[i] = (byte)(i ^ 0x5A);
        }

        storage.WriteBlock(0, 400, blockData);

        var readBack = storage.ReadBlock(0, 400, 400);
        Assert.That(readBack, Is.EqualTo(blockData));
    }

    [Test]
    public void BEP47_padding_file_skipped_on_WritePiece_and_zero_filled_on_ReadPiece()
    {
        var torrent = new Torrent
        {
            InfoHash = "padtest",
            PieceLength = 800,
            TotalSize = 800,
            SavePath = _tempDir
        };

        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "f1.bin", Size = 300 },
            new TorrentFile { Path = ".pad/200", Size = 200, IsPaddingFile = true },
            new TorrentFile { Path = "f2.bin", Size = 300 }
        };

        var pieceData = new byte[800];
        Array.Fill(pieceData, (byte)0xEE);

        _storage.WritePiece(torrent, files, 0, pieceData);

        var padFile = Path.Combine(_tempDir, ".pad", "200");
        Assert.That(File.Exists(padFile), Is.False);

        var destBuf = new byte[800];
        Array.Fill(destBuf, (byte)0xFF);

        var bytesRead = _storage.ReadPiece(torrent, files, 0, destBuf);

        var expectedEE = new byte[300];
        Array.Fill(expectedEE, (byte)0xEE);

        Assert.That(bytesRead, Is.EqualTo(800));
        Assert.That(destBuf.AsSpan(0, 300).ToArray(), Is.EqualTo(expectedEE));
        Assert.That(destBuf.AsSpan(300, 200).ToArray(), Is.EqualTo(new byte[200])); // zero-filled
        Assert.That(destBuf.AsSpan(500, 300).ToArray(), Is.EqualTo(expectedEE));
    }
}
