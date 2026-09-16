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
using NzbDrone.Core.Exceptions;
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
        _torrentService.Received().Update(Arg.Is<Torrent>(t => t.Category == "movies" && t.Status == TorrentStatus.Paused));
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

    [Test]
    public void AddTags_sets_both_TagIds_and_Label()
    {
        var torrent = new Torrent
        {
            Id = 10,
            InfoHash = "testhash",
            Label = "Existing",
            TagIds = new List<int> { 1 }
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _tagService.SyncTagsFromLabels(Arg.Any<IEnumerable<string>>())
            .Returns(new List<int> { 1, 2 });

        var result = _controller.AddTags("testhash", "NewTag");

        Assert.That(result, Is.InstanceOf<ContentResult>());
        Assert.That(torrent.Label, Is.EqualTo("Existing, NewTag"));
        Assert.That(torrent.TagIds, Is.EqualTo(new List<int> { 1, 2 }));
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public void GetTags_Returns_Tokens_From_Torrents_And_TagService()
    {
        var torrent1 = new Torrent { Id = 1, Label = "tag1, tag2" };
        var torrent2 = new Torrent { Id = 2, Label = "tag2; tag3" };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        _tagService.GetAll().Returns(new List<Tag>
        {
            new Tag { Id = 1, Label = "tag4" },
            new Tag { Id = 2, Label = "tag1" }
        });

        var result = _controller.GetTags();
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result.Result;
        var tags = ok.Value as List<string>;

        Assert.That(tags, Is.Not.Null);
        Assert.That(tags, Does.Contain("tag1"));
        Assert.That(tags, Does.Contain("tag2"));
        Assert.That(tags, Does.Contain("tag3"));
        Assert.That(tags, Does.Contain("tag4"));
        Assert.That(tags.Count, Is.EqualTo(4));
    }

    [Test]
    public void DeleteTags_Deletes_From_TagService_And_Torrents()
    {
        var tag1 = new Tag { Id = 10, Label = "tag1" };
        var tag2 = new Tag { Id = 20, Label = "tag2" };
        _tagService.GetAll().Returns(new List<Tag> { tag1, tag2 });

        var torrent = new Torrent
        {
            Id = 1,
            Label = "tag1, tag2",
            TagIds = new List<int> { 10, 20 }
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _tagService.SyncTagsFromLabels(Arg.Any<IEnumerable<string>>()).Returns(new List<int> { 20 });

        var result = _controller.DeleteTags("tag1");

        Assert.That(result, Is.InstanceOf<ContentResult>());
        _tagService.Received(1).Delete(10);
        _tagService.DidNotReceive().Delete(20);
        Assert.That(torrent.Label, Is.EqualTo("tag2"));
        Assert.That(torrent.TagIds, Is.EqualTo(new List<int> { 20 }));
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public async Task AddTorrents_With_Both_Category_And_Tags_Preserves_Category_And_Label_Distinctly()
    {
        var magnet = "magnet:?xt=urn:btih:4a5e1234567890abcdef1234567890abcdef1234&dn=Test";
        var request = new QBitAddTorrentsRequest
        {
            Urls = magnet,
            Category = "SciFi",
            Tags = "tag1, tag2",
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

        _torrentService.Received().Update(Arg.Is<Torrent>(t =>
            t.Category == "SciFi" &&
            t.Label == "tag1, tag2"));
    }

    [Test]
    public void QBitTorrentSnapshot_Maps_Category_And_Tags_Accurately()
    {
        var torrent = new Torrent
        {
            Name = "Snapshot Test",
            Category = "Movies",
            Label = "Action, 1080p",
            TotalSize = 1000,
            Downloaded = 500,
            Progress = 0.5,
            RatioLimit = 1.5,
            SeedingTimeLimit = 120,
        };

        var snapshot = QBitTorrentSnapshot.FromTorrent(torrent, "/downloads", "/downloads/Snapshot Test");

        Assert.That(snapshot.Category, Is.EqualTo("Movies"));
        Assert.That(snapshot.Tags, Is.EqualTo("Action, 1080p"));
        Assert.That(snapshot.RatioLimit, Is.EqualTo(1.5));
        Assert.That(snapshot.SeedingTimeLimit, Is.EqualTo(120));
    }

    [Test]
    public void SetShareLimits_Updates_Ratio_And_Seeding_Time_Limits_On_Torrents()
    {
        var torrent = new Torrent
        {
            Id = 10,
            InfoHash = "testhash",
            RatioLimit = null,
            SeedingTimeLimit = null,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var result = _controller.SetShareLimits("testhash", ratioLimit: 2.5, seedingTimeLimit: 180);

        Assert.That(result, Is.InstanceOf<ContentResult>());
        Assert.That(torrent.RatioLimit, Is.EqualTo(2.5));
        Assert.That(torrent.SeedingTimeLimit, Is.EqualTo(180));
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public async Task AddTorrents_ReAdding_Existing_Torrent_Applies_And_Persists_Updated_Options()
    {
        var magnet = "magnet:?xt=urn:btih:4a5e1234567890abcdef1234567890abcdef1234&dn=Existing";
        var request = new QBitAddTorrentsRequest
        {
            Urls = magnet,
            Category = "Documentaries",
            Tags = "HD, Nature",
            RatioLimit = 3.0,
            SeedingTimeLimit = 240,
        };

        var existingTorrent = new Torrent
        {
            Id = 5,
            Name = "Existing",
            InfoHash = "4a5e1234567890abcdef1234567890abcdef1234",
            Category = "OldCategory",
            Label = "OldLabel",
            RatioLimit = 1.0,
            SeedingTimeLimit = 60,
        };

        _torrentImportService.ImportFromMagnet(magnet).Returns(existingTorrent);

        var result = await _controller.AddTorrents(request);

        Assert.That(result, Is.InstanceOf<ContentResult>());
        Assert.That(existingTorrent.Category, Is.EqualTo("Documentaries"));
        Assert.That(existingTorrent.Label, Is.EqualTo("HD, Nature"));
        Assert.That(existingTorrent.RatioLimit, Is.EqualTo(3.0));
        Assert.That(existingTorrent.SeedingTimeLimit, Is.EqualTo(240));
        _torrentService.Received().Update(existingTorrent);
    }

    [Test]
    public async Task AddTorrents_When_Import_Throws_Duplicate_Applies_And_Persists_Options_On_Existing_Torrent()
    {
        var hash = "4a5e1234567890abcdef1234567890abcdef1234";
        var magnet = $"magnet:?xt=urn:btih:{hash}&dn=Existing";
        var request = new QBitAddTorrentsRequest
        {
            Urls = magnet,
            Category = "NewCategory",
            Tags = "NewTags",
            RatioLimit = 2.0,
        };

        var existingTorrent = new Torrent
        {
            Id = 7,
            Name = "Existing",
            InfoHash = hash,
            Category = "OldCategory",
            Label = "OldLabel",
        };

        _torrentImportService.ImportFromMagnet(magnet)
            .Returns(_ => throw new DuplicateTorrentException(hash));
        _torrentService.GetByInfoHash(hash).Returns(existingTorrent);

        var result = await _controller.AddTorrents(request);

        Assert.That(result, Is.InstanceOf<ContentResult>());
        Assert.That(existingTorrent.Category, Is.EqualTo("NewCategory"));
        Assert.That(existingTorrent.Label, Is.EqualTo("NewTags"));
        Assert.That(existingTorrent.RatioLimit, Is.EqualTo(2.0));
        _torrentService.Received().Update(existingTorrent);
    }
}
