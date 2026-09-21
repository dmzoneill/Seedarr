using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Mcp;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Test.Mcp;

[TestFixture]
public class McpServiceTest
{
    private ITorrentService _torrentService;
    private ITorrentFileService _torrentFileService;
    private ITrackerEntryService _trackerEntryService;
    private ITorrentImportService _torrentImportService;
    private ISeedingService _seedingService;
    private ISpeedHistoryService _speedHistoryService;
    private ITagService _tagService;
    private IHealthCheckService _healthCheckService;
    private IDiskSpaceService _diskSpaceService;
    private ITrackerAnnounceService _trackerAnnounceService;
    private ITrackerScrapeService _trackerScrapeService;

    private McpService _service;

    [SetUp]
    public void SetUp()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _torrentImportService = Substitute.For<ITorrentImportService>();
        _seedingService = Substitute.For<ISeedingService>();
        _speedHistoryService = Substitute.For<ISpeedHistoryService>();
        _tagService = Substitute.For<ITagService>();
        _healthCheckService = Substitute.For<IHealthCheckService>();
        _diskSpaceService = Substitute.For<IDiskSpaceService>();
        _trackerAnnounceService = Substitute.For<ITrackerAnnounceService>();
        _trackerScrapeService = Substitute.For<ITrackerScrapeService>();

