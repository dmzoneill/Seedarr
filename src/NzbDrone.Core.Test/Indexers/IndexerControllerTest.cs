using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Prowlarr;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Network;
using NzbDrone.Core.Torrents;
using Seedarr.Api.V1.Indexers;
using Seedarr.Api.V1.Torrents;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class IndexerControllerTest
{
    private IProxySettingsProvider _proxySettingsProvider;
    private IIndexerFactory _indexerFactory;
    private ITorrentService _torrentService;
    private ITorrentFileService _torrentFileService;
    private ITrackerEntryService _trackerEntryService;
    private ITorrentFileParser _torrentFileParser;
    private IDownloadHistoryService _downloadHistoryService;
    private IIndexerStatusService _indexerStatusService;
    private IRssRuleRepository _rssRuleRepository;
    private IndexerController _controller;

    [SetUp]
    public void SetUp()
    {
        _proxySettingsProvider = Substitute.For<IProxySettingsProvider>();
        _indexerFactory = Substitute.For<IIndexerFactory>();
        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _torrentFileParser = Substitute.For<ITorrentFileParser>();
        _downloadHistoryService = Substitute.For<IDownloadHistoryService>();
        _indexerStatusService = Substitute.For<IIndexerStatusService>();
        _rssRuleRepository = Substitute.For<IRssRuleRepository>();

        _controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository);
    }

    [TestCase("http://169.254.169.254/latest/meta-data")]
    [TestCase("http://127.0.0.1:8080")]
    [TestCase("http://localhost:9696")]
    [TestCase("http://10.0.0.1:9696")]
    [TestCase("http://192.168.1.1:9696")]
    [TestCase("http://172.16.0.1:9696")]
    public void TestDirect_with_unsafe_url_returns_bad_request(string url)
    {
        var definition = new IndexerDefinition
        {
            Name = "Unsafe Probe",
            IndexerType = "Prowlarr",
            Url = url,
            ApiKey = "testkey"
        };

        var result = _controller.TestDirect(definition);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Target host/URL is not permitted."));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void TestDirect_with_empty_url_returns_bad_request(string url)
    {
        var definition = new IndexerDefinition
        {
            Name = "Empty URL",
            IndexerType = "Prowlarr",
            Url = url,
            ApiKey = "testkey"
        };

        var result = _controller.TestDirect(definition);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void TestDirect_with_null_body_returns_bad_request()
    {
        var result = _controller.TestDirect(null);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void TestDirect_unmasks_api_key_when_existing_indexer_and_masked_key()
    {
        var existing = new IndexerDefinition
        {
            Id = 1,
            Name = "Existing Prowlarr",
            IndexerType = "Prowlarr",
            Url = "http://8.8.8.8:9696",
            ApiKey = "real_secret_api_key"
        };
        _indexerFactory.Get(1).Returns(existing);

        var incoming = new IndexerDefinition
        {
            Id = 1,
            Name = "Existing Prowlarr",
            IndexerType = "Prowlarr",
            Url = "http://8.8.8.8:9696",
            ApiKey = "****_api_key"
        };

        var result = _controller.TestDirect(incoming);

        Assert.That(incoming.ApiKey, Is.EqualTo("real_secret_api_key"));
        _indexerFactory.Received(1).Get(1);
    }

    [Test]
    public void TestDirect_unmasks_api_key_when_existing_indexer_and_empty_key()
    {
        var existing = new IndexerDefinition
        {
            Id = 1,
            Name = "Existing Prowlarr",
            IndexerType = "Prowlarr",
            Url = "http://8.8.8.8:9696",
            ApiKey = "saved_secret_api_key"
        };
        _indexerFactory.Get(1).Returns(existing);

        var incoming = new IndexerDefinition
        {
            Id = 1,
            Name = "Existing Prowlarr",
            IndexerType = "Prowlarr",
            Url = "http://8.8.8.8:9696",
            ApiKey = ""
        };

        var result = _controller.TestDirect(incoming);

        Assert.That(incoming.ApiKey, Is.EqualTo("saved_secret_api_key"));
        _indexerFactory.Received(1).Get(1);
    }

    [Test]
    public void TestConnection_with_unsafe_url_returns_bad_request()
    {
        var existing = new IndexerDefinition
        {
            Id = 1,
            Name = "Unsafe Indexer",
            IndexerType = "Prowlarr",
            Url = "http://169.254.169.254",
            ApiKey = "test"
        };
        _indexerFactory.Get(1).Returns(existing);

        var result = _controller.TestConnection(1);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Target host/URL is not permitted."));
    }

    [Test]
    public void TestConnection_for_nonexistent_indexer_returns_not_found()
    {
        _indexerFactory.Get(99).Returns((IndexerDefinition)null);

        var result = _controller.TestConnection(99);

        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
    }

    [TestCase("http://169.254.169.254/latest/meta-data")]
    [TestCase("http://127.0.0.1:8080/file.torrent")]
    [TestCase("http://localhost:9696/download")]
    [TestCase("http://10.0.0.1:9696/download")]
    [TestCase("http://192.168.1.1:9696/download")]
    [TestCase("http://172.16.0.1:9696/download")]
    public void DownloadRelease_with_unsafe_download_url_returns_bad_request(string url)
    {
        var request = new DownloadReleaseRequest
        {
            Title = "Unsafe Torrent",
            DownloadUrl = url
        };

        var result = _controller.DownloadRelease(request);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Invalid or unsafe download URL."));
    }

    [Test]
    public void DownloadRelease_does_not_send_api_key_to_cross_origin_host()
    {
        var handler = new FakeHttpMessageHandler();
        var client = new HttpClient(handler);
        var controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository,
            client);

        var indexerDef = new IndexerDefinition
        {
            Id = 1,
            Name = "Source Indexer",
            IndexerType = "Torznab",
            Url = "http://8.8.8.8:9696",
            ApiKey = "secret_api_key"
        };
        _indexerFactory.Get(1).Returns(indexerDef);

        var request = new DownloadReleaseRequest
        {
            Title = "Cross Origin Torrent",
            DownloadUrl = "http://8.8.4.4:8080/download/1.torrent",
            IndexerId = 1
        };

        controller.DownloadRelease(request);

        Assert.That(handler.SentRequest, Is.Not.Null);
        Assert.That(handler.SentRequest.Headers.Contains("X-Api-Key"), Is.False);
    }

    [Test]
    public void DownloadRelease_sends_api_key_to_same_origin_host()
    {
        var handler = new FakeHttpMessageHandler();
        var client = new HttpClient(handler);
        var controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository,
            client);

        var indexerDef = new IndexerDefinition
        {
            Id = 1,
            Name = "Source Indexer",
            IndexerType = "Torznab",
            Url = "http://8.8.8.8:9696",
            ApiKey = "secret_api_key"
        };
        _indexerFactory.Get(1).Returns(indexerDef);

        var request = new DownloadReleaseRequest
        {
            Title = "Same Origin Torrent",
            DownloadUrl = "http://8.8.8.8:9696/download/1.torrent",
            IndexerId = 1
        };

        controller.DownloadRelease(request);

        Assert.That(handler.SentRequest, Is.Not.Null);
        Assert.That(handler.SentRequest.Headers.Contains("X-Api-Key"), Is.True);
        Assert.That(handler.SentRequest.Headers.GetValues("X-Api-Key").First(), Is.EqualTo("secret_api_key"));
    }

    [Test]
    public async Task Search_when_indexer_returns_401_records_failure_and_does_not_call_record_success()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                ReasonPhrase = "Unauthorized"
            }
        };
        var client = new HttpClient(handler);
        var controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository,
            client);

        var indexerDef = new IndexerDefinition
        {
            Id = 1,
            Name = "Torznab 1",
            IndexerType = "Torznab",
            Url = "http://8.8.8.8:9696",
            ApiKey = "key",
            Enable = true,
            EnableSearch = true
        };
        _indexerFactory.All().Returns(new List<IndexerDefinition> { indexerDef });

        var result = await controller.Search("ubuntu");

        _indexerStatusService.Received(1).RecordFailure(1, 401, Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<TimeSpan?>());
        _indexerStatusService.DidNotReceive().RecordSuccess(1);
    }

    [Test]
    public async Task Search_when_indexer_returns_429_records_failure_with_retry_after()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            ReasonPhrase = "Too Many Requests"
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));

        var handler = new FakeHttpMessageHandler { ResponseToReturn = response };
        var client = new HttpClient(handler);
        var controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository,
            client);

        var indexerDef = new IndexerDefinition
        {
            Id = 2,
            Name = "Torznab 2",
            IndexerType = "Torznab",
            Url = "http://8.8.8.8:9696",
            ApiKey = "key",
            Enable = true,
            EnableSearch = true
        };
        _indexerFactory.All().Returns(new List<IndexerDefinition> { indexerDef });

        await controller.Search("ubuntu");

        _indexerStatusService.Received(1).RecordFailure(
            2,
            429,
            Arg.Any<string>(),
            Arg.Any<Exception>(),
            Arg.Is<TimeSpan?>(t => t.HasValue && t.Value.TotalSeconds == 120));
        _indexerStatusService.DidNotReceive().RecordSuccess(2);
    }

    [Test]
    public async Task Search_when_indexer_succeeds_records_success()
    {
        var xml = @"<?xml version=""1.0""?><rss version=""2.0""><channel></channel></rss>";
        var handler = new FakeHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml)
            }
        };
        var client = new HttpClient(handler);
        var controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository,
            client);

        var indexerDef = new IndexerDefinition
        {
            Id = 3,
            Name = "Torznab 3",
            IndexerType = "Torznab",
            Url = "http://8.8.8.8:9696",
            ApiKey = "key",
            Enable = true,
            EnableSearch = true
        };
        _indexerFactory.All().Returns(new List<IndexerDefinition> { indexerDef });

        await controller.Search("ubuntu");

        _indexerStatusService.Received(1).RecordSuccess(3);
        _indexerStatusService.DidNotReceive().RecordFailure(3, Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<TimeSpan?>());
    }

    [Test]
    public async Task Search_with_structured_parameters_sends_tvsearch_and_records_success()
    {
        var xml = @"<?xml version=""1.0""?><rss version=""2.0""><channel></channel></rss>";
        var handler = new FakeHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml)
            }
        };
        var client = new HttpClient(handler);
        var controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository,
            client);

        var indexerDef = new IndexerDefinition
        {
            Id = 4,
            Name = "Torznab 4",
            IndexerType = "Torznab",
            Url = "http://8.8.8.8:9696",
            ApiKey = "mykey",
            Enable = true,
            EnableSearch = true
        };
        _indexerFactory.All().Returns(new List<IndexerDefinition> { indexerDef });

        var result = await controller.Search(
            query: "Breaking Bad",
            searchType: "tvsearch",
            season: 1,
            ep: 3,
            tvdbId: "81189");

        Assert.That(result, Is.Not.Null);
        _indexerStatusService.Received(1).RecordSuccess(4);
        Assert.That(handler.SentRequest, Is.Not.Null);
        var uri = handler.SentRequest.RequestUri.OriginalString;
        Assert.That(uri, Does.Contain("t=tvsearch"));
        Assert.That(uri, Does.Contain("q=Breaking%20Bad"));
        Assert.That(uri, Does.Contain("season=1"));
        Assert.That(uri, Does.Contain("ep=3"));
        Assert.That(uri, Does.Contain("tvdbid=81189"));
    }

    [Test]
    public async Task Search_when_prowlarr_returns_401_records_failure_and_does_not_call_record_success()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                ReasonPhrase = "Unauthorized"
            }
        };
        var client = new HttpClient(handler);
        var controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository,
            client);

        var indexerDef = new IndexerDefinition
        {
            Id = 10,
            Name = "Prowlarr 10",
            IndexerType = "Prowlarr",
            Url = "http://8.8.8.8:9696",
            ApiKey = "wrong-key",
            Enable = true,
            EnableSearch = true
        };
        _indexerFactory.All().Returns(new List<IndexerDefinition> { indexerDef });

        var result = await controller.Search("ubuntu");

        _indexerStatusService.Received(1).RecordFailure(10, 401, Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<TimeSpan?>());
        _indexerStatusService.DidNotReceive().RecordSuccess(10);
    }

    [Test]
    public async Task Search_multi_indexer_runs_in_parallel_and_aggregates_successful_results_while_ignoring_failed_indexers()
    {
        var defHealthy = new IndexerDefinition
        {
            Id = 101,
            Name = "Healthy Indexer",
            IndexerType = "Torznab",
            Url = "http://1.1.1.1:9696",
            ApiKey = "key1",
            Enable = true,
            EnableSearch = true
        };
        var defBroken = new IndexerDefinition
        {
            Id = 102,
            Name = "Broken Indexer",
            IndexerType = "Torznab",
            Url = "http://2.2.2.2:9696",
            ApiKey = "key2",
            Enable = true,
            EnableSearch = true
        };
        var defServerErr = new IndexerDefinition
        {
            Id = 103,
            Name = "ServerError Indexer",
            IndexerType = "Torznab",
            Url = "http://3.3.3.3:9696",
            ApiKey = "key3",
            Enable = true,
            EnableSearch = true
        };

        _indexerFactory.All().Returns(new List<IndexerDefinition> { defHealthy, defBroken, defServerErr });

        var xml = @"<?xml version=""1.0""?>
<rss version=""2.0"">
  <channel>
    <item>
      <title>Ubuntu Linux 24.04</title>
      <guid>http://example.com/item/1</guid>
      <torznab:attr name=""seeders"" value=""50"" xmlns:torznab=""http://torznab.com/schemas/2015/feed""/>
    </item>
  </channel>
</rss>";

        var handler = new FakeRoutingHttpMessageHandler(req =>
        {
            if (req.RequestUri.Host == "1.1.1.1")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) };
            }

            if (req.RequestUri.Host == "2.2.2.2")
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { ReasonPhrase = "Unauthorized" };
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError) { ReasonPhrase = "Internal Error" };
        });

        var client = new HttpClient(handler);
        var controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository,
            client);

        var actionResult = await controller.Search("ubuntu");
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var list = okResult.Value as List<NzbDrone.Core.Indexers.ReleaseInfo>;
        Assert.That(list, Is.Not.Null);
        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0].Title, Is.EqualTo("Ubuntu Linux 24.04"));

        _indexerStatusService.Received(1).RecordSuccess(101);
        _indexerStatusService.Received(1).RecordFailure(102, 401, Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<TimeSpan?>());
        _indexerStatusService.Received(1).RecordFailure(103, 500, Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<TimeSpan?>());
    }

    [Test]
    public async Task Search_when_targeted_indexer_fails_returns_error_response_with_message()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                ReasonPhrase = "Unauthorized"
            }
        };
        var client = new HttpClient(handler);
        var controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository,
            client);

        var indexerDef = new IndexerDefinition
        {
            Id = 50,
            Name = "Torznab 50",
            IndexerType = "Torznab",
            Url = "http://8.8.8.8:9696",
            ApiKey = "key",
            Enable = true,
            EnableSearch = true
        };
        _indexerFactory.Get(50).Returns(indexerDef);

        var actionResult = await controller.Search(query: "ubuntu", indexerId: 50);
        var objResult = actionResult.Result as ObjectResult;
        Assert.That(objResult, Is.Not.Null);
        Assert.That(objResult.StatusCode, Is.EqualTo(401));

        _indexerStatusService.Received(1).RecordFailure(50, 401, Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<TimeSpan?>());
        _indexerStatusService.DidNotReceive().RecordSuccess(50);
    }

    [Test]
    public async Task Search_when_targeted_indexer_returns_500_returns_internal_server_error()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                ReasonPhrase = "Internal Server Error"
            }
        };
        var client = new HttpClient(handler);
        var controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository,
            client);

        var indexerDef = new IndexerDefinition
        {
            Id = 51,
            Name = "Torznab 51",
            IndexerType = "Torznab",
            Url = "http://8.8.8.8:9696",
            ApiKey = "key",
            Enable = true,
            EnableSearch = true
        };
        _indexerFactory.Get(51).Returns(indexerDef);

        var actionResult = await controller.Search(query: "ubuntu", indexerId: 51);
        var objResult = actionResult.Result as ObjectResult;
        Assert.That(objResult, Is.Not.Null);
        Assert.That(objResult.StatusCode, Is.EqualTo(500));

        _indexerStatusService.Received(1).RecordFailure(51, 500, Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<TimeSpan?>());
    }

    [Test]
    public async Task Search_when_targeted_indexer_not_found_returns_not_found()
    {
        var controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository);

        _indexerFactory.Get(999).Returns((IndexerDefinition)null);

        var actionResult = await controller.Search(query: "ubuntu", indexerId: 999);
        var notFoundResult = actionResult.Result as NotFoundObjectResult;
        Assert.That(notFoundResult, Is.Not.Null);
        Assert.That(notFoundResult.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task Search_when_targeted_indexer_is_disabled_returns_bad_request()
    {
        var controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository);

        var def = new IndexerDefinition { Id = 77, Name = "Disabled Indexer", Enable = false, EnableSearch = true };
        _indexerFactory.Get(77).Returns(def);

        var actionResult = await controller.Search(query: "ubuntu", indexerId: 77);
        var badResult = actionResult.Result as BadRequestObjectResult;
        Assert.That(badResult, Is.Not.Null);
        Assert.That(badResult.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task Search_when_targeted_indexer_is_disabled_by_status_service_returns_service_unavailable()
    {
        var controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository);

        var def = new IndexerDefinition { Id = 88, Name = "Backoff Indexer", Enable = true, EnableSearch = true };
        _indexerFactory.Get(88).Returns(def);
        _indexerStatusService.IsDisabled(88).Returns(true);

        var actionResult = await controller.Search(query: "ubuntu", indexerId: 88);
        var objResult = actionResult.Result as ObjectResult;
        Assert.That(objResult, Is.Not.Null);
        Assert.That(objResult.StatusCode, Is.EqualTo(503));
    }

    [Test]
    public void TestConnection_when_indexer_fails_records_failure_with_status_code()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                ReasonPhrase = "Unauthorized"
            }
        };
        var client = new HttpClient(handler);
        var controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository,
            client);

        var indexerDef = new IndexerDefinition
        {
            Id = 4,
            Name = "Torznab 4",
            IndexerType = "Torznab",
            Url = "http://8.8.8.8:9696",
            ApiKey = "key"
        };
        _indexerFactory.Get(4).Returns(indexerDef);

        var result = controller.TestConnection(4);

        _indexerStatusService.Received(1).RecordFailure(4, 401, Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<TimeSpan?>());
        _indexerStatusService.DidNotReceive().RecordSuccess(4);
    }

    [Test]
    public void SyncProwlarr_should_return_bad_request_when_service_is_null()
    {
        var result = _controller.SyncProwlarr(new ProwlarrSyncRequest());
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void SyncProwlarr_should_delegate_to_service_and_return_ok()
    {
        var syncService = Substitute.For<IProwlarrIndexerSyncService>();
        var expectedResult = new ProwlarrSyncResult
        {
            Success = true,
            Added = 2,
            Updated = 1,
            Removed = 0,
            Message = "Prowlarr sync complete: 2 added, 1 updated, 0 removed."
        };
        syncService.Sync(Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<string>()).Returns(expectedResult);

        var controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository,
            null,
            syncService);

        var request = new ProwlarrSyncRequest { ProwlarrIndexerId = 5, BaseUrl = "http://localhost:9696", ApiKey = "api-123" };
        var actionResult = controller.SyncProwlarr(request);

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        Assert.That(okResult.Value, Is.EqualTo(expectedResult));

        syncService.Received(1).Sync(5, "http://localhost:9696", "api-123");
    }

    [Test]
    public void DownloadRelease_with_media_ids_and_seeding_requirements_populates_torrent_and_metadata()
    {
        var mediaMetadataRepo = Substitute.For<ITorrentMediaMetadataRepository>();
        var torrentService = Substitute.For<ITorrentService>();
        Torrent addedTorrent = null;
        torrentService.Add(Arg.Do<Torrent>(t =>
        {
            t.Id = 42;
            addedTorrent = t;
        })).Returns(x => addedTorrent);

        var controller = new IndexerController(
            _indexerFactory,
            torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository,
            null,
            null,
            mediaMetadataRepo);

        var request = new DownloadReleaseRequest
        {
            Title = "The Shawshank Redemption",
            MagnetUrl = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=The.Shawshank.Redemption",
            ImdbId = "0111161",
            TmdbId = 278,
            TvdbId = 73545,
            MinimumRatio = 1.5,
            MinimumSeedTime = 259200
        };

        var response = controller.DownloadRelease(request);

        Assert.That(response.Result, Is.InstanceOf<OkObjectResult>());
        Assert.That(addedTorrent, Is.Not.Null);
        Assert.That(addedTorrent.RatioLimit, Is.EqualTo(1.5));
        Assert.That(addedTorrent.SeedingTimeLimit, Is.EqualTo(259200));

        mediaMetadataRepo.Received(1).Upsert(Arg.Is<TorrentMediaMetadata>(m =>
            m.TorrentId == 42 &&
            m.Title == "The Shawshank Redemption" &&
            m.ImdbId == "tt0111161" &&
            m.TmdbId == "278" &&
            m.TvdbId == "73545"));
    }

    [Test]
    public void DownloadRelease_with_torznab_indexer_records_history_with_torznab_source()
    {
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 101, Name = "Test Torznab Release" });

        var torznabDef = new IndexerDefinition
        {
            Id = 55,
            Name = "IPTorrents",
            IndexerType = "Torznab"
        };
        _indexerFactory.Get(55).Returns(torznabDef);

        var controller = new IndexerController(
            _indexerFactory,
            torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            _indexerStatusService,
            _proxySettingsProvider,
            _rssRuleRepository);

        var request = new DownloadReleaseRequest
        {
            Title = "Test Torznab Release",
            MagnetUrl = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Test.Torznab.Release",
            IndexerId = 55,
            IndexerName = "IPTorrents"
        };

        var response = controller.DownloadRelease(request);

        Assert.That(response.Result, Is.InstanceOf<OkObjectResult>());
        _downloadHistoryService.Received(1).RecordTorrentAdded(
            Arg.Any<Torrent>(),
            source: Arg.Is<string>(s => s.Contains("Torznab")),
            magnetUrl: Arg.Any<string>(),
            downloadUrl: Arg.Any<string>(),
            indexerName: "IPTorrents");
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage SentRequest { get; private set; }
        public HttpResponseMessage ResponseToReturn { get; set; } = new(System.Net.HttpStatusCode.BadRequest);

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SentRequest = request;
            return ResponseToReturn;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SentRequest = request;
            return Task.FromResult(ResponseToReturn);
        }
    }

    private class FakeRoutingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _router;

        public FakeRoutingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> router)
        {
            _router = router;
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _router(request);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_router(request));
        }
    }
}
