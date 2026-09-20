using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BencodeNET.Objects;
using BencodeNET.Parsing;
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

        _service = new FastResumeService(new Lazy<ITorrentService>(() => _torrentService), _pieceStorage, _appFolderInfo);
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

    [Test]
    public void Serialize_should_produce_libtorrent_compliant_bencode_schema()
    {
        var fileMtime = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var expectedMtimeSec = new DateTimeOffset(fileMtime).ToUnixTimeSeconds();

        var data = new FastResumeData
        {
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Bitfield = new[] { true, false, true },
            Uploaded = 123456,
            Downloaded = 654321,
            ActiveTime = 3600,
            SeedingTime = 1800,
            FinishedTime = 1735689600,
            SequentialDownload = true,
            Allocation = "sparse",
            SavePath = "/downloads/completed",
            FilePriority = new List<int> { 1, 0, 7 },
            PiecePriority = new byte[] { 1, 2, 7 },
            Unfinished = new List<FastResumeUnfinishedPiece>
            {
                new()
                {
                    Piece = 142,
                    Bitmask = new byte[] { 0xFF, 0x00 },
                    Adler32 = 12345678
                }
            },
            Files = new List<FastResumeFileEntry>
            {
                new() { Path = "video.mkv", Length = 1048576, Mtime = fileMtime },
                new() { Path = "sub.srt", Length = 2048, Mtime = fileMtime }
            }
        };

        var bytes = FastResumeBencodeSerializer.SerializeToBytes(data);
        Assert.That(bytes, Is.Not.Null);

        var parser = new BencodeParser();
        var dict = parser.Parse<BDictionary>(bytes);

        // Required schema fields per Issue #403
        Assert.That(dict["file-format"].ToString(), Is.EqualTo("libtorrent resume file"));
        Assert.That(((BNumber)dict["file-version"]).Value, Is.EqualTo(1));
        Assert.That(((BString)dict["info-hash"]).Value.ToArray().Length, Is.EqualTo(20));
        Assert.That(((BString)dict["info-hash"]).Value.ToArray(), Is.EqualTo(Convert.FromHexString(data.InfoHash)));

        var piecesBytes = ((BString)dict["pieces"]).Value.ToArray();
        Assert.That(piecesBytes.Length, Is.EqualTo(3));
        Assert.That(piecesBytes[0], Is.EqualTo(1));
        Assert.That(piecesBytes[1], Is.EqualTo(0));
        Assert.That(piecesBytes[2], Is.EqualTo(1));

        Assert.That(((BNumber)dict["total_uploaded"]).Value, Is.EqualTo(123456));
        Assert.That(((BNumber)dict["total_downloaded"]).Value, Is.EqualTo(654321));
        Assert.That(((BNumber)dict["active_time"]).Value, Is.EqualTo(3600));
        Assert.That(((BNumber)dict["seeding_time"]).Value, Is.EqualTo(1800));
        Assert.That(((BNumber)dict["finished_time"]).Value, Is.EqualTo(1735689600));
        Assert.That(((BNumber)dict["sequential_download"]).Value, Is.EqualTo(1));
        Assert.That(dict["save_path"].ToString(), Is.EqualTo("/downloads/completed"));
        Assert.That(dict["allocation"].ToString(), Is.EqualTo("sparse"));

        // File & piece priorities
        var fpList = (BList)dict["file_priority"];
        Assert.That(fpList.Count, Is.EqualTo(3));
        Assert.That(((BNumber)fpList[0]).Value, Is.EqualTo(1));
        Assert.That(((BNumber)fpList[1]).Value, Is.EqualTo(0));
        Assert.That(((BNumber)fpList[2]).Value, Is.EqualTo(7));

        var ppBytes = ((BString)dict["piece_priority"]).Value.ToArray();
        Assert.That(ppBytes, Is.EqualTo(new byte[] { 1, 2, 7 }));

        // Unfinished pieces
        var ufList = (BList)dict["unfinished"];
        Assert.That(ufList.Count, Is.EqualTo(1));
        var ufDict = (BDictionary)ufList[0];
        Assert.That(((BNumber)ufDict["piece"]).Value, Is.EqualTo(142));
        Assert.That(((BString)ufDict["bitmask"]).Value.ToArray(), Is.EqualTo(new byte[] { 0xFF, 0x00 }));
        Assert.That(((BNumber)ufDict["adler32"]).Value, Is.EqualTo(12345678));

        // File sizes & mtimes
        var fsList = (BList)dict["file sizes"];
        Assert.That(fsList.Count, Is.EqualTo(2));
        Assert.That(((BNumber)fsList[0]).Value, Is.EqualTo(1048576));
        Assert.That(((BNumber)fsList[1]).Value, Is.EqualTo(2048));

        var mtList = (BList)dict["mtime"];
        Assert.That(mtList.Count, Is.EqualTo(2));
        Assert.That(((BNumber)mtList[0]).Value, Is.EqualTo(expectedMtimeSec));
        Assert.That(((BNumber)mtList[1]).Value, Is.EqualTo(expectedMtimeSec));

        var mfList = (BList)dict["mapped_files"];
        Assert.That(mfList.Count, Is.EqualTo(2));
        Assert.That(mfList[0].ToString(), Is.EqualTo("video.mkv"));
        Assert.That(mfList[1].ToString(), Is.EqualTo("sub.srt"));
    }

    [Test]
    public void Deserialize_should_parse_standard_libtorrent_bencode()
    {
        var rawInfoHash = new byte[20];
        Array.Fill(rawInfoHash, (byte)0x42);

        var dict = new BDictionary
        {
            ["file-format"] = new BString("libtorrent resume file"),
            ["file-version"] = new BNumber(1),
            ["info-hash"] = new BString(rawInfoHash),
            ["pieces"] = new BString(new byte[] { 1, 1, 0, 1 }),
            ["total_uploaded"] = new BNumber(9999),
            ["total_downloaded"] = new BNumber(8888),
            ["active_time"] = new BNumber(500),
            ["seeding_time"] = new BNumber(250),
            ["finished_time"] = new BNumber(1700000000),
            ["sequential_download"] = new BNumber(1),
            ["save_path"] = new BString("/var/torrents"),
            ["allocation"] = new BString("sparse"),
            ["file_priority"] = new BList { (IBObject)new BNumber(1), (IBObject)new BNumber(0) },
            ["piece_priority"] = new BString(new byte[] { 1, 1, 0, 1 }),
            ["file sizes"] = new BList { (IBObject)new BNumber(5000), (IBObject)new BNumber(3000) },
            ["mtime"] = new BList { (IBObject)new BNumber(1700000100), (IBObject)new BNumber(1700000200) },
            ["mapped_files"] = new BList { new BString("dir/file1.bin"), new BString("dir/file2.bin") },
            ["unfinished"] = new BList
            {
                new BDictionary
                {
                    ["piece"] = new BNumber(2),
                    ["bitmask"] = new BString(new byte[] { 0xAA, 0x55 }),
                    ["adler32"] = new BNumber(987654)
                }
            }
        };

        var bytes = dict.EncodeAsBytes();
        var data = FastResumeBencodeSerializer.DeserializeFromBytes(bytes);

        Assert.That(data, Is.Not.Null);
        Assert.That(data.FileFormat, Is.EqualTo("libtorrent resume file"));
        Assert.That(data.FileVersion, Is.EqualTo(1));
        Assert.That(data.InfoHash, Is.EqualTo(Convert.ToHexString(rawInfoHash).ToLowerInvariant()));
        Assert.That(data.Bitfield, Is.EqualTo(new[] { true, true, false, true }));
        Assert.That(data.Progress, Is.EqualTo(0.75));
        Assert.That(data.Status, Is.EqualTo("Downloading"));
        Assert.That(data.Uploaded, Is.EqualTo(9999));
        Assert.That(data.Downloaded, Is.EqualTo(8888));
        Assert.That(data.ActiveTime, Is.EqualTo(500));
        Assert.That(data.SeedingTime, Is.EqualTo(250));
        Assert.That(data.FinishedTime, Is.EqualTo(1700000000));
        Assert.That(data.SequentialDownload, Is.True);
        Assert.That(data.SavePath, Is.EqualTo("/var/torrents"));
        Assert.That(data.Allocation, Is.EqualTo("sparse"));
        Assert.That(data.FilePriority, Is.EqualTo(new[] { 1, 0 }));
        Assert.That(data.PiecePriority, Is.EqualTo(new byte[] { 1, 1, 0, 1 }));

        Assert.That(data.Unfinished.Count, Is.EqualTo(1));
        Assert.That(data.Unfinished[0].Piece, Is.EqualTo(2));
        Assert.That(data.Unfinished[0].Bitmask, Is.EqualTo(new byte[] { 0xAA, 0x55 }));
        Assert.That(data.Unfinished[0].Adler32, Is.EqualTo(987654));

        Assert.That(data.Files.Count, Is.EqualTo(2));
        Assert.That(data.Files[0].Path, Is.EqualTo("dir/file1.bin"));
        Assert.That(data.Files[0].Length, Is.EqualTo(5000));
        Assert.That(data.Files[0].Mtime, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1700000100).UtcDateTime));

        Assert.That(data.Files[1].Path, Is.EqualTo("dir/file2.bin"));
        Assert.That(data.Files[1].Length, Is.EqualTo(3000));
        Assert.That(data.Files[1].Mtime, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1700000200).UtcDateTime));
    }

    [Test]
    public void Deserialize_should_fallback_to_qBt_savePath_when_save_path_missing()
    {
        var dict = new BDictionary
        {
            ["file-format"] = new BString("libtorrent resume file"),
            ["file-version"] = new BNumber(1),
            ["qBt-savePath"] = new BString("/qbittorrent/data"),
            ["qBt-name"] = new BString("qbittorrent_movie.mp4"),
            ["file sizes"] = new BList { (IBObject)new BNumber(1024) }
        };

        var bytes = dict.EncodeAsBytes();
        var data = FastResumeBencodeSerializer.DeserializeFromBytes(bytes);

        Assert.That(data.SavePath, Is.EqualTo("/qbittorrent/data"));
        Assert.That(data.Files.Count, Is.EqualTo(1));
        Assert.That(data.Files[0].Path, Is.EqualTo("qbittorrent_movie.mp4"));
    }

    [Test]
    public void Deserialize_should_support_packed_bitfields_when_version_greater_than_1()
    {
        var dict = new BDictionary
        {
            ["file-format"] = new BString("libtorrent resume file"),
            ["file-version"] = new BNumber(2),
            // Packed bitfield: byte 0b10100000 = pieces 0 and 2 set out of 8
            ["pieces"] = new BString(new byte[] { 0xA0 })
        };

        var bytes = dict.EncodeAsBytes();
        var data = FastResumeBencodeSerializer.DeserializeFromBytes(bytes);

        Assert.That(data.Bitfield.Length, Is.EqualTo(8));
        Assert.That(data.Bitfield[0], Is.True);
        Assert.That(data.Bitfield[1], Is.False);
        Assert.That(data.Bitfield[2], Is.True);
        Assert.That(data.Bitfield[3], Is.False);
    }

    [Test]
    public void LoadFastResume_should_gracefully_fallback_to_json_when_loading_legacy_fastresume_file()
    {
        var torrent = new Torrent
        {
            Id = 42,
            InfoHash = "legacyjasonhash123456789012345678901234",
            SavePath = _tempAppDataFolder,
            PieceCount = 2,
            Progress = 1.0,
            Status = TorrentStatus.Seeding
        };

        var filePath = Path.Combine(_tempAppDataFolder, "legacy.dat");
        File.WriteAllBytes(filePath, new byte[1024]);

        var legacyData = new FastResumeData
        {
            InfoHash = torrent.InfoHash,
            Bitfield = new[] { true, true },
            Progress = 1.0,
            Status = "Seeding",
            SavePath = _tempAppDataFolder,
            Files = new List<FastResumeFileEntry>
            {
                new() { Path = "legacy.dat", Length = 1024, Mtime = File.GetLastWriteTimeUtc(filePath) }
            }
        };

        var fastResumeDir = Path.Combine(_tempAppDataFolder, "fastresume");
        Directory.CreateDirectory(fastResumeDir);
        File.WriteAllText(Path.Combine(fastResumeDir, $"{torrent.InfoHash.ToLowerInvariant()}.fastresume"), STJson.ToJson(legacyData));

        _torrentService.GetByInfoHash(torrent.InfoHash).Returns(torrent);

        var loaded = _service.LoadFastResume(torrent.InfoHash);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded.InfoHash, Is.EqualTo(torrent.InfoHash));
        Assert.That(loaded.Progress, Is.EqualTo(1.0));
        _pieceStorage.Received(1).SetVerifiedPieces(torrent.InfoHash, Arg.Is<bool[]>(b => b.Length == 2 && b[0] && b[1]));
    }
}
