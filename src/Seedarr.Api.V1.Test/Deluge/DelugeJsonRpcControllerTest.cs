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
}
