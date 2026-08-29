using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using NLog;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.DownloadClients.Sync;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.DownloadClients.Sync;

[TestFixture]
public class DownloadClientSyncServiceTest
{
    private IDownloadClientFactory _downloadClientFactory;
    private IIndexerFactory _indexerFactory;
    private ITorrentService _torrentService;
    private ITorrentFileParser _torrentFileParser;
    private TestableDownloadClientSyncService _service;

    private class TestableDownloadClientSyncService : DownloadClientSyncService
    {
        public IDownloadClient InjectedClient { get; set; }
        public IIndexer InjectedIndexer { get; set; }

        public TestableDownloadClientSyncService(
            IDownloadClientFactory downloadClientFactory,
            IIndexerFactory indexerFactory,
            ITorrentService torrentService,
            ITorrentFileParser torrentFileParser)
            : base(downloadClientFactory, indexerFactory, torrentService, torrentFileParser)
        {
        }

        protected override IDownloadClient CreateClient(DownloadClientDefinition definition)
        {
            return InjectedClient ?? base.CreateClient(definition);
        }

        protected override IIndexer CreateIndexer(IndexerDefinition definition)
        {
            return InjectedIndexer ?? base.CreateIndexer(definition);
        }
    }

    [SetUp]
    public void Setup()
    {
        _downloadClientFactory = Substitute.For<IDownloadClientFactory>();
        _indexerFactory = Substitute.For<IIndexerFactory>();
        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileParser = Substitute.For<ITorrentFileParser>();

        _service = new TestableDownloadClientSyncService(
            _downloadClientFactory,
            _indexerFactory,
            _torrentService,
            _torrentFileParser);
    }

    [Test]
    public void Sync_should_return_zeros_when_no_clients_configured()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>());

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));
    }

    [Test]
    public void Sync_should_skip_disabled_clients()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>
        {
            new() { Id = 1, Name = "Disabled qBit", ClientType = "QBitTorrent", Enable = false }
        });

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));
    }

    [Test]
    public void Sync_should_skip_items_with_empty_infohash()
    {
        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new() { Title = "Invalid item", InfoHash = "" },
            new() { Title = "Null hash item", InfoHash = null }
        });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>
        {
            new() { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent", Enable = true }
        });

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));
        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
    }

    [Test]
    public void Sync_should_skip_items_already_existing_in_database()
    {
        var hash = "0123456789abcdef0123456789abcdef01234567";
        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { InfoHash = hash, Name = "Existing Torrent" }
        });

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new() { Title = "Existing Torrent", InfoHash = hash }
        });

        _service.InjectedClient = mockClient;
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>
        {
            new() { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent", Enable = true }
        });

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(1));
        Assert.That(result.Failed, Is.EqualTo(0));
        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
    }

    [Test]
    public void Sync_should_add_torrent_when_client_provides_torrent_bytes()
    {
        var hash = "aabbccddeeff00112233445566778899aabbccdd";
        var rawBytes = new byte[] { 0x64, 0x38, 0x3a };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new() { Title = "Ubuntu 24.04 ISO", InfoHash = hash, TotalSize = 1000000 }
        });
        mockClient.GetTorrentFile(hash).Returns(rawBytes);

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(new ParsedTorrent
        {
            Name = "Ubuntu 24.04 Desktop",
            TotalSize = 1000000,
            PieceCount = 500,
            PieceLength = 2000
        });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>
        {
            new() { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent", Enable = true }
        });

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(1));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.Name == "Ubuntu 24.04 Desktop" &&
            t.InfoHash == hash &&
            t.TotalSize == 1000000 &&
            t.PieceCount == 500 &&
            t.PieceLength == 2000 &&
            t.Status == TorrentStatus.Stopped));
    }

    [Test]
    public void Sync_should_fallback_to_indexer_when_client_does_not_provide_torrent_bytes()
    {
        var hash = "11223344556677889900aabbccddeeff00112233";
        var rawBytes = new byte[] { 0x64, 0x38, 0x3a };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new() { Title = "Arch Linux", InfoHash = hash }
        });
        mockClient.GetTorrentFile(hash).Returns((byte[])null);

        var mockIndexer = Substitute.For<IIndexer>();
        mockIndexer.FetchTorrentByHash(Arg.Any<IndexerDefinition>(), hash).Returns(rawBytes);

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(new ParsedTorrent
        {
            Name = "Arch Linux 2026",
            TotalSize = 800000,
            PieceCount = 400,
            PieceLength = 2000
        });

        _indexerFactory.All().Returns(new List<IndexerDefinition>
        {
            new() { Id = 1, Name = "Prowlarr", IndexerType = "Prowlarr", Enable = true, Url = "http://localhost:9696", ApiKey = "key" }
        });

        _service.InjectedClient = mockClient;
        _service.InjectedIndexer = mockIndexer;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>
        {
            new() { Id = 1, Name = "Deluge", ClientType = "Deluge", Enable = true }
        });

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(1));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.Name == "Arch Linux 2026" &&
            t.InfoHash == hash &&
            t.TotalSize == 800000));
    }

    [Test]
    public void Sync_should_fail_item_when_neither_client_nor_indexer_provides_torrent_bytes()
    {
        var hash = "99887766554433221100ffeeddccbbaa99887766";

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new() { Title = "Missing Torrent", InfoHash = hash }
        });
        mockClient.GetTorrentFile(hash).Returns((byte[])null);

        _indexerFactory.All().Returns(new List<IndexerDefinition>());

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>
        {
            new() { Id = 1, Name = "Transmission", ClientType = "Transmission", Enable = true }
        });

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(1));
        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
    }

    [Test]
    public void Sync_should_handle_client_get_items_exception_gracefully()
    {
        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetItems().Throws(new HttpRequestException("Connection refused"));

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>
        {
            new() { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent", Enable = true }
        });

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(1));
    }
}
