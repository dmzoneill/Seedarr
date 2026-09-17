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
}
