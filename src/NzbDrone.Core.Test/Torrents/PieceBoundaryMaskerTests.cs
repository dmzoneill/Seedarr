using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class PieceBoundaryMaskerTests
{
    private PieceBoundaryMasker _subject;

    [SetUp]
    public void SetUp()
    {
        _subject = new PieceBoundaryMasker();
    }

    [Test]
    public void ComputeWantedPieces_when_boundary_piece_shared_between_wanted_and_unwanted_file_marks_boundary_piece_wanted()
    {
        // Total size: 6000, Piece length: 1000 => 6 pieces (0..5)
        // File A: bytes 0..2499 (pieces 0, 1, 2) - Wanted
        // File B: bytes 2500..5999 (pieces 2, 3, 4, 5) - Unwanted
        // Piece 2 is a shared boundary piece and must be marked wanted
        var torrent = new Torrent
        {
            TotalSize = 6000,
            PieceLength = 1000,
            PieceCount = 6
        };

        var files = new List<TorrentFile>
        {
            new TorrentFile { ByteOffset = 0, Size = 2500, Wanted = true },
            new TorrentFile { ByteOffset = 2500, Size = 3500, Wanted = false }
        };

        var result = _subject.ComputeWantedPieces(torrent, files);

        Assert.That(result, Has.Length.EqualTo(6));
        Assert.That(result[0], Is.True, "Piece 0 belongs to wanted File A");
        Assert.That(result[1], Is.True, "Piece 1 belongs to wanted File A");
        Assert.That(result[2], Is.True, "Piece 2 is boundary piece shared between wanted File A and unwanted File B");
        Assert.That(result[3], Is.False, "Piece 3 is interior to unwanted File B and must be masked out");
        Assert.That(result[4], Is.False, "Piece 4 is interior to unwanted File B and must be masked out");
        Assert.That(result[5], Is.False, "Piece 5 is interior to unwanted File B and must be masked out");
    }

    [Test]
    public void ComputeWantedPieces_masks_out_interior_pieces_strictly_inside_unwanted_files()
    {
        var torrent = new Torrent
        {
            TotalSize = 5000,
            PieceLength = 1000,
            PieceCount = 5
        };

        var files = new List<TorrentFile>
        {
            new TorrentFile { ByteOffset = 0, Size = 1000, Wanted = true },     // Piece 0
            new TorrentFile { ByteOffset = 1000, Size = 3000, Wanted = false }, // Pieces 1, 2, 3
            new TorrentFile { ByteOffset = 4000, Size = 1000, Wanted = true }   // Piece 4
        };

        var result = _subject.ComputeWantedPieces(torrent, files);

        Assert.That(result, Has.Length.EqualTo(5));
        Assert.That(result[0], Is.True);
        Assert.That(result[1], Is.False);
        Assert.That(result[2], Is.False);
        Assert.That(result[3], Is.False);
        Assert.That(result[4], Is.True);
    }

    [Test]
    public void ComputeWantedPieces_handles_single_file_and_zero_byte_edge_cases()
    {
        var torrent = new Torrent
        {
            TotalSize = 3000,
            PieceLength = 1000,
            PieceCount = 3
        };

        // Single file wanted
        var singleWanted = new List<TorrentFile>
        {
            new TorrentFile { ByteOffset = 0, Size = 3000, Wanted = true }
        };
        var singleResult = _subject.ComputeWantedPieces(torrent, singleWanted);
        Assert.That(singleResult, Is.EqualTo(new[] { true, true, true }));

        // Single file unwanted
        var singleUnwanted = new List<TorrentFile>
        {
            new TorrentFile { ByteOffset = 0, Size = 3000, Wanted = false }
        };
        var singleUnwantedResult = _subject.ComputeWantedPieces(torrent, singleUnwanted);
        Assert.That(singleUnwantedResult, Is.EqualTo(new[] { false, false, false }));

        // 0-byte file edge case along with wanted file
        var withZeroByteFile = new List<TorrentFile>
        {
            new TorrentFile { ByteOffset = 0, Size = 0, Wanted = true },
            new TorrentFile { ByteOffset = 0, Size = 1000, Wanted = true },
            new TorrentFile { ByteOffset = 1000, Size = 0, Wanted = false },
            new TorrentFile { ByteOffset = 1000, Size = 2000, Wanted = false }
        };
        var zeroByteResult = _subject.ComputeWantedPieces(torrent, withZeroByteFile);
        Assert.That(zeroByteResult, Is.EqualTo(new[] { true, false, false }));
    }

    [Test]
    public void ComputeWantedPieces_when_torrent_or_piece_length_is_invalid_returns_empty_array()
    {
        Assert.That(_subject.ComputeWantedPieces(null, null), Is.Empty);
        Assert.That(_subject.ComputeWantedPieces(new Torrent { PieceLength = 0 }, null), Is.Empty);
    }

    [Test]
    public void CalculateWantedSize_returns_sum_of_wanted_files_when_selective()
    {
        var torrent = new Torrent { TotalSize = 3500 };
        var files = new List<TorrentFile>
        {
            new TorrentFile { Size = 1000, Wanted = true },
            new TorrentFile { Size = 2000, Wanted = false },
            new TorrentFile { Size = 500, Wanted = true }
        };

        var wantedSize = _subject.CalculateWantedSize(torrent, files);
        Assert.That(wantedSize, Is.EqualTo(1500L));
    }

    [Test]
    public void CalculateWantedSize_returns_total_size_when_not_selective()
    {
        var torrent = new Torrent { TotalSize = 3000 };
        var files = new List<TorrentFile>
        {
            new TorrentFile { Size = 1000, Wanted = true },
            new TorrentFile { Size = 2000, Wanted = true }
        };

        var wantedSize = _subject.CalculateWantedSize(torrent, files);
        Assert.That(wantedSize, Is.EqualTo(3000L));

        // When files is null, returns torrent TotalSize
        Assert.That(_subject.CalculateWantedSize(torrent, null), Is.EqualTo(3000L));
    }

    [Test]
    public void IsSelectiveDownload_returns_true_only_when_unwanted_files_exist()
    {
        Assert.That(_subject.IsSelectiveDownload(null), Is.False);
        Assert.That(_subject.IsSelectiveDownload(new List<TorrentFile>()), Is.False);

        var allWanted = new List<TorrentFile>
        {
            new TorrentFile { Size = 1000, Wanted = true },
            new TorrentFile { Size = 2000, Wanted = true }
        };
        Assert.That(_subject.IsSelectiveDownload(allWanted), Is.False);

        var selective = new List<TorrentFile>
        {
            new TorrentFile { Size = 1000, Wanted = true },
            new TorrentFile { Size = 2000, Wanted = false }
        };
        Assert.That(_subject.IsSelectiveDownload(selective), Is.True);
    }
}
