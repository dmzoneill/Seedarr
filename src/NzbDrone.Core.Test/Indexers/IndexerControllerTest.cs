using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
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
}
