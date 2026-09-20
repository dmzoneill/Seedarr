using System.Net;
using Microsoft.AspNetCore.Http;
using NzbDrone.Core.RemotePathMappings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using Seedarr.Api.V1.QBittorrent;

namespace Seedarr.Api.V1.Test.QBittorrent;

[TestFixture]
public class QBittorrentApiControllerTest
{
    private ITorrentService _torrentService;
    private ITorrentFileService _torrentFileService;
    private ITorrentFileParser _torrentFileParser;
    private ITorrentImportService _torrentImportService;
    private ITrackerEntryService _trackerEntryService;
    private IConfigService _configService;
    private ITagService _tagService;
    private IConfigFileProvider _configFileProvider;
    private ICategoryService _categoryService;
    private QBittorrentApiController _controller;

    [SetUp]
    public void SetUp()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _torrentFileParser = Substitute.For<ITorrentFileParser>();
        _torrentImportService = Substitute.For<ITorrentImportService>();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _configService = Substitute.For<IConfigService>();
        _tagService = Substitute.For<ITagService>();
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _categoryService = Substitute.For<ICategoryService>();

        _configFileProvider.AuthenticationEnabled.Returns(false);

        _controller = new QBittorrentApiController(
            _torrentService,
            _torrentFileService,
            _torrentFileParser,
            _torrentImportService,
            _trackerEntryService,
            _configService,
            _tagService,
            _configFileProvider,
            categoryService: _categoryService);
    }

    [Test]
    public void RenameFile_Updates_File_Path()
    {
        const string hash = "abcdef1234567890abcdef1234567890abcdef12";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = hash,
            Name = "Test Torrent",
        };
        var file = new TorrentFile
        {
            Id = 10,
            TorrentId = 1,
            Path = "Season 1/Episode 01.mkv",
            Size = 5000,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file });

        var result = _controller.RenameFile(hash, "Season 1/Episode 01.mkv", "Season 1/Episode 01 - Pilot.mkv");

        Assert.That(result, Is.InstanceOf<ContentResult>());
        var content = (ContentResult)result;
        Assert.That(content.Content, Is.EqualTo("Ok."));
        Assert.That(file.Path, Is.EqualTo("Season 1/Episode 01 - Pilot.mkv"));
        _torrentFileService.Received(1).Update(file);
        _torrentService.Received(1).Recheck(1);
    }

    [Test]
    public void RenameFile_With_Directory_Traversal_In_NewPath_Returns_BadRequest()
    {
        const string hash = "abcdef1234567890abcdef1234567890abcdef12";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = hash,
            Name = "Test Torrent",
        };
        var file = new TorrentFile
        {
            Id = 10,
            TorrentId = 1,
            Path = "Season 1/Episode 01.mkv",
            Size = 5000,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file });

        var result = _controller.RenameFile(hash, "Season 1/Episode 01.mkv", "../../outside.mkv");

        Assert.That(result, Is.InstanceOf<BadRequestResult>());
        _torrentFileService.DidNotReceive().Update(Arg.Any<TorrentFile>());
    }

    [Test]
    public void RenameFile_With_Directory_Traversal_In_OldPath_Returns_BadRequest()
    {
        const string hash = "abcdef1234567890abcdef1234567890abcdef12";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = hash,
            Name = "Test Torrent",
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var result = _controller.RenameFile(hash, "../outside.mkv", "Season 1/Episode 01.mkv");

        Assert.That(result, Is.InstanceOf<BadRequestResult>());
        _torrentFileService.DidNotReceive().Update(Arg.Any<TorrentFile>());
    }

    [Test]
    public void RenameFile_With_Leading_Slash_In_NewPath_Sanitizes_Path()
    {
        const string hash = "abcdef1234567890abcdef1234567890abcdef12";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = hash,
            Name = "Test Torrent",
        };
        var file = new TorrentFile
        {
            Id = 10,
            TorrentId = 1,
            Path = "Season 1/Episode 01.mkv",
            Size = 5000,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file });

        var result = _controller.RenameFile(hash, "Season 1/Episode 01.mkv", "/Season 1/Episode 01 - Renamed.mkv");

        Assert.That(result, Is.InstanceOf<ContentResult>());
        var content = (ContentResult)result;
        Assert.That(content.Content, Is.EqualTo("Ok."));
        Assert.That(file.Path, Is.EqualTo("Season 1/Episode 01 - Renamed.mkv"));
        _torrentFileService.Received(1).Update(file);
    }

    [Test]
    public void RenameFile_Renames_File_On_Disk_If_It_Exists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "qbittorrent_rename_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var torrentDir = Path.Combine(tempDir, "MyTorrent");
            var seasonDir = Path.Combine(torrentDir, "Season 1");
            Directory.CreateDirectory(seasonDir);

            var oldFilePath = Path.Combine(seasonDir, "Episode 01.mkv");
            File.WriteAllText(oldFilePath, "sample video data");

            const string hash = "abcdef1234567890abcdef1234567890abcdef12";
            var torrent = new Torrent
            {
                Id = 1,
                InfoHash = hash,
                Name = "MyTorrent",
                SavePath = tempDir,
            };
            var file = new TorrentFile
            {
                Id = 10,
                TorrentId = 1,
                Path = "Season 1/Episode 01.mkv",
                Size = 5000,
            };

            _torrentService.GetAll().Returns(new List<Torrent> { torrent });
            _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file });

            var result = _controller.RenameFile(hash, "Season 1/Episode 01.mkv", "Season 1/Episode 01 - Pilot.mkv");

            Assert.That(result, Is.InstanceOf<ContentResult>());
            var content = (ContentResult)result;
            Assert.That(content.Content, Is.EqualTo("Ok."));
            Assert.That(file.Path, Is.EqualTo("Season 1/Episode 01 - Pilot.mkv"));

            var newFilePath = Path.Combine(seasonDir, "Episode 01 - Pilot.mkv");
            Assert.That(File.Exists(oldFilePath), Is.False, "Old file should not exist after rename");
            Assert.That(File.Exists(newFilePath), Is.True, "New file should exist after rename");
            Assert.That(File.ReadAllText(newFilePath), Is.EqualTo("sample video data"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public void RenameTorrent_With_Directory_Traversal_Returns_BadRequest()
    {
        const string hash = "abcdef1234567890abcdef1234567890abcdef12";
        var result = _controller.RenameTorrent(hash, "../MaliciousName");
        Assert.That(result, Is.InstanceOf<BadRequestResult>());
        _torrentService.DidNotReceive().Update(Arg.Any<Torrent>());
    }

    [Test]
    public void RenameFolder_Updates_Prefix_Of_Nested_Files()
    {
        const string hash = "abcdef1234567890abcdef1234567890abcdef12";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = hash,
            Name = "Test Torrent",
        };
        var file1 = new TorrentFile
        {
            Id = 11,
            TorrentId = 1,
            Path = "Season 1/Episode 01.mkv",
            Size = 5000,
        };
        var file2 = new TorrentFile
        {
            Id = 12,
            TorrentId = 1,
            Path = "Season 1/Subtitles/en.srt",
            Size = 100,
        };
        var file3 = new TorrentFile
        {
            Id = 13,
            TorrentId = 1,
            Path = "Season 2/Episode 01.mkv",
            Size = 5000,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file1, file2, file3 });

        var result = _controller.RenameFolder(hash, "Season 1", "Season 01");

        Assert.That(result, Is.InstanceOf<ContentResult>());
        var content = (ContentResult)result;
        Assert.That(content.Content, Is.EqualTo("Ok."));
        Assert.That(file1.Path, Is.EqualTo("Season 01/Episode 01.mkv"));
        Assert.That(file2.Path, Is.EqualTo("Season 01/Subtitles/en.srt"));
        Assert.That(file3.Path, Is.EqualTo("Season 2/Episode 01.mkv"));
        _torrentFileService.Received(1).Update(file1);
        _torrentFileService.Received(1).Update(file2);
        _torrentFileService.DidNotReceive().Update(file3);
        _torrentService.Received(1).Recheck(1);
    }

    [Test]
    public void CreateCategory_Persists_Category_And_SavePath()
    {
        _configService.WatchFolderPath.Returns("/downloads");
        _categoryService.GetAll().Returns(new List<Category>());

        var result = _controller.CreateCategory("music", "/downloads/music");

        Assert.That(result, Is.InstanceOf<ContentResult>());
        var content = (ContentResult)result;
        Assert.That(content.Content, Is.EqualTo("Ok."));

        var categoriesResult = _controller.GetCategories();
        Assert.That(categoriesResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)categoriesResult.Result;
        var dict = okResult.Value as Dictionary<string, object>;
        Assert.That(dict, Is.Not.Null);
        Assert.That(dict.ContainsKey("music"), Is.True);
    }

    [Test]
    public async Task AddTorrents_Routes_Destination_For_Music_Category()
    {
        _configService.WatchFolderPath.Returns("/downloads");
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Daft Punk - Discovery",
            InfoHash = "0123456789abcdef0123456789abcdef01234567"
        };
        _torrentImportService.ImportFromMagnet(Arg.Any<string>()).Returns(torrent);

        await _controller.AddTorrents(new QBitAddTorrentsRequest
        {
            Urls = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Daft+Punk+-+Discovery",
            Category = "music"
        });

        Assert.That(torrent.SourcePath, Is.EqualTo(Path.Combine("/downloads", "music")));
        _torrentService.Received().Update(Arg.Is<Torrent>(t => t.SourcePath == Path.Combine("/downloads", "music")));
    }

    [Test]
    public async Task AddTorrents_Routes_Destination_For_Books_Category()
    {
        _configService.WatchFolderPath.Returns("/downloads");
        var torrent = new Torrent
        {
            Id = 2,
            Name = "Dune",
            InfoHash = "1111111111111111111111111111111111111111"
        };
        _torrentImportService.ImportFromMagnet(Arg.Any<string>()).Returns(torrent);

        await _controller.AddTorrents(new QBitAddTorrentsRequest
        {
            Urls = "magnet:?xt=urn:btih:1111111111111111111111111111111111111111&dn=Dune",
            Category = "books"
        });

        Assert.That(torrent.SourcePath, Is.EqualTo(Path.Combine("/downloads", "books")));
        _torrentService.Received().Update(Arg.Is<Torrent>(t => t.SourcePath == Path.Combine("/downloads", "books")));
    }

    [Test]
    public async Task AddTorrents_Routes_Destination_For_Custom_Created_Category()
    {
        _controller.CreateCategory("audiobooks", "/media/audiobooks");

        var torrent = new Torrent
        {
            Id = 3,
            Name = "Audiobook Title",
            InfoHash = "2222222222222222222222222222222222222222"
        };
        _torrentImportService.ImportFromMagnet(Arg.Any<string>()).Returns(torrent);

        await _controller.AddTorrents(new QBitAddTorrentsRequest
        {
            Urls = "magnet:?xt=urn:btih:2222222222222222222222222222222222222222&dn=Audiobook",
            Category = "audiobooks"
        });

        Assert.That(torrent.SourcePath, Is.EqualTo("/media/audiobooks"));
        _torrentService.Received().Update(Arg.Is<Torrent>(t => t.SourcePath == "/media/audiobooks"));
    }

    [Test]
    public void SetFilePriority_Updates_Wanted_And_Priority_For_Single_File()
    {
        const string hash = "abcdef1234567890abcdef1234567890abcdef12";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = hash,
            Name = "Test Torrent",
        };
        var file = new TorrentFile
        {
            Id = 10,
            TorrentId = 1,
            Path = "test.mkv",
            Size = 5000,
            Wanted = false,
            Priority = 0,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file });

        var result = _controller.SetFilePriority(hash: hash, id: "0", priority: 1);

        Assert.That(result, Is.InstanceOf<ContentResult>());
        var content = (ContentResult)result;
        Assert.That(content.Content, Is.EqualTo("Ok."));
        Assert.That(file.Wanted, Is.True);
        Assert.That(file.Priority, Is.EqualTo(1));
        _torrentFileService.Received(1).Update(file);
    }

    [Test]
    public void SetFilePriority_Sets_Wanted_False_When_Priority_Is_Zero()
    {
        const string hash = "abcdef1234567890abcdef1234567890abcdef12";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = hash,
            Name = "Test Torrent",
        };
        var file = new TorrentFile
        {
            Id = 10,
            TorrentId = 1,
            Path = "test.mkv",
            Size = 5000,
            Wanted = true,
            Priority = 1,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file });

        var result = _controller.SetFilePriority(hash: hash, id: "0", priority: 0);

        Assert.That(result, Is.InstanceOf<ContentResult>());
        var content = (ContentResult)result;
        Assert.That(content.Content, Is.EqualTo("Ok."));
        Assert.That(file.Wanted, Is.False);
        Assert.That(file.Priority, Is.EqualTo(0));
        _torrentFileService.Received(1).Update(file);
    }

    [Test]
    public void SetFilePriority_Updates_Multiple_Pipe_And_Comma_Separated_Files()
    {
        const string hash = "abcdef1234567890abcdef1234567890abcdef12";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = hash,
            Name = "Test Torrent",
        };
        var file1 = new TorrentFile
        {
            Id = 10,
            TorrentId = 1,
            Path = "test1.mkv",
            Size = 5000,
            Wanted = true,
            Priority = 1,
        };
        var file2 = new TorrentFile
        {
            Id = 11,
            TorrentId = 1,
            Path = "test2.mkv",
            Size = 5000,
            Wanted = true,
            Priority = 1,
        };
        var file3 = new TorrentFile
        {
            Id = 12,
            TorrentId = 1,
            Path = "test3.mkv",
            Size = 5000,
            Wanted = true,
            Priority = 1,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file1, file2, file3 });

        var result = _controller.SetFilePriority(hash: hash, ids: "0|2", priority: 6);

        Assert.That(result, Is.InstanceOf<ContentResult>());
        Assert.That(file1.Priority, Is.EqualTo(6));
        Assert.That(file1.Wanted, Is.True);
        Assert.That(file3.Priority, Is.EqualTo(6));
        Assert.That(file3.Wanted, Is.True);
        Assert.That(file2.Priority, Is.EqualTo(1));
        _torrentFileService.Received(1).Update(file1);
        _torrentFileService.Received(1).Update(file3);
        _torrentFileService.DidNotReceive().Update(file2);
    }

    [Test]
    public void SetFilePriority_Accepts_Form_Parameters()
    {
        const string hash = "abcdef1234567890abcdef1234567890abcdef12";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = hash,
            Name = "Test Torrent",
        };
        var file = new TorrentFile
        {
            Id = 10,
            TorrentId = 1,
            Path = "test.mkv",
            Size = 5000,
            Wanted = true,
            Priority = 1,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file });

        var result = _controller.SetFilePriority(hashForm: hash, idsForm: "0", priorityForm: 7);

        Assert.That(result, Is.InstanceOf<ContentResult>());
        Assert.That(file.Wanted, Is.True);
        Assert.That(file.Priority, Is.EqualTo(7));
        _torrentFileService.Received(1).Update(file);
    }

    [Test]
    public void SetFilePriority_Returns_NotFound_When_Torrent_DoesNotExist()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _controller.SetFilePriority(hash: "nonexistent", id: "0", priority: 1);

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void SetFilePriority_Returns_BadRequest_When_Parameters_Missing()
    {
        var resultNoHash = _controller.SetFilePriority(hash: null, id: "0", priority: 1);
        Assert.That(resultNoHash, Is.InstanceOf<BadRequestResult>());

        var resultNoPrio = _controller.SetFilePriority(hash: "somehash", id: "0", priority: null);
        Assert.That(resultNoPrio, Is.InstanceOf<BadRequestResult>());

        var resultNoId = _controller.SetFilePriority(hash: "somehash", id: null, priority: 1);
        Assert.That(resultNoId, Is.InstanceOf<BadRequestResult>());
    }

    [Test]
    public void EditCategory_Persists_Changes_To_CategoryService()
    {
        var existing = new Category { Id = 5, Name = "movies", SavePath = "/old/movies" };
        _categoryService.GetByName("movies").Returns(existing);

        var result = _controller.EditCategory("movies", "/new/movies");

        Assert.That(result, Is.InstanceOf<ContentResult>());
        var content = (ContentResult)result;
        Assert.That(content.Content, Is.EqualTo("Ok."));
        _categoryService.Received(1).Update(Arg.Is<Category>(c => c.Id == 5 && c.Name == "movies" && c.SavePath == "/new/movies"));
    }

    [Test]
    public void RemoveCategories_Deletes_From_CategoryService()
    {
        var cat = new Category { Id = 7, Name = "anime" };
        _categoryService.GetByName("anime").Returns(cat);

        var result = _controller.RemoveCategories("anime");

        Assert.That(result, Is.InstanceOf<ContentResult>());
        var content = (ContentResult)result;
        Assert.That(content.Content, Is.EqualTo("Ok."));
        _categoryService.Received(1).Delete(7);
    }

    [Test]
    public void GetCategories_Returns_Categories_From_CategoryService()
    {
        var cat = new Category { Id = 1, Name = "series", SavePath = "/media/series" };
        _categoryService.GetAll().Returns(new List<Category> { cat });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _controller.GetCategories();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result.Result;
        var dict = ok.Value as Dictionary<string, object>;
        Assert.That(dict, Is.Not.Null);
        Assert.That(dict.ContainsKey("series"), Is.True);
    }

    [Test]
    public void DeleteTorrents_Preserves_Active_Seeding_Torrent_When_Goals_Unmet()
    {
        var torrent = new Torrent
        {
            Id = 99,
            InfoHash = "seedgoalunmethash",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            Ratio = 0.8,
            RatioLimit = 2.0
        };

        _configService.PreserveSeedingOnArrDelete.Returns(true);
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var result = _controller.DeleteTorrents("seedgoalunmethash", deleteFiles: true);

        Assert.That(result, Is.InstanceOf<ContentResult>());
        var content = (ContentResult)result;
        Assert.That(content.Content, Is.EqualTo("Ok."));
        _torrentService.DidNotReceive().Delete(99, Arg.Any<bool>());
    }

    [Test]
    public void DeleteTorrents_Deletes_Seeding_Torrent_When_Goals_Met()
    {
        var torrent = new Torrent
        {
            Id = 100,
            InfoHash = "seedgoalmethash",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            Ratio = 2.5,
            RatioLimit = 2.0
        };

        _configService.PreserveSeedingOnArrDelete.Returns(true);
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var result = _controller.DeleteTorrents("seedgoalmethash", deleteFiles: true);

        Assert.That(result, Is.InstanceOf<ContentResult>());
        _torrentService.Received(1).Delete(100, true);
    }

    [Test]
    public void DeleteTorrents_Deletes_When_PreserveSeeding_Disabled()
    {
        var torrent = new Torrent
        {
            Id = 101,
            InfoHash = "guarddisabledhash",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            Ratio = 0.5,
            RatioLimit = 2.0
        };

        _configService.PreserveSeedingOnArrDelete.Returns(false);
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var result = _controller.DeleteTorrents("guarddisabledhash", deleteFiles: true);

        Assert.That(result, Is.InstanceOf<ContentResult>());
        _torrentService.Received(1).Delete(101, true);
    }

    [Test]
    public void TorrentsInfo_Remaps_SavePath_And_ContentPath_Outbound()
    {
        var remotePathMappingService = Substitute.For<IRemotePathMappingService>();
        remotePathMappingService.RemapLocalToRemote("192.168.1.50", "/downloads/movies")
            .Returns(@"X:\Downloads\movies");
        remotePathMappingService.RemapLocalToRemote("192.168.1.50", "/downloads/movies/MyMovie")
            .Returns(@"X:\Downloads\movies\MyMovie");

        var controller = new QBittorrentApiController(
            _torrentService,
            _torrentFileService,
            _torrentFileParser,
            _torrentImportService,
            _trackerEntryService,
            _configService,
            _tagService,
            _configFileProvider,
            remotePathMappingService: remotePathMappingService,
            categoryService: _categoryService);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "aabbccddeeff00112233aabbccddeeff00112233",
            Name = "MyMovie",
            SourcePath = "/downloads/movies",
            Status = TorrentStatus.Seeding,
            TotalSize = 1000,
            Progress = 1.0,
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var result = controller.GetTorrentsInfo();
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var list = okResult.Value as List<Dictionary<string, object>>;
        Assert.That(list, Is.Not.Null);
        Assert.That(list[0]["save_path"], Is.EqualTo(@"X:\Downloads\movies"));
        Assert.That(list[0]["content_path"], Is.EqualTo(@"X:\Downloads\movies\MyMovie"));
    }

    [Test]
    public void SetLocation_Remaps_Inbound_Path_And_Updates_Both_SourcePath_And_SavePath()
    {
        var remotePathMappingService = Substitute.For<IRemotePathMappingService>();
        remotePathMappingService.RemapRemoteToLocal("192.168.1.50", @"X:\Downloads\movies")
            .Returns("/downloads/movies");

        var controller = new QBittorrentApiController(
            _torrentService,
            _torrentFileService,
            _torrentFileParser,
            _torrentImportService,
            _trackerEntryService,
            _configService,
            _tagService,
            _configFileProvider,
            remotePathMappingService: remotePathMappingService,
            categoryService: _categoryService);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "aabbccddeeff00112233aabbccddeeff00112233",
            Name = "MyMovie",
            SourcePath = "/downloads/old",
            SavePath = "/downloads/old",
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var actionResult = controller.SetLocation("aabbccddeeff00112233aabbccddeeff00112233", @"X:\Downloads\movies");
        Assert.That(actionResult, Is.InstanceOf<ContentResult>());
        Assert.That(torrent.SourcePath, Is.EqualTo("/downloads/movies"));
        Assert.That(torrent.SavePath, Is.EqualTo("/downloads/movies"));
        _torrentService.Received(1).Update(torrent);
    }
}
