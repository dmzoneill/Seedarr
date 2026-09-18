using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class FastResumeServiceTest
{
    private ITorrentService _torrentService;
    private IPieceStorage _pieceStorage;
    private IAppFolderInfo _appFolderInfo;
    private string _tempAppDataFolder;
    private FastResumeService _service;

    [SetUp]
    public void SetUp()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _pieceStorage = Substitute.For<IPieceStorage>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();

        _tempAppDataFolder = Path.Combine(Path.GetTempPath(), "seedarr_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempAppDataFolder);
        _appFolderInfo.AppDataFolder.Returns(_tempAppDataFolder);

        _service = new FastResumeService(_torrentService, _pieceStorage, _appFolderInfo);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempAppDataFolder))
            {
                Directory.Delete(_tempAppDataFolder, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Test]
    public void SaveAll_should_save_fast_resume_for_all_torrents()
    {
        var torrent1 = new Torrent
        {
            Id = 1,
            InfoHash = "1111111111111111111111111111111111111111",
            Uploaded = 1000,
            Downloaded = 2000,
            Progress = 1.0,
            PieceCount = 4,
            Status = TorrentStatus.Seeding
        };
        var torrent2 = new Torrent
        {
            Id = 2,
            InfoHash = "2222222222222222222222222222222222222222",
            Uploaded = 500,
            Downloaded = 1000,
            Progress = 0.5,
            PieceCount = 4,
            Status = TorrentStatus.Downloading
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        _service.SaveAll();

        var file1 = Path.Combine(_tempAppDataFolder, "fastresume", $"{torrent1.InfoHash.ToLowerInvariant()}.fastresume");
        var file2 = Path.Combine(_tempAppDataFolder, "fastresume", $"{torrent2.InfoHash.ToLowerInvariant()}.fastresume");

        Assert.That(File.Exists(file1), Is.True);
        Assert.That(File.Exists(file2), Is.True);
    }

    [Test]
    public void SaveFastResume_should_persist_bitfield_and_metadata()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "abcdefabcdefabcdefabcdefabcdefabcdefabcd",
            Uploaded = 12345,
            Downloaded = 67890,
            Progress = 0.5,
            PieceCount = 3,
            Status = TorrentStatus.Downloading
        };

        var bitfield = new[] { true, false, true };
        _pieceStorage.GetVerifiedPieces(torrent.InfoHash).Returns(bitfield);

        _service.SaveFastResume(torrent);

        var loaded = _service.LoadFastResume(torrent.InfoHash);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded.InfoHash, Is.EqualTo(torrent.InfoHash));
        Assert.That(loaded.Uploaded, Is.EqualTo(12345));
        Assert.That(loaded.Downloaded, Is.EqualTo(67890));
        Assert.That(loaded.Bitfield, Is.EqualTo(bitfield));
    }

    [Test]
    public void LoadFastResume_should_restore_bitfield_into_piece_storage()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "1234567890123456789012345678901234567890",
            Uploaded = 100,
            Downloaded = 200,
            Progress = 1.0,
            PieceCount = 2,
            Status = TorrentStatus.Seeding
        };

        _pieceStorage.GetVerifiedPieces(torrent.InfoHash).Returns((bool[])null);

        _service.SaveFastResume(torrent);

        _service.LoadFastResume(torrent.InfoHash);

        _pieceStorage.Received(1).SetVerifiedPieces(torrent.InfoHash, Arg.Is<bool[]>(b => b.Length == 2 && b[0] && b[1]));
    }

    [Test]
    public void SaveFastResume_should_not_leave_temp_file_on_success()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "abcdefabcdefabcdefabcdefabcdefabcdefabcd",
            Uploaded = 100,
            Downloaded = 200,
            Progress = 1.0,
            PieceCount = 1,
            Status = TorrentStatus.Seeding
        };

        _service.SaveFastResume(torrent);

        var filePath = Path.Combine(_tempAppDataFolder, "fastresume", $"{torrent.InfoHash.ToLowerInvariant()}.fastresume");
        var tempPath = $"{filePath}.tmp";

        Assert.That(File.Exists(filePath), Is.True);
        Assert.That(File.Exists(tempPath), Is.False);
    }

    [Test]
    public void SaveFastResume_should_overwrite_existing_target_file()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "abcdefabcdefabcdefabcdefabcdefabcdefabcd",
            Uploaded = 100,
            Downloaded = 200,
            Progress = 0.5,
            PieceCount = 2,
            Status = TorrentStatus.Downloading
        };

        _service.SaveFastResume(torrent);

        torrent.Uploaded = 500;
        torrent.Progress = 1.0;
        torrent.Status = TorrentStatus.Seeding;

        _service.SaveFastResume(torrent);

        var loaded = _service.LoadFastResume(torrent.InfoHash);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded.Uploaded, Is.EqualTo(500));
        Assert.That(loaded.Progress, Is.EqualTo(1.0));
    }

    [Test]
    public void SaveFastResume_should_cleanup_temp_file_on_failure()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "abcdefabcdefabcdefabcdefabcdefabcdefabcd",
            Uploaded = 100,
            Downloaded = 200,
            Progress = 1.0,
            PieceCount = 1,
            Status = TorrentStatus.Seeding
        };

        var resumeDir = Path.Combine(_tempAppDataFolder, "fastresume");
        Directory.CreateDirectory(resumeDir);
        var filePath = Path.Combine(resumeDir, $"{torrent.InfoHash.ToLowerInvariant()}.fastresume");
        var tempPath = $"{filePath}.tmp";

        Directory.CreateDirectory(filePath);

        Assert.Catch<Exception>(() => _service.SaveFastResume(torrent));
        Assert.That(File.Exists(tempPath), Is.False);
    }

    [Test]
    public void LoadFastResume_should_succeed_when_file_size_and_mtime_match()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "1234567890123456789012345678901234567890",
            SavePath = _tempAppDataFolder,
            PieceCount = 2,
            Progress = 1.0,
            Status = TorrentStatus.Seeding
        };

        var filePath = Path.Combine(_tempAppDataFolder, "testfile.dat");
        File.WriteAllBytes(filePath, new byte[1024]);
        var mtime = File.GetLastWriteTimeUtc(filePath);

        var resumeData = new FastResumeData
        {
            InfoHash = torrent.InfoHash,
            Bitfield = new[] { true, true },
            Progress = 1.0,
            Status = "Seeding",
            SavePath = _tempAppDataFolder,
            Files = new List<FastResumeFileEntry>
            {
                new() { Path = "testfile.dat", Length = 1024, Mtime = mtime }
            }
        };

        var fastResumeDir = Path.Combine(_tempAppDataFolder, "fastresume");
        Directory.CreateDirectory(fastResumeDir);
        File.WriteAllText(Path.Combine(fastResumeDir, $"{torrent.InfoHash.ToLowerInvariant()}.fastresume"), STJson.ToJson(resumeData));

        _torrentService.GetByInfoHash(torrent.InfoHash).Returns(torrent);

        var loaded = _service.LoadFastResume(torrent.InfoHash);

        Assert.That(loaded, Is.Not.Null);
        _pieceStorage.Received(1).SetVerifiedPieces(torrent.InfoHash, Arg.Is<bool[]>(b => b.Length == 2 && b[0] && b[1]));
        _torrentService.DidNotReceive().Recheck(torrent.Id);
    }

    [Test]
    public void LoadFastResume_should_trigger_recheck_fallback_when_file_size_mismatches()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "1234567890123456789012345678901234567890",
            SavePath = _tempAppDataFolder,
            PieceCount = 2,
            Progress = 1.0,
            Status = TorrentStatus.Seeding
        };

        var filePath = Path.Combine(_tempAppDataFolder, "truncated.dat");
        File.WriteAllBytes(filePath, new byte[512]);

        var resumeData = new FastResumeData
        {
            InfoHash = torrent.InfoHash,
            Bitfield = new[] { true, true },
            Progress = 1.0,
            Status = "Seeding",
            SavePath = _tempAppDataFolder,
            Files = new List<FastResumeFileEntry>
            {
                new() { Path = "truncated.dat", Length = 1024, Mtime = File.GetLastWriteTimeUtc(filePath) }
            }
        };

        var fastResumeDir = Path.Combine(_tempAppDataFolder, "fastresume");
        Directory.CreateDirectory(fastResumeDir);
        File.WriteAllText(Path.Combine(fastResumeDir, $"{torrent.InfoHash.ToLowerInvariant()}.fastresume"), STJson.ToJson(resumeData));

        _torrentService.GetByInfoHash(torrent.InfoHash).Returns(torrent);

        var loaded = _service.LoadFastResume(torrent.InfoHash);

        Assert.That(loaded, Is.Null);
        _pieceStorage.DidNotReceive().SetVerifiedPieces(torrent.InfoHash, Arg.Is<bool[]>(b => b.All(x => x)));
        _pieceStorage.Received().SetVerifiedPieces(torrent.InfoHash, null);
        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Status == TorrentStatus.QueuedForChecking));
        _torrentService.Received(1).Recheck(torrent.Id);
    }

    [Test]
    public void LoadFastResume_should_trigger_recheck_fallback_when_file_is_missing()
    {
        var torrent = new Torrent
        {
            Id = 2,
            InfoHash = "abcdef1234567890abcdef1234567890abcdef12",
            SavePath = _tempAppDataFolder,
            PieceCount = 2,
            Progress = 1.0,
            Status = TorrentStatus.Seeding
        };

        var resumeData = new FastResumeData
        {
            InfoHash = torrent.InfoHash,
            Bitfield = new[] { true, true },
            Progress = 1.0,
            Status = "Seeding",
            SavePath = _tempAppDataFolder,
            Files = new List<FastResumeFileEntry>
            {
                new() { Path = "non_existent.dat", Length = 1024, Mtime = DateTime.UtcNow }
            }
        };

        var fastResumeDir = Path.Combine(_tempAppDataFolder, "fastresume");
        Directory.CreateDirectory(fastResumeDir);
        File.WriteAllText(Path.Combine(fastResumeDir, $"{torrent.InfoHash.ToLowerInvariant()}.fastresume"), STJson.ToJson(resumeData));

        _torrentService.GetByInfoHash(torrent.InfoHash).Returns(torrent);

        var loaded = _service.LoadFastResume(torrent.InfoHash);

        Assert.That(loaded, Is.Null);
        _pieceStorage.DidNotReceive().SetVerifiedPieces(torrent.InfoHash, Arg.Is<bool[]>(b => b.All(x => x)));
        _pieceStorage.Received().SetVerifiedPieces(torrent.InfoHash, null);
        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Status == TorrentStatus.QueuedForChecking));
        _torrentService.Received(1).Recheck(torrent.Id);
    }

    [Test]
    public void LoadFastResume_should_trigger_recheck_fallback_without_unhandled_crash_when_fastresume_corrupted()
    {
        var torrent = new Torrent
        {
            Id = 3,
            InfoHash = "corruptedhash123456789012345678901234567",
            SavePath = _tempAppDataFolder,
            PieceCount = 4,
            Progress = 0.5,
            Status = TorrentStatus.Downloading
        };

        var fastResumeDir = Path.Combine(_tempAppDataFolder, "fastresume");
        Directory.CreateDirectory(fastResumeDir);
        File.WriteAllText(Path.Combine(fastResumeDir, $"{torrent.InfoHash.ToLowerInvariant()}.fastresume"), "{\"infoHash\": \"corrupted\", invalid_json_syntax!!!");

        _torrentService.GetByInfoHash(torrent.InfoHash).Returns(torrent);

        FastResumeData loaded = null;
        Assert.DoesNotThrow(() => loaded = _service.LoadFastResume(torrent.InfoHash));

        Assert.That(loaded, Is.Null);
        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Status == TorrentStatus.QueuedForChecking));
        _torrentService.Received(1).Recheck(torrent.Id);
    }

    [Test]
    public void LoadFastResume_should_trigger_recheck_fallback_when_mtime_mismatches()
    {
        var torrent = new Torrent
        {
            Id = 4,
            InfoHash = "mtimehash12345678901234567890123456789012",
            SavePath = _tempAppDataFolder,
            PieceCount = 2,
            Progress = 1.0,
            Status = TorrentStatus.Seeding
        };

        var filePath = Path.Combine(_tempAppDataFolder, "modified_external.dat");
        File.WriteAllBytes(filePath, new byte[1024]);
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);

        var pastMtime = DateTime.UtcNow.AddHours(-5);

        var resumeData = new FastResumeData
        {
            InfoHash = torrent.InfoHash,
            Bitfield = new[] { true, true },
            Progress = 1.0,
            Status = "Seeding",
            SavePath = _tempAppDataFolder,
            Files = new List<FastResumeFileEntry>
            {
                new() { Path = "modified_external.dat", Length = 1024, Mtime = pastMtime }
            }
        };

        var fastResumeDir = Path.Combine(_tempAppDataFolder, "fastresume");
        Directory.CreateDirectory(fastResumeDir);
        File.WriteAllText(Path.Combine(fastResumeDir, $"{torrent.InfoHash.ToLowerInvariant()}.fastresume"), STJson.ToJson(resumeData));

        _torrentService.GetByInfoHash(torrent.InfoHash).Returns(torrent);

        var loaded = _service.LoadFastResume(torrent.InfoHash);

        Assert.That(loaded, Is.Null);
        _pieceStorage.DidNotReceive().SetVerifiedPieces(torrent.InfoHash, Arg.Is<bool[]>(b => b.All(x => x)));
        _pieceStorage.Received().SetVerifiedPieces(torrent.InfoHash, null);
        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Status == TorrentStatus.QueuedForChecking));
        _torrentService.Received(1).Recheck(torrent.Id);
    }

    [Test]
    public void PerformPieceHashVerification_should_reconstruct_bitfield_and_write_fresh_fastresume()
    {
        var torrent = new Torrent
        {
            Id = 5,
            InfoHash = "recheckverif1234567890123456789012345678",
            SavePath = _tempAppDataFolder,
            PieceCount = 2,
            PieceLength = 512,
            TotalSize = 1024,
            Progress = 0.0,
            Status = TorrentStatus.QueuedForChecking
        };

        var filePath = Path.Combine(_tempAppDataFolder, "verified_file.dat");
        File.WriteAllBytes(filePath, new byte[1024]);

        var resumeData = new FastResumeData
        {
            InfoHash = torrent.InfoHash,
            Bitfield = new[] { false, false },
            SavePath = _tempAppDataFolder,
            Files = new List<FastResumeFileEntry>
            {
                new() { Path = "verified_file.dat", Length = 1024, Mtime = File.GetLastWriteTimeUtc(filePath) }
            }
        };

        _service.PerformPieceHashVerification(torrent, resumeData);

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(torrent.Progress, Is.EqualTo(1.0));
        _pieceStorage.Received(1).SetVerifiedPieces(torrent.InfoHash, Arg.Is<bool[]>(b => b.Length == 2 && b[0] && b[1]));

        var writtenFile = Path.Combine(_tempAppDataFolder, "fastresume", $"{torrent.InfoHash.ToLowerInvariant()}.fastresume");
        Assert.That(File.Exists(writtenFile), Is.True);
    }

    [Test]
    public void LoadAll_should_reconcile_fastresume_for_all_torrents()
    {
        var torrent1 = new Torrent
        {
            Id = 1,
            InfoHash = "1111111111111111111111111111111111111111",
            SavePath = _tempAppDataFolder,
            PieceCount = 1,
            Progress = 1.0,
            Status = TorrentStatus.Seeding
        };
        var torrent2 = new Torrent
        {
            Id = 2,
            InfoHash = "2222222222222222222222222222222222222222",
            SavePath = _tempAppDataFolder,
            PieceCount = 1,
            Progress = 1.0,
            Status = TorrentStatus.Seeding
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var file1 = Path.Combine(_tempAppDataFolder, "file1.dat");
        File.WriteAllBytes(file1, new byte[512]);

        var resumeDir = Path.Combine(_tempAppDataFolder, "fastresume");
        Directory.CreateDirectory(resumeDir);

        var data1 = new FastResumeData
        {
            InfoHash = torrent1.InfoHash,
            Bitfield = new[] { true },
            SavePath = _tempAppDataFolder,
            Files = new List<FastResumeFileEntry>
            {
                new() { Path = "file1.dat", Length = 512, Mtime = File.GetLastWriteTimeUtc(file1) }
            }
        };
        File.WriteAllText(Path.Combine(resumeDir, $"{torrent1.InfoHash.ToLowerInvariant()}.fastresume"), STJson.ToJson(data1));

        _service.LoadAll();

        _pieceStorage.Received(1).SetVerifiedPieces(torrent1.InfoHash, Arg.Is<bool[]>(b => b.Length == 1 && b[0]));
    }
}
