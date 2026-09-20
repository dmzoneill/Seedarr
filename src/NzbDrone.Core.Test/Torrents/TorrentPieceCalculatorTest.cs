using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentPieceCalculatorTest
{
    [Test]
    public void CalculateForFile_returns_zero_when_piece_length_is_zero_or_negative()
    {
        var (offset, count) = TorrentPieceCalculator.CalculateForFile(100, 500, 0);
        Assert.That(offset, Is.EqualTo(0));
        Assert.That(count, Is.EqualTo(0));

        var (negOffset, negCount) = TorrentPieceCalculator.CalculateForFile(100, 500, -1);
        Assert.That(negOffset, Is.EqualTo(0));
        Assert.That(negCount, Is.EqualTo(0));
    }

    [Test]
    public void CalculateForFile_handles_zero_size_file()
    {
        // 0-byte file starting at offset 2500 with pieceLength 1000
        var (offset, count) = TorrentPieceCalculator.CalculateForFile(2500, 0, 1000);
        Assert.That(offset, Is.EqualTo(2));
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public void CalculateForFile_single_file_spanning_multiple_pieces()
    {
        // File of 2500 bytes starting at offset 0 with pieceLength 1000 -> pieces 0, 1, 2
        var (offset, count) = TorrentPieceCalculator.CalculateForFile(0, 2500, 1000);
        Assert.That(offset, Is.EqualTo(0));
        Assert.That(count, Is.EqualTo(3));
    }

    [Test]
    public void CalculateForFile_exact_piece_boundary()
    {
        // File of exactly 2000 bytes starting at offset 0 with pieceLength 1000 -> pieces 0, 1
        var (offset, count) = TorrentPieceCalculator.CalculateForFile(0, 2000, 1000);
        Assert.That(offset, Is.EqualTo(0));
        Assert.That(count, Is.EqualTo(2));

        // Second file starting at exact boundary 2000 of size 1000 -> piece 2
        var (offset2, count2) = TorrentPieceCalculator.CalculateForFile(2000, 1000, 1000);
        Assert.That(offset2, Is.EqualTo(2));
        Assert.That(count2, Is.EqualTo(1));
    }

    [Test]
    public void CalculatePieceBoundaries_handles_null_and_empty_list()
    {
        Assert.DoesNotThrow(() => TorrentPieceCalculator.CalculatePieceBoundaries(null, 1024));
        Assert.DoesNotThrow(() => TorrentPieceCalculator.CalculatePieceBoundaries(new List<TorrentFile>(), 1024));
    }

    [Test]
    public void CalculatePieceBoundaries_calculates_sequential_boundaries_for_multi_file_torrent()
    {
        var files = new List<TorrentFile>
        {
            new() { Path = "f1", Size = 500 },
            new() { Path = "f2", Size = 1500 },
            new() { Path = "f3", Size = 0 },
            new() { Path = "f4", Size = 2000 }
        };

        TorrentPieceCalculator.CalculatePieceBoundaries(files, 1000);

        // f1: bytes 0..500 -> piece 0
        Assert.That(files[0].PieceOffset, Is.EqualTo(0));
        Assert.That(files[0].PieceCount, Is.EqualTo(1));

        // f2: bytes 500..2000 -> pieces [0, 1]
        Assert.That(files[1].PieceOffset, Is.EqualTo(0));
        Assert.That(files[1].PieceCount, Is.EqualTo(2));

        // f3: bytes 2000..2000 (0 bytes) -> piece offset 2, count 0
        Assert.That(files[2].PieceOffset, Is.EqualTo(2));
        Assert.That(files[2].PieceCount, Is.EqualTo(0));

        // f4: bytes 2000..4000 -> pieces [2, 3]
        Assert.That(files[3].PieceOffset, Is.EqualTo(2));
        Assert.That(files[3].PieceCount, Is.EqualTo(2));
    }
}