        _service = new McpService(
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentImportService,
            _seedingService,
            _speedHistoryService,
            _tagService,
            _healthCheckService,
            _diskSpaceService,
            _trackerAnnounceService,
            _trackerScrapeService);
    }

    [Test]
    public async Task ProcessMessageAsync_should_handle_initialize()
    {
        var request = new JsonRpcRequest
        {
            Id = 1,
            Method = "initialize"
        };

        var response = await _service.ProcessMessageAsync(request);
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Error, Is.Null);

        var result = response.Result as McpInitializeResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result.ProtocolVersion, Is.EqualTo("2024-11-05"));
        Assert.That(result.ServerInfo.Name, Does.Contain("Seedarr MCP Server"));
    }

    [Test]
    public async Task ProcessMessageAsync_should_handle_ping()
    {
        var request = new JsonRpcRequest
        {
            Id = 2,
            Method = "ping"
        };

        var response = await _service.ProcessMessageAsync(request);
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Error, Is.Null);
    }

    [Test]
    public async Task ProcessMessageAsync_should_return_method_not_found_for_unknown_method()
    {
        var request = new JsonRpcRequest
        {
            Id = 3,
            Method = "unknown/nonexistent"
        };

        var response = await _service.ProcessMessageAsync(request);
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Error, Is.Not.Null);
        Assert.That(response.Error.Code, Is.EqualTo(-32601));
    }

    [Test]
    public async Task ProcessMessageAsync_should_list_all_seven_tools()
    {
        var request = new JsonRpcRequest
        {
            Id = 4,
            Method = "tools/list"
        };

        var response = await _service.ProcessMessageAsync(request);
        var result = response.Result as McpToolListResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Tools.Count, Is.EqualTo(7));

        var toolNames = result.Tools.ConvertAll(t => t.Name);
        Assert.That(toolNames, Does.Contain("list_torrents"));
        Assert.That(toolNames, Does.Contain("get_torrent_details"));
        Assert.That(toolNames, Does.Contain("pause_torrent"));
        Assert.That(toolNames, Does.Contain("resume_torrent"));
        Assert.That(toolNames, Does.Contain("add_torrent"));
        Assert.That(toolNames, Does.Contain("boost_trackers"));
        Assert.That(toolNames, Does.Contain("get_seeding_metrics"));
    }

    [Test]
    public async Task ProcessMessageAsync_tools_call_list_torrents_with_filters()
    {
        var torrents = new List<Torrent>
        {
            new() { Id = 1, Name = "Ubuntu Linux", Status = TorrentStatus.Seeding, Category = "Linux", Ratio = 2.5 },
            new() { Id = 2, Name = "Debian Linux", Status = TorrentStatus.Paused, Category = "Linux", Ratio = 0.8 },
            new() { Id = 3, Name = "Fedora Linux", Status = TorrentStatus.Seeding, Category = "OS", Ratio = 1.2 }
        };
        _torrentService.GetAll().Returns(torrents);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":10,\"method\":\"tools/call\",\"params\":{\"name\":\"list_torrents\",\"arguments\":{\"status\":\"Seeding\",\"category\":\"Linux\",\"min_ratio\":1.0}}}";

        var response = await _service.ProcessMessageJsonAsync(requestJson);
        Assert.That(response.Error, Is.Null);

        var toolResult = response.Result as McpToolCallResult;
        Assert.That(toolResult, Is.Not.Null);
        Assert.That(toolResult.IsError, Is.False);
        Assert.That(toolResult.Content[0].Text, Does.Contain("Ubuntu Linux"));
        Assert.That(toolResult.Content[0].Text, Does.Not.Contain("Debian Linux"));
        Assert.That(toolResult.Content[0].Text, Does.Not.Contain("Fedora Linux"));
    }

    [Test]
    public async Task ProcessMessageAsync_tools_call_get_torrent_details()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Arch Linux",
            InfoHash = "abc123def456",
            Status = TorrentStatus.Seeding,
            TotalSize = 1024 * 1024 * 100,
            Ratio = 3.14
        };
        _torrentService.Get(42).Returns(torrent);

        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 42, Path = "archlinux.iso", Size = 1024 * 1024 * 100 }
        };
        _torrentFileService.GetByTorrentId(42).Returns(files);

        var trackers = new List<TrackerEntry>
        {
            new() { Id = 1, TorrentId = 42, Url = "https://tracker.archlinux.org/announce", Status = TrackerStatus.Working }
        };
        _trackerEntryService.GetByTorrentId(42).Returns(trackers);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":11,\"method\":\"tools/call\",\"params\":{\"name\":\"get_torrent_details\",\"arguments\":{\"id\":42}}}";

        var response = await _service.ProcessMessageJsonAsync(requestJson);
        Assert.That(response.Error, Is.Null);

        var toolResult = response.Result as McpToolCallResult;
        Assert.That(toolResult.IsError, Is.False);
        Assert.That(toolResult.Content[0].Text, Does.Contain("Arch Linux"));
        Assert.That(toolResult.Content[0].Text, Does.Contain("archlinux.iso"));
        Assert.That(toolResult.Content[0].Text, Does.Contain("tracker.archlinux.org"));
    }

    [Test]
    public async Task ProcessMessageAsync_tools_call_pause_and_resume_torrent()
    {
        var torrent = new Torrent { Id = 7, Name = "Alpine Linux" };
        _torrentService.Get(7).Returns(torrent);

        var pauseJson = "{\"jsonrpc\":\"2.0\",\"id\":12,\"method\":\"tools/call\",\"params\":{\"name\":\"pause_torrent\",\"arguments\":{\"id\":7}}}";
        var pauseResp = await _service.ProcessMessageJsonAsync(pauseJson);
        _torrentService.Received(1).Pause(7);
        Assert.That((pauseResp.Result as McpToolCallResult)?.Content[0].Text, Does.Contain("paused successfully"));

        var resumeJson = "{\"jsonrpc\":\"2.0\",\"id\":13,\"method\":\"tools/call\",\"params\":{\"name\":\"resume_torrent\",\"arguments\":{\"id\":7}}}";
        var resumeResp = await _service.ProcessMessageJsonAsync(resumeJson);
        _torrentService.Received(1).Start(7);
        Assert.That((resumeResp.Result as McpToolCallResult)?.Content[0].Text, Does.Contain("resumed successfully"));
    }

    [Test]
    public async Task ProcessMessageAsync_tools_call_add_torrent_magnet()
    {
        var added = new Torrent { Id = 15, Name = "FreeBSD", InfoHash = "f00baa" };
        _torrentImportService.ImportFromMagnet("magnet:?xt=urn:btih:f00baa").Returns(added);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":14,\"method\":\"tools/call\",\"params\":{\"name\":\"add_torrent\",\"arguments\":{\"magnet\":\"magnet:?xt=urn:btih:f00baa\"}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        Assert.That((response.Result as McpToolCallResult)?.Content[0].Text, Does.Contain("Added torrent 'FreeBSD'"));
    }

    [Test]
    public async Task ProcessMessageAsync_tools_call_get_seeding_metrics()
    {
        _seedingService.GetStats().Returns(new SeedingStats
        {
            ActiveTorrents = 5,
            TotalUploaded = 5000000,
            TotalDownloaded = 1000000,
            AverageRatio = 5.0
        });

        _speedHistoryService.GetHistory().Returns(new List<SpeedSnapshot>
        {
            new() { UploadSpeed = 250000, DownloadSpeed = 10000 }
        });

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":15,\"method\":\"tools/call\",\"params\":{\"name\":\"get_seeding_metrics\",\"arguments\":{}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var text = (response.Result as McpToolCallResult)?.Content[0].Text;
        Assert.That(text, Does.Contain("\"activeTorrents\":5"));
        Assert.That(text, Does.Contain("\"uploadSpeed\":250000"));
    }

    [Test]
    public async Task ProcessMessageAsync_resources_list_and_read()
    {
        var listJson = "{\"jsonrpc\":\"2.0\",\"id\":20,\"method\":\"resources/list\"}";
        var listResp = await _service.ProcessMessageJsonAsync(listJson);
        var listResult = listResp.Result as McpResourceListResult;
        Assert.That(listResult.Resources.Count, Is.EqualTo(3));

        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { Id = 1, Name = "Test Torrent", Status = TorrentStatus.Seeding }
        });

        var readJson = "{\"jsonrpc\":\"2.0\",\"id\":21,\"method\":\"resources/read\",\"params\":{\"uri\":\"seedarr://torrents\"}}";
        var readResp = await _service.ProcessMessageJsonAsync(readJson);
        var readResult = readResp.Result as McpResourceReadResult;
        Assert.That(readResult.Contents[0].Text, Does.Contain("Test Torrent"));

        var statusJson = "{\"jsonrpc\":\"2.0\",\"id\":22,\"method\":\"resources/read\",\"params\":{\"uri\":\"seedarr://system/status\"}}";
        var statusResp = await _service.ProcessMessageJsonAsync(statusJson);
        var statusResult = statusResp.Result as McpResourceReadResult;
        Assert.That(statusResult.Contents[0].Text, Does.Contain("health"));
    }

    [Test]
    public async Task ProcessMessageAsync_prompts_list_and_get()
    {
        var listJson = "{\"jsonrpc\":\"2.0\",\"id\":30,\"method\":\"prompts/list\"}";
        var listResp = await _service.ProcessMessageJsonAsync(listJson);
        var listResult = listResp.Result as McpPromptListResult;
        Assert.That(listResult.Prompts.Count, Is.EqualTo(3));

        var getJson = "{\"jsonrpc\":\"2.0\",\"id\":31,\"method\":\"prompts/get\",\"params\":{\"name\":\"swarm_diagnosis\",\"arguments\":{\"torrent_id\":\"42\"}}}";
        var getResp = await _service.ProcessMessageJsonAsync(getJson);
        var getResult = getResp.Result as McpPromptGetResult;
        Assert.That(getResult.Messages[0].Content.Text, Does.Contain("torrent '42'"));
    }
}
