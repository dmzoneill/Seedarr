using System.Net;
using NzbDrone.Core.RemotePathMappings;
using System;
using System.Collections.Generic;
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

namespace Seedarr.Api.V1.Test.Transmission;

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
        httpContext.Request.Headers[TransmissionRpcController.SessionHeaderName] = TransmissionRpcController.CurrentSessionId;
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };
    }

    [Test]
    public async Task HandleRpc_TorrentRenamePath_Updates_TorrentFile_Path_Not_Torrent_Name_And_Returns_Arguments()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Original Torrent Name",
            InfoHash = "1122334455667788990011223344556677889900",
        };
        var file = new TorrentFile
        {
            Id = 10,
            TorrentId = 1,
            Path = "Season 01/Episode 01.mkv",
            Size = 1000,
        };

        _torrentService.Get(1).Returns(torrent);
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file });

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-rename-path",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["ids"] = JsonDocument.Parse("[1]").RootElement,
                ["path"] = JsonDocument.Parse("\"Season 01/Episode 01.mkv\"").RootElement,
                ["name"] = JsonDocument.Parse("\"Episode 01 - Pilot.mkv\"").RootElement,
            },
            Tag = JsonDocument.Parse("42").RootElement,
        };

        var result = await _controller.HandleRpc(request);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var response = ok.Value as TransmissionRpcResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Result, Is.EqualTo("success"));

        var args = response.Arguments as Dictionary<string, object>;
        Assert.That(args, Is.Not.Null);
        Assert.That(args["path"], Is.EqualTo("Season 01/Episode 01.mkv"));
        Assert.That(args["name"], Is.EqualTo("Episode 01 - Pilot.mkv"));
        Assert.That(args["id"], Is.EqualTo(1));

        Assert.That(file.Path, Is.EqualTo("Season 01/Episode 01 - Pilot.mkv"));
        Assert.That(torrent.Name, Is.EqualTo("Original Torrent Name"));
        _torrentFileService.Received(1).Update(file);
        _torrentService.DidNotReceive().Update(torrent);
        _torrentService.Received(1).Recheck(1);
    }

    [Test]
    public async Task HandleRpc_TorrentRenamePath_Rejects_Directory_Traversal()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Original Torrent Name",
            InfoHash = "aabbccddeeff00112233aabbccddeeff00112233",
        };
        var file = new TorrentFile
        {
            Id = 10,
            TorrentId = 1,
            Path = "Season 01/Episode 01.mkv",
            Size = 1000,
        };

        _torrentService.Get(1).Returns(torrent);
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file });

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-rename-path",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["ids"] = JsonDocument.Parse("[1]").RootElement,
                ["path"] = JsonDocument.Parse("\"Season 01/Episode 01.mkv\"").RootElement,
                ["name"] = JsonDocument.Parse("\"../../evil.mkv\"").RootElement,
            },
            Tag = JsonDocument.Parse("42").RootElement,
        };

        var result = await _controller.HandleRpc(request);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var response = ok.Value as TransmissionRpcResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Result, Is.Not.EqualTo("success"));
        _torrentFileService.DidNotReceive().Update(Arg.Any<TorrentFile>());
    }

    [Test]
    public async Task HandleRpc_TorrentRenamePath_FolderRename_Updates_Matching_Child_Files()
    {
        var torrent = new Torrent
        {
            Id = 2,
            Name = "Multi File Torrent",
            InfoHash = "aabbccddeeff00112233aabbccddeeff00112233",
        };
        var file1 = new TorrentFile
        {
            Id = 21,
            TorrentId = 2,
            Path = "Season 1/Episode 01.mkv",
            Size = 2000,
        };
        var file2 = new TorrentFile
        {
            Id = 22,
            TorrentId = 2,
            Path = "Season 1/Episode 02.mkv",
            Size = 2000,
        };
        var file3 = new TorrentFile
        {
            Id = 23,
            TorrentId = 2,
            Path = "Season 2/Episode 01.mkv",
            Size = 2000,
        };

        _torrentService.Get(2).Returns(torrent);
        _torrentFileService.GetByTorrentId(2).Returns(new List<TorrentFile> { file1, file2, file3 });

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-rename-path",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["ids"] = JsonDocument.Parse("[2]").RootElement,
                ["path"] = JsonDocument.Parse("\"Season 1\"").RootElement,
                ["name"] = JsonDocument.Parse("\"Season 01\"").RootElement,
            },
        };

        var result = await _controller.HandleRpc(request);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());

        Assert.That(file1.Path, Is.EqualTo("Season 01/Episode 01.mkv"));
        Assert.That(file2.Path, Is.EqualTo("Season 01/Episode 02.mkv"));
        Assert.That(file3.Path, Is.EqualTo("Season 2/Episode 01.mkv"));

        _torrentFileService.Received(1).Update(file1);
        _torrentFileService.Received(1).Update(file2);
        _torrentFileService.DidNotReceive().Update(file3);
        Assert.That(torrent.Name, Is.EqualTo("Multi File Torrent"));
        _torrentService.Received(1).Recheck(2);
    }

    [Test]
    public async Task HandleRpc_TorrentGet_Projects_SizeWhenDone_FileStats_Wanted_Priorities_And_ErrorString()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Transmission Test",
            InfoHash = "1122334455667788990011223344556677889900",
            TotalSize = 10000,
            Downloaded = 4000,
            Progress = 0.4,
            Status = TorrentStatus.Downloading,
            ErrorMessage = "Tracker connection error",
        };

        var file1 = new TorrentFile
        {
            Id = 101,
            TorrentId = 1,
            Path = "file1.mkv",
            Size = 6000,
            Wanted = true,
            Priority = 1,
            BytesCompleted = 3000,
        };
        var file2 = new TorrentFile
        {
            Id = 102,
            TorrentId = 1,
            Path = "file2.mkv",
            Size = 4000,
            Wanted = false,
            Priority = -1,
            BytesCompleted = 1000,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file1, file2 });

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-get",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["fields"] = JsonDocument.Parse("[\"id\", \"sizeWhenDone\", \"leftUntilDone\", \"errorString\", \"fileStats\", \"priorities\", \"wanted\"]").RootElement,
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
        var torrentList = args["torrents"] as List<Dictionary<string, object>>;
        Assert.That(torrentList, Is.Not.Null);
        Assert.That(torrentList.Count, Is.EqualTo(1));

        var t = torrentList[0];
        Assert.That(t["id"], Is.EqualTo(1));
        Assert.That(t["sizeWhenDone"], Is.EqualTo(6000L));
        Assert.That(t["leftUntilDone"], Is.EqualTo(2000L));
        Assert.That(t["errorString"], Is.EqualTo("Tracker connection error"));

        var fileStats = t["fileStats"] as List<Dictionary<string, object>>;
        Assert.That(fileStats, Is.Not.Null);
        Assert.That(fileStats.Count, Is.EqualTo(2));
        Assert.That(fileStats[0]["wanted"], Is.EqualTo(true));
        Assert.That(fileStats[0]["priority"], Is.EqualTo(1));
        Assert.That(fileStats[0]["bytesCompleted"], Is.EqualTo(3000L));
        Assert.That(fileStats[1]["wanted"], Is.EqualTo(false));
        Assert.That(fileStats[1]["priority"], Is.EqualTo(-1));
        Assert.That(fileStats[1]["bytesCompleted"], Is.EqualTo(1000L));

        var priorities = t["priorities"] as List<int>;
        Assert.That(priorities, Is.Not.Null);
        Assert.That(priorities, Is.EqualTo(new List<int> { 1, -1 }));

        var wanted = t["wanted"] as List<int>;
        Assert.That(wanted, Is.Not.Null);
        Assert.That(wanted, Is.EqualTo(new List<int> { 1, 0 }));
    }

    [Test]
    public async Task HandleRpc_TorrentSet_Persists_FilesWanted_FilesUnwanted_And_Priorities()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Transmission Test",
            InfoHash = "1122334455667788990011223344556677889900",
        };

        var file1 = new TorrentFile
        {
            Id = 101,
            TorrentId = 1,
            Path = "file1.mkv",
            Size = 6000,
            Wanted = false,
            Priority = 0,
        };
        var file2 = new TorrentFile
        {
            Id = 102,
            TorrentId = 1,
            Path = "file2.mkv",
            Size = 4000,
            Wanted = true,
            Priority = 0,
        };

        _torrentService.Get(1).Returns(torrent);
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file1, file2 });

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-set",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["ids"] = JsonDocument.Parse("[1]").RootElement,
                ["files-wanted"] = JsonDocument.Parse("[0]").RootElement,
                ["files-unwanted"] = JsonDocument.Parse("[1]").RootElement,
                ["priority-high"] = JsonDocument.Parse("[0]").RootElement,
                ["priority-low"] = JsonDocument.Parse("[1]").RootElement,
                ["seedRatioLimit"] = JsonDocument.Parse("2.5").RootElement,
                ["seedIdleLimit"] = JsonDocument.Parse("60").RootElement,
            },
        };

        var result = await _controller.HandleRpc(request);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var response = ok.Value as TransmissionRpcResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Result, Is.EqualTo("success"));

        Assert.That(file1.Wanted, Is.True);
        Assert.That(file1.Priority, Is.EqualTo(1));
        Assert.That(file2.Wanted, Is.False);
        Assert.That(file2.Priority, Is.EqualTo(-1));
        Assert.That(torrent.RatioLimit, Is.EqualTo(2.5));
        Assert.That(torrent.SeedingTimeLimit, Is.EqualTo(60));

        _torrentFileService.Received(1).Update(file1);
        _torrentFileService.Received(1).Update(file2);
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public async Task HandleRpc_TorrentSet_Persists_PriorityNormal()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Transmission Test Normal Prio",
            InfoHash = "1122334455667788990011223344556677889900",
        };

        var file = new TorrentFile
        {
            Id = 101,
            TorrentId = 1,
            Path = "file1.mkv",
            Size = 6000,
            Wanted = true,
            Priority = 1,
        };

        _torrentService.Get(1).Returns(torrent);
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file });

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-set",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["ids"] = JsonDocument.Parse("[1]").RootElement,
                ["priority-normal"] = JsonDocument.Parse("[0]").RootElement,
            },
        };

        var result = await _controller.HandleRpc(request);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());

        Assert.That(file.Priority, Is.EqualTo(0));
        _torrentFileService.Received(1).Update(file);
    }

    [Test]
    public async Task HandleRpc_FreeSpace_Returns_Drive_FreeSpace()
    {
        var request = new TransmissionRpcRequest
        {
            Method = "free-space",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["path"] = JsonDocument.Parse("\"/\"").RootElement,
            },
            Tag = JsonDocument.Parse("123").RootElement,
        };

        var result = await _controller.HandleRpc(request);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var response = ok.Value as TransmissionRpcResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Result, Is.EqualTo("success"));

        var args = response.Arguments as Dictionary<string, object>;
        Assert.That(args, Is.Not.Null);
        Assert.That(args["path"], Is.EqualTo("/"));
        Assert.That((long)args["size-bytes"], Is.GreaterThan(0L));
    }

    [Test]
    public async Task HandleRpc_Missing_Session_Header_Returns_409_Conflict_With_Html_Content_And_Header()
    {
        _controller.ControllerContext.HttpContext.Request.Headers.Remove(TransmissionRpcController.SessionHeaderName);

        var request = new TransmissionRpcRequest { Method = "session-get" };
        var result = await _controller.HandleRpc(request);

        Assert.That(result, Is.InstanceOf<ContentResult>());
        var content = (ContentResult)result;
        Assert.That(content.StatusCode, Is.EqualTo(409));
        Assert.That(content.ContentType, Is.EqualTo("text/html"));
        Assert.That(content.Content, Does.Contain("<h1>409: Conflict</h1>"));
        Assert.That(content.Content, Does.Contain(TransmissionRpcController.CurrentSessionId));
        Assert.That(_controller.Response.Headers.ContainsKey(TransmissionRpcController.SessionHeaderName), Is.True);
        Assert.That((string)_controller.Response.Headers[TransmissionRpcController.SessionHeaderName], Is.EqualTo(TransmissionRpcController.CurrentSessionId));
    }

    [Test]
    public async Task HandleRpc_Invalid_Session_Header_Returns_409_Conflict_With_Html_Content_And_Header()
    {
        _controller.ControllerContext.HttpContext.Request.Headers[TransmissionRpcController.SessionHeaderName] = "invalid-session-token-123";

        var request = new TransmissionRpcRequest { Method = "session-get" };
        var result = await _controller.HandleRpc(request);

        Assert.That(result, Is.InstanceOf<ContentResult>());
        var content = (ContentResult)result;
        Assert.That(content.StatusCode, Is.EqualTo(409));
        Assert.That(content.ContentType, Is.EqualTo("text/html"));
        Assert.That(content.Content, Does.Contain("<h1>409: Conflict</h1>"));
        Assert.That(content.Content, Does.Contain(TransmissionRpcController.CurrentSessionId));
        Assert.That(_controller.Response.Headers.ContainsKey(TransmissionRpcController.SessionHeaderName), Is.True);
        Assert.That((string)_controller.Response.Headers[TransmissionRpcController.SessionHeaderName], Is.EqualTo(TransmissionRpcController.CurrentSessionId));
    }

    [Test]
    public async Task HandleRpc_Valid_Session_Header_Succeeds()
    {
        _controller.ControllerContext.HttpContext.Request.Headers[TransmissionRpcController.SessionHeaderName] = TransmissionRpcController.CurrentSessionId;

        var request = new TransmissionRpcRequest { Method = "session-get" };
        var result = await _controller.HandleRpc(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var response = ok.Value as TransmissionRpcResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Result, Is.EqualTo("success"));
    }

    [Test]
    public async Task HandleRpc_SessionSet_Updates_Speed_Limits_And_Ratio_Limits_In_Config()
    {
        Dictionary<string, object> savedDict = null;
        _configService.When(c => c.SaveConfigDictionary(Arg.Any<Dictionary<string, object>>()))
            .Do(call => savedDict = call.Arg<Dictionary<string, object>>());

        var request = new TransmissionRpcRequest
        {
            Method = "session-set",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["speed-limit-down"] = JsonDocument.Parse("2500").RootElement,
                ["speed-limit-up"] = JsonDocument.Parse("1500").RootElement,
                ["speed-limit-down-enabled"] = JsonDocument.Parse("true").RootElement,
                ["speed-limit-up-enabled"] = JsonDocument.Parse("false").RootElement,
                ["seedRatioLimit"] = JsonDocument.Parse("2.75").RootElement,
                ["seedRatioLimited"] = JsonDocument.Parse("true").RootElement,
            },
        };

        var result = await _controller.HandleRpc(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var response = ok.Value as TransmissionRpcResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Result, Is.EqualTo("success"));

        Assert.That(savedDict, Is.Not.Null);
        Assert.That(savedDict["MaxDownloadSpeedKbps"], Is.EqualTo(2500));
        Assert.That(savedDict["MaxUploadSpeedKbps"], Is.EqualTo(1500));
        Assert.That(savedDict["SpeedLimitDownEnabled"], Is.EqualTo(true));
        Assert.That(savedDict["SpeedLimitUpEnabled"], Is.EqualTo(false));
        Assert.That(savedDict["GlobalSeedRatioLimit"], Is.EqualTo(2.75));
        Assert.That(savedDict["SeedRatioLimited"], Is.EqualTo(true));
    }

    [Test]
    public async Task HandleRpc_SessionGet_Returns_Accurate_Speed_And_Ratio_Limits()
    {
        _configService.SpeedLimitDownEnabled.Returns(true);
        _configService.SpeedLimitUpEnabled.Returns(false);
        _configService.GlobalSeedRatioLimit.Returns(3.25);
        _configService.SeedRatioLimited.Returns(true);

        var request = new TransmissionRpcRequest { Method = "session-get" };
        var result = await _controller.HandleRpc(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var response = ok.Value as TransmissionRpcResponse;
        Assert.That(response, Is.Not.Null);
        var args = response.Arguments as Dictionary<string, object>;
        Assert.That(args, Is.Not.Null);
        Assert.That(args["speed-limit-down-enabled"], Is.EqualTo(true));
        Assert.That(args["speed-limit-up-enabled"], Is.EqualTo(false));
        Assert.That(args["seedRatioLimit"], Is.EqualTo(3.25));
        Assert.That(args["seedRatioLimited"], Is.EqualTo(true));
    }
    [Test]
    public async Task HandleRpc_TorrentAdd_DuplicateTorrent_Returns_Matching_Duplicate_Not_Unrelated_First_Torrent()
    {
        var torrent1 = new Torrent
        {
            Id = 1,
            Name = "Unrelated First Torrent",
            InfoHash = "1111111111111111111111111111111111111111",
        };
        var torrent2 = new Torrent
        {
            Id = 2,
            Name = "Target Duplicate Torrent",
            InfoHash = "2222222222222222222222222222222222222222",
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });
        _torrentService.GetByInfoHash("2222222222222222222222222222222222222222").Returns(torrent2);
        _torrentImportService.ImportFromMagnet(Arg.Is<string>(s => s.Contains("2222222222222222222222222222222222222222")))
            .Returns(_ => throw new InvalidOperationException("Torrent with infohash 2222222222222222222222222222222222222222 already exists"));

        var magnet = "magnet:?xt=urn:btih:2222222222222222222222222222222222222222&dn=TargetDuplicate";
        var request = new TransmissionRpcRequest
        {
            Method = "torrent-add",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["filename"] = JsonDocument.Parse($"\"{magnet}\"").RootElement,
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
        var duplicate = args["torrent-duplicate"] as Dictionary<string, object>;
        Assert.That(duplicate, Is.Not.Null);
        Assert.That(duplicate["id"], Is.EqualTo(2));
        Assert.That(duplicate["name"], Is.EqualTo("Target Duplicate Torrent"));
        Assert.That(duplicate["hashString"], Is.EqualTo("2222222222222222222222222222222222222222"));
    }

    [Test]
    public async Task HandleRpc_TorrentSet_WithEmptyLabelsArray_Clears_Labels_And_TagIds()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Label Test",
            Label = "movies,hd",
            Category = "movies",
            TagIds = new List<int> { 10, 20 },
        };

        _torrentService.Get(1).Returns(torrent);

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-set",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["ids"] = JsonDocument.Parse("[1]").RootElement,
                ["labels"] = JsonDocument.Parse("[]").RootElement,
            },
        };

        var result = await _controller.HandleRpc(request);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var response = ok.Value as TransmissionRpcResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Result, Is.EqualTo("success"));

        Assert.That(torrent.Label, Is.Empty);
        Assert.That(torrent.TagIds, Is.Empty);
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public async Task HandleRpc_TorrentGet_Returns_SavePath_As_DownloadDir_When_SavePath_Set_And_SourcePath_Empty()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "SavePath Test",
            SavePath = "/media/torrents/custom_save",
            SourcePath = null,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-get",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["fields"] = JsonDocument.Parse("["id", "downloadDir"]").RootElement,
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
        var torrents = args["torrents"] as List<Dictionary<string, object>>;
        Assert.That(torrents, Is.Not.Null);
        Assert.That(torrents.Count, Is.EqualTo(1));
        Assert.That(torrents[0]["downloadDir"], Is.EqualTo("/media/torrents/custom_save"));
    }

    [Test]
    public async Task HandleRpc_TorrentGet_Returns_Category_In_Labels_When_Label_Is_Empty()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Category Fallback Test",
            Label = null,
            Category = "sonarr-tv",
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-get",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["fields"] = JsonDocument.Parse("["id", "labels"]").RootElement,
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
        var torrents = args["torrents"] as List<Dictionary<string, object>>;
        Assert.That(torrents, Is.Not.Null);
        Assert.That(torrents.Count, Is.EqualTo(1));
        var labels = torrents[0]["labels"] as List<string>;
        Assert.That(labels, Is.Not.Null);
        Assert.That(labels, Is.EqualTo(new List<string> { "sonarr-tv" }));
    }

    [Test]
    public async Task HandleRpc_TorrentRemove_Preserves_Active_Seeding_Torrent_When_Goals_Unmet()
    {
        var torrent = new Torrent
        {
            Id = 50,
            Name = "PreservedSeed",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            Ratio = 0.5,
            RatioLimit = 2.0
        };

        _configService.PreserveSeedingOnArrDelete.Returns(true);
        _torrentService.Get(50).Returns(torrent);

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-remove",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["ids"] = JsonDocument.Parse("[50]").RootElement,
                ["delete-local-data"] = JsonDocument.Parse("true").RootElement,
            },
        };

        var result = await _controller.HandleRpc(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var resp = ok.Value as TransmissionRpcResponse;
        Assert.That(resp, Is.Not.Null);
        Assert.That(resp.Result, Is.EqualTo("success"));
        _torrentService.DidNotReceive().Delete(50, Arg.Any<bool>());
    }

    [Test]
    public async Task HandleRpc_TorrentRemove_Deletes_Seeding_Torrent_When_Goals_Satisfied()
    {
        var torrent = new Torrent
        {
            Id = 51,
            Name = "FinishedSeed",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            Ratio = 2.1,
            RatioLimit = 2.0
        };

        _configService.PreserveSeedingOnArrDelete.Returns(true);
        _torrentService.Get(51).Returns(torrent);

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-remove",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["ids"] = JsonDocument.Parse("[51]").RootElement,
                ["delete-local-data"] = JsonDocument.Parse("true").RootElement,
            },
        };

        var result = await _controller.HandleRpc(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        var resp = ok.Value as TransmissionRpcResponse;
        Assert.That(resp, Is.Not.Null);
        Assert.That(resp.Result, Is.EqualTo("success"));
        _torrentService.Received(1).Delete(51, true);
    }

    [Test]
    public async Task HandleRpc_TorrentRemove_Deletes_When_PreserveSeeding_Disabled()
    {
        var torrent = new Torrent
        {
            Id = 52,
            Name = "UnprotectedSeed",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            Ratio = 0.5,
            RatioLimit = 2.0
        };

        _configService.PreserveSeedingOnArrDelete.Returns(false);
        _torrentService.Get(52).Returns(torrent);

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-remove",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["ids"] = JsonDocument.Parse("[52]").RootElement,
                ["delete-local-data"] = JsonDocument.Parse("true").RootElement,
            },
        };

        var result = await _controller.HandleRpc(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _torrentService.Received(1).Delete(52, true);
    }

    [Test]
    public async Task TorrentGet_Remaps_DownloadDir_Outbound()
    {
        var remotePathMappingService = Substitute.For<IRemotePathMappingService>();
        remotePathMappingService.RemapLocalToRemote("192.168.1.60", "/downloads/tv")
            .Returns(@"Z:\Downloads\tv");

        var controller = new TransmissionRpcController(
            _torrentService,
            _torrentFileService,
            _torrentFileParser,
            _torrentImportService,
            _trackerEntryService,
            _configService,
            _configFileProvider,
            remotePathMappingService: remotePathMappingService,
            categoryService: _categoryService);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.60");
        httpContext.Request.Headers[TransmissionRpcController.SessionHeaderName] = TransmissionRpcController.CurrentSessionId;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "1234567890123456789012345678901234567890",
            Name = "Show.S01E01",
            SourcePath = "/downloads/tv",
            Status = TorrentStatus.Downloading,
            TotalSize = 2000,
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-get",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["fields"] = JsonDocument.Parse("["id", "name", "downloadDir"]").RootElement,
            },
        };

        var result = await controller.HandleRpc(request);
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var response = okResult.Value as TransmissionRpcResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Result, Is.EqualTo("success"));

        var dict = response.Arguments as Dictionary<string, object>;
        var torrents = dict["torrents"] as List<Dictionary<string, object>>;
        Assert.That(torrents, Is.Not.Null);
        Assert.That(torrents[0]["downloadDir"], Is.EqualTo(@"Z:\Downloads\tv"));
    }

    [Test]
    public async Task TorrentSetLocation_Remaps_Inbound_Location()
    {
        var remotePathMappingService = Substitute.For<IRemotePathMappingService>();
        remotePathMappingService.RemapRemoteToLocal("192.168.1.60", @"Z:\Downloads\tv")
            .Returns("/downloads/tv");

        var controller = new TransmissionRpcController(
            _torrentService,
            _torrentFileService,
            _torrentFileParser,
            _torrentImportService,
            _trackerEntryService,
            _configService,
            _configFileProvider,
            remotePathMappingService: remotePathMappingService,
            categoryService: _categoryService);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.60");
        httpContext.Request.Headers[TransmissionRpcController.SessionHeaderName] = TransmissionRpcController.CurrentSessionId;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent
        {
            Id = 10,
            InfoHash = "1234567890123456789012345678901234567890",
            Name = "Show.S01E01",
            SourcePath = "/downloads/old",
            SavePath = "/downloads/old",
        };
        _torrentService.Get(10).Returns(torrent);

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-set-location",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["ids"] = JsonDocument.Parse("[10]").RootElement,
                ["location"] = JsonDocument.Parse(""Z:\\Downloads\\tv"").RootElement,
                ["move"] = JsonDocument.Parse("false").RootElement,
            },
        };

        var result = await controller.HandleRpc(request);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(torrent.SourcePath, Is.EqualTo("/downloads/tv"));
        Assert.That(torrent.SavePath, Is.EqualTo("/downloads/tv"));
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public async Task TorrentSetLocation_WithMoveTrue_AndRelocationService_Invokes_RelocateTorrentAsync()
    {
        var relocationService = Substitute.For<ITorrentRelocationService>();
        var remotePathMappingService = Substitute.For<IRemotePathMappingService>();
        remotePathMappingService.RemapRemoteToLocal("192.168.1.60", @"Z:\Downloads\tv")
            .Returns("/downloads/tv");

        var controller = new TransmissionRpcController(
            _torrentService,
            _torrentFileService,
            _torrentFileParser,
            _torrentImportService,
            _trackerEntryService,
            _configService,
            _configFileProvider,
            relocationService: relocationService,
            remotePathMappingService: remotePathMappingService,
            categoryService: _categoryService);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.60");
        httpContext.Request.Headers[TransmissionRpcController.SessionHeaderName] = TransmissionRpcController.CurrentSessionId;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent
        {
            Id = 10,
            InfoHash = "1234567890123456789012345678901234567890",
            Name = "Show.S01E01",
            SourcePath = "/downloads/old",
            SavePath = "/downloads/old",
        };
        _torrentService.Get(10).Returns(torrent);

        var request = new TransmissionRpcRequest
        {
            Method = "torrent-set-location",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["ids"] = JsonDocument.Parse("[10]").RootElement,
                ["location"] = JsonDocument.Parse("\"Z:\\\\Downloads\\\\tv\"").RootElement,
                ["move"] = JsonDocument.Parse("true").RootElement,
            },
        };

        var result = await controller.HandleRpc(request);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        relocationService.Received(1).RelocateTorrentAsync(10, "/downloads/tv");
    }
}

