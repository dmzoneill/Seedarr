using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
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

namespace NzbDrone.Core.Test.McpTests;

[TestFixture]
public class McpServerAndToolsTests
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
    public async Task ToolsList_should_return_all_seven_registered_tools()
    {
        var request = new JsonRpcRequest
        {
            Id = 1,
            Method = "tools/list"
        };

        var response = await _service.ProcessMessageAsync(request);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Error, Is.Null);

        var result = response.Result as McpToolListResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Tools.Count, Is.EqualTo(7));

        var names = result.Tools.ConvertAll(t => t.Name);
        Assert.That(names, Does.Contain("list_torrents"));
        Assert.That(names, Does.Contain("get_torrent_details"));
        Assert.That(names, Does.Contain("pause_torrent"));
        Assert.That(names, Does.Contain("resume_torrent"));
        Assert.That(names, Does.Contain("add_torrent"));
        Assert.That(names, Does.Contain("boost_trackers"));
        Assert.That(names, Does.Contain("get_seeding_metrics"));
    }

    [Test]
    public async Task ToolsList_should_include_valid_schemas_and_descriptions()
    {
        var request = new JsonRpcRequest
        {
            Id = 2,
            Method = "tools/list"
        };

        var response = await _service.ProcessMessageAsync(request);
        var result = response.Result as McpToolListResult;

        Assert.That(result, Is.Not.Null);
        foreach (var tool in result.Tools)
        {
            Assert.That(tool.Name, Is.Not.Null.And.Not.Empty);
            Assert.That(tool.Description, Is.Not.Null.And.Not.Empty);
            Assert.That(tool.InputSchema, Is.Not.Null);
        }
    }

    [Test]
    public async Task Initialize_should_return_expected_protocol_version_and_capabilities()
    {
        var request = new JsonRpcRequest
        {
            Id = "init-1",
            Method = "initialize"
        };

        var response = await _service.ProcessMessageAsync(request);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Error, Is.Null);

        var initResult = response.Result as McpInitializeResult;
        Assert.That(initResult, Is.Not.Null);
        Assert.That(initResult.ProtocolVersion, Is.EqualTo("2024-11-05"));
        Assert.That(initResult.Capabilities, Is.Not.Null);
        Assert.That(initResult.Capabilities.Tools, Is.Not.Null);
        Assert.That(initResult.Capabilities.Resources, Is.Not.Null);
        Assert.That(initResult.Capabilities.Prompts, Is.Not.Null);
        Assert.That(initResult.ServerInfo, Is.Not.Null);
        Assert.That(initResult.ServerInfo.Name, Is.EqualTo("Seedarr MCP Server"));
        Assert.That(initResult.ServerInfo.Version, Is.EqualTo("1.0.0"));
    }

    [Test]
    public async Task ProcessMessageAsync_with_null_request_should_return_invalid_request_error()
    {
        var response = await _service.ProcessMessageAsync(null);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Error, Is.Not.Null);
        Assert.That(response.Error.Code, Is.EqualTo(-32600));
        Assert.That(response.Error.Message, Does.Contain("Invalid Request"));
    }

    [Test]
    public async Task ProcessMessageAsync_with_unknown_method_should_return_method_not_found_error()
    {
        var request = new JsonRpcRequest
        {
            Id = 99,
            Method = "custom/unknown_action"
        };

        var response = await _service.ProcessMessageAsync(request);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Error, Is.Not.Null);
        Assert.That(response.Error.Code, Is.EqualTo(-32601));
        Assert.That(response.Error.Message, Does.Contain("Method 'custom/unknown_action' not found."));
    }

    [Test]
    public async Task ProcessMessageAsync_with_ping_should_return_success_response()
    {
        var request = new JsonRpcRequest
        {
            Id = "ping-123",
            Method = "ping"
        };

        var response = await _service.ProcessMessageAsync(request);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Id, Is.EqualTo("ping-123"));
        Assert.That(response.Error, Is.Null);
        Assert.That(response.Result, Is.Not.Null);
    }

    [Test]
    public async Task ProcessMessageAsync_with_notifications_initialized_should_return_null()
    {
        var request = new JsonRpcRequest
        {
            Method = "notifications/initialized"
        };

        var response = await _service.ProcessMessageAsync(request);

        Assert.That(response, Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task ProcessMessageJsonAsync_with_empty_or_whitespace_json_should_return_parse_error(string json)
    {
        var response = await _service.ProcessMessageJsonAsync(json);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Error, Is.Not.Null);
        Assert.That(response.Error.Code, Is.EqualTo(-32700));
        Assert.That(response.Error.Message, Does.Contain("Parse error"));
    }

    [Test]
    public async Task ProcessMessageJsonAsync_with_malformed_json_syntax_should_return_parse_error()
    {
        var invalidJson = "{ this is not valid json :; ";

        var response = await _service.ProcessMessageJsonAsync(invalidJson);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Error, Is.Not.Null);
        Assert.That(response.Error.Code, Is.EqualTo(-32700));
        Assert.That(response.Error.Message, Does.Contain("Parse error"));
    }

    [Test]
    public async Task ProcessMessageAsync_when_handler_throws_should_return_internal_error_code_32603()
    {
        _torrentService.GetAll().Returns(_ => throw new InvalidOperationException("Simulated database failure"));

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":100,\"method\":\"tools/call\",\"params\":{\"name\":\"list_torrents\",\"arguments\":{}}}";

        var response = await _service.ProcessMessageJsonAsync(requestJson);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Error, Is.Not.Null);
        Assert.That(response.Error.Code, Is.EqualTo(-32603));
        Assert.That(response.Error.Message, Does.Contain("Simulated database failure"));
    }

    [Test]
    public async Task ListTorrents_with_no_filters_should_return_all_torrents()
    {
        var torrents = new List<Torrent>
        {
            new() { Id = 1, Name = "Torrent Alpha", Status = TorrentStatus.Seeding, Ratio = 1.5 },
            new() { Id = 2, Name = "Torrent Beta", Status = TorrentStatus.Downloading, Ratio = 0.5 }
        };
        _torrentService.GetAll().Returns(torrents);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"list_torrents\",\"arguments\":{}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        Assert.That(response.Error, Is.Null);
        var result = response.Result as McpToolCallResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("Torrent Alpha"));
        Assert.That(result.Content[0].Text, Does.Contain("Torrent Beta"));
    }

    [Test]
    public async Task ListTorrents_filter_by_status_should_be_case_insensitive()
    {
        var torrents = new List<Torrent>
        {
            new() { Id = 1, Name = "Seeding Tor", Status = TorrentStatus.Seeding },
            new() { Id = 2, Name = "Paused Tor", Status = TorrentStatus.Paused },
            new() { Id = 3, Name = "Downloading Tor", Status = TorrentStatus.Downloading }
        };
        _torrentService.GetAll().Returns(torrents);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"list_torrents\",\"arguments\":{\"status\":\"seeding\"}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("Seeding Tor"));
        Assert.That(result.Content[0].Text, Does.Not.Contain("Paused Tor"));
        Assert.That(result.Content[0].Text, Does.Not.Contain("Downloading Tor"));
    }

    [Test]
    public async Task ListTorrents_filter_by_category_should_be_case_insensitive()
    {
        var torrents = new List<Torrent>
        {
            new() { Id = 1, Name = "Docu 1", Category = "Documentary" },
            new() { Id = 2, Name = "Movie 1", Category = "Movies" }
        };
        _torrentService.GetAll().Returns(torrents);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"list_torrents\",\"arguments\":{\"category\":\"documentary\"}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("Docu 1"));
        Assert.That(result.Content[0].Text, Does.Not.Contain("Movie 1"));
    }

    [Test]
    public async Task ListTorrents_filter_by_tag_numeric_id_should_filter_correctly()
    {
        var torrents = new List<Torrent>
        {
            new() { Id = 1, Name = "Tagged Tor", TagIds = new List<int> { 10, 20 } },
            new() { Id = 2, Name = "Untagged Tor", TagIds = new List<int> { 30 } }
        };
        _torrentService.GetAll().Returns(torrents);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"list_torrents\",\"arguments\":{\"tag\":\"10\"}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("Tagged Tor"));
        Assert.That(result.Content[0].Text, Does.Not.Contain("Untagged Tor"));
    }

    [Test]
    public async Task ListTorrents_filter_by_tag_name_should_resolve_tag_via_tag_service()
    {
        var tags = new List<Tag>
        {
            new() { Id = 7, Label = "Anime" },
            new() { Id = 8, Label = "Music" }
        };
        _tagService.GetAll().Returns(tags);

        var torrents = new List<Torrent>
        {
            new() { Id = 1, Name = "Anime Series", TagIds = new List<int> { 7 } },
            new() { Id = 2, Name = "Music Album", TagIds = new List<int> { 8 } }
        };
        _torrentService.GetAll().Returns(torrents);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"list_torrents\",\"arguments\":{\"tag\":\"anime\"}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("Anime Series"));
        Assert.That(result.Content[0].Text, Does.Not.Contain("Music Album"));
    }

    [Test]
    public async Task ListTorrents_filter_by_tag_name_when_tag_not_found_should_return_empty_list()
    {
        _tagService.GetAll().Returns(new List<Tag>());

        var torrents = new List<Torrent>
        {
            new() { Id = 1, Name = "Some Tor", TagIds = new List<int> { 1 } }
        };
        _torrentService.GetAll().Returns(torrents);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"tools/call\",\"params\":{\"name\":\"list_torrents\",\"arguments\":{\"tag\":\"nonexistent_label\"}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Is.EqualTo("[]"));
    }

    [Test]
    public async Task ListTorrents_filter_by_min_and_max_ratio_should_filter_correctly()
    {
        var torrents = new List<Torrent>
        {
            new() { Id = 1, Name = "Low Ratio", Ratio = 0.5 },
            new() { Id = 2, Name = "Medium Ratio", Ratio = 1.5 },
            new() { Id = 3, Name = "High Ratio", Ratio = 3.0 }
        };
        _torrentService.GetAll().Returns(torrents);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"list_torrents\",\"arguments\":{\"min_ratio\":1.0,\"max_ratio\":2.0}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("Medium Ratio"));
        Assert.That(result.Content[0].Text, Does.Not.Contain("Low Ratio"));
        Assert.That(result.Content[0].Text, Does.Not.Contain("High Ratio"));
    }

    [Test]
    public async Task ListTorrents_when_torrent_service_returns_null_should_return_empty_list()
    {
        _torrentService.GetAll().Returns((List<Torrent>)null);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":8,\"method\":\"tools/call\",\"params\":{\"name\":\"list_torrents\",\"arguments\":{}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Is.EqualTo("[]"));
    }

    [Test]
    public async Task GetTorrentDetails_by_id_should_return_torrent_files_trackers_and_peer_stats()
    {
        var torrent = new Torrent
        {
            Id = 10,
            Name = "Debian 12",
            InfoHash = "1234567890abcdef1234567890abcdef12345678",
            Status = TorrentStatus.Seeding,
            TotalSize = 1024 * 1024 * 700,
            Ratio = 1.8,
            Seeders = 45,
            Leechers = 5
        };
        _torrentService.Get(10).Returns(torrent);

        var files = new List<TorrentFile>
        {
            new() { Id = 1, TorrentId = 10, Path = "debian.iso", Size = 1024 * 1024 * 700, Wanted = true }
        };
        _torrentFileService.GetByTorrentId(10).Returns(files);

        var trackers = new List<TrackerEntry>
        {
            new() { Id = 1, TorrentId = 10, Url = "http://tracker.debian.org/announce", Status = TrackerStatus.Working, Seeders = 45 }
        };
        _trackerEntryService.GetByTorrentId(10).Returns(trackers);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":11,\"method\":\"tools/call\",\"params\":{\"name\":\"get_torrent_details\",\"arguments\":{\"id\":10}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("Debian 12"));
        Assert.That(result.Content[0].Text, Does.Contain("debian.iso"));
        Assert.That(result.Content[0].Text, Does.Contain("tracker.debian.org"));
        Assert.That(result.Content[0].Text, Does.Contain("\"seeders\":45"));
    }

    [Test]
    public async Task GetTorrentDetails_by_hash_should_find_torrent_and_return_details()
    {
        var hash = "abcdef1234567890abcdef1234567890abcdef12";
        var torrent = new Torrent
        {
            Id = 25,
            Name = "Fedora 40",
            InfoHash = hash,
            Status = TorrentStatus.Seeding
        };
        _torrentService.FindByInfoHash(hash).Returns(torrent);

        var requestJson = $"{{\"jsonrpc\":\"2.0\",\"id\":12,\"method\":\"tools/call\",\"params\":{{\"name\":\"get_torrent_details\",\"arguments\":{{\"hash\":\"{hash}\"}}}}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("Fedora 40"));
    }

    [Test]
    public async Task GetTorrentDetails_when_torrent_not_found_should_return_error_result()
    {
        _torrentService.Get(999).Returns((Torrent)null);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":13,\"method\":\"tools/call\",\"params\":{\"name\":\"get_torrent_details\",\"arguments\":{\"id\":999}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.True);
        Assert.That(result.Content[0].Text, Does.Contain("Torrent not found."));
    }

    [Test]
    public async Task GetTorrentDetails_when_file_and_tracker_services_are_null_should_return_empty_arrays()
    {
        var minimalService = new McpService(_torrentService);
        var torrent = new Torrent { Id = 3, Name = "Standalone Torrent", InfoHash = "abc" };
        _torrentService.Get(3).Returns(torrent);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":14,\"method\":\"tools/call\",\"params\":{\"name\":\"get_torrent_details\",\"arguments\":{\"id\":3}}}";
        var response = await minimalService.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("\"files\":[]"));
        Assert.That(result.Content[0].Text, Does.Contain("\"trackers\":[]"));
    }

    [Test]
    public async Task PauseTorrent_by_id_should_call_torrent_service_pause()
    {
        var torrent = new Torrent { Id = 5, Name = "Ubuntu Desktop" };
        _torrentService.Get(5).Returns(torrent);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":20,\"method\":\"tools/call\",\"params\":{\"name\":\"pause_torrent\",\"arguments\":{\"id\":5}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        _torrentService.Received(1).Pause(5);
        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("paused successfully"));
    }

    [Test]
    public async Task PauseTorrent_by_hash_should_call_torrent_service_pause()
    {
        var torrent = new Torrent { Id = 8, Name = "Arch Linux", InfoHash = "hash123" };
        _torrentService.FindByInfoHash("hash123").Returns(torrent);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":21,\"method\":\"tools/call\",\"params\":{\"name\":\"pause_torrent\",\"arguments\":{\"hash\":\"hash123\"}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        _torrentService.Received(1).Pause(8);
        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("paused successfully"));
    }

    [Test]
    public async Task PauseTorrent_when_torrent_not_found_should_return_error_result()
    {
        _torrentService.Get(404).Returns((Torrent)null);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":22,\"method\":\"tools/call\",\"params\":{\"name\":\"pause_torrent\",\"arguments\":{\"id\":404}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.True);
        Assert.That(result.Content[0].Text, Does.Contain("Torrent not found."));
    }

    [Test]
    public async Task ResumeTorrent_by_id_should_call_torrent_service_start()
    {
        var torrent = new Torrent { Id = 6, Name = "Kubuntu" };
        _torrentService.Get(6).Returns(torrent);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":23,\"method\":\"tools/call\",\"params\":{\"name\":\"resume_torrent\",\"arguments\":{\"id\":6}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        _torrentService.Received(1).Start(6);
        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("resumed successfully"));
    }

    [Test]
    public async Task ResumeTorrent_by_hash_should_call_torrent_service_start()
    {
        var torrent = new Torrent { Id = 12, Name = "Xubuntu", InfoHash = "xubhash" };
        _torrentService.FindByInfoHash("xubhash").Returns(torrent);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":24,\"method\":\"tools/call\",\"params\":{\"name\":\"resume_torrent\",\"arguments\":{\"hash\":\"xubhash\"}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        _torrentService.Received(1).Start(12);
        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("resumed successfully"));
    }

    [Test]
    public async Task ResumeTorrent_when_torrent_not_found_should_return_error_result()
    {
        _torrentService.Get(404).Returns((Torrent)null);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":25,\"method\":\"tools/call\",\"params\":{\"name\":\"resume_torrent\",\"arguments\":{\"id\":404}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.True);
        Assert.That(result.Content[0].Text, Does.Contain("Torrent not found."));
    }

    [Test]
    public async Task AddTorrent_with_magnet_should_call_import_from_magnet()
    {
        var magnetUri = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Test";
        var added = new Torrent { Id = 33, Name = "Test", InfoHash = "0123456789abcdef0123456789abcdef01234567" };
        _torrentImportService.ImportFromMagnet(magnetUri).Returns(added);

        var requestJson = $"{{\"jsonrpc\":\"2.0\",\"id\":30,\"method\":\"tools/call\",\"params\":{{\"name\":\"add_torrent\",\"arguments\":{{\"magnet\":\"{magnetUri}\"}}}}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        _torrentImportService.Received(1).ImportFromMagnet(magnetUri);
        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("Added torrent 'Test'"));
    }

    [Test]
    public async Task AddTorrent_with_base64_torrent_file_should_decode_and_call_import_from_file()
    {
        var fakeBytes = new byte[] { 0x64, 0x38, 0x3a, 0x61, 0x6e, 0x6e, 0x6f, 0x75, 0x6e, 0x63, 0x65, 0x65 };
        var base64 = Convert.ToBase64String(fakeBytes);
        var added = new Torrent { Id = 34, Name = "TorrentFromFile", InfoHash = "importedhash" };
        _torrentImportService.ImportFromFile(Arg.Any<Stream>(), "custom.torrent").Returns(added);

        var requestJson = $"{{\"jsonrpc\":\"2.0\",\"id\":31,\"method\":\"tools/call\",\"params\":{{\"name\":\"add_torrent\",\"arguments\":{{\"torrent_file\":\"{base64}\",\"file_name\":\"custom.torrent\"}}}}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        _torrentImportService.Received(1).ImportFromFile(Arg.Any<Stream>(), "custom.torrent");
        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("Added torrent 'TorrentFromFile'"));
    }

    [Test]
    public async Task AddTorrent_with_invalid_base64_should_return_error_result()
    {
        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":32,\"method\":\"tools/call\",\"params\":{\"name\":\"add_torrent\",\"arguments\":{\"torrent_file\":\"!!!not_valid_base64\"}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.True);
        Assert.That(result.Content[0].Text, Does.Contain("Invalid base64 encoding"));
    }

    [Test]
    public async Task AddTorrent_with_neither_magnet_nor_file_should_return_error_result()
    {
        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":33,\"method\":\"tools/call\",\"params\":{\"name\":\"add_torrent\",\"arguments\":{}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.True);
        Assert.That(result.Content[0].Text, Does.Contain("Either 'magnet' or 'torrent_file' must be provided."));
    }

    [Test]
    public async Task AddTorrent_when_import_service_is_null_should_return_error_result()
    {
        var minimalService = new McpService(_torrentService, torrentImportService: null);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":34,\"method\":\"tools/call\",\"params\":{\"name\":\"add_torrent\",\"arguments\":{\"magnet\":\"magnet:?xt=urn:btih:abc\"}}}";
        var response = await minimalService.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.True);
        Assert.That(result.Content[0].Text, Does.Contain("Torrent import service is unavailable."));
    }

    [Test]
    public async Task BoostTrackers_for_specific_torrent_by_id_should_announce_and_scrape()
    {
        var torrent = new Torrent { Id = 19, Name = "CentOS" };
        _torrentService.Get(19).Returns(torrent);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":40,\"method\":\"tools/call\",\"params\":{\"name\":\"boost_trackers\",\"arguments\":{\"id\":19}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        _trackerAnnounceService.Received(1).AnnounceTorrent(torrent, force: true);
        await _trackerScrapeService.Received(1).ScrapeTorrentAsync(19, Arg.Any<CancellationToken>());

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("Boosted trackers for torrent 'CentOS'"));
    }

    [Test]
    public async Task BoostTrackers_for_specific_torrent_by_hash_should_announce_and_scrape()
    {
        var torrent = new Torrent { Id = 22, Name = "RockyLinux", InfoHash = "rockyhash" };
        _torrentService.FindByInfoHash("rockyhash").Returns(torrent);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":41,\"method\":\"tools/call\",\"params\":{\"name\":\"boost_trackers\",\"arguments\":{\"hash\":\"rockyhash\"}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        _trackerAnnounceService.Received(1).AnnounceTorrent(torrent, force: true);
        await _trackerScrapeService.Received(1).ScrapeTorrentAsync(22, Arg.Any<CancellationToken>());

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("Boosted trackers for torrent 'RockyLinux'"));
    }

    [Test]
    public async Task BoostTrackers_when_announce_throws_should_continue_to_scrape()
    {
        var torrent = new Torrent { Id = 20, Name = "Gentoo" };
        _torrentService.Get(20).Returns(torrent);

        _trackerAnnounceService.When(x => x.AnnounceTorrent(torrent, force: true))
            .Do(_ => throw new InvalidOperationException("Network announce error"));

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":42,\"method\":\"tools/call\",\"params\":{\"name\":\"boost_trackers\",\"arguments\":{\"id\":20}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        await _trackerScrapeService.Received(1).ScrapeTorrentAsync(20, Arg.Any<CancellationToken>());

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("scraped"));
    }

    [Test]
    public async Task BoostTrackers_for_all_torrents_when_no_arguments_provided()
    {
        _trackerScrapeService.ScrapeAllTorrentsAsync(Arg.Any<CancellationToken>()).Returns(15);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":43,\"method\":\"tools/call\",\"params\":{\"name\":\"boost_trackers\",\"arguments\":{}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        await _trackerScrapeService.Received(1).ScrapeAllTorrentsAsync(Arg.Any<CancellationToken>());
        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        Assert.That(result.Content[0].Text, Does.Contain("15 torrents scraped"));
    }

    [Test]
    public async Task BoostTrackers_cancellationToken_should_be_passed_to_scrape_service()
    {
        using var cts = new CancellationTokenSource();
        _trackerScrapeService.ScrapeAllTorrentsAsync(cts.Token).Returns(5);

        var request = new JsonRpcRequest
        {
            Id = 44,
            Method = "tools/call",
            Params = JsonSerializer.SerializeToElement(new { name = "boost_trackers", arguments = new { } }, McpJsonOptions.Default)
        };

        var response = await _service.ProcessMessageAsync(request, cts.Token);

        await _trackerScrapeService.Received(1).ScrapeAllTorrentsAsync(cts.Token);
        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
    }

    [Test]
    public async Task GetSeedingMetrics_should_return_stats_and_latest_speed_history()
    {
        _seedingService.GetStats().Returns(new SeedingStats
        {
            ActiveTorrents = 12,
            TotalUploaded = 10_000_000_000L,
            TotalDownloaded = 5_000_000_000L,
            AverageRatio = 2.0
        });

        _speedHistoryService.GetHistory().Returns(new List<SpeedSnapshot>
        {
            new() { UploadSpeed = 100_000L, DownloadSpeed = 50_000L },
            new() { UploadSpeed = 850_000L, DownloadSpeed = 200_000L }
        });

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":50,\"method\":\"tools/call\",\"params\":{\"name\":\"get_seeding_metrics\",\"arguments\":{}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        var text = result.Content[0].Text;
        Assert.That(text, Does.Contain("\"activeTorrents\":12"));
        Assert.That(text, Does.Contain("\"totalUploaded\":10000000000"));
        Assert.That(text, Does.Contain("\"uploadSpeed\":850000"));
        Assert.That(text, Does.Contain("\"downloadSpeed\":200000"));
    }

    [Test]
    public async Task GetSeedingMetrics_when_speed_history_is_empty_should_default_speeds_to_zero()
    {
        _seedingService.GetStats().Returns(new SeedingStats { ActiveTorrents = 0 });
        _speedHistoryService.GetHistory().Returns(new List<SpeedSnapshot>());

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":51,\"method\":\"tools/call\",\"params\":{\"name\":\"get_seeding_metrics\",\"arguments\":{}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        var text = result.Content[0].Text;
        Assert.That(text, Does.Contain("\"uploadSpeed\":0"));
        Assert.That(text, Does.Contain("\"downloadSpeed\":0"));
    }

    [Test]
    public async Task GetSeedingMetrics_when_seeding_services_are_null_should_return_zero_defaults()
    {
        var minimalService = new McpService(_torrentService);

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":52,\"method\":\"tools/call\",\"params\":{\"name\":\"get_seeding_metrics\",\"arguments\":{}}}";
        var response = await minimalService.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result.IsError, Is.False);
        var text = result.Content[0].Text;
        Assert.That(text, Does.Contain("\"activeTorrents\":0"));
    }

    [Test]
    public async Task ToolCall_when_params_are_missing_should_return_error_result()
    {
        var request = new JsonRpcRequest
        {
            Id = 60,
            Method = "tools/call",
            Params = null
        };

        var response = await _service.ProcessMessageAsync(request);

        var result = response.Result as McpToolCallResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsError, Is.True);
        Assert.That(result.Content[0].Text, Does.Contain("Parameters are missing."));
    }

    [Test]
    public async Task ToolCall_with_unknown_tool_name_should_return_error_result()
    {
        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":61,\"method\":\"tools/call\",\"params\":{\"name\":\"nonexistent_tool_action\",\"arguments\":{}}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        var result = response.Result as McpToolCallResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsError, Is.True);
        Assert.That(result.Content[0].Text, Does.Contain("Unknown tool 'nonexistent_tool_action'."));
    }

    [Test]
    public async Task ResourcesList_should_return_standard_resources()
    {
        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":70,\"method\":\"resources/list\"}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        Assert.That(response.Error, Is.Null);
        var result = response.Result as McpResourceListResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Resources.Count, Is.EqualTo(3));

        var uris = result.Resources.ConvertAll(r => r.Uri);
        Assert.That(uris, Does.Contain("seedarr://torrents"));
        Assert.That(uris, Does.Contain("seedarr://system/status"));
        Assert.That(uris, Does.Contain("seedarr://logs/recent"));
    }

    [Test]
    public async Task ResourceRead_with_missing_uri_should_return_error_32602()
    {
        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":71,\"method\":\"resources/read\",\"params\":{}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        Assert.That(response.Error, Is.Not.Null);
        Assert.That(response.Error.Code, Is.EqualTo(-32602));
        Assert.That(response.Error.Message, Does.Contain("Parameter 'uri' is required."));
    }

    [Test]
    public async Task ResourceRead_with_unknown_uri_should_return_error_32602()
    {
        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":72,\"method\":\"resources/read\",\"params\":{\"uri\":\"seedarr://unknown_endpoint\"}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        Assert.That(response.Error, Is.Not.Null);
        Assert.That(response.Error.Code, Is.EqualTo(-32602));
        Assert.That(response.Error.Message, Does.Contain("Resource 'seedarr://unknown_endpoint' not found."));
    }

    [Test]
    public async Task ResourceRead_torrents_uri_should_return_torrent_list()
    {
        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { Id = 1, Name = "Resource Torrent", Status = TorrentStatus.Seeding }
        });

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":73,\"method\":\"resources/read\",\"params\":{\"uri\":\"seedarr://torrents\"}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        Assert.That(response.Error, Is.Null);
        var result = response.Result as McpResourceReadResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Contents.Count, Is.EqualTo(1));
        Assert.That(result.Contents[0].Text, Does.Contain("Resource Torrent"));
    }

    [Test]
    public async Task ResourceRead_system_status_uri_should_return_health_and_disk_space()
    {
        _healthCheckService.PerformChecks().Returns(new List<HealthCheckResult>
        {
            new() { Source = "Database", Type = HealthCheckResultType.Ok, Message = "DB operational" }
        });

        _diskSpaceService.GetDiskSpace().Returns(new List<DiskSpaceInfo>
        {
            new() { Path = "/data", FreeSpace = 1_000_000_000L, TotalSpace = 2_000_000_000L }
        });

        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":74,\"method\":\"resources/read\",\"params\":{\"uri\":\"seedarr://system/status\"}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        Assert.That(response.Error, Is.Null);
        var result = response.Result as McpResourceReadResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Contents[0].Text, Does.Contain("DB operational"));
        Assert.That(result.Contents[0].Text, Does.Contain("/data"));
    }

    [Test]
    public async Task PromptsList_should_return_three_prompts()
    {
        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":80,\"method\":\"prompts/list\"}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        Assert.That(response.Error, Is.Null);
        var result = response.Result as McpPromptListResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Prompts.Count, Is.EqualTo(3));

        var names = result.Prompts.ConvertAll(p => p.Name);
        Assert.That(names, Does.Contain("swarm_diagnosis"));
        Assert.That(names, Does.Contain("ratio_balancing"));
        Assert.That(names, Does.Contain("arr_config"));
    }

    [Test]
    public async Task PromptGet_with_missing_name_should_return_error_32602()
    {
        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":81,\"method\":\"prompts/get\",\"params\":{}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        Assert.That(response.Error, Is.Not.Null);
        Assert.That(response.Error.Code, Is.EqualTo(-32602));
        Assert.That(response.Error.Message, Does.Contain("Parameter 'name' is required."));
    }

    [Test]
    public async Task PromptGet_with_unknown_prompt_name_should_return_error_32602()
    {
        var requestJson = "{\"jsonrpc\":\"2.0\",\"id\":82,\"method\":\"prompts/get\",\"params\":{\"name\":\"mystery_prompt\"}}";
        var response = await _service.ProcessMessageJsonAsync(requestJson);

        Assert.That(response.Error, Is.Not.Null);
        Assert.That(response.Error.Code, Is.EqualTo(-32602));
        Assert.That(response.Error.Message, Does.Contain("Prompt 'mystery_prompt' not found."));
    }

    [Test]
    public async Task PromptGet_swarm_diagnosis_with_and_without_torrent_id()
    {
        var defaultJson = "{\"jsonrpc\":\"2.0\",\"id\":83,\"method\":\"prompts/get\",\"params\":{\"name\":\"swarm_diagnosis\"}}";
        var defaultResp = await _service.ProcessMessageJsonAsync(defaultJson);
        var defaultResult = defaultResp.Result as McpPromptGetResult;
        Assert.That(defaultResult.Messages[0].Content.Text, Does.Contain("diagnosing BitTorrent swarm health across all torrents"));

        var specificJson = "{\"jsonrpc\":\"2.0\",\"id\":84,\"method\":\"prompts/get\",\"params\":{\"name\":\"swarm_diagnosis\",\"arguments\":{\"torrent_id\":\"99\"}}}";
        var specificResp = await _service.ProcessMessageJsonAsync(specificJson);
        var specificResult = specificResp.Result as McpPromptGetResult;
        Assert.That(specificResult.Messages[0].Content.Text, Does.Contain("torrent '99'"));
    }

    [Test]
    public async Task PromptGet_ratio_balancing_with_and_without_target_ratio()
    {
        var defaultJson = "{\"jsonrpc\":\"2.0\",\"id\":85,\"method\":\"prompts/get\",\"params\":{\"name\":\"ratio_balancing\"}}";
        var defaultResp = await _service.ProcessMessageJsonAsync(defaultJson);
        var defaultResult = defaultResp.Result as McpPromptGetResult;
        Assert.That(defaultResult.Messages[0].Content.Text, Does.Contain("target ratio of 1.0"));

        var specificJson = "{\"jsonrpc\":\"2.0\",\"id\":86,\"method\":\"prompts/get\",\"params\":{\"name\":\"ratio_balancing\",\"arguments\":{\"target_ratio\":\"2.5\"}}}";
        var specificResp = await _service.ProcessMessageJsonAsync(specificJson);
        var specificResult = specificResp.Result as McpPromptGetResult;
        Assert.That(specificResult.Messages[0].Content.Text, Does.Contain("target ratio of 2.5"));
    }

    [Test]
    public async Task PromptGet_arr_config_with_and_without_arr_type()
    {
        var defaultJson = "{\"jsonrpc\":\"2.0\",\"id\":87,\"method\":\"prompts/get\",\"params\":{\"name\":\"arr_config\"}}";
        var defaultResp = await _service.ProcessMessageJsonAsync(defaultJson);
        var defaultResult = defaultResp.Result as McpPromptGetResult;
        Assert.That(defaultResult.Messages[0].Content.Text, Does.Contain("Radarr/Sonarr"));

        var specificJson = "{\"jsonrpc\":\"2.0\",\"id\":88,\"method\":\"prompts/get\",\"params\":{\"name\":\"arr_config\",\"arguments\":{\"arr_type\":\"Lidarr\"}}}";
        var specificResp = await _service.ProcessMessageJsonAsync(specificJson);
        var specificResult = specificResp.Result as McpPromptGetResult;
        Assert.That(specificResult.Messages[0].Content.Text, Does.Contain("Lidarr"));
    }
}
