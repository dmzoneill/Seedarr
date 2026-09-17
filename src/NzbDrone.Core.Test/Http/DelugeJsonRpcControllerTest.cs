using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
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
    private ICategoryService _categoryService;
    private ITrackerEntryService _trackerService;
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
        _categoryService = Substitute.For<ICategoryService>();
        _trackerService = Substitute.For<ITrackerEntryService>();

        _configFileProvider.AuthenticationEnabled.Returns(false);

        _controller = new DelugeJsonRpcController(
            _torrentService,
            _torrentFileService,
            _torrentFileParser,
            _torrentImportService,
            _configService,
            _tagService,
            _configFileProvider,
            categoryService: _categoryService,
            trackerService: _trackerService);
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

    [Test]
    public async Task HandleRpc_CoreGetTorrentStatus_Projects_Hash_And_TotalRemaining_Correctly()
    {
        var partialTorrent = new Torrent
        {
            Id = 1,
            Name = "Partial Torrent",
            InfoHash = "AABBCCDDEEFF00112233445566778899AABBCCDD",
            TotalSize = 10_000_000L,
            Progress = 0.4,
            Status = TorrentStatus.Downloading,
            DateAdded = DateTime.UtcNow,
        };

        var completedTorrent = new Torrent
        {
            Id = 2,
            Name = "Completed Torrent",
            InfoHash = "11223344556677889900AABBCCDDEEFF00112233",
            TotalSize = 5_000_000L,
            Progress = 1.0,
            Status = TorrentStatus.Seeding,
            DateAdded = DateTime.UtcNow,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { partialTorrent, completedTorrent });

        // Test partial torrent projection
        var jsonPartial = "{\"method\": \"core.get_torrent_status\", \"params\": [\"aabbccddeeff00112233445566778899aabbccdd\", [\"hash\", \"total_remaining\", \"name\"]], \"id\": 101}";
        using var docPartial = JsonDocument.Parse(jsonPartial);
        var resultPartial = await _controller.HandleRpc(docPartial.RootElement);
        Assert.That(resultPartial, Is.InstanceOf<JsonResult>());
        var jsonPartialResult = (JsonResult)resultPartial;

        var serializedPartial = JsonSerializer.Serialize(jsonPartialResult.Value);
        using var resDocPartial = JsonDocument.Parse(serializedPartial);
        var resMapPartial = resDocPartial.RootElement.GetProperty("result");
        Assert.That(resMapPartial.GetProperty("hash").GetString(), Is.EqualTo("aabbccddeeff00112233445566778899aabbccdd"));
        Assert.That(resMapPartial.GetProperty("total_remaining").GetInt64(), Is.EqualTo(6_000_000L));
        Assert.That(resMapPartial.GetProperty("name").GetString(), Is.EqualTo("Partial Torrent"));

        // Test completed torrent projection
        var jsonCompleted = "{\"method\": \"core.get_torrent_status\", \"params\": [\"11223344556677889900aabbccddeeff00112233\", [\"hash\", \"total_remaining\", \"name\"]], \"id\": 102}";
        using var docCompleted = JsonDocument.Parse(jsonCompleted);
        var resultCompleted = await _controller.HandleRpc(docCompleted.RootElement);
        Assert.That(resultCompleted, Is.InstanceOf<JsonResult>());
        var jsonCompletedResult = (JsonResult)resultCompleted;

        var serializedCompleted = JsonSerializer.Serialize(jsonCompletedResult.Value);
        using var resDocCompleted = JsonDocument.Parse(serializedCompleted);
        var resMapCompleted = resDocCompleted.RootElement.GetProperty("result");
        Assert.That(resMapCompleted.GetProperty("hash").GetString(), Is.EqualTo("11223344556677889900aabbccddeeff00112233"));
        Assert.That(resMapCompleted.GetProperty("total_remaining").GetInt64(), Is.EqualTo(0L));
        Assert.That(resMapCompleted.GetProperty("name").GetString(), Is.EqualTo("Completed Torrent"));
    }

    [Test]
    public async Task HandleRpc_WebAddTorrents_With_FilePath_Imports_Without_FormatException()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempFile, new byte[] { 0x64, 0x31, 0x3a, 0x65 });

            var createdTorrent = new Torrent
            {
                Id = 10,
                Name = "FileTorrent",
                InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            };

            _torrentImportService.ImportFromFile(Arg.Any<Stream>(), Arg.Any<string>()).Returns(createdTorrent);

            var escapedPath = tempFile.Replace("\\", "\\\\");
            var json = $"{{\"method\": \"web.add_torrents\", \"params\": [[{{\"path\": \"{escapedPath}\", \"options\": {{\"label\": \"imported\"}}}}]], \"id\": 201}}";
            using var doc = JsonDocument.Parse(json);

            var result = await _controller.HandleRpc(doc.RootElement);
            Assert.That(result, Is.InstanceOf<JsonResult>());
            var jsonResult = (JsonResult)result;

            var serialized = JsonSerializer.Serialize(jsonResult.Value);
            using var resDoc = JsonDocument.Parse(serialized);
            Assert.That(resDoc.RootElement.GetProperty("result").GetBoolean(), Is.True);
            _torrentImportService.Received(1).ImportFromFile(Arg.Any<Stream>(), Arg.Any<string>());

            // Verify non-existent file path does not throw FormatException
            var invalidPathJson = "{\"method\": \"web.add_torrents\", \"params\": [[{\"path\": \"/nonexistent/test/path.torrent\", \"options\": {}}]], \"id\": 202}";
            using var docInvalid = JsonDocument.Parse(invalidPathJson);
            var resultInvalid = await _controller.HandleRpc(docInvalid.RootElement);
            Assert.That(resultInvalid, Is.InstanceOf<JsonResult>());
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
    public async Task HandleRpc_WebConnectionState_Maintains_State_Across_Connected_Connect_And_Disconnect()
    {
        DelugeJsonRpcController.IsWebConnected = true;

        // 1. Initially connected
        var jsonConnected = "{\"method\": \"web.connected\", \"params\": [], \"id\": 301}";
        using (var doc = JsonDocument.Parse(jsonConnected))
        {
            var result = await _controller.HandleRpc(doc.RootElement);
            var jsonResult = (JsonResult)result;
            var serialized = JsonSerializer.Serialize(jsonResult.Value);
            using var resDoc = JsonDocument.Parse(serialized);
            Assert.That(resDoc.RootElement.GetProperty("result").GetBoolean(), Is.True);
        }

        // 2. Disconnect
        var jsonDisconnect = "{\"method\": \"web.disconnect\", \"params\": [], \"id\": 302}";
        using (var doc = JsonDocument.Parse(jsonDisconnect))
        {
            var result = await _controller.HandleRpc(doc.RootElement);
            var jsonResult = (JsonResult)result;
            var serialized = JsonSerializer.Serialize(jsonResult.Value);
            using var resDoc = JsonDocument.Parse(serialized);
            Assert.That(resDoc.RootElement.GetProperty("result").GetBoolean(), Is.True);
        }

        // 3. Now disconnected
        using (var doc = JsonDocument.Parse(jsonConnected))
        {
            var result = await _controller.HandleRpc(doc.RootElement);
            var jsonResult = (JsonResult)result;
            var serialized = JsonSerializer.Serialize(jsonResult.Value);
            using var resDoc = JsonDocument.Parse(serialized);
            Assert.That(resDoc.RootElement.GetProperty("result").GetBoolean(), Is.False);
        }

        // 4. Connect
        var jsonConnect = "{\"method\": \"web.connect\", \"params\": [\"host-1\"], \"id\": 303}";
        using (var doc = JsonDocument.Parse(jsonConnect))
        {
            var result = await _controller.HandleRpc(doc.RootElement);
            var jsonResult = (JsonResult)result;
            var serialized = JsonSerializer.Serialize(jsonResult.Value);
            using var resDoc = JsonDocument.Parse(serialized);
            Assert.That(resDoc.RootElement.GetProperty("result").GetBoolean(), Is.True);
        }

        // 5. Connected again
        using (var doc = JsonDocument.Parse(jsonConnected))
        {
            var result = await _controller.HandleRpc(doc.RootElement);
            var jsonResult = (JsonResult)result;
            var serialized = JsonSerializer.Serialize(jsonResult.Value);
            using var resDoc = JsonDocument.Parse(serialized);
            Assert.That(resDoc.RootElement.GetProperty("result").GetBoolean(), Is.True);
        }
    }

    [Test]
    public async Task HandleRpc_CorePauseTorrent_With_Empty_Hashes_Does_Not_Pause_All_Torrents()
    {
        var torrent1 = new Torrent { Id = 1, Name = "T1", InfoHash = "1111111111111111111111111111111111111111", Status = TorrentStatus.Downloading };
        var torrent2 = new Torrent { Id = 2, Name = "T2", InfoHash = "2222222222222222222222222222222222222222", Status = TorrentStatus.Downloading };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var json = "{\"method\": \"core.pause_torrent\", \"params\": [[]], \"id\": 401}";
        using var doc = JsonDocument.Parse(json);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());
        var jsonResult = (JsonResult)result;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        Assert.That(resDoc.RootElement.GetProperty("result").GetBoolean(), Is.False);

        _torrentService.DidNotReceive().Update(Arg.Any<Torrent>());
        Assert.That(torrent1.Status, Is.EqualTo(TorrentStatus.Downloading));
        Assert.That(torrent2.Status, Is.EqualTo(TorrentStatus.Downloading));
    }

    [Test]
    public async Task HandleRpc_CorePauseAllTorrents_Pauses_All_Torrents()
    {
        var torrent1 = new Torrent { Id = 1, Name = "T1", InfoHash = "1111111111111111111111111111111111111111", Status = TorrentStatus.Downloading };
        var torrent2 = new Torrent { Id = 2, Name = "T2", InfoHash = "2222222222222222222222222222222222222222", Status = TorrentStatus.Downloading };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var json = "{\"method\": \"core.pause_all_torrents\", \"params\": [], \"id\": 402}";
        using var doc = JsonDocument.Parse(json);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());
        var jsonResult = (JsonResult)result;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        Assert.That(resDoc.RootElement.GetProperty("result").GetBoolean(), Is.True);

        _torrentService.Received(1).Update(torrent1);
        _torrentService.Received(1).Update(torrent2);
        Assert.That(torrent1.Status, Is.EqualTo(TorrentStatus.Paused));
        Assert.That(torrent2.Status, Is.EqualTo(TorrentStatus.Paused));
    }

    [Test]
    public async Task HandleRpc_CoreAddTorrentMagnet_OnDuplicate_Returns_Existing_Torrent_InfoHash()
    {
        var infoHash = "aabbccddeeff00112233445566778899aabbccdd";
        var magnet = $"magnet:?xt=urn:btih:{infoHash}&dn=DuplicateMagnet";
        var existingTorrent = new Torrent
        {
            Id = 5,
            Name = "DuplicateMagnet",
            InfoHash = infoHash,
        };

        _torrentImportService.ImportFromMagnet(magnet)
            .Returns(_ => throw new InvalidOperationException("Torrent with this info hash already exists"));
        _torrentService.GetByInfoHash(infoHash).Returns(existingTorrent);

        var json = $"{{\"method\": \"core.add_torrent_magnet\", \"params\": [\"{magnet}\", {{\"label\": \"tv\"}}], \"id\": 403}}";
        using var doc = JsonDocument.Parse(json);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());
        var jsonResult = (JsonResult)result;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        Assert.That(resDoc.RootElement.GetProperty("result").GetString(), Is.EqualTo(infoHash));
        Assert.That(resDoc.RootElement.GetProperty("error").ValueKind, Is.EqualTo(JsonValueKind.Null));
        _torrentService.Received().Update(Arg.Is<Torrent>(t => t.Id == 5 && t.Label == "tv"));
    }

    [Test]
    public async Task HandleRpc_CoreAddTorrentFile_OnDuplicate_Returns_Existing_Torrent_InfoHash()
    {
        var infoHash = "aabbccddeeff00112233445566778899aabbccdd";
        var existingTorrent = new Torrent
        {
            Id = 6,
            Name = "DuplicateFileTorrent",
            InfoHash = infoHash,
        };

        var parsed = new ParsedTorrent
        {
            Name = "DuplicateFileTorrent",
            InfoHash = infoHash,
        };

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(parsed);
        _torrentImportService.ImportFromFile(Arg.Any<Stream>(), Arg.Any<string>())
            .Returns(_ => throw new InvalidOperationException("Torrent with this info hash already exists"));
        _torrentService.GetByInfoHash(infoHash).Returns(existingTorrent);

        var base64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
        var json = $"{{\"method\": \"core.add_torrent_file\", \"params\": [\"test.torrent\", \"{base64}\", {{\"label\": \"movies\"}}], \"id\": 404}}";
        using var doc = JsonDocument.Parse(json);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());
        var jsonResult = (JsonResult)result;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        Assert.That(resDoc.RootElement.GetProperty("result").GetString(), Is.EqualTo(infoHash));
        Assert.That(resDoc.RootElement.GetProperty("error").ValueKind, Is.EqualTo(JsonValueKind.Null));
        _torrentService.Received().Update(Arg.Is<Torrent>(t => t.Id == 6 && t.Label == "movies"));
    }

    [Test]
    public async Task HandleRpc_CoreRemoveTorrent_Returns_False_When_Target_Hash_Does_Not_Exist()
    {
        var existingTorrent = new Torrent
        {
            Id = 1,
            Name = "Existing",
            InfoHash = "1111111111111111111111111111111111111111",
        };
        _torrentService.GetAll().Returns(new List<Torrent> { existingTorrent });

        var json = "{\"method\": \"core.remove_torrent\", \"params\": [\"nonexistenthash\"], \"id\": 405}";
        using var doc = JsonDocument.Parse(json);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());
        var jsonResult = (JsonResult)result;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        Assert.That(resDoc.RootElement.GetProperty("result").GetBoolean(), Is.False);

        _torrentService.DidNotReceive().Delete(Arg.Any<int>(), Arg.Any<bool>());
    }

    [Test]
    public async Task HandleRpc_AddTorrentMagnet_With_Label_Uses_Category_SavePath()
    {
        var category = new Category { Id = 1, Name = "tv-sonarr", SavePath = "/data/media/tv" };
        _categoryService.GetByName("tv-sonarr").Returns(category);
        _categoryService.GetSavePathForCategory("tv-sonarr", Arg.Any<string>()).Returns("/data/media/tv");

        var imported = new Torrent
        {
            Id = 7,
            Name = "DelugeTest",
            InfoHash = "2222222222222222222222222222222222222222",
        };
        _torrentImportService.ImportFromMagnet(Arg.Any<string>()).Returns(imported);

        var json = "{\"method\": \"core.add_torrent_magnet\", \"params\": [\"magnet:?xt=urn:btih:2222222222222222222222222222222222222222\", {\"label\": \"tv-sonarr\"}], \"id\": 500}";
        using var doc = JsonDocument.Parse(json);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());

        _torrentService.Received().Update(Arg.Is<Torrent>(t => t.Id == 7 && t.Category == "tv-sonarr" && t.SourcePath == "/data/media/tv"));
    }

    [Test]
    public async Task HandleRpc_LabelSetOptions_Updates_Category_SavePath()
    {
        var existing = new Category { Id = 3, Name = "tv-sonarr", SavePath = "/old/path" };
        _categoryService.GetByName("tv-sonarr").Returns(existing);

        var json = "{\"method\": \"label.set_options\", \"params\": [\"tv-sonarr\", {\"move_completed_path\": \"/data/media/tv\"}], \"id\": 501}";
        using var doc = JsonDocument.Parse(json);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());

        _categoryService.Received(1).Update(Arg.Is<Category>(c => c.Id == 3 && c.SavePath == "/data/media/tv"));
    }

    [Test]
    public async Task HandleRpc_CoreGetConfig_Reflects_AlternativeSpeedMode_When_True()
    {
        _configService.AlternativeSpeedEnabled.Returns(true);
        _configService.AltDownloadSpeedKbps.Returns(100);
        _configService.AltUploadSpeedKbps.Returns(50);

        var json = "{\"method\": \"core.get_config\", \"params\": [], \"id\": 600}";
        using var doc = JsonDocument.Parse(json);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());
        var jsonResult = (JsonResult)result;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        var resObj = resDoc.RootElement.GetProperty("result");
        Assert.That(resObj.GetProperty("max_download_speed").GetDouble(), Is.EqualTo(100.0));
        Assert.That(resObj.GetProperty("max_upload_speed").GetDouble(), Is.EqualTo(50.0));
        Assert.That(resObj.GetProperty("alt_speed_enabled").GetBoolean(), Is.True);
    }

    [Test]
    public async Task HandleRpc_WebUpdateUi_Reflects_AlternativeSpeedMode_When_True()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());
        _configService.AlternativeSpeedEnabled.Returns(true);
        _configService.AltDownloadSpeedKbps.Returns(100);
        _configService.AltUploadSpeedKbps.Returns(50);

        var json = "{\"method\": \"web.update_ui\", \"params\": [[], {}], \"id\": 601}";
        using var doc = JsonDocument.Parse(json);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());
        var jsonResult = (JsonResult)result;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        var stats = resDoc.RootElement.GetProperty("result").GetProperty("stats");
        Assert.That(stats.GetProperty("max_download").GetDouble(), Is.EqualTo(100.0));
        Assert.That(stats.GetProperty("max_upload").GetDouble(), Is.EqualTo(50.0));
    }

    [Test]
    public async Task HandleRpc_CoreSetConfig_Updates_AltSpeeds_When_AlternativeSpeedMode_Active()
    {
        _configService.AlternativeSpeedEnabled.Returns(true);

        var json = "{\"method\": \"core.set_config\", \"params\": [{\"max_download_speed\": 150.0, \"alt_speed_enabled\": false}], \"id\": 602}";
        using var doc = JsonDocument.Parse(json);

        var result = await _controller.HandleRpc(doc.RootElement);
        Assert.That(result, Is.InstanceOf<JsonResult>());

        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            d.ContainsKey("AltDownloadSpeedKbps") && (int)d["AltDownloadSpeedKbps"] == 150 &&
            d.ContainsKey("AlternativeSpeedEnabled") && (bool)d["AlternativeSpeedEnabled"] == false));
    }

    [Test]
    public async Task HandleRpc_CoreSetTorrentFilePriorities_PersistsPriorities_And_UpdatesWantedFlags()
    {
        var hash = "aabbccddeeff00112233445566778899aabbccdd";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = hash,
            Name = "Test Torrent",
        };

        var file1 = new TorrentFile
        {
            Id = 10,
            TorrentId = 1,
            Path = "file1.mkv",
            Size = 1000L,
            Priority = 1,
            Wanted = true,
        };

        var file2 = new TorrentFile
        {
            Id = 11,
            TorrentId = 1,
            Path = "file2.nfo",
            Size = 200L,
            Priority = 1,
            Wanted = true,
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file1, file2 });

        var json = $"{{\"method\": \"core.set_torrent_file_priorities\", \"params\": [\"{hash}\", [0, 5]], \"id\": 701}}";
        using var doc = JsonDocument.Parse(json);

        var actionResult = await _controller.HandleRpc(doc.RootElement);
        Assert.That(actionResult, Is.InstanceOf<JsonResult>());
        var jsonResult = (JsonResult)actionResult;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        Assert.That(resDoc.RootElement.GetProperty("result").GetBoolean(), Is.True);

        Assert.That(file1.Priority, Is.EqualTo(0));
        Assert.That(file1.Wanted, Is.False);
        Assert.That(file2.Priority, Is.EqualTo(5));
        Assert.That(file2.Wanted, Is.True);

        _torrentFileService.Received(1).Update(Arg.Is<TorrentFile>(f => f.Id == 10 && f.Priority == 0 && !f.Wanted));
        _torrentFileService.Received(1).Update(Arg.Is<TorrentFile>(f => f.Id == 11 && f.Priority == 5 && f.Wanted));
    }

    [Test]
    public async Task HandleRpc_CoreGetTorrentStatus_WithProjectedFields_ReturnsAllProjectedFields()
    {
        var hash = "aabbccddeeff00112233445566778899aabbccdd";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = hash,
            Name = "Multi File Torrent",
            TotalSize = 1200L,
            Progress = 0.5,
            TrackerUrl = "http://fallback-tracker.com/announce",
        };

        var file1 = new TorrentFile
        {
            Id = 10,
            TorrentId = 1,
            Path = "folder/movie.mkv",
            Size = 1000L,
            BytesCompleted = 500L,
            Priority = 1,
            ByteOffset = 0L,
        };

        var file2 = new TorrentFile
        {
            Id = 11,
            TorrentId = 1,
            Path = "folder/sample.mkv",
            Size = 200L,
            BytesCompleted = 200L,
            Priority = 4,
            ByteOffset = 1000L,
        };

        var tracker1 = new TrackerEntry
        {
            Id = 101,
            TorrentId = 1,
            Tier = 0,
            Url = "http://tracker1.com/announce",
        };

        var tracker2 = new TrackerEntry
        {
            Id = 102,
            TorrentId = 1,
            Tier = 1,
            Url = "http://tracker2.com/announce",
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file1, file2 });
        _trackerService.GetByTorrentId(1).Returns(new List<TrackerEntry> { tracker1, tracker2 });

        var json = $"{{\"method\": \"core.get_torrent_status\", \"params\": [\"{hash}\", [\"files\", \"file_progress\", \"file_priorities\", \"num_files\", \"trackers\"]], \"id\": 702}}";
        using var doc = JsonDocument.Parse(json);

        var actionResult = await _controller.HandleRpc(doc.RootElement);
        Assert.That(actionResult, Is.InstanceOf<JsonResult>());
        var jsonResult = (JsonResult)actionResult;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        var result = resDoc.RootElement.GetProperty("result");

        Assert.That(result.GetProperty("num_files").GetInt32(), Is.EqualTo(2));

        var filesElem = result.GetProperty("files");
        Assert.That(filesElem.GetArrayLength(), Is.EqualTo(2));

        var f0 = filesElem[0];
        Assert.That(f0.GetProperty("index").GetInt32(), Is.EqualTo(0));
        Assert.That(f0.GetProperty("path").GetString(), Is.EqualTo("folder/movie.mkv"));
        Assert.That(f0.GetProperty("size").GetInt64(), Is.EqualTo(1000L));
        Assert.That(f0.GetProperty("offset").GetInt64(), Is.EqualTo(0L));

        var f1 = filesElem[1];
        Assert.That(f1.GetProperty("index").GetInt32(), Is.EqualTo(1));
        Assert.That(f1.GetProperty("path").GetString(), Is.EqualTo("folder/sample.mkv"));
        Assert.That(f1.GetProperty("size").GetInt64(), Is.EqualTo(200L));
        Assert.That(f1.GetProperty("offset").GetInt64(), Is.EqualTo(1000L));

        var progressElem = result.GetProperty("file_progress");
        Assert.That(progressElem.GetArrayLength(), Is.EqualTo(2));
        Assert.That(progressElem[0].GetDouble(), Is.EqualTo(0.5).Within(0.001));
        Assert.That(progressElem[1].GetDouble(), Is.EqualTo(1.0).Within(0.001));

        var prioritiesElem = result.GetProperty("file_priorities");
        Assert.That(prioritiesElem.GetArrayLength(), Is.EqualTo(2));
        Assert.That(prioritiesElem[0].GetInt32(), Is.EqualTo(1));
        Assert.That(prioritiesElem[1].GetInt32(), Is.EqualTo(4));

        var trackersElem = result.GetProperty("trackers");
        Assert.That(trackersElem.GetArrayLength(), Is.EqualTo(2));
        Assert.That(trackersElem[0].GetProperty("tier").GetInt32(), Is.EqualTo(0));
        Assert.That(trackersElem[0].GetProperty("url").GetString(), Is.EqualTo("http://tracker1.com/announce"));
        Assert.That(trackersElem[1].GetProperty("tier").GetInt32(), Is.EqualTo(1));
        Assert.That(trackersElem[1].GetProperty("url").GetString(), Is.EqualTo("http://tracker2.com/announce"));
    }

    [Test]
    public async Task HandleRpc_ErrorResponse_NonExistentTorrent_ReturnsStructuredErrorObject()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());

        var json = "{\"method\": \"core.get_torrent_status\", \"params\": [\"nonexistenthash0000000000000000000000\", [\"name\"]], \"id\": 801}";
        using var doc = JsonDocument.Parse(json);

        var actionResult = await _controller.HandleRpc(doc.RootElement);
        var jsonResult = (JsonResult)actionResult;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        var root = resDoc.RootElement;

        Assert.That(root.GetProperty("result").ValueKind, Is.EqualTo(JsonValueKind.Null));
        var error = root.GetProperty("error");
        Assert.That(error.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(error.GetProperty("message").GetString(), Is.EqualTo("Torrent not found"));
        Assert.That(error.GetProperty("code").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task HandleRpc_ErrorResponse_InvalidArguments_ReturnsStructuredErrorObject()
    {
        var json = "{\"method\": \"core.add_torrent_file\", \"params\": [], \"id\": 802}";
        using var doc = JsonDocument.Parse(json);

        var actionResult = await _controller.HandleRpc(doc.RootElement);
        var jsonResult = (JsonResult)actionResult;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        var root = resDoc.RootElement;

        Assert.That(root.GetProperty("result").ValueKind, Is.EqualTo(JsonValueKind.Null));
        var error = root.GetProperty("error");
        Assert.That(error.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(error.GetProperty("message").GetString(), Is.EqualTo("Invalid arguments for add_torrent_file"));
        Assert.That(error.GetProperty("code").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task HandleRpc_ErrorResponse_UnknownMethod_ReturnsStructuredErrorObject()
    {
        var json = "{\"method\": \"unknown.some_method\", \"params\": [], \"id\": 803}";
        using var doc = JsonDocument.Parse(json);

        var actionResult = await _controller.HandleRpc(doc.RootElement);
        var jsonResult = (JsonResult)actionResult;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        var root = resDoc.RootElement;

        Assert.That(root.GetProperty("result").ValueKind, Is.EqualTo(JsonValueKind.Null));
        var error = root.GetProperty("error");
        Assert.That(error.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(error.GetProperty("message").GetString(), Does.Contain("not implemented"));
        Assert.That(error.GetProperty("code").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task HandleRpc_AuthLogin_WhenAuthEnabled_AcceptsValidApiKey()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("valid-api-key-xyz");

        var json = "{\"method\": \"auth.login\", \"params\": [\"valid-api-key-xyz\"], \"id\": 804}";
        using var doc = JsonDocument.Parse(json);

        var actionResult = await _controller.HandleRpc(doc.RootElement);
        var jsonResult = (JsonResult)actionResult;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        var root = resDoc.RootElement;

        Assert.That(root.GetProperty("result").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("error").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task HandleRpc_AuthLogin_WhenAuthEnabled_AcceptsValidUserPassword()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("different-api-key");
        _configService.GetValue("Password", Arg.Any<string>()).Returns("user-secret-password");
        _configService.GetValue("Password").Returns("user-secret-password");

        var json = "{\"method\": \"auth.login\", \"params\": [\"user-secret-password\"], \"id\": 805}";
        using var doc = JsonDocument.Parse(json);

        var actionResult = await _controller.HandleRpc(doc.RootElement);
        var jsonResult = (JsonResult)actionResult;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        var root = resDoc.RootElement;

        Assert.That(root.GetProperty("result").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("error").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task HandleRpc_AuthLogin_WhenAuthEnabled_RejectsInvalidCredentials()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("valid-api-key");
        _configService.GetValue("Password", Arg.Any<string>()).Returns("valid-user-password");
        _configService.GetValue("Password").Returns("valid-user-password");

        var json = "{\"method\": \"auth.login\", \"params\": [\"wrong-credential\"], \"id\": 806}";
        using var doc = JsonDocument.Parse(json);

        var actionResult = await _controller.HandleRpc(doc.RootElement);
        var jsonResult = (JsonResult)actionResult;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        var root = resDoc.RootElement;

        Assert.That(root.GetProperty("result").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("error").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }
}
