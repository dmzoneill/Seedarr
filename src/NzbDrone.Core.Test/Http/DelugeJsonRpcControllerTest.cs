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

    [Test]
    public async Task HandleRpc_LabelGetLabels_Returns_Merged_TagService_And_Torrent_Labels()
    {
        _tagService.GetAll().Returns(new List<Tag>
        {
            new Tag { Id = 1, Label = "delugeTag1" },
            new Tag { Id = 2, Label = "delugeTag2" },
        });

        var torrent = new Torrent
        {
            Id = 1,
            Label = "delugeTag2, delugeTag3; delugeTag4",
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var json = "{\"method\": \"label.get_labels\", \"params\": [], \"id\": 123}";
        using var doc = JsonDocument.Parse(json);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());
        var jsonResult = (JsonResult)result;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        var resultArr = resDoc.RootElement.GetProperty("result");
        Assert.That(resultArr.ValueKind, Is.EqualTo(JsonValueKind.Array));

        var labels = new List<string>();
        foreach (var item in resultArr.EnumerateArray())
        {
            labels.Add(item.GetString());
        }

        Assert.That(labels, Does.Contain("delugeTag1"));
        Assert.That(labels, Does.Contain("delugeTag2"));
        Assert.That(labels, Does.Contain("delugeTag3"));
        Assert.That(labels, Does.Contain("delugeTag4"));
        Assert.That(labels.Count, Is.EqualTo(4));
    }

    [Test]
    public async Task HandleRpc_LabelRemove_Deletes_From_TagService_And_Torrents()
    {
        var tag1 = new Tag { Id = 5, Label = "removetag" };
        var tag2 = new Tag { Id = 6, Label = "keep" };
        _tagService.GetAll().Returns(new List<Tag> { tag1, tag2 });

        var torrent = new Torrent
        {
            Id = 1,
            Label = "removetag, keep",
            TagIds = new List<int> { 5, 6 },
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _tagService.SyncTagsFromLabels(Arg.Any<IEnumerable<string>>()).Returns(new List<int> { 6 });

        var json = "{\"method\": \"label.remove\", \"params\": [\"removetag\"], \"id\": 456}";
        using var doc = JsonDocument.Parse(json);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());

        _tagService.Received(1).Delete(5);
        _tagService.DidNotReceive().Delete(6);
        Assert.That(torrent.Label, Is.EqualTo("keep"));
        Assert.That(torrent.TagIds, Is.EqualTo(new List<int> { 6 }));
        _torrentService.Received(1).Update(torrent);
    }
}
