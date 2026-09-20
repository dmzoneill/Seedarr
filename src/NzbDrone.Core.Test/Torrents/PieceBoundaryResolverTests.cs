using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class PieceBoundaryResolverTests
{
    private PieceBoundaryResolver _resolver;

    [SetUp]
    public void SetUp()
    {
        _resolver = new PieceBoundaryResolver();
    }

    [Test]
    public void ResolvePiece_when_piece_spans_two_files_returns_correct_slices()
    {
        // File A: 600 bytes (0..600)
        // File B: 800 bytes (600..1400)
        // Piece length: 500
        // Piece 0 (0..500): completely in File A
        // Piece 1 (500..1000): 100 bytes from File A (500..600), 400 bytes from File B (0..400)
        var fileA = new TorrentFile { Path = "fileA.bin", Size = 600 };
        var fileB = new TorrentFile { Path = "fileB.bin", Size = 800 };
        var files = new List<TorrentFile> { fileA, fileB };

        var slices = _resolver.ResolvePiece(1, 500, 1400, files);

        Assert.That(slices.Count, Is.EqualTo(2));

        Assert.That(slices[0].File, Is.SameAs(fileA));
        Assert.That(slices[0].FileOffset, Is.EqualTo(500));
        Assert.That(slices[0].SliceLength, Is.EqualTo(100));
        Assert.That(slices[0].BufferOffset, Is.EqualTo(0));

        Assert.That(slices[1].File, Is.SameAs(fileB));
        Assert.That(slices[1].FileOffset, Is.EqualTo(0));
        Assert.That(slices[1].SliceLength, Is.EqualTo(400));
        Assert.That(slices[1].BufferOffset, Is.EqualTo(100));
    }

    [Test]
    public void ResolvePiece_when_tiny_file_contained_entirely_inside_one_piece()
    {
        // File A: 300 bytes (0..300)
        // File B: 100 bytes (300..400) -> contained entirely inside piece 0
        // File C: 600 bytes (400..1000)
        // Piece length: 500, Piece 0 (0..500)
        var fileA = new TorrentFile { Path = "fileA.bin", Size = 300 };
        var fileB = new TorrentFile { Path = "fileB.bin", Size = 100 };
        var fileC = new TorrentFile { Path = "fileC.bin", Size = 600 };
        var files = new List<TorrentFile> { fileA, fileB, fileC };

        var slices = _resolver.ResolvePiece(0, 500, 1000, files);

        Assert.That(slices.Count, Is.EqualTo(3));

        Assert.That(slices[0].File, Is.SameAs(fileA));
        Assert.That(slices[0].FileOffset, Is.EqualTo(0));
        Assert.That(slices[0].SliceLength, Is.EqualTo(300));
        Assert.That(slices[0].BufferOffset, Is.EqualTo(0));

        Assert.That(slices[1].File, Is.SameAs(fileB));
        Assert.That(slices[1].FileOffset, Is.EqualTo(0));
        Assert.That(slices[1].SliceLength, Is.EqualTo(100));
        Assert.That(slices[1].BufferOffset, Is.EqualTo(300));

        Assert.That(slices[2].File, Is.SameAs(fileC));
        Assert.That(slices[2].FileOffset, Is.EqualTo(0));
        Assert.That(slices[2].SliceLength, Is.EqualTo(100));
        Assert.That(slices[2].BufferOffset, Is.EqualTo(400));
    }

    [Test]
    public void ResolvePiece_for_final_piece_shorter_than_piece_length()
    {
        // File A: 600 bytes (0..600)
        // File B: 800 bytes (600..1400)
        // Piece length: 500
        // Total size: 1400. Piece count: 3 (pieces 0, 1, 2)
        // Piece 2 (1000..1400): length 400 bytes, entirely in File B at offset 400
        var fileA = new TorrentFile { Path = "fileA.bin", Size = 600 };
        var fileB = new TorrentFile { Path = "fileB.bin", Size = 800 };
        var files = new List<TorrentFile> { fileA, fileB };

        var slices = _resolver.ResolvePiece(2, 500, 1400, files);

        Assert.That(slices.Count, Is.EqualTo(1));
        Assert.That(slices[0].File, Is.SameAs(fileB));
        Assert.That(slices[0].FileOffset, Is.EqualTo(400));
        Assert.That(slices[0].SliceLength, Is.EqualTo(400));
        Assert.That(slices[0].BufferOffset, Is.EqualTo(0));
    }

    [Test]
    public void ResolvePiece_skips_zero_length_and_empty_files_gracefully()
    {
        var file0 = new TorrentFile { Path = "empty.bin", Size = 0 };
        var file1 = new TorrentFile { Path = "file1.bin", Size = 500 };
        var file2 = new TorrentFile { Path = "zero.bin", Size = 0 };
        var file3 = new TorrentFile { Path = "file2.bin", Size = 500 };
        var files = new List<TorrentFile> { file0, file1, file2, file3 };

        var slices = _resolver.ResolvePiece(0, 750, 1000, files);

        Assert.That(slices.Count, Is.EqualTo(2));
        Assert.That(slices[0].File, Is.SameAs(file1));
        Assert.That(slices[0].SliceLength, Is.EqualTo(500));
        Assert.That(slices[1].File, Is.SameAs(file3));
        Assert.That(slices[1].SliceLength, Is.EqualTo(250));
    }

    [Test]
    public void ResolvePiece_returns_empty_when_out_of_bounds_or_invalid()
    {
        var files = new List<TorrentFile> { new TorrentFile { Path = "file.bin", Size = 1000 } };

        Assert.That(_resolver.ResolvePiece(-1, 500, 1000, files), Is.Empty);
        Assert.That(_resolver.ResolvePiece(2, 500, 1000, files), Is.Empty);
        Assert.That(_resolver.ResolvePiece(0, 0, 1000, files), Is.Empty);
        Assert.That(_resolver.ResolvePiece(0, 500, 0, null), Is.Empty);
    }
}
