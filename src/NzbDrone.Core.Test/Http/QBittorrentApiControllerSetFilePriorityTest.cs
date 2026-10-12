using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using Seedarr.Api.V1.QBittorrent;
using Seedarr.Http.Security;

namespace NzbDrone.Core.Test.Http;

[TestFixture]
public class QBittorrentApiControllerSetFilePriorityTest
{
    private ITorrentService _torrentService;
    private ITorrentFileService _torrentFileService;
    private QBittorrentApiController _controller;

    [SetUp]
    public void SetUp()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        var torrentFileParser = Substitute.For<ITorrentFileParser>();
        var torrentImportService = Substitute.For<ITorrentImportService>();
        var trackerEntryService = Substitute.For<ITrackerEntryService>();
        var configService = Substitute.For<IConfigService>();
        var tagService = Substitute.For<ITagService>();
        var configFileProvider = Substitute.For<IConfigFileProvider>();
        var categoryService = Substitute.For<ICategoryService>();
        configFileProvider.AuthenticationEnabled.Returns(false);

        _controller = new QBittorrentApiController(
            _torrentService,
            _torrentFileService,
            torrentFileParser,
            torrentImportService,
            trackerEntryService,
            configService,
            tagService,
            configFileProvider,
            sessionStore: new RpcSessionStore(),
            categoryService: categoryService);
    }

    [Test]
    public void SetFilePriority_Sets_Wanted_False_When_Priority_Is_Zero()
    {
        const string hash = "abcdef1234567890abcdef1234567890abcdef12";
        var torrent = new Torrent { Id = 1, InfoHash = hash, Name = "Test Torrent" };
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
        Assert.That(file.Wanted, Is.False);
        Assert.That(file.Priority, Is.EqualTo(0));
        _torrentFileService.Received(1).Update(file);
    }

    [Test]
    public void SetFilePriority_Updates_Multiple_Pipe_And_Comma_Separated_Files()
    {
        const string hash = "abcdef1234567890abcdef1234567890abcdef12";
        var torrent = new Torrent { Id = 1, InfoHash = hash, Name = "Test Torrent" };
        var file1 = new TorrentFile { Id = 10, TorrentId = 1, Path = "test1.mkv", Size = 5000, Wanted = true, Priority = 1 };
        var file2 = new TorrentFile { Id = 11, TorrentId = 1, Path = "test2.mkv", Size = 5000, Wanted = true, Priority = 1 };
        var file3 = new TorrentFile { Id = 12, TorrentId = 1, Path = "test3.mkv", Size = 5000, Wanted = true, Priority = 1 };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file1, file2, file3 });

        var result = _controller.SetFilePriority(hash: hash, ids: "0|2", priority: 6);

        Assert.That(result, Is.InstanceOf<ContentResult>());
        Assert.That(file1.Priority, Is.EqualTo(6));
        Assert.That(file3.Priority, Is.EqualTo(6));
        _torrentFileService.Received(1).Update(file1);
        _torrentFileService.Received(1).Update(file3);
        _torrentFileService.DidNotReceive().Update(file2);
    }

    [Test]
    public void SetFilePriority_Accepts_Form_Parameters()
    {
        const string hash = "abcdef1234567890abcdef1234567890abcdef12";
        var torrent = new Torrent { Id = 1, InfoHash = hash, Name = "Test Torrent" };
        var file = new TorrentFile { Id = 10, TorrentId = 1, Path = "test.mkv", Size = 5000, Wanted = true, Priority = 1 };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file });

        var result = _controller.SetFilePriority(hashForm: hash, idsForm: "0", priorityForm: 7);

        Assert.That(result, Is.InstanceOf<ContentResult>());
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
}
