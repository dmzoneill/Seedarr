using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentRelocationServiceTests
{
    private ITorrentService _torrentService;
    private IEventAggregator _eventAggregator;
    private IConfigService _configService;
    private TorrentRelocationService _subject;
    private string _sourceDir;
    private string _destDir;

    [SetUp]
    public void SetUp()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _configService = Substitute.For<IConfigService>();
        _subject = new TorrentRelocationService(_torrentService, _eventAggregator, _configService);

        _sourceDir = Path.Combine(Path.GetTempPath(), "reloc_src_" + Guid.NewGuid().ToString("N"));
        _destDir = Path.Combine(Path.GetTempPath(), "reloc_dst_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_sourceDir))
        {
            try
            {
                Directory.Delete(_sourceDir, true);
            }
            catch
            {
            }
        }

        if (Directory.Exists(_destDir))
        {
            try
            {
                Directory.Delete(_destDir, true);
            }
            catch
            {
            }
        }
    }

    [Test]
    public async Task Fast_atomic_move_on_same_filesystem()
    {
        var fileName = "atomic_test.bin";
        var sourceFilePath = Path.Combine(_sourceDir, fileName);
        var expectedBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await File.WriteAllBytesAsync(sourceFilePath, expectedBytes);

        var torrent = new Torrent
        {
            Id = 42,
            Name = fileName,
            SavePath = _sourceDir,
            SourcePath = _sourceDir
        };
        _torrentService.Get(42).Returns(torrent);

        var result = await _subject.RelocateTorrentAsync(42, _destDir);

        Assert.That(result, Is.True);
        Assert.That(File.Exists(sourceFilePath), Is.False);

        var destFilePath = Path.Combine(_destDir, fileName);
        Assert.That(File.Exists(destFilePath), Is.True);
        var destBytes = await File.ReadAllBytesAsync(destFilePath);
        Assert.That(destBytes, Is.EqualTo(expectedBytes));

        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 42 && t.SavePath == _destDir && t.SourcePath == _destDir));
        _eventAggregator.Received().PublishEvent(Arg.Is<FileMoveCompletedEvent>(e => e.Torrent.Id == 42));
    }

    [Test]
    public async Task Copy_verify_delete_fallback_across_directories()
    {
        _subject.ForceFallbackCopy = true;

        var subDir = Path.Combine(_sourceDir, "TorrentDir");
        Directory.CreateDirectory(subDir);
        var file1 = Path.Combine(subDir, "file1.dat");
        var file2 = Path.Combine(subDir, "file2.dat");
        var data1 = new byte[1024];
        var data2 = new byte[2048];
        new Random(42).NextBytes(data1);
        new Random(84).NextBytes(data2);
        await File.WriteAllBytesAsync(file1, data1);
        await File.WriteAllBytesAsync(file2, data2);

        var torrent = new Torrent
        {
            Id = 100,
            Name = "TorrentDir",
            SavePath = _sourceDir,
            SourcePath = _sourceDir
        };
        _torrentService.Get(100).Returns(torrent);

        var result = await _subject.RelocateTorrentAsync(100, _destDir);

        Assert.That(result, Is.True);
        Assert.That(Directory.Exists(subDir), Is.False);

        var destSubDir = Path.Combine(_destDir, "TorrentDir");
        var destFile1 = Path.Combine(destSubDir, "file1.dat");
        var destFile2 = Path.Combine(destSubDir, "file2.dat");
        Assert.That(File.Exists(destFile1), Is.True);
        Assert.That(File.Exists(destFile2), Is.True);

        var read1 = await File.ReadAllBytesAsync(destFile1);
        var read2 = await File.ReadAllBytesAsync(destFile2);
        Assert.That(read1, Is.EqualTo(data1));
        Assert.That(read2, Is.EqualTo(data2));

        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 100 && t.SavePath == _destDir));
        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentRelocationProgressEvent>(e => e.TorrentId == 100 && e.IsComplete && e.Progress >= 1.0));
    }

    [Test]
    public async Task Integrity_check_rejects_truncated_copy_and_preserves_source_file()
    {
        _subject.ForceFallbackCopy = true;
        _subject.SimulateTruncation = true;

        var fileName = "integrity_test.bin";
        var sourceFilePath = Path.Combine(_sourceDir, fileName);
        var originalData = new byte[8192];
        new Random(123).NextBytes(originalData);
        await File.WriteAllBytesAsync(sourceFilePath, originalData);

        var torrent = new Torrent
        {
            Id = 200,
            Name = fileName,
            SavePath = _sourceDir,
            SourcePath = _sourceDir
        };
        _torrentService.Get(200).Returns(torrent);

        var result = await _subject.RelocateTorrentAsync(200, _destDir);

        Assert.That(result, Is.False);
        // Source must be preserved
        Assert.That(File.Exists(sourceFilePath), Is.True);
        var sourceBytes = await File.ReadAllBytesAsync(sourceFilePath);
        Assert.That(sourceBytes, Is.EqualTo(originalData));

        // Partial destination must be cleaned up
        var destFilePath = Path.Combine(_destDir, fileName);
        Assert.That(File.Exists(destFilePath), Is.False);

        _eventAggregator.Received().PublishEvent(Arg.Is<FileMoveFailedEvent>(e => e.ErrorMessage.Contains("Integrity verification failed")));
    }

    [Test]
    public async Task Timestamp_preservation()
    {
        _subject.ForceFallbackCopy = true;

        var fileName = "timestamp_test.bin";
        var sourceFilePath = Path.Combine(_sourceDir, fileName);
        await File.WriteAllBytesAsync(sourceFilePath, new byte[] { 10, 20, 30 });

        var expectedTimestamp = new DateTime(2022, 5, 15, 12, 34, 56, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sourceFilePath, expectedTimestamp);

        var torrent = new Torrent
        {
            Id = 300,
            Name = fileName,
            SavePath = _sourceDir,
            SourcePath = _sourceDir
        };
        _torrentService.Get(300).Returns(torrent);

        var result = await _subject.RelocateTorrentAsync(300, _destDir);

        Assert.That(result, Is.True);
        var destFilePath = Path.Combine(_destDir, fileName);
        Assert.That(File.Exists(destFilePath), Is.True);

        var actualTimestamp = File.GetLastWriteTimeUtc(destFilePath);
        Assert.That(Math.Abs((actualTimestamp - expectedTimestamp).TotalSeconds), Is.LessThan(2.0));
    }

    [Test]
    public async Task Cancellation_token_stops_copy_and_preserves_source()
    {
        _subject.ForceFallbackCopy = true;

        var fileName = "cancel_test.bin";
        var sourceFilePath = Path.Combine(_sourceDir, fileName);
        var originalData = new byte[4096];
        new Random(456).NextBytes(originalData);
        await File.WriteAllBytesAsync(sourceFilePath, originalData);

        var torrent = new Torrent
        {
            Id = 400,
            Name = fileName,
            SavePath = _sourceDir,
            SourcePath = _sourceDir
        };
        _torrentService.Get(400).Returns(torrent);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await _subject.RelocateTorrentAsync(400, _destDir, cts.Token);

        Assert.That(result, Is.False);
        // Source file must be preserved
        Assert.That(File.Exists(sourceFilePath), Is.True);
        var sourceBytes = await File.ReadAllBytesAsync(sourceFilePath);
        Assert.That(sourceBytes, Is.EqualTo(originalData));

        // Destination file must not be left behind
        var destFilePath = Path.Combine(_destDir, fileName);
        Assert.That(File.Exists(destFilePath), Is.False);
    }

    [Test]
    public void IsCrossDeviceException_detects_exdev_and_cross_device_messages()
    {
        var exdevEx = new IOException("Invalid cross-device link", 18);
        Assert.That(TorrentRelocationService.IsCrossDeviceException(exdevEx), Is.True);

        var winNotSameDev = new IOException("The system cannot move the file to a different disk drive.", unchecked((int)0x80070011));
        Assert.That(TorrentRelocationService.IsCrossDeviceException(winNotSameDev), Is.True);

        var textEx = new IOException("EXDEV occurred while moving");
        Assert.That(TorrentRelocationService.IsCrossDeviceException(textEx), Is.True);

        var regularEx = new IOException("File already exists", 80);
        Assert.That(TorrentRelocationService.IsCrossDeviceException(regularEx), Is.False);

        Assert.That(TorrentRelocationService.IsCrossDeviceException(null), Is.False);
    }
}
