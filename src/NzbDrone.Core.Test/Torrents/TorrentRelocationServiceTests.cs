using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentRelocationServiceTests
{
    private ITorrentService _torrentService;
    private IEventAggregator _eventAggregator;
    private IConfigService _configService;
    private IConnectionManager _connectionManager;
    private IPeerServer _peerServer;
    private IPieceStorage _pieceStorage;
    private TorrentRelocationService _subject;
    private string _sourceDir;
    private string _destDir;

    [SetUp]
    public void SetUp()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _configService = Substitute.For<IConfigService>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _peerServer = Substitute.For<IPeerServer>();
        _pieceStorage = Substitute.For<IPieceStorage>();

        _subject = new TorrentRelocationService(
            _torrentService,
            _eventAggregator,
            _configService,
            _connectionManager,
            _peerServer,
            _pieceStorage);

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
            SourcePath = _sourceDir,
            Status = TorrentStatus.Downloading
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
            SourcePath = _sourceDir,
            Status = TorrentStatus.Downloading
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
            SourcePath = _sourceDir,
            Status = TorrentStatus.Downloading
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
            SourcePath = _sourceDir,
            Status = TorrentStatus.Downloading
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
            SourcePath = _sourceDir,
            Status = TorrentStatus.Downloading
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
    public async Task Exclusive_lock_blocks_concurrent_relocations_on_same_torrent()
    {
        var torrentId = 500;
        var sem = _subject.GetTorrentLock(torrentId);

        // Pre-acquire the lock
        await sem.WaitAsync();
        Assert.That(_subject.IsLocked(torrentId), Is.True);

        // Attempting a relocation while the lock is held should block or time out
        using var cts = new CancellationTokenSource(100);
        try
        {
            var result = await _subject.RelocateTorrentAsync(torrentId, _destDir, cts.Token);
            Assert.That(result, Is.False);
        }
        catch (OperationCanceledException)
        {
            // Expected if WaitAsync throws on cancellation
        }

        // Release the lock
        sem.Release();
        Assert.That(_subject.IsLocked(torrentId), Is.False);
    }

    [Test]
    public async Task Peer_choking_and_handle_teardown_called_prior_to_file_operations()
    {
        var fileName = "peer_test.bin";
        var sourceFilePath = Path.Combine(_sourceDir, fileName);
        await File.WriteAllBytesAsync(sourceFilePath, new byte[] { 1, 2, 3 });

        var torrent = new Torrent
        {
            Id = 600,
            Name = fileName,
            InfoHash = "abc600def",
            SavePath = _sourceDir,
            SourcePath = _sourceDir,
            Status = TorrentStatus.Seeding
        };
        _torrentService.Get(600).Returns(torrent);

        var chokeCalledBeforeMove = false;
        var flushCalledBeforeMove = false;
        var closeHandlesCalledBeforeMove = false;

        _peerServer.When(x => x.ChokePeers("abc600def")).Do(_ =>
        {
            var destPath = Path.Combine(_destDir, fileName);
            if (!File.Exists(destPath))
            {
                chokeCalledBeforeMove = true;
            }
        });

        _pieceStorage.When(x => x.Flush()).Do(_ =>
        {
            var destPath = Path.Combine(_destDir, fileName);
            if (!File.Exists(destPath))
            {
                flushCalledBeforeMove = true;
            }
        });

        _pieceStorage.When(x => x.CloseHandles("abc600def")).Do(_ =>
        {
            var destPath = Path.Combine(_destDir, fileName);
            if (!File.Exists(destPath))
            {
                closeHandlesCalledBeforeMove = true;
            }
        });

        var result = await _subject.RelocateTorrentAsync(600, _destDir);

        Assert.That(result, Is.True);
        Assert.That(chokeCalledBeforeMove, Is.True);
        Assert.That(flushCalledBeforeMove, Is.True);
        Assert.That(closeHandlesCalledBeforeMove, Is.True);

        _peerServer.Received(1).ChokePeers("abc600def");
        _peerServer.Received(1).UnchokePeers("abc600def");
        _pieceStorage.Received(1).Flush();
        _pieceStorage.Received(1).CloseHandles("abc600def");
    }

    [Test]
    public async Task Status_is_restored_and_lock_is_released_when_relocation_encounters_error()
    {
        _subject.ForceFallbackCopy = true;
        _subject.SimulateTruncation = true;

        var fileName = "error_restore_test.bin";
        var sourceFilePath = Path.Combine(_sourceDir, fileName);
        await File.WriteAllBytesAsync(sourceFilePath, new byte[] { 1, 2, 3, 4, 5 });

        var torrent = new Torrent
        {
            Id = 700,
            Name = fileName,
            InfoHash = "error700hash",
            SavePath = _sourceDir,
            SourcePath = _sourceDir,
            Status = TorrentStatus.Seeding
        };
        _torrentService.Get(700).Returns(torrent);

        var result = await _subject.RelocateTorrentAsync(700, _destDir);

        Assert.That(result, Is.False);

        // Status must be restored to previous status (Seeding), not left in Moving
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));

        // Lock must be released
        Assert.That(_subject.IsLocked(700), Is.False);

        // Peers should be unchoked
        _peerServer.Received(1).UnchokePeers("error700hash");
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

    [Test]
    public async Task Multi_file_relocation_rollback_when_file_fails_midway_cleans_up_destination_and_preserves_source()
    {
        _subject.ForceFallbackCopy = true;

        var subDir = Path.Combine(_sourceDir, "RollbackTorrent");
        Directory.CreateDirectory(subDir);
        var file1 = Path.Combine(subDir, "file1.dat");
        var file2 = Path.Combine(subDir, "file2.dat");
        var file3 = Path.Combine(subDir, "file3.dat");
        var data1 = new byte[] { 1, 2, 3, 4, 5 };
        var data2 = new byte[] { 6, 7, 8, 9, 10 };
        var data3 = new byte[] { 11, 12, 13, 14, 15 };
        await File.WriteAllBytesAsync(file1, data1);
        await File.WriteAllBytesAsync(file2, data2);
        await File.WriteAllBytesAsync(file3, data3);

        var torrent = new Torrent
        {
            Id = 800,
            Name = "RollbackTorrent",
            SavePath = _sourceDir,
            SourcePath = _sourceDir,
            Status = TorrentStatus.Seeding,
        };
        _torrentService.Get(800).Returns(torrent);

        // Fail midway on file index 1 (file2)
        _subject.SimulateFailureOnFileIndex = 1;

        var result = await _subject.RelocateTorrentAsync(800, _destDir);

        Assert.That(result, Is.False);

        // All source files must be intact
        Assert.That(Directory.Exists(subDir), Is.True);
        Assert.That(File.Exists(file1), Is.True);
        Assert.That(File.Exists(file2), Is.True);
        Assert.That(File.Exists(file3), Is.True);
        Assert.That(await File.ReadAllBytesAsync(file1), Is.EqualTo(data1));
        Assert.That(await File.ReadAllBytesAsync(file2), Is.EqualTo(data2));
        Assert.That(await File.ReadAllBytesAsync(file3), Is.EqualTo(data3));

        // Destination must be completely cleaned up (no destination files or empty directory left)
        var destSubDir = Path.Combine(_destDir, "RollbackTorrent");
        var destFile1 = Path.Combine(destSubDir, "file1.dat");
        var destFile2 = Path.Combine(destSubDir, "file2.dat");
        var destFile3 = Path.Combine(destSubDir, "file3.dat");
        Assert.That(File.Exists(destFile1), Is.False);
        Assert.That(File.Exists(destFile2), Is.False);
        Assert.That(File.Exists(destFile3), Is.False);
        Assert.That(Directory.Exists(destSubDir), Is.False);

        // Torrent status and save path must be restored
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(torrent.SavePath, Is.EqualTo(_sourceDir));
        _eventAggregator.Received().PublishEvent(Arg.Is<FileMoveFailedEvent>(e => e.Torrent.Id == 800));
    }

    [Test]
    public async Task Preflight_disk_space_check_aborts_early_when_insufficient_free_space()
    {
        var fileName = "space_test.bin";
        var sourceFilePath = Path.Combine(_sourceDir, fileName);
        var testData = new byte[1024];
        await File.WriteAllBytesAsync(sourceFilePath, testData);

        var torrent = new Torrent
        {
            Id = 801,
            Name = fileName,
            SavePath = _sourceDir,
            SourcePath = _sourceDir,
            Status = TorrentStatus.Downloading,
        };
        _torrentService.Get(801).Returns(torrent);

        var diskProvider = Substitute.For<IDiskProvider>();
        diskProvider.GetAvailableFreeSpace(Arg.Any<string>()).Returns(100L); // 100 bytes available < 1024 bytes required
        diskProvider.CheckFolderWritable(Arg.Any<string>()).Returns(true);
        _subject.DiskProvider = diskProvider;

        var result = await _subject.RelocateTorrentAsync(801, _destDir);

        Assert.That(result, Is.False);

        // Source file must be intact
        Assert.That(File.Exists(sourceFilePath), Is.True);

        // Destination file must never have been touched or created
        var destFilePath = Path.Combine(_destDir, fileName);
        Assert.That(File.Exists(destFilePath), Is.False);

        // Failure event published and status preserved
        _eventAggregator.Received().PublishEvent(Arg.Is<FileMoveFailedEvent>(e => e.ErrorMessage.Contains("Insufficient free space")));
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Downloading));
    }

    [Test]
    public async Task Preflight_write_permission_check_aborts_early_when_directory_is_readonly()
    {
        var fileName = "perm_test.bin";
        var sourceFilePath = Path.Combine(_sourceDir, fileName);
        var testData = new byte[512];
        await File.WriteAllBytesAsync(sourceFilePath, testData);

        var torrent = new Torrent
        {
            Id = 802,
            Name = fileName,
            SavePath = _sourceDir,
            SourcePath = _sourceDir,
            Status = TorrentStatus.Downloading,
        };
        _torrentService.Get(802).Returns(torrent);

        var diskProvider = Substitute.For<IDiskProvider>();
        diskProvider.GetAvailableFreeSpace(Arg.Any<string>()).Returns(10_000_000L);
        diskProvider.CheckFolderWritable(Arg.Any<string>()).Returns(false); // read-only directory
        _subject.DiskProvider = diskProvider;

        var result = await _subject.RelocateTorrentAsync(802, _destDir);

        Assert.That(result, Is.False);

        // Source file must be intact
        Assert.That(File.Exists(sourceFilePath), Is.True);

        // Destination file must never have been created
        var destFilePath = Path.Combine(_destDir, fileName);
        Assert.That(File.Exists(destFilePath), Is.False);

        // Failure event published and status preserved
        _eventAggregator.Received().PublishEvent(Arg.Is<FileMoveFailedEvent>(e => e.ErrorMessage.Contains("not writable") || e.ErrorMessage.Contains("access is denied")));
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Downloading));
    }
}
