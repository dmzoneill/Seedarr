using System;
using System.Collections.Generic;
using System.IO;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
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
}
