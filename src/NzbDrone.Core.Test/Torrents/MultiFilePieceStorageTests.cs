using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
}
