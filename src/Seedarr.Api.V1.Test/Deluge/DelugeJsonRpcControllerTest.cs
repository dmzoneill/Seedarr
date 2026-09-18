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
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using Seedarr.Api.V1.Deluge;

namespace Seedarr.Api.V1.Test.Deluge;

[TestFixture]
public class DelugeJsonRpcControllerTest
{
    private ITorrentService _torrentService;
    private ITorrentFileService _torrentFileService;
    private ITorrentFileParser _torrentFileParser;
    private ITorrentImportService _torrentImportService;
    private ITrackerEntryService _trackerService;
    private IConfigService _configService;
    private ITagService _tagService;
    private IConfigFileProvider _configFileProvider;
    private ICategoryService _categoryService;
    private IDiskSpaceService _diskSpaceService;
    private DelugeJsonRpcController _controller;

    [SetUp]
    public void SetUp()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _torrentFileParser = Substitute.For<ITorrentFileParser>();
        _torrentImportService = Substitute.For<ITorrentImportService>();
        _trackerService = Substitute.For<ITrackerEntryService>();
        _configService = Substitute.For<IConfigService>();
        _tagService = Substitute.For<ITagService>();
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _categoryService = Substitute.For<ICategoryService>();
        _diskSpaceService = Substitute.For<IDiskSpaceService>();

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
            trackerService: _trackerService,
            diskSpaceService: _diskSpaceService);

        var httpContext = new DefaultHttpContext();
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };
    }

    [Test]
    public async Task CoreSetTorrentFilePriorities_PersistsPriorities_And_UpdatesWantedFlags()
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

        var json = $"{{\"method\": \"core.set_torrent_file_priorities\", \"params\": [\"{hash}\", [0, 5]], \"id\": 1}}";
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
    public async Task CoreGetTorrentStatus_WithProjectedFields_ReturnsAllProjectedFields()
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

        var json = $"{{\"method\": \"core.get_torrent_status\", \"params\": [\"{hash}\", [\"files\", \"file_progress\", \"file_priorities\", \"num_files\", \"trackers\"]], \"id\": 2}}";
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
    public async Task CoreGetTorrentStatus_FallbackTracker_WhenNoTrackerEntries()
    {
        var hash = "aabbccddeeff00112233445566778899aabbccdd";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = hash,
            Name = "Fallback Tracker Torrent",
            TotalSize = 500L,
            TrackerUrl = "http://fallback.tracker.org/announce",
        };

        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile>());
        _trackerService.GetByTorrentId(1).Returns(new List<TrackerEntry>());

        var json = $"{{\"method\": \"core.get_torrent_status\", \"params\": [\"{hash}\", [\"trackers\", \"num_files\"]], \"id\": 3}}";
        using var doc = JsonDocument.Parse(json);

        var actionResult = await _controller.HandleRpc(doc.RootElement);
        var jsonResult = (JsonResult)actionResult;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        var result = resDoc.RootElement.GetProperty("result");

        Assert.That(result.GetProperty("num_files").GetInt32(), Is.EqualTo(1));

        var trackersElem = result.GetProperty("trackers");
        Assert.That(trackersElem.GetArrayLength(), Is.EqualTo(1));
        Assert.That(trackersElem[0].GetProperty("tier").GetInt32(), Is.EqualTo(0));
        Assert.That(trackersElem[0].GetProperty("url").GetString(), Is.EqualTo("http://fallback.tracker.org/announce"));
    }

    [Test]
    public async Task ErrorResponse_NonExistentTorrent_ReturnsStructuredErrorObject()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());

        var json = "{\"method\": \"core.get_torrent_status\", \"params\": [\"nonexistenthash0000000000000000000000\", [\"name\"]], \"id\": 10}";
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
    public async Task ErrorResponse_InvalidArguments_ReturnsStructuredErrorObject()
    {
        var json = "{\"method\": \"core.add_torrent_file\", \"params\": [], \"id\": 11}";
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
    public async Task ErrorResponse_UnknownMethod_ReturnsStructuredErrorObject()
    {
        var json = "{\"method\": \"unknown.some_method\", \"params\": [], \"id\": 12}";
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
    public async Task AuthLogin_WhenAuthEnabled_AcceptsValidApiKey()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("valid-api-key-xyz");

        var json = "{\"method\": \"auth.login\", \"params\": [\"valid-api-key-xyz\"], \"id\": 20}";
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
    public async Task AuthLogin_WhenAuthEnabled_AcceptsValidUserPassword()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("different-api-key");
        _configService.GetValue("Password", Arg.Any<string>()).Returns("user-secret-password");
        _configService.GetValue("Password").Returns("user-secret-password");

        var json = "{\"method\": \"auth.login\", \"params\": [\"user-secret-password\"], \"id\": 21}";
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
    public async Task AuthLogin_WhenAuthEnabled_RejectsInvalidCredentials()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("valid-api-key");
        _configService.GetValue("Password", Arg.Any<string>()).Returns("valid-user-password");
        _configService.GetValue("Password").Returns("valid-user-password");

        var json = "{\"method\": \"auth.login\", \"params\": [\"wrong-credential\"], \"id\": 22}";
        using var doc = JsonDocument.Parse(json);

        var actionResult = await _controller.HandleRpc(doc.RootElement);
        var jsonResult = (JsonResult)actionResult;

        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        var root = resDoc.RootElement;

        Assert.That(root.GetProperty("result").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("error").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task CoreGetFreeSpace_WithExplicitPath_ResolvesFromDiskSpaceService()
    {
        var targetPath = "/media/downloads";
        _diskSpaceService.GetDiskSpaceForPath(targetPath).Returns(new DiskSpaceInfo
        {
            Path = targetPath,
            FreeSpace = 500L * 1024 * 1024 * 1024,
            TotalSpace = 1000L * 1024 * 1024 * 1024,
        });

        var json = $"{{\"method\": \"core.get_free_space\", \"params\": [\"{targetPath}\"], \"id\": 30}}";
        using var doc = JsonDocument.Parse(json);

        var actionResult = await _controller.HandleRpc(doc.RootElement);
        var jsonResult = (JsonResult)actionResult;
        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);

        Assert.That(resDoc.RootElement.GetProperty("result").GetInt64(), Is.EqualTo(500L * 1024 * 1024 * 1024));
    }

    [Test]
    public async Task CoreGetFreeSpace_WithoutPath_ResolvesFromDefaultPath()
    {
        _configService.DefaultSavePath.Returns("/default/save/path");
        _diskSpaceService.GetDiskSpaceForPath("/default/save/path").Returns(new DiskSpaceInfo
        {
            Path = "/default/save/path",
            FreeSpace = 250L * 1024 * 1024 * 1024,
            TotalSpace = 500L * 1024 * 1024 * 1024,
        });

        var json = "{\"method\": \"core.get_free_space\", \"params\": [], \"id\": 31}";
        using var doc = JsonDocument.Parse(json);

        var actionResult = await _controller.HandleRpc(doc.RootElement);
        var jsonResult = (JsonResult)actionResult;
        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);

        Assert.That(resDoc.RootElement.GetProperty("result").GetInt64(), Is.EqualTo(250L * 1024 * 1024 * 1024));
    }

    [Test]
    public async Task WebUpdateUi_ReturnsDynamicFreeSpaceFromDiskSpaceService()
    {
        _configService.DefaultSavePath.Returns("/downloads");
        _diskSpaceService.GetDiskSpaceForPath("/downloads").Returns(new DiskSpaceInfo
        {
            Path = "/downloads",
            FreeSpace = 420L * 1024 * 1024 * 1024,
            TotalSpace = 1000L * 1024 * 1024 * 1024,
        });

        var json = "{\"method\": \"web.update_ui\", \"params\": [[], {}], \"id\": 32}";
        using var doc = JsonDocument.Parse(json);

        var actionResult = await _controller.HandleRpc(doc.RootElement);
        var jsonResult = (JsonResult)actionResult;
        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        var stats = resDoc.RootElement.GetProperty("result").GetProperty("stats");

        Assert.That(stats.GetProperty("free_space").GetInt64(), Is.EqualTo(420L * 1024 * 1024 * 1024));
    }

    [Test]
    public async Task CoreAddTorrentMagnet_AppliesSavePath_AndDelugeOptions()
    {
        var magnet = "magnet:?xt=urn:btih:aabbccddeeff00112233445566778899aabbccdd&dn=Test";
        var torrent = new Torrent
        {
            Id = 5,
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            Name = "Test",
        };
        _torrentImportService.ImportFromMagnet(magnet).Returns(torrent);

        var json = "{\"method\": \"core.add_torrent_magnet\", \"params\": [\"" + magnet + "\", {\"save_path\": \"/custom/save/path\", \"prioritize_first_last_pieces\": true, \"sequential_download\": true, \"add_paused\": true}], \"id\": 33}";
        using var doc = JsonDocument.Parse(json);

        var actionResult = await _controller.HandleRpc(doc.RootElement);
        var jsonResult = (JsonResult)actionResult;
        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);

        Assert.That(resDoc.RootElement.GetProperty("result").GetString(), Is.EqualTo("aabbccddeeff00112233445566778899aabbccdd"));
        Assert.That(torrent.SavePath, Is.EqualTo("/custom/save/path"));
        Assert.That(torrent.SourcePath, Is.EqualTo("/custom/save/path"));
        Assert.That(torrent.FirstLastPiecePrio, Is.True);
        Assert.That(torrent.SequentialDownload, Is.True);
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Paused));
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public async Task WebGetTorrentInfo_WithExistingFilePath_ParsesFromDisk()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempFile, new byte[] { 1, 2, 3, 4 });
            _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(new ParsedTorrent
            {
                Name = "DiskTorrent",
                TotalSize = 12345L,
                Files = new List<ParsedTorrentFile>
                {
                    new ParsedTorrentFile { Path = "sub/file1.mkv", Size = 12345L },
                },
            });

            var escapedPath = tempFile.Replace("\\", "\\\\");
            var json = $"{{\"method\": \"web.get_torrent_info\", \"params\": [\"{escapedPath}\"], \"id\": 34}}";
            using var doc = JsonDocument.Parse(json);

            var actionResult = await _controller.HandleRpc(doc.RootElement);
            var jsonResult = (JsonResult)actionResult;
            var serialized = JsonSerializer.Serialize(jsonResult.Value);
            using var resDoc = JsonDocument.Parse(serialized);
            var result = resDoc.RootElement.GetProperty("result");

            Assert.That(result.GetProperty("name").GetString(), Is.EqualTo("DiskTorrent"));
            Assert.That(result.GetProperty("size").GetInt64(), Is.EqualTo(12345L));
            var filesTree = result.GetProperty("files_tree");
            Assert.That(filesTree.TryGetProperty("sub", out var subTree), Is.True);
            Assert.That(subTree.TryGetProperty("file1.mkv", out var fileProp), Is.True);
            Assert.That(fileProp.GetArrayLength(), Is.EqualTo(3));
            Assert.That(fileProp[0].GetInt32(), Is.EqualTo(0));
            Assert.That(fileProp[1].GetInt64(), Is.EqualTo(12345L));
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
    public async Task CoreSetConfig_SavesQueueLimits()
    {
        var json = "{\"method\": \"core.set_config\", \"params\": [{\"max_active_limit\": 20, \"max_active_downloading\": 8, \"max_active_seeding\": 12}], \"id\": 35}";
        using var doc = JsonDocument.Parse(json);

        var actionResult = await _controller.HandleRpc(doc.RootElement);
        var jsonResult = (JsonResult)actionResult;
        var serialized = JsonSerializer.Serialize(jsonResult.Value);
        using var resDoc = JsonDocument.Parse(serialized);

        Assert.That(resDoc.RootElement.GetProperty("result").GetBoolean(), Is.True);
        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            d.ContainsKey("MaxActiveLimit") && (int)d["MaxActiveLimit"] == 20 &&
            d.ContainsKey("MaxActiveDownloads") && (int)d["MaxActiveDownloads"] == 8 &&
            d.ContainsKey("MaxActiveUploads") && (int)d["MaxActiveUploads"] == 12));
    }
}
