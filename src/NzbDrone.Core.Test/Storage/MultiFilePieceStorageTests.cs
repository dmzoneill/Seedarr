using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Storage;
using NzbDrone.Core.Torrents;
using MultiFilePieceStorage = NzbDrone.Core.Storage.MultiFilePieceStorage;

namespace NzbDrone.Core.Test.Storage;

[TestFixture]
public class MultiFilePieceStorageTests
{
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_storage_spec_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
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
    public void Calculates_prefix_offsets_correctly()
    {
        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "f1.dat", Size = 500 },
            new TorrentFile { Path = "f2.dat", Size = 300 },
            new TorrentFile { Path = "f3.dat", Size = 700 }
        };

        using var storage = new MultiFilePieceStorage(_tempDir, files, pieceLength: 500);

        Assert.That(storage.PrefixOffsets.Count, Is.EqualTo(3));
        Assert.That(storage.PrefixOffsets[0], Is.EqualTo(0));
        Assert.That(storage.PrefixOffsets[1], Is.EqualTo(500));
        Assert.That(storage.PrefixOffsets[2], Is.EqualTo(800));
        Assert.That(storage.TotalSize, Is.EqualTo(1500));
        Assert.That(storage.PieceCount, Is.EqualTo(3));
    }

    [Test]
    public void Binary_search_finds_starting_file_accurately()
    {
        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "f1.dat", Size = 500 },
            new TorrentFile { Path = "f2.dat", Size = 300 },
            new TorrentFile { Path = "f3.dat", Size = 700 }
        };

        using var storage = new MultiFilePieceStorage(_tempDir, files, pieceLength: 500);

        Assert.That(storage.FindStartingFileIndex(0), Is.EqualTo(0));
        Assert.That(storage.FindStartingFileIndex(250), Is.EqualTo(0));
        Assert.That(storage.FindStartingFileIndex(499), Is.EqualTo(0));
        Assert.That(storage.FindStartingFileIndex(500), Is.EqualTo(1));
        Assert.That(storage.FindStartingFileIndex(799), Is.EqualTo(1));
        Assert.That(storage.FindStartingFileIndex(800), Is.EqualTo(2));
        Assert.That(storage.FindStartingFileIndex(1499), Is.EqualTo(2));
        Assert.That(storage.FindStartingFileIndex(1500), Is.EqualTo(3));
    }

    [Test]
    public void SingleFile_Block_ReadWrite_Cleanly()
    {
        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "single.bin", Size = 4096 }
        };

        using var storage = new MultiFilePieceStorage(_tempDir, files, pieceLength: 4096);

        var blockData = new byte[1024];
        for (var i = 0; i < blockData.Length; i++)
        {
            blockData[i] = (byte)((i * 7) & 0xFF);
        }

        storage.WriteBlock(0, 512, blockData);

        var filePath = Path.Combine(_tempDir, "single.bin");
        Assert.That(File.Exists(filePath), Is.True);

        var readBlock = storage.ReadBlock(0, 512, 1024);
        Assert.That(readBlock, Is.EqualTo(blockData));

        var fullPiece = storage.ReadPiece(0);
        Assert.That(fullPiece.Length, Is.EqualTo(4096));
        Assert.That(fullPiece.AsSpan(512, 1024).ToArray(), Is.EqualTo(blockData));
        Assert.That(fullPiece.AsSpan(0, 512).ToArray(), Is.EqualTo(new byte[512]));
    }

    [Test]
    public void MultiFile_BoundarySpanning_TwoFiles_ReadWrite_IdenticalData()
    {
        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "partA.bin", Size = 1500 },
            new TorrentFile { Path = "partB.bin", Size = 2500 }
        };

        using var storage = new MultiFilePieceStorage(_tempDir, files, pieceLength: 4000);

        // Block spans from byte 1000 to 2000 (length 1000):
        // 500 bytes in partA (1000..1500) and 500 bytes in partB (0..500)
        var writeData = new byte[1000];
        for (var i = 0; i < writeData.Length; i++)
        {
            writeData[i] = (byte)((i + 42) & 0xFF);
        }

        storage.WriteBlock(0, 1000, writeData);

        var fileAPath = Path.Combine(_tempDir, "partA.bin");
        var fileBPath = Path.Combine(_tempDir, "partB.bin");

        Assert.That(File.Exists(fileAPath), Is.True);
        Assert.That(File.Exists(fileBPath), Is.True);
        Assert.That(new FileInfo(fileAPath).Length, Is.EqualTo(1500));
        Assert.That(new FileInfo(fileBPath).Length, Is.EqualTo(500));

        var readData = storage.ReadBlock(0, 1000, 1000);
        Assert.That(readData, Is.EqualTo(writeData));
    }

    [Test]
    public void MultiFile_BoundarySpanning_ThreeFiles_ReadWrite_IdenticalData()
    {
        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "file1.dat", Size = 500 },
            new TorrentFile { Path = "file2.dat", Size = 300 },
            new TorrentFile { Path = "file3.dat", Size = 1000 }
        };

        using var storage = new MultiFilePieceStorage(_tempDir, files, pieceLength: 1800);

        // Block spans from 400 to 1200 (length 800):
        // - file1.dat: 100 bytes (400..500)
        // - file2.dat: 300 bytes (0..300) [entire file]
        // - file3.dat: 400 bytes (0..400)
        var writeData = new byte[800];
        for (var i = 0; i < writeData.Length; i++)
        {
            writeData[i] = (byte)((i * 13) & 0xFF);
        }

        storage.WriteBlock(0, 400, writeData);

        var f1 = Path.Combine(_tempDir, "file1.dat");
        var f2 = Path.Combine(_tempDir, "file2.dat");
        var f3 = Path.Combine(_tempDir, "file3.dat");

        Assert.That(File.Exists(f1), Is.True);
        Assert.That(File.Exists(f2), Is.True);
        Assert.That(File.Exists(f3), Is.True);
        Assert.That(new FileInfo(f1).Length, Is.EqualTo(500));
        Assert.That(new FileInfo(f2).Length, Is.EqualTo(300));
        Assert.That(new FileInfo(f3).Length, Is.EqualTo(400));

        var readData = storage.ReadBlock(0, 400, 800);
        Assert.That(readData, Is.EqualTo(writeData));
    }

    [Test]
    public void BoundarySpanning_AcrossMultiplePieces_AndFileBoundaries()
    {
        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "sub/a.dat", Size = 800 },
            new TorrentFile { Path = "sub/b.dat", Size = 1200 },
            new TorrentFile { Path = "sub/c.dat", Size = 600 }
        };

        // TotalSize = 2600. PieceLength = 1024.
        // Piece 0: 0..1024 -> a.dat (800), b.dat (224)
        // Piece 1: 1024..2048 -> b.dat (976), c.dat (48)
        // Piece 2: 2048..2600 -> c.dat (552)
        using var storage = new MultiFilePieceStorage(_tempDir, files, pieceLength: 1024);

        var piece0Data = new byte[1024];
        var piece1Data = new byte[1024];
        var piece2Data = new byte[552];

        Array.Fill(piece0Data, (byte)0x11);
        Array.Fill(piece1Data, (byte)0x22);
        Array.Fill(piece2Data, (byte)0x33);

        storage.WritePiece(0, piece0Data);
        storage.WritePiece(1, piece1Data);
        storage.WritePiece(2, piece2Data);

        var read0 = storage.ReadPiece(0);
        var read1 = storage.ReadPiece(1);
        var read2 = storage.ReadPiece(2);

        Assert.That(read0, Is.EqualTo(piece0Data));
        Assert.That(read1, Is.EqualTo(piece1Data));
        Assert.That(read2, Is.EqualTo(piece2Data));

        // Sub-block across boundary in Piece 1
        var block = storage.ReadBlock(1, 950, 50); // 26 bytes in b.dat, 24 bytes in c.dat
        Assert.That(block.Length, Is.EqualTo(50));
        Assert.That(block, Is.EqualTo(piece1Data.AsSpan(950, 50).ToArray()));
    }

    [Test]
    public void OutOfBounds_Requests_RejectedSafely()
    {
        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "bounds.dat", Size = 1000 }
        };

        using var storage = new MultiFilePieceStorage(_tempDir, files, pieceLength: 500);
        // Piece 0: 0..500. Piece 1: 500..1000. Total size: 1000. Piece count: 2.

        // Negative pieceIndex
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.ReadBlock(-1, 0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.WriteBlock(-1, 0, new byte[100]));
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.ReadPiece(-1));

        // Negative begin
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.ReadBlock(0, -1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.WriteBlock(0, -1, new byte[100]));

        // Negative length
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.ReadBlock(0, 0, -10));

        // Begin exceeds piece size
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.ReadBlock(0, 501, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.WriteBlock(0, 501, new byte[10]));

        // Begin + length exceeds piece size
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.ReadBlock(0, 450, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.WriteBlock(0, 450, new byte[100]));

        // PieceIndex beyond piece count
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.ReadBlock(2, 0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.WriteBlock(2, 0, new byte[100]));
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.ReadPiece(2));
    }

    [Test]
    public void PaddingFiles_SkippedOnWrite_ZeroFilledOnRead_AdjacentFilesUntouched()
    {
        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "media.bin", Size = 500 },
            new TorrentFile { Path = ".pad/200", Size = 200, IsPaddingFile = true },
            new TorrentFile { Path = "subtitles.bin", Size = 500 }
        };

        using var storage = new MultiFilePieceStorage(_tempDir, files, pieceLength: 1200);

        // Block spans [400, 800) of length 400:
        // - media.bin: 100 bytes (400..500)
        // - .pad/200: 200 bytes (0..200) [padding]
        // - subtitles.bin: 100 bytes (0..100)
        var writeData = new byte[400];
        Array.Fill(writeData, (byte)0xAA, 0, 100);
        Array.Fill(writeData, (byte)0xBB, 100, 200); // padding portion
        Array.Fill(writeData, (byte)0xCC, 300, 100);

        storage.WriteBlock(0, 400, writeData);

        var padPath = Path.Combine(_tempDir, ".pad", "200");
        var mediaPath = Path.Combine(_tempDir, "media.bin");
        var subPath = Path.Combine(_tempDir, "subtitles.bin");

        // The padding file must NOT be written to disk
        Assert.That(File.Exists(padPath), Is.False);
        Assert.That(Directory.Exists(Path.Combine(_tempDir, ".pad")), Is.False);

        // Real files must exist and contain exact written data
        Assert.That(File.Exists(mediaPath), Is.True);
        Assert.That(File.Exists(subPath), Is.True);
        Assert.That(new FileInfo(mediaPath).Length, Is.EqualTo(500));
        Assert.That(new FileInfo(subPath).Length, Is.EqualTo(100));

        var expectedAA = new byte[100];
        Array.Fill(expectedAA, (byte)0xAA);
        var expectedCC = new byte[100];
        Array.Fill(expectedCC, (byte)0xCC);

        var mediaBytes = File.ReadAllBytes(mediaPath);
        Assert.That(mediaBytes.AsSpan(400, 100).ToArray(), Is.EqualTo(expectedAA));

        var subBytes = File.ReadAllBytes(subPath);
        Assert.That(subBytes.AsSpan(0, 100).ToArray(), Is.EqualTo(expectedCC));

        // Read back the block: padding span must be transparently zero-filled
        var readData = storage.ReadBlock(0, 400, 400);

        var expectedData = new byte[400];
        Array.Fill(expectedData, (byte)0xAA, 0, 100);
        Array.Fill(expectedData, (byte)0x00, 100, 200); // zero-filled
        Array.Fill(expectedData, (byte)0xCC, 300, 100);

        Assert.That(readData, Is.EqualTo(expectedData));
    }

    [Test]
    public void PaddingFile_PathConvention_WithoutFlag_TreatedAsPadding()
    {
        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "data.bin", Size = 300 },
            new TorrentFile { Path = ".pad/64", Size = 64, IsPaddingFile = false }, // Flag false, but path is .pad/
            new TorrentFile { Path = "data2.bin", Size = 300 }
        };

        using var storage = new MultiFilePieceStorage(_tempDir, files, pieceLength: 664);

        var writeData = new byte[664];
        Array.Fill(writeData, (byte)0x77);

        storage.WritePiece(0, writeData);

        var padPath = Path.Combine(_tempDir, ".pad", "64");
        Assert.That(File.Exists(padPath), Is.False);

        var expected77 = new byte[300];
        Array.Fill(expected77, (byte)0x77);

        var readPiece = storage.ReadPiece(0);
        Assert.That(readPiece.AsSpan(0, 300).ToArray(), Is.EqualTo(expected77));
        Assert.That(readPiece.AsSpan(300, 64).ToArray(), Is.EqualTo(new byte[64])); // Zero-filled
        Assert.That(readPiece.AsSpan(364, 300).ToArray(), Is.EqualTo(expected77));
    }

    [Test]
    public void Empty_Block_ZeroLength_DoesNotThrow()
    {
        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "empty_test.bin", Size = 1000 }
        };

        using var storage = new MultiFilePieceStorage(_tempDir, files, pieceLength: 1000);

        var read = storage.ReadBlock(0, 0, 0);
        Assert.That(read, Is.Empty);

        Assert.DoesNotThrow(() => storage.WriteBlock(0, 0, ReadOnlyMemory<byte>.Empty));
        Assert.DoesNotThrow(() => storage.WriteBlock(0, 0, Array.Empty<byte>()));
    }

    [Test]
    public void MultiFilePieceStorage_Constructed_With_Torrent()
    {
        var torrent = new Torrent
        {
            InfoHash = "abc12345",
            PieceLength = 500,
            TotalSize = 1000,
            SavePath = _tempDir
        };

        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "t1.bin", Size = 500 },
            new TorrentFile { Path = "t2.bin", Size = 500 }
        };

        using var storage = new MultiFilePieceStorage(torrent, files);

        var data = new byte[200];
        Array.Fill(data, (byte)0x99);

        storage.WriteBlock(0, 100, data);
        var read = storage.ReadBlock(0, 100, 200);

        Assert.That(read, Is.EqualTo(data));
    }

    [Test]
    public void Concurrent_Block_Reads_And_Writes_Are_ThreadSafe()
    {
        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "c1.bin", Size = 1000 },
            new TorrentFile { Path = "c2.bin", Size = 1000 }
        };

        using var storage = new MultiFilePieceStorage(_tempDir, files, pieceLength: 1000);

        // Pre-write initial data
        for (var p = 0; p < 2; p++)
        {
            var init = new byte[1000];
            Array.Fill(init, (byte)(p + 1));
            storage.WritePiece(p, init);
        }

        Parallel.For(0, 40, i =>
        {
            var piece = i % 2;
            var offset = (i % 5) * 100;
            var len = 100;

            if (i % 2 == 0)
            {
                var writeBuf = new byte[len];
                Array.Fill(writeBuf, (byte)(piece + 1));
                storage.WriteBlock(piece, offset, writeBuf);
            }
            else
            {
                var readBuf = storage.ReadBlock(piece, offset, len);
                Assert.That(readBuf.Length, Is.EqualTo(len));
                Assert.That(readBuf[0], Is.EqualTo((byte)(piece + 1)));
            }
        });
    }
}
