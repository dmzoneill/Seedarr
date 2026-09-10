using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using Seedarr.Api.V1.Deluge;

namespace NzbDrone.Core.Test.Http;

[TestFixture]
public class DelugeJsonRpcControllerTest
{
    private ITorrentService _torrentService;
    private ITorrentFileService _torrentFileService;
    private ITorrentFileParser _torrentFileParser;
    private ITorrentImportService _torrentImportService;
    private IConfigService _configService;
    private ITagService _tagService;
    private IConfigFileProvider _configFileProvider;
    private DelugeJsonRpcController _controller;

    [SetUp]
    public void SetUp()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _torrentFileParser = Substitute.For<ITorrentFileParser>();
        _torrentImportService = Substitute.For<ITorrentImportService>();
        _configService = Substitute.For<IConfigService>();
        _tagService = Substitute.For<ITagService>();
        _configFileProvider = Substitute.For<IConfigFileProvider>();

        _configFileProvider.AuthenticationEnabled.Returns(false);

        _controller = new DelugeJsonRpcController(
            _torrentService,
            _torrentFileService,
            _torrentFileParser,
            _torrentImportService,
            _configService,
            _tagService,
            _configFileProvider);
    }

    [Test]
    public async Task HandleRpc_AuthLogin_WhenAuthDisabled_Returns_True()
    {
        var json = "{\"method\": \"auth.login\", \"params\": [\"secret\"], \"id\": 1}";
        using var doc = JsonDocument.Parse(json);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());
        var jsonResult = (JsonResult)result;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        Assert.That(resDoc.RootElement.GetProperty("result").GetBoolean(), Is.True);
        Assert.That(resDoc.RootElement.GetProperty("id").GetInt64(), Is.EqualTo(1));
    }

    [Test]
    public async Task HandleRpc_CoreGetVersion_Returns_Version_2_1_1()
    {
        var json = "{\"method\": \"core.get_version\", \"params\": [], \"id\": 42}";
        using var doc = JsonDocument.Parse(json);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());
        var jsonResult = (JsonResult)result;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        Assert.That(resDoc.RootElement.GetProperty("result").GetString(), Is.EqualTo("2.1.1"));
        Assert.That(resDoc.RootElement.GetProperty("id").GetInt64(), Is.EqualTo(42));
    }

    [Test]
    public async Task HandleRpc_CoreGetTorrentsStatus_Returns_Torrents_Map()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Deluge Test",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            TotalSize = 5000000,
            Progress = 0.75,
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 200000,
            UploadSpeed = 50000,
            DateAdded = DateTime.UtcNow,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var json = "{\"method\": \"core.get_torrents_status\", \"params\": [{}, [\"name\", \"progress\", \"state\", \"total_size\"]], \"id\": 10}";
        using var doc = JsonDocument.Parse(json);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());
        var jsonResult = (JsonResult)result;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        var resMap = resDoc.RootElement.GetProperty("result");
        Assert.That(resMap.TryGetProperty("aabbccddeeff00112233445566778899aabbccdd", out var tObj), Is.True);
        Assert.That(tObj.GetProperty("name").GetString(), Is.EqualTo("Deluge Test"));
        Assert.That(tObj.GetProperty("state").GetString(), Is.EqualTo("Downloading"));
        Assert.That(tObj.GetProperty("progress").GetDouble(), Is.EqualTo(75.0));
    }

    [Test]
    public async Task HandleRpc_CoreAddTorrentMagnet_Calls_ImportService()
    {
        var magnet = "magnet:?xt=urn:btih:aabbccddeeff00112233445566778899aabbccdd&dn=MagnetTest";
        var createdTorrent = new Torrent
        {
            Id = 5,
            Name = "MagnetTest",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
        };

        _torrentImportService.ImportFromMagnet(magnet).Returns(createdTorrent);

        var json = $"{{\"method\": \"core.add_torrent_magnet\", \"params\": [\"{magnet}\", {{\"label\": \"tv\"}}], \"id\": 99}}";
        using var doc = JsonDocument.Parse(json);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());
        var jsonResult = (JsonResult)result;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        Assert.That(resDoc.RootElement.GetProperty("result").GetString(), Is.EqualTo("aabbccddeeff00112233445566778899aabbccdd"));

        _torrentImportService.Received(1).ImportFromMagnet(magnet);
        _torrentService.Received().Update(Arg.Is<Torrent>(t => t.Label == "tv"));
    }

    [Test]
    public async Task HandleRpc_BatchArray_Processes_All_Requests()
    {
        var batchJson = "[{\"method\": \"core.get_version\", \"params\": [], \"id\": 1}, {\"method\": \"daemon.info\", \"params\": [], \"id\": 2}]";
        using var doc = JsonDocument.Parse(batchJson);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());
        var jsonResult = (JsonResult)result;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        Assert.That(resDoc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(resDoc.RootElement.GetArrayLength(), Is.EqualTo(2));
    }
}
