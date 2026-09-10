using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using Seedarr.Api.V1.QBittorrent;

namespace NzbDrone.Core.Test.Http;

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

        _configFileProvider.AuthenticationEnabled.Returns(false);

        _controller = new QBittorrentApiController(
            _torrentService,
            _torrentFileService,
            _torrentFileParser,
            _torrentImportService,
            _trackerEntryService,
            _configService,
            _tagService,
            _configFileProvider);
    }

    [Test]
    public void GetVersion_Returns_qBittorrent_Version()
    {
        var result = _controller.GetVersion();
        Assert.That(result.Result, Is.InstanceOf<ContentResult>());
        var content = (ContentResult)result.Result;
        Assert.That(content.Content, Is.EqualTo("v4.4.2"));
    }

    [Test]
    public void GetWebApiVersion_Returns_Version_2_8_3()
    {
        var result = _controller.GetWebApiVersion();
        Assert.That(result.Result, Is.InstanceOf<ContentResult>());
        var content = (ContentResult)result.Result;
        Assert.That(content.Content, Is.EqualTo("2.8.3"));
    }

    [Test]
    public void GetPreferences_Returns_Valid_Dictionary()
    {
        _configService.ListeningPort.Returns(6881);
        _configService.MaxDownloadSpeedKbps.Returns(1250);
        _configService.MaxUploadSpeedKbps.Returns(625);

        var result = _controller.GetPreferences();
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result.Result;
        var dict = ok.Value as Dictionary<string, object>;
        Assert.That(dict, Is.Not.Null);
        Assert.That(dict["listen_port"], Is.EqualTo(6881));
        Assert.That(dict["dl_limit"], Is.EqualTo(1250 * 1024));
        Assert.That(dict["up_limit"], Is.EqualTo(625 * 1024));
    }

    [Test]
    public void GetTorrentsInfo_Returns_Mapped_Torrents()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Test Torrent",
            InfoHash = "4a5e1234567890abcdef1234567890abcdef1234",
            TotalSize = 1048576,
            Progress = 0.5,
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 1024,
            UploadSpeed = 512,
            Seeders = 10,
            Leechers = 5,
            DateAdded = DateTime.UtcNow,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var result = _controller.GetTorrentsInfo();
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result.Result;
        var list = ok.Value as List<Dictionary<string, object>>;
        Assert.That(list, Is.Not.Null);
        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0]["hash"], Is.EqualTo("4a5e1234567890abcdef1234567890abcdef1234"));
        Assert.That(list[0]["name"], Is.EqualTo("Test Torrent"));
        Assert.That(list[0]["state"], Is.EqualTo("downloading"));
    }

    [Test]
    public async Task AddTorrents_With_Magnet_Calls_ImportService()
    {
        var magnet = "magnet:?xt=urn:btih:4a5e1234567890abcdef1234567890abcdef1234&dn=Test";
        var request = new QBitAddTorrentsRequest
        {
            Urls = magnet,
            Category = "movies",
            Paused = "true",
        };

        var createdTorrent = new Torrent
        {
            Id = 1,
            Name = "Test",
            InfoHash = "4a5e1234567890abcdef1234567890abcdef1234",
            Status = TorrentStatus.Downloading,
        };

        _torrentImportService.ImportFromMagnet(magnet).Returns(createdTorrent);

        var result = await _controller.AddTorrents(request);
        Assert.That(result, Is.InstanceOf<ContentResult>());
        var content = (ContentResult)result;
        Assert.That(content.Content, Is.EqualTo("Ok."));

        _torrentImportService.Received(1).ImportFromMagnet(magnet);
        _torrentService.Received().Update(Arg.Is<Torrent>(t => t.Label == "movies" && t.Status == TorrentStatus.Paused));
    }

    [Test]
    public void Pause_And_Resume_Torrents_Updates_Status()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "abc",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var pauseResult = _controller.PauseTorrents("abc");
        Assert.That(pauseResult, Is.InstanceOf<ContentResult>());
        _torrentService.Received().Update(Arg.Is<Torrent>(t => t.Status == TorrentStatus.Paused));

        var resumeResult = _controller.ResumeTorrents("abc");
        Assert.That(resumeResult, Is.InstanceOf<ContentResult>());
        _torrentService.Received().Update(Arg.Is<Torrent>(t => t.Status == TorrentStatus.Downloading));
    }

    [Test]
    public void DeleteTorrents_Calls_Delete_On_TorrentService()
    {
        var torrent = new Torrent
        {
            Id = 42,
            InfoHash = "deletehash",
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var result = _controller.DeleteTorrents("deletehash", deleteFiles: true);
        Assert.That(result, Is.InstanceOf<ContentResult>());
        _torrentService.Received(1).Delete(42, true);
    }

    [Test]
    public void GetMainData_Returns_Full_Update_When_Rid_Is_0()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Sync Torrent",
            InfoHash = "synchash",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            TotalSize = 2048,
            Downloaded = 2048,
            Uploaded = 4096,
            Ratio = 2.0,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var result = _controller.GetMainData(0);
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result.Result;
        var dict = ok.Value as Dictionary<string, object>;
        Assert.That(dict, Is.Not.Null);
        Assert.That(dict["full_update"], Is.EqualTo(true));
        Assert.That(dict.ContainsKey("torrents"), Is.True);
        Assert.That(dict.ContainsKey("server_state"), Is.True);
    }
}
