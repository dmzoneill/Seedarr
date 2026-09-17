using System;
using System.Collections.Generic;
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
}
