using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
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

        _configFileProvider.AuthenticationEnabled.Returns(false);

        _controller = new TransmissionRpcController(
            _torrentService,
            _torrentFileService,
            _torrentFileParser,
            _torrentImportService,
            _trackerEntryService,
            _configService,
            _configFileProvider);

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
}
