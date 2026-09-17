using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using Seedarr.Api.V1.Transmission;

namespace NzbDrone.Core.Test.Http;

[TestFixture]
public class TransmissionRpcControllerTest
{
    private ITorrentService _torrentService;
    private ITorrentFileService _torrentFileService;
    private ITorrentFileParser _torrentFileParser;
    private ITorrentImportService _torrentImportService;
    private ITrackerEntryService _trackerEntryService;
    private IConfigService _configService;
    private IConfigFileProvider _configFileProvider;
    private ICategoryService _categoryService;
    private TransmissionRpcController _controller;

    [SetUp]
    public void SetUp()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _torrentFileParser = Substitute.For<ITorrentFileParser>();
        _torrentImportService = Substitute.For<ITorrentImportService>();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _configService = Substitute.For<IConfigService>();
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _categoryService = Substitute.For<ICategoryService>();

        _configFileProvider.AuthenticationEnabled.Returns(false);

        _controller = new TransmissionRpcController(
            _torrentService,
            _torrentFileService,
            _torrentFileParser,
            _torrentImportService,
            _trackerEntryService,
            _configService,
            _configFileProvider,
            categoryService: _categoryService);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Transmission-Session-Id"] = "test-session-id";
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };
    }

    [Test]
    public void HandleRpc_Missing_Session_Header_Returns_409_Conflict_With_Header()
    {
        _controller.ControllerContext.HttpContext.Request.Headers.Remove("X-Transmission-Session-Id");

        var request = new TransmissionRpcRequest { Method = "session-get" };
        var result = _controller.HandleRpc(request).GetAwaiter().GetResult();

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        var objResult = (ObjectResult)result;
        Assert.That(objResult.StatusCode, Is.EqualTo(409));
        Assert.That(_controller.Response.Headers.ContainsKey("X-Transmission-Session-Id"), Is.True);
    }

    [Test]
    public async Task HandleRpc_SessionGet_Returns_Session_Information()
    {
        var request = new TransmissionRpcRequest { Method = "session-get", Tag = JsonDocument.Parse("100").RootElement };
        var result = await _controller.HandleRpc(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var response = ok.Value as TransmissionRpcResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Result, Is.EqualTo("success"));

        var dict = response.Arguments as Dictionary<string, object>;
        Assert.That(dict, Is.Not.Null);
        Assert.That(dict["version"], Is.EqualTo("3.00 (Seedarr)"));
        Assert.That(dict["rpc-version"], Is.EqualTo(17));
    }

    [Test]
    public async Task HandleRpc_TorrentGet_Returns_Mapped_Torrents()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Transmission Test",
            InfoHash = "1122334455667788990011223344556677889900",
            TotalSize = 1000000,
            Progress = 0.5,
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 100000,
            UploadSpeed = 50000,
            DateAdded = DateTime.UtcNow,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _trackerEntryService.GetByTorrentId(1).Returns(new List<TrackerEntry>());
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile>());

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-get",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["fields"] = JsonDocument.Parse("[\"id\", \"name\", \"hashString\", \"percentDone\", \"status\"]").RootElement,
            },
            Tag = JsonDocument.Parse("5").RootElement,
        };

        var result = await _controller.HandleRpc(request);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var response = ok.Value as TransmissionRpcResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Result, Is.EqualTo("success"));

        var args = response.Arguments as Dictionary<string, object>;
        Assert.That(args, Is.Not.Null);
        var torrentList = args["torrents"] as List<Dictionary<string, object>>;
        Assert.That(torrentList, Is.Not.Null);
        Assert.That(torrentList.Count, Is.EqualTo(1));
        Assert.That(torrentList[0]["id"], Is.EqualTo(1));
        Assert.That(torrentList[0]["hashString"], Is.EqualTo("1122334455667788990011223344556677889900"));
        Assert.That(torrentList[0]["status"], Is.EqualTo(4));
    }

    [Test]
    public async Task HandleRpc_TorrentAdd_With_Magnet_Returns_Torrent_Added()
    {
        var magnet = "magnet:?xt=urn:btih:1122334455667788990011223344556677889900&dn=TrMagnet";
        var created = new Torrent
        {
            Id = 3,
            Name = "TrMagnet",
            InfoHash = "1122334455667788990011223344556677889900",
        };

        _torrentImportService.ImportFromMagnet(magnet).Returns(created);

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-add",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["filename"] = JsonDocument.Parse($"\"{magnet}\"").RootElement,
                ["paused"] = JsonDocument.Parse("true").RootElement,
            },
        };

        var result = await _controller.HandleRpc(request);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var response = ok.Value as TransmissionRpcResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Result, Is.EqualTo("success"));

        var args = response.Arguments as Dictionary<string, object>;
        Assert.That(args, Is.Not.Null);
        var addedObj = args["torrent-added"] as Dictionary<string, object>;
        Assert.That(addedObj, Is.Not.Null);
        Assert.That(addedObj["id"], Is.EqualTo(3));
        Assert.That(addedObj["hashString"], Is.EqualTo("1122334455667788990011223344556677889900"));

        _torrentImportService.Received(1).ImportFromMagnet(magnet);
        _torrentService.Received().Update(Arg.Is<Torrent>(t => t.Status == TorrentStatus.Paused));
    }

    [Test]
    public async Task HandleRpc_TorrentSet_WithLabels_Sets_Both_TagIds_And_Label()
    {
        var tagService = Substitute.For<ITagService>();
        var controller = new TransmissionRpcController(
            _torrentService,
            _torrentFileService,
            _torrentFileParser,
            _torrentImportService,
            _trackerEntryService,
            _configService,
            _configFileProvider,
            tagService);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Transmission-Session-Id"] = "test-session-id";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent
        {
            Id = 5,
            InfoHash = "abcde",
        };
        _torrentService.Get(5).Returns(torrent);
        tagService.SyncTagsFromLabels(Arg.Any<IEnumerable<string>>()).Returns(new List<int> { 10, 20 });

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-set",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["ids"] = JsonDocument.Parse("[5]").RootElement,
                ["labels"] = JsonDocument.Parse("[\"alpha\", \"beta\"]").RootElement,
            },
        };

        var result = await controller.HandleRpc(request);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(torrent.Label, Is.EqualTo("alpha,beta"));
        Assert.That(torrent.TagIds, Is.EqualTo(new List<int> { 10, 20 }));
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public async Task HandleRpc_TorrentSet_WithEmptyLabels_Clears_Label_And_TagIds()
    {
        var tagService = Substitute.For<ITagService>();
        var controller = new TransmissionRpcController(
            _torrentService,
            _torrentFileService,
            _torrentFileParser,
            _torrentImportService,
            _trackerEntryService,
            _configService,
            _configFileProvider,
            tagService);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Transmission-Session-Id"] = "test-session-id";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent
        {
            Id = 7,
            InfoHash = "fedcba",
            Label = "existingLabel",
            TagIds = new List<int> { 42 },
        };
        _torrentService.Get(7).Returns(torrent);

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-set",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["ids"] = JsonDocument.Parse("[7]").RootElement,
                ["labels"] = JsonDocument.Parse("[]").RootElement,
            },
        };

        var result = await controller.HandleRpc(request);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(torrent.Label, Is.EqualTo(string.Empty));
        Assert.That(torrent.TagIds, Is.Empty);
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public async Task HandleRpc_TorrentGet_With_Standard_Fields_Does_Not_Invoke_File_Or_Tracker_Services()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Transmission Standard Poll",
            InfoHash = "1122334455667788990011223344556677889900",
            TotalSize = 1000000,
            Progress = 0.5,
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 100000,
            UploadSpeed = 50000,
            DateAdded = DateTime.UtcNow,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-get",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["fields"] = JsonDocument.Parse("[\"id\", \"name\", \"status\", \"percentDone\", \"rateDownload\", \"rateUpload\", \"eta\"]").RootElement,
            },
        };

        var result = await _controller.HandleRpc(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _torrentFileService.DidNotReceive().GetByTorrentId(Arg.Any<int>());
        _trackerEntryService.DidNotReceive().GetByTorrentId(Arg.Any<int>());
    }

    [Test]
    public async Task HandleRpc_TorrentGet_With_Files_Or_Trackers_Invokes_File_Or_Tracker_Services()
    {
        var torrent = new Torrent
        {
            Id = 2,
            Name = "Transmission Files/Trackers Poll",
            InfoHash = "2233445566778899001122334455667788990011",
            TotalSize = 2000000,
            Progress = 0.8,
            Status = TorrentStatus.Downloading,
            DateAdded = DateTime.UtcNow,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _torrentFileService.GetByTorrentId(2).Returns(new List<TorrentFile>
        {
            new TorrentFile { Path = "test.mkv", Size = 2000000 },
        });
        _trackerEntryService.GetByTorrentId(2).Returns(new List<TrackerEntry>
        {
            new TrackerEntry { Id = 1, Url = "http://tracker.example.com/announce", Tier = 0 },
        });

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-get",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["fields"] = JsonDocument.Parse("[\"id\", \"files\", \"trackers\"]").RootElement,
            },
        };

        var result = await _controller.HandleRpc(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _torrentFileService.Received(1).GetByTorrentId(2);
        _trackerEntryService.Received(1).GetByTorrentId(2);
    }

    [Test]
    public async Task HandleRpc_TorrentAdd_With_Valid_Local_Filesystem_Path_Accepts_Path()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempFile, new byte[] { 0x64, 0x31, 0x3a, 0x61 });

            var created = new Torrent
            {
                Id = 10,
                Name = "LocalTorrent",
                InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            };

            _torrentImportService.ImportFromFile(Arg.Any<Stream>(), Arg.Any<string>()).Returns(created);

            var request = new TransmissionRpcRequest
            {
                Method = "torrent-add",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["filename"] = JsonDocument.Parse(JsonSerializer.Serialize(tempFile)).RootElement,
                },
            };

            var result = await _controller.HandleRpc(request);

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            var ok = (OkObjectResult)result;
            var response = ok.Value as TransmissionRpcResponse;
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Result, Is.EqualTo("success"));

            var args = response.Arguments as Dictionary<string, object>;
            Assert.That(args, Is.Not.Null);
            var addedObj = args["torrent-added"] as Dictionary<string, object>;
            Assert.That(addedObj, Is.Not.Null);
            Assert.That(addedObj["id"], Is.EqualTo(10));

            _torrentImportService.Received(1).ImportFromFile(Arg.Any<Stream>(), Path.GetFileName(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public void ExtractIds_Resolves_Hashes_Efficiently_Without_Repeated_Queries()
    {
        var torrent1 = new Torrent { Id = 1, InfoHash = "hash1" };
        var torrent2 = new Torrent { Id = 2, InfoHash = "hash2" };
        var torrent3 = new Torrent { Id = 3, InfoHash = "hash3" };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2, torrent3 });

        var arguments = new Dictionary<string, JsonElement>
        {
            ["ids"] = JsonDocument.Parse("[\"hash1\", \"hash2\", \"hash3\"]").RootElement,
        };

        var ids = _controller.ExtractIds(arguments);

        Assert.That(ids, Is.EqualTo(new List<int> { 1, 2, 3 }));
        _torrentService.Received(1).GetAll();
    }

    [Test]
    public async Task HandleRpc_TorrentGet_With_RecentlyActive_Includes_Recently_Changed_Torrents()
    {
        var completedRecently = new Torrent
        {
            Id = 1,
            Name = "Completed Recently",
            Status = TorrentStatus.Stopped,
            LastActive = DateTime.UtcNow.AddMinutes(-2),
            Progress = 1.0,
        };
        var stoppedLongAgo = new Torrent
        {
            Id = 2,
            Name = "Stopped Long Ago",
            Status = TorrentStatus.Stopped,
            LastActive = DateTime.UtcNow.AddMinutes(-30),
            Progress = 1.0,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { completedRecently, stoppedLongAgo });

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-get",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["ids"] = JsonDocument.Parse("\"recently-active\"").RootElement,
                ["fields"] = JsonDocument.Parse("[\"id\", \"name\"]").RootElement,
            },
        };

        var result = await _controller.HandleRpc(request);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var response = ok.Value as TransmissionRpcResponse;
        Assert.That(response, Is.Not.Null);

        var args = response.Arguments as Dictionary<string, object>;
        var torrents = args["torrents"] as List<Dictionary<string, object>>;
        Assert.That(torrents, Is.Not.Null);
        Assert.That(torrents.Count, Is.EqualTo(1));
        Assert.That(torrents[0]["id"], Is.EqualTo(1));
    }

    [Test]
    public async Task HandleRpc_TorrentRemove_Without_Ids_Does_Not_Delete_Any_Torrents()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "T1" },
            new Torrent { Id = 2, Name = "T2" },
        };
        _torrentService.GetAll().Returns(torrents);

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-remove",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["delete-local-data"] = JsonDocument.Parse("true").RootElement,
            },
        };

        var result = await _controller.HandleRpc(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _torrentService.DidNotReceive().Delete(Arg.Any<int>(), Arg.Any<bool>());
    }

    [Test]
    public async Task HandleRpc_TorrentStop_And_Start_Without_Ids_Does_Not_Affect_Any_Torrents()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "T1", Status = TorrentStatus.Downloading },
            new Torrent { Id = 2, Name = "T2", Status = TorrentStatus.Seeding },
        };
        _torrentService.GetAll().Returns(torrents);

        var stopRequest = new TransmissionRpcRequest
        {
            Method = "torrent-stop",
            Arguments = new Dictionary<string, JsonElement>(),
        };

        var stopResult = await _controller.HandleRpc(stopRequest);
        Assert.That(stopResult, Is.InstanceOf<OkObjectResult>());
        _torrentService.DidNotReceive().Get(Arg.Any<int>());
        _torrentService.DidNotReceive().Update(Arg.Any<Torrent>());

        var startRequest = new TransmissionRpcRequest
        {
            Method = "torrent-start",
            Arguments = new Dictionary<string, JsonElement>(),
        };

        var startResult = await _controller.HandleRpc(startRequest);
        Assert.That(startResult, Is.InstanceOf<OkObjectResult>());
        _torrentService.DidNotReceive().Get(Arg.Any<int>());
        _torrentService.DidNotReceive().Update(Arg.Any<Torrent>());
    }

    [Test]
    public async Task TorrentDeletedEvent_Records_Removed_Id_So_RecentlyActive_Poll_Reports_It()
    {
        TransmissionRpcController.ClearRecentlyRemovedIds();

        _torrentService.GetAll().Returns(new List<Torrent>());

        _controller.Handle(new TorrentDeletedEvent(42));

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-get",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["ids"] = JsonDocument.Parse("\"recently-active\"").RootElement,
                ["fields"] = JsonDocument.Parse("[\"id\", \"name\"]").RootElement,
            },
        };

        var result = await _controller.HandleRpc(request);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var response = ok.Value as TransmissionRpcResponse;
        Assert.That(response, Is.Not.Null);

        var args = response.Arguments as Dictionary<string, object>;
        Assert.That(args, Is.Not.Null);
        var removed = args["removed"] as List<int>;
        Assert.That(removed, Is.Not.Null);
        Assert.That(removed, Does.Contain(42));
    }

    [Test]
    public void TransmissionRpcTorrentDeletedEventHandler_Records_Removed_Id()
    {
        TransmissionRpcController.ClearRecentlyRemovedIds();

        var handler = new TransmissionRpcTorrentDeletedEventHandler();
        handler.Handle(new TorrentDeletedEvent(99));

        var removed = TransmissionRpcController.GetRecentlyRemovedIds();
        Assert.That(removed, Does.Contain(99));
    }

    [Test]
    public async Task HandleRpc_TorrentAdd_With_Labels_Resolves_Category_SavePath_When_DownloadDir_Omitted()
    {
        var category = new Category { Id = 1, Name = "tv-sonarr", SavePath = "/data/media/tv" };
        _categoryService.GetByName("tv-sonarr").Returns(category);
        _categoryService.GetSavePathForCategory("tv-sonarr", Arg.Any<string>()).Returns("/data/media/tv");

        var imported = new Torrent
        {
            Id = 15,
            Name = "ImportedTest",
            InfoHash = "aabbcc11223344556677889900aabbcc11223344",
        };
        _torrentImportService.ImportFromMagnet(Arg.Any<string>()).Returns(imported);

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-add",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["filename"] = JsonDocument.Parse("\"magnet:?xt=urn:btih:aabbcc11223344556677889900aabbcc11223344&dn=ImportedTest\"").RootElement,
                ["labels"] = JsonDocument.Parse("[\"tv-sonarr\"]").RootElement,
            },
        };

        var result = await _controller.HandleRpc(request);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());

        _torrentService.Received(1).Update(Arg.Is<Torrent>(t => t.Category == "tv-sonarr" && t.SourcePath == "/data/media/tv"));
    }
}
