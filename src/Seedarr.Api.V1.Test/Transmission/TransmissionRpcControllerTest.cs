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
        httpContext.Request.Headers["X-Transmission-Session-Id"] = "test-session-id";
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
}
