using System;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Extraction;

[TestFixture]
public class ArchiveExtractorServiceTest
{
    private IEventAggregator _eventAggregator;
    private ArchiveExtractorService _subject;
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _eventAggregator = Substitute.For<IEventAggregator>();
        _subject = new ArchiveExtractorService(_eventAggregator);
        _tempDir = Path.Combine(Path.GetTempPath(), "Seedarr_Extraction_Test_" + Guid.NewGuid().ToString("N"));
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
                // Ignore cleanup errors
            }
        }
    }

    [TestCase("Release.Name.part01.rar", true)]
    [TestCase("Release.Name.part1.rar", true)]
    [TestCase("Release.Name.part001.rar", true)]
    [TestCase("Release.Name.part02.rar", false)]
    [TestCase("Release.Name.part2.rar", false)]
    [TestCase("Release.Name.part03.rar", false)]
    [TestCase("Release.Name.part10.rar", false)]
    [TestCase("Release.Name.r00", false)]
    [TestCase("Release.Name.r01", false)]
    [TestCase("Release.Name.s00", false)]
    [TestCase("Release.Name.rar", true)]
    [TestCase("Release.Name.zip", true)]
    [TestCase("Release.Name.7z", true)]
    [TestCase("Release.Name.7z.001", true)]
    [TestCase("Release.Name.7z.002", false)]
    [TestCase("Release.Name.mkv", false)]
    [TestCase("Release.Name.mp4", false)]
    [TestCase("Release.Name.nfo", false)]
    [TestCase("Release.Name.txt", false)]
    public void IsPrimaryArchive_correctly_classifies_archives(string fileName, bool expected)
    {
        var filePath = Path.Combine(_tempDir, fileName);
        var actual = _subject.IsPrimaryArchive(filePath);
        Assert.That(actual, Is.EqualTo(expected), $"Failed classification for {fileName}");
    }

    [Test]
    public void IsPrimaryArchive_rar_when_part01_exists_returns_false()
    {
        var part01File = Path.Combine(_tempDir, "Release.part01.rar");
        var rarFile = Path.Combine(_tempDir, "Release.rar");
        File.WriteAllText(part01File, "part01 dummy");
        File.WriteAllText(rarFile, "rar dummy");

        var isPrimary = _subject.IsPrimaryArchive(rarFile);
        Assert.That(isPrimary, Is.False, "Expected Release.rar to not be primary when Release.part01.rar is present");
    }

    [Test]
    public async Task ExtractTorrentArchiveAsync_extracts_zip_and_preserves_source_archive()
    {
        var torrentDir = Path.Combine(_tempDir, "TorrentData");
        var extractDir = Path.Combine(_tempDir, "Extracted");
        Directory.CreateDirectory(torrentDir);

        var zipPath = Path.Combine(torrentDir, "Release.zip");
        CreateSampleZip(zipPath, "sample.mkv", "dummy video content 12345");

        var torrent = new Torrent
        {
            Id = 10,
            Name = "Release.Zip.Test",
            SavePath = torrentDir,
        };

        var result = await _subject.ExtractTorrentArchiveAsync(torrent, destination: extractDir, deleteArchive: false);

        Assert.That(result.Success, Is.True);
        Assert.That(File.Exists(zipPath), Is.True, "Source zip archive must remain intact when deleteArchive is false");

        var extractedFile = Path.Combine(extractDir, "sample.mkv");
        Assert.That(File.Exists(extractedFile), Is.True, "Extracted file should exist at destination");
        Assert.That(await File.ReadAllTextAsync(extractedFile), Is.EqualTo("dummy video content 12345"));
    }

    [Test]
    public async Task ExtractTorrentArchiveAsync_deletes_archive_when_deleteArchive_is_true()
    {
        var torrentDir = Path.Combine(_tempDir, "TorrentDataDelete");
        Directory.CreateDirectory(torrentDir);

        var zipPath = Path.Combine(torrentDir, "ReleaseToDelete.zip");
        CreateSampleZip(zipPath, "sample2.mkv", "another dummy video");

        var torrent = new Torrent
        {
            Id = 11,
            Name = "Release.Zip.DeleteTest",
            SavePath = torrentDir,
        };

        var result = await _subject.ExtractTorrentArchiveAsync(torrent, deleteArchive: true);

        Assert.That(result.Success, Is.True);
        Assert.That(File.Exists(zipPath), Is.False, "Source zip archive must be deleted when deleteArchive is true");
        Assert.That(File.Exists(Path.Combine(torrentDir, "sample2.mkv")), Is.True);
    }

    [Test]
    public async Task ExtractTorrentArchiveAsync_publishes_ArchiveExtractionCompletedEvent_on_success()
    {
        var torrentDir = Path.Combine(_tempDir, "TorrentDataSuccess");
        Directory.CreateDirectory(torrentDir);

        var zipPath = Path.Combine(torrentDir, "Release.zip");
        CreateSampleZip(zipPath, "video.mp4", "mp4 content");

        var torrent = new Torrent
        {
            Id = 20,
            Name = "Release.Success.Test",
            SavePath = torrentDir,
        };

        var result = await _subject.ExtractTorrentArchiveAsync(torrent);

        Assert.That(result.Success, Is.True);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<ArchiveExtractionCompletedEvent>(e => e.Torrent == torrent));
    }

    [Test]
    public async Task ExtractTorrentArchiveAsync_publishes_ArchiveExtractionFailedEvent_on_error()
    {
        var nonExistentDir = Path.Combine(_tempDir, "DoesNotExist");

        var torrent = new Torrent
        {
            Id = 30,
            Name = "Release.Failed.Test",
            SavePath = nonExistentDir,
        };

        var result = await _subject.ExtractTorrentArchiveAsync(torrent);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Is.Not.Null);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<ArchiveExtractionFailedEvent>(e => e.Torrent == torrent && !string.IsNullOrEmpty(e.ErrorMessage)));
    }

    [Test]
    public void ArchiveExtractionEventHandler_triggers_extraction_when_archives_present()
    {
        var torrentDir = Path.Combine(_tempDir, "TorrentDataEvent");
        Directory.CreateDirectory(torrentDir);
        var zipPath = Path.Combine(torrentDir, "Release.zip");
        CreateSampleZip(zipPath, "media.mkv", "media");

        var torrent = new Torrent
        {
            Id = 40,
            Name = "Release.Event.Test",
            SavePath = torrentDir,
        };

        var extractorMock = Substitute.For<IArchiveExtractorService>();
        extractorMock.IsPrimaryArchive(Arg.Any<string>()).Returns(call => ((string)call[0]).EndsWith(".zip"));

        var handler = new ArchiveExtractionEventHandler(extractorMock);
        handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        // Give background Task.Run time to invoke
        Task.Delay(100).Wait();

        extractorMock.Received(1).ExtractTorrentArchiveAsync(torrent);
    }

    [Test]
    public void ArchiveExtractionEventHandler_skips_extraction_when_no_archives_present()
    {
        var torrentDir = Path.Combine(_tempDir, "TorrentDataNoArchives");
        Directory.CreateDirectory(torrentDir);
        File.WriteAllText(Path.Combine(torrentDir, "video.mkv"), "dummy video");

        var torrent = new Torrent
        {
            Id = 50,
            Name = "Release.NoArchive.Test",
            SavePath = torrentDir,
        };

        var extractorMock = Substitute.For<IArchiveExtractorService>();
        extractorMock.IsPrimaryArchive(Arg.Any<string>()).Returns(false);

        var handler = new ArchiveExtractionEventHandler(extractorMock);
        handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        Task.Delay(50).Wait();

        extractorMock.DidNotReceive().ExtractTorrentArchiveAsync(Arg.Any<Torrent>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    private static void CreateSampleZip(string zipFilePath, string entryName, string content)
    {
        using var zipStream = new FileStream(zipFilePath, FileMode.Create);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(entryName);
        using var entryStream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        entryStream.Write(bytes, 0, bytes.Length);
    }

    private static void CreateSymlinkZip(string zipFilePath, string entryName, string targetPath)
    {
        using var fs = new FileStream(zipFilePath, FileMode.Create);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(entryName);
        entry.ExternalAttributes = 0xA1ED << 16;
        using var writer = new StreamWriter(entry.Open());
        writer.Write(targetPath);
    }

    [Test]
    public async Task ExtractTorrentArchiveAsync_rejects_zip_slip_traversal()
    {
        var torrentDir = Path.Combine(_tempDir, "TorrentDataZipSlip");
        var extractDir = Path.Combine(_tempDir, "ExtractedZipSlip");
        Directory.CreateDirectory(torrentDir);

        var zipPath = Path.Combine(torrentDir, "EvilSlip.zip");
        CreateSampleZip(zipPath, "../../evil.txt", "malicious payload");

        var torrent = new Torrent
        {
            Id = 60,
            Name = "Evil.ZipSlip.Release",
            SavePath = torrentDir,
        };

        var result = await _subject.ExtractTorrentArchiveAsync(torrent, destination: extractDir);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Zip-Slip"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<ArchiveExtractionFailedEvent>(e => e.ErrorMessage.Contains("Zip-Slip")));
        Assert.That(File.Exists(Path.Combine(_tempDir, "evil.txt")), Is.False);
    }

    [Test]
    public void ExtractArchiveFile_throws_SecurityException_on_zip_slip_traversal()
    {
        var extractDir = Path.Combine(_tempDir, "DirectExtractSlip");
        Directory.CreateDirectory(extractDir);

        var zipPath = Path.Combine(_tempDir, "DirectEvilSlip.zip");
        CreateSampleZip(zipPath, "../outside.txt", "escaped file");

        var ex = Assert.Throws<SecurityException>(() => _subject.ExtractArchiveFile(zipPath, extractDir));
        Assert.That(ex.Message, Does.Contain("Zip-Slip"));
    }

    [Test]
    public async Task ExtractTorrentArchiveAsync_rejects_symlink_pointing_outside()
    {
        var torrentDir = Path.Combine(_tempDir, "TorrentDataSymlink");
        var extractDir = Path.Combine(_tempDir, "ExtractedSymlink");
        Directory.CreateDirectory(torrentDir);

        var zipPath = Path.Combine(torrentDir, "EvilSymlink.zip");
        CreateSymlinkZip(zipPath, "symlink_entry.txt", "../../etc/passwd");

        var torrent = new Torrent
        {
            Id = 61,
            Name = "Evil.Symlink.Release",
            SavePath = torrentDir,
        };

        var result = await _subject.ExtractTorrentArchiveAsync(torrent, destination: extractDir);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Zip-Slip"));
    }

    [Test]
    public async Task ExtractTorrentArchiveAsync_fails_when_disk_space_insufficient()
    {
        var torrentDir = Path.Combine(_tempDir, "TorrentDataDiskSpaceFail");
        var extractDir = Path.Combine(_tempDir, "ExtractedDiskSpaceFail");
        Directory.CreateDirectory(torrentDir);

        var zipPath = Path.Combine(torrentDir, "Payload.zip");
        CreateSampleZip(zipPath, "large_file.mkv", new string('a', 200));

        var diskMock = Substitute.For<IDiskProvider>();
        diskMock.GetAvailableFreeSpace(Arg.Any<string>()).Returns(50); // 50 bytes free, but 200 * 1.15 = 230 required

        var subject = new ArchiveExtractorService(_eventAggregator, diskMock);

        var torrent = new Torrent
        {
            Id = 70,
            Name = "DiskSpace.Fail.Release",
            SavePath = torrentDir,
        };

        var result = await subject.ExtractTorrentArchiveAsync(torrent, destination: extractDir);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Insufficient free disk space"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<ArchiveExtractionFailedEvent>(e => e.ErrorMessage.Contains("Insufficient free disk space")));
    }

    [Test]
    public async Task ExtractTorrentArchiveAsync_succeeds_when_disk_space_sufficient()
    {
        var torrentDir = Path.Combine(_tempDir, "TorrentDataDiskSpaceOk");
        var extractDir = Path.Combine(_tempDir, "ExtractedDiskSpaceOk");
        Directory.CreateDirectory(torrentDir);

        var zipPath = Path.Combine(torrentDir, "PayloadOk.zip");
        CreateSampleZip(zipPath, "media.mkv", "normal payload");

        var diskMock = Substitute.For<IDiskProvider>();
        diskMock.GetAvailableFreeSpace(Arg.Any<string>()).Returns(50000);

        var subject = new ArchiveExtractorService(_eventAggregator, diskMock);

        var torrent = new Torrent
        {
            Id = 71,
            Name = "DiskSpace.Ok.Release",
            SavePath = torrentDir,
        };

        var result = await subject.ExtractTorrentArchiveAsync(torrent, destination: extractDir);

        Assert.That(result.Success, Is.True);
        Assert.That(File.Exists(Path.Combine(extractDir, "media.mkv")), Is.True);
    }

    [Test]
    public async Task ExtractTorrentArchiveAsync_rejects_zip_bomb_exceeding_compression_ratio()
    {
        var torrentDir = Path.Combine(_tempDir, "TorrentDataZipBombRatio");
        Directory.CreateDirectory(torrentDir);

        var zipPath = Path.Combine(torrentDir, "ZipBombRatio.zip");
        // 50,000 zeros compress to ~60 bytes, yielding a ratio > 800:1
        CreateSampleZip(zipPath, "bomb.bin", new string('0', 50000));

        var torrent = new Torrent
        {
            Id = 80,
            Name = "ZipBomb.Ratio.Release",
            SavePath = torrentDir,
        };

        _subject.MaxCompressionRatio = 100.0;
        var result = await _subject.ExtractTorrentArchiveAsync(torrent);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Zip-bomb detected"));
    }

    [Test]
    public async Task ExtractTorrentArchiveAsync_rejects_entry_exceeding_max_single_file_size()
    {
        var torrentDir = Path.Combine(_tempDir, "TorrentDataZipBombSize");
        Directory.CreateDirectory(torrentDir);

        var zipPath = Path.Combine(torrentDir, "ZipBombSize.zip");
        CreateSampleZip(zipPath, "oversized.bin", new string('x', 2000));

        var torrent = new Torrent
        {
            Id = 81,
            Name = "ZipBomb.Size.Release",
            SavePath = torrentDir,
        };

        _subject.MaxSingleFileUncompressedSize = 500;
        var result = await _subject.ExtractTorrentArchiveAsync(torrent);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("exceeds maximum single file limit"));
    }

    [Test]
    public void CleanupExtractedFiles_prunes_extracted_media_and_preserves_archive_parts()
    {
        var torrentDir = Path.Combine(_tempDir, "TorrentDataCleanup");
        Directory.CreateDirectory(torrentDir);

        var zipPath = Path.Combine(torrentDir, "Release.zip");
        using (var fs = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry1 = archive.CreateEntry("movie.mkv");
            using (var w = new StreamWriter(entry1.Open())) { w.Write("movie data"); }
            var entry2 = archive.CreateEntry("sample.mp4");
            using (var w = new StreamWriter(entry2.Open())) { w.Write("sample data"); }
        }

        // Create extracted duplicate media files
        var extractedMovie = Path.Combine(torrentDir, "movie.mkv");
        var extractedSample = Path.Combine(torrentDir, "sample.mp4");
        File.WriteAllText(extractedMovie, "extracted duplicate movie");
        File.WriteAllText(extractedSample, "extracted duplicate sample");

        // Create companion archive volumes and checksum files that must be preserved
        var part01Rar = Path.Combine(torrentDir, "Release.part01.rar");
        var part02Rar = Path.Combine(torrentDir, "Release.part02.rar");
        var r00File = Path.Combine(torrentDir, "Release.r00");
        var sfvFile = Path.Combine(torrentDir, "Release.sfv");
        File.WriteAllText(part01Rar, "rar part 1");
        File.WriteAllText(part02Rar, "rar part 2");
        File.WriteAllText(r00File, "rar slice");
        File.WriteAllText(sfvFile, "checksum");

        var torrent = new Torrent
        {
            Id = 90,
            Name = "Cleanup.Release.Test",
            SavePath = torrentDir,
        };

        var prunedCount = _subject.CleanupExtractedFiles(torrent);

        Assert.That(prunedCount, Is.EqualTo(2));
        Assert.That(File.Exists(extractedMovie), Is.False, "Extracted movie must be pruned");
        Assert.That(File.Exists(extractedSample), Is.False, "Extracted sample must be pruned");
        Assert.That(File.Exists(part01Rar), Is.True, "Original archive slice part01 must be preserved");
        Assert.That(File.Exists(part02Rar), Is.True, "Original archive slice part02 must be preserved");
        Assert.That(File.Exists(r00File), Is.True, "Original archive slice r00 must be preserved");
        Assert.That(File.Exists(sfvFile), Is.True, "Verification file sfv must be preserved");
        Assert.That(File.Exists(zipPath), Is.True, "Original zip archive must be preserved");
    }

    [TestCase("Release.rar", true)]
    [TestCase("Release.part01.rar", true)]
    [TestCase("Release.part02.rar", true)]
    [TestCase("Release.r00", true)]
    [TestCase("Release.r01", true)]
    [TestCase("Release.zip", true)]
    [TestCase("Release.7z", true)]
    [TestCase("Release.7z.001", true)]
    [TestCase("Release.sfv", true)]
    [TestCase("Release.par2", true)]
    [TestCase("Release.nfo", true)]
    [TestCase("Release.torrent", true)]
    [TestCase("Release.mkv", false)]
    [TestCase("Release.mp4", false)]
    [TestCase("Release.avi", false)]
    [TestCase("Release.txt", false)]
    public void IsArchiveFile_correctly_classifies_archive_and_non_archive_files(string fileName, bool expected)
    {
        var filePath = Path.Combine(_tempDir, fileName);
        var actual = ArchiveExtractorService.IsArchiveFile(filePath);
        Assert.That(actual, Is.EqualTo(expected), $"Failed classification for {fileName}");
    }
}
