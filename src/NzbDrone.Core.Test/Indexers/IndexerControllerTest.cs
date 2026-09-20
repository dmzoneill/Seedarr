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
    public void Search_when_indexer_returns_401_records_failure_and_does_not_call_record_success()
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

        var result = controller.Search("ubuntu");

        _indexerStatusService.Received(1).RecordFailure(1, 401, Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<TimeSpan?>());
        _indexerStatusService.DidNotReceive().RecordSuccess(1);
    }

    [Test]
    public void Search_when_indexer_returns_429_records_failure_with_retry_after()
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

        controller.Search("ubuntu");

        _indexerStatusService.Received(1).RecordFailure(
            2,
            429,
            Arg.Any<string>(),
            Arg.Any<Exception>(),
            Arg.Is<TimeSpan?>(t => t.HasValue && t.Value.TotalSeconds == 120));
        _indexerStatusService.DidNotReceive().RecordSuccess(2);
    }

    [Test]
    public void Search_when_indexer_succeeds_records_success()
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

        controller.Search("ubuntu");

        _indexerStatusService.Received(1).RecordSuccess(3);
        _indexerStatusService.DidNotReceive().RecordFailure(3, Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<TimeSpan?>());
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
}
