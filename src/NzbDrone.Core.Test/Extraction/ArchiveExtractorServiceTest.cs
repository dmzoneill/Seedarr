using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
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
}
