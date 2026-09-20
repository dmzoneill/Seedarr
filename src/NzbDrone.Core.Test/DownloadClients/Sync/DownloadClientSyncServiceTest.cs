using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using NLog;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.DownloadClients.Sync;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.DownloadClients.Sync;

[TestFixture]
public class DownloadClientSyncServiceTest
{
    private IDownloadClientFactory _downloadClientFactory;
    private IIndexerFactory _indexerFactory;
    private ITorrentService _torrentService;
    private ITorrentFileParser _torrentFileParser;
    private IRemotePathMappingService _remotePathMappingService;
    private TestableDownloadClientSyncService _service;

    private class TestableDownloadClientSyncService : DownloadClientSyncService
    {
        public IDownloadClient InjectedClient { get; set; }
        public IIndexer InjectedIndexer { get; set; }

        public TestableDownloadClientSyncService(
            IDownloadClientFactory downloadClientFactory,
            IIndexerFactory indexerFactory,
            ITorrentService torrentService,
            ITorrentFileParser torrentFileParser,
            IRemotePathMappingService remotePathMappingService = null)
            : base(downloadClientFactory, indexerFactory, torrentService, torrentFileParser, remotePathMappingService: remotePathMappingService)
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
        _remotePathMappingService = Substitute.For<IRemotePathMappingService>();
        _remotePathMappingService.Remap(Arg.Any<string>(), Arg.Any<string>()).Returns(x => x.ArgAt<string>(1));

        _service = new TestableDownloadClientSyncService(
            _downloadClientFactory,
            _indexerFactory,
            _torrentService,
            _torrentFileParser,
            _remotePathMappingService);
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

    [Test]
    public void GetClientItems_should_return_enriched_items_with_library_status()
    {
        var existingHash = "aaaa111122223333444455556666777788889999";
        var missingHash = "bbbb111122223333444455556666777788889999";

        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { Id = 42, InfoHash = existingHash, Name = "Existing Show" }
        });

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new()
            {
                DownloadId = "dl-1",
                Title = "Existing Show",
                InfoHash = existingHash,
                TotalSize = 1000,
                RemainingSize = 0,
                Status = "seeding",
                Category = "tv",
                OutputPath = "/downloads/tv",
                IsPrivate = true
            },
            new()
            {
                DownloadId = "dl-2",
                Title = "New Movie",
                InfoHash = missingHash,
                TotalSize = 2000,
                RemainingSize = 1000,
                Status = "downloading",
                Category = "movies",
                OutputPath = "/downloads/movies",
                IsPrivate = false
            }
        });

        _service.InjectedClient = mockClient;
        _downloadClientFactory.Get(1).Returns(new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBittorrent",
            ClientType = "QBitTorrent",
            Enable = true
        });

        var items = _service.GetClientItems(1);

        Assert.That(items, Has.Count.EqualTo(2));

        var first = items[0];
        Assert.That(first.Title, Is.EqualTo("Existing Show"));
        Assert.That(first.IsInLibrary, Is.True);
        Assert.That(first.LibraryTorrentId, Is.EqualTo(42));
        Assert.That(first.Progress, Is.EqualTo(100.0));
        Assert.That(first.IsPrivate, Is.True);

        var second = items[1];
        Assert.That(second.Title, Is.EqualTo("New Movie"));
        Assert.That(second.IsInLibrary, Is.False);
        Assert.That(second.LibraryTorrentId, Is.Null);
        Assert.That(second.Progress, Is.EqualTo(50.0));
        Assert.That(second.IsPrivate, Is.False);
    }

    [Test]
    public void GetClientItems_should_throw_when_client_not_found()
    {
        _downloadClientFactory.Get(99).Returns((DownloadClientDefinition)null);

        Assert.Throws<ArgumentException>(() => _service.GetClientItems(99));
    }

    [Test]
    public void ImportTorrent_should_add_torrent_and_return_instance()
    {
        var hash = "cccc111122223333444455556666777788889999";
        var rawBytes = new byte[] { 0x64, 0x38, 0x3a };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetTorrentFile(hash).Returns(rawBytes);

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(new ParsedTorrent
        {
            Name = "Imported Torrent",
            TotalSize = 5000,
            PieceCount = 25,
            PieceLength = 200
        });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.Get(1).Returns(new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBittorrent",
            ClientType = "QBitTorrent",
            Enable = true
        });

        var torrent = _service.ImportTorrent(1, hash);

        Assert.That(torrent, Is.Not.Null);
        Assert.That(torrent.Name, Is.EqualTo("Imported Torrent"));
        Assert.That(torrent.InfoHash, Is.EqualTo(hash));
        Assert.That(torrent.TotalSize, Is.EqualTo(5000));
        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.InfoHash == hash));
    }

    [Test]
    public void ImportTorrent_should_return_existing_if_already_in_library()
    {
        var hash = "dddd111122223333444455556666777788889999";
        var existingTorrent = new Torrent { Id = 10, InfoHash = hash, Name = "Already Exists" };

        _torrentService.GetAll().Returns(new List<Torrent> { existingTorrent });
        _downloadClientFactory.Get(1).Returns(new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBittorrent",
            ClientType = "QBitTorrent",
            Enable = true
        });

        var result = _service.ImportTorrent(1, hash);

        Assert.That(result, Is.SameAs(existingTorrent));
        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
    }

    [Test]
    public void ImportTorrents_should_import_multiple_hashes_and_aggregate_results()
    {
        var hash1 = "eeee111122223333444455556666777788889999";
        var hash2 = "ffff111122223333444455556666777788889999";
        var rawBytes = new byte[] { 0x64, 0x38, 0x3a };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetTorrentFile(hash1).Returns(rawBytes);
        mockClient.GetTorrentFile(hash2).Returns((byte[])null);

        _indexerFactory.All().Returns(new List<IndexerDefinition>());

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(new ParsedTorrent
        {
            Name = "Imported 1",
            TotalSize = 1000,
            PieceCount = 10,
            PieceLength = 100
        });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.Get(1).Returns(new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBittorrent",
            ClientType = "QBitTorrent",
            Enable = true
        });

        var result = _service.ImportTorrents(1, new List<string> { hash1, hash2 });

        Assert.That(result.Added, Is.EqualTo(1));
        Assert.That(result.Failed, Is.EqualTo(1));
        Assert.That(result.Items, Has.Count.EqualTo(2));
        Assert.That(result.Items[0].InfoHash, Is.EqualTo(hash1));
        Assert.That(result.Items[0].Success, Is.True);
        Assert.That(result.Items[0].ErrorMessage, Is.Null);
        Assert.That(result.Items[1].InfoHash, Is.EqualTo(hash2));
        Assert.That(result.Items[1].Success, Is.False);
        Assert.That(result.Items[1].ErrorMessage, Is.Not.Null);
    }

    [Test]
    public void ImportTorrents_should_call_GetItems_at_most_once_for_batch_import()
    {
        var hash1 = "1111111122223333444455556666777788889999";
        var hash2 = "2222222222223333444455556666777788889999";
        var hash3 = "3333333322223333444455556666777788889999";
        var rawBytes = new byte[] { 0x64, 0x38, 0x3a };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetTorrentFile(Arg.Any<string>()).Returns(rawBytes);
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new() { Title = "Item 1", InfoHash = hash1, TotalSize = 1000, RemainingSize = 0 },
            new() { Title = "Item 2", InfoHash = hash2, TotalSize = 2000, RemainingSize = 500 },
            new() { Title = "Item 3", InfoHash = hash3, TotalSize = 3000, RemainingSize = 0 }
        });

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(
            new ParsedTorrent { Name = "Item 1", TotalSize = 1000, PieceCount = 10, PieceLength = 100 },
            new ParsedTorrent { Name = "Item 2", TotalSize = 2000, PieceCount = 20, PieceLength = 100 },
            new ParsedTorrent { Name = "Item 3", TotalSize = 3000, PieceCount = 30, PieceLength = 100 });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.Get(1).Returns(new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBittorrent",
            ClientType = "QBitTorrent",
            Enable = true
        });

        var result = _service.ImportTorrents(1, new List<string> { hash1, hash2, hash3 });

        Assert.That(result.Added, Is.EqualTo(3));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));

        mockClient.Received(1).GetItems();
        _torrentService.Received(1).GetAll();
        _torrentService.Received(3).Add(Arg.Any<Torrent>());
    }

    [Test]
    public void ImportTorrents_should_skip_existing_hashes_and_call_GetItems_at_most_once()
    {
        var existingHash = "aaaa000022223333444455556666777788889999";
        var newHash = "bbbb000022223333444455556666777788889999";
        var rawBytes = new byte[] { 0x64, 0x38, 0x3a };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetTorrentFile(newHash).Returns(rawBytes);
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new() { Title = "New Item", InfoHash = newHash, TotalSize = 5000, RemainingSize = 0 }
        });

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(new ParsedTorrent
        {
            Name = "New Item",
            TotalSize = 5000,
            PieceCount = 25,
            PieceLength = 200
        });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { Id = 1, InfoHash = existingHash, Name = "Existing" }
        });
        _downloadClientFactory.Get(1).Returns(new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBittorrent",
            ClientType = "QBitTorrent",
            Enable = true
        });

        var result = _service.ImportTorrents(1, new List<string> { existingHash, newHash });

        Assert.That(result.Added, Is.EqualTo(1));
        Assert.That(result.Skipped, Is.EqualTo(1));
        Assert.That(result.Failed, Is.EqualTo(0));

        mockClient.Received(1).GetItems();
        _torrentService.Received(1).GetAll();
        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.InfoHash == newHash));
    }

    [TestCase("qbittorrent")]
    [TestCase("qBittorrent")]
    [TestCase("transmission")]
    [TestCase("deluge")]
    public void ImportTorrents_should_create_client_successfully_with_case_insensitive_client_type(string clientType)
    {
        var hash = "cccc000022223333444455556666777788889999";
        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetTorrentFile(hash).Returns(new byte[] { 0x64, 0x38, 0x3a });
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new() { Title = "Test Torrent", InfoHash = hash, TotalSize = 1000, RemainingSize = 0 }
        });

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(new ParsedTorrent
        {
            Name = "Test Torrent",
            TotalSize = 1000,
            PieceCount = 10,
            PieceLength = 100
        });

        _service.InjectedClient = null;
        var def = new DownloadClientDefinition
        {
            Id = 1,
            Name = "Dynamic Client",
            ClientType = clientType,
            Enable = true
        };
        _downloadClientFactory.Get(1).Returns(def);
        _downloadClientFactory.CreateClient(def).Returns(mockClient);
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _service.ImportTorrents(1, new List<string> { hash });

        Assert.That(result.Added, Is.EqualTo(1));
        Assert.That(result.Failed, Is.EqualTo(0));
        _downloadClientFactory.Received(1).CreateClient(def);
    }

    [Test]
    public void ImportTorrents_should_execute_bulk_import_efficiently_without_per_item_queries()
    {
        var hashes = Enumerable.Range(1, 50)
            .Select(i => $"{i:D4}000022223333444455556666777788889999")
            .ToList();

        var mockClient = Substitute.For<IDownloadClient>();
        var items = hashes.Select(h => new DownloadClientItem
        {
            Title = $"Item {h}",
            InfoHash = h,
            TotalSize = 1000,
            RemainingSize = 0
        }).ToList();

        mockClient.GetTorrentFile(Arg.Any<string>()).Returns(new byte[] { 0x64, 0x38, 0x3a });
        mockClient.GetItems().Returns(items);

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(callInfo => new ParsedTorrent
        {
            Name = "Parsed Item",
            TotalSize = 1000,
            PieceCount = 10,
            PieceLength = 100
        });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.Get(1).Returns(new DownloadClientDefinition
        {
            Id = 1,
            Name = "Bulk Client",
            ClientType = "QBitTorrent",
            Enable = true
        });

        var result = _service.ImportTorrents(1, hashes);

        Assert.That(result.Added, Is.EqualTo(50));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));

        // GetItems() and GetAll() MUST only be called ONCE upfront for the entire batch of 50
        mockClient.Received(1).GetItems();
        _torrentService.Received(1).GetAll();
        _torrentService.Received(50).Add(Arg.Any<Torrent>());
    }

    [Test]
    public void ImportTorrent_should_preserve_completed_downloaded_bytes_and_mark_completed_and_stopped()
    {
        var hash = "aaaa111122223333444455556666777788889999";
        var rawBytes = new byte[] { 0x64, 0x38, 0x3a };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetTorrentFile(hash).Returns(rawBytes);
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new()
            {
                Title = "Completed Linux ISO",
                InfoHash = hash,
                TotalSize = 8000,
                RemainingSize = 0,
                Status = "seeding"
            }
        });

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(new ParsedTorrent
        {
            Name = "Completed Linux ISO",
            TotalSize = 8000,
            PieceCount = 40,
            PieceLength = 200
        });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.Get(1).Returns(new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBittorrent",
            ClientType = "QBitTorrent",
            Enable = true
        });

        var torrent = _service.ImportTorrent(1, hash);

        Assert.That(torrent, Is.Not.Null);
        Assert.That(torrent.Downloaded, Is.EqualTo(8000));
        Assert.That(torrent.TotalSize, Is.EqualTo(8000));
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(torrent.ForceCompleted, Is.True);
        Assert.That(torrent.Progress, Is.EqualTo(1.0));

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.InfoHash == hash &&
            t.Downloaded == 8000 &&
            t.TotalSize == 8000 &&
            t.Status == TorrentStatus.Seeding &&
            t.ForceCompleted &&
            t.Progress == 1.0));
    }

    [Test]
    public void ImportTorrent_should_preserve_partial_downloaded_bytes_when_remaining_is_positive()
    {
        var hash = "bbbb111122223333444455556666777788889999";
        var rawBytes = new byte[] { 0x64, 0x38, 0x3a };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetTorrentFile(hash).Returns(rawBytes);
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new()
            {
                Title = "Partial Torrent",
                InfoHash = hash,
                TotalSize = 10000,
                RemainingSize = 4000,
                Status = "downloading"
            }
        });

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(new ParsedTorrent
        {
            Name = "Partial Torrent",
            TotalSize = 10000,
            PieceCount = 50,
            PieceLength = 200
        });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.Get(1).Returns(new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBittorrent",
            ClientType = "QBitTorrent",
            Enable = true
        });

        var torrent = _service.ImportTorrent(1, hash);

        Assert.That(torrent, Is.Not.Null);
        Assert.That(torrent.Downloaded, Is.EqualTo(6000));
        Assert.That(torrent.TotalSize, Is.EqualTo(10000));
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Downloading));
        Assert.That(torrent.ForceCompleted, Is.False);
        Assert.That(torrent.Progress, Is.EqualTo(0.6));

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.InfoHash == hash &&
            t.Downloaded == 6000 &&
            t.TotalSize == 10000 &&
            t.Status == TorrentStatus.Downloading &&
            !t.ForceCompleted &&
            t.Progress == 0.6));
    }

    [Test]
    public void ImportTorrent_should_preserve_downloaded_bytes_from_matching_item_when_raw_bytes_null()
    {
        var hash = "cccc222233334444555566667777888899990000";

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetTorrentFile(hash).Returns((byte[])null);
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new()
            {
                Title = "Completed Client Metadata Torrent",
                InfoHash = hash,
                TotalSize = 5000,
                RemainingSize = 0,
                IsPrivate = true,
                Status = "seeding"
            }
        });
        mockClient.GetTrackers(hash).Returns(new List<string> { "http://tracker.example.com/announce" });

        _indexerFactory.All().Returns(new List<IndexerDefinition>());
        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.Get(1).Returns(new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBittorrent",
            ClientType = "QBitTorrent",
            Enable = true
        });

        var torrent = _service.ImportTorrent(1, hash);

        Assert.That(torrent, Is.Not.Null);
        Assert.That(torrent.Downloaded, Is.EqualTo(5000));
        Assert.That(torrent.TotalSize, Is.EqualTo(5000));
        Assert.That(torrent.IsPrivate, Is.True);
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(torrent.ForceCompleted, Is.True);
        Assert.That(torrent.Progress, Is.EqualTo(1.0));

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.InfoHash == hash &&
            t.Downloaded == 5000 &&
            t.TotalSize == 5000 &&
            t.IsPrivate &&
            t.Status == TorrentStatus.Seeding &&
            t.ForceCompleted &&
            t.Progress == 1.0));
    }

    [Test]
    public void Sync_should_update_existing_torrent_status_progress_ratio_and_category()
    {
        var hash = "dddd111122223333444455556666777788889999";
        var existingTorrent = new Torrent
        {
            Id = 42,
            InfoHash = hash,
            Name = "Existing Torrent",
            TotalSize = 10000,
            Downloaded = 2000,
            Uploaded = 5000,
            Progress = 0.2,
            Status = TorrentStatus.Downloading,
            Category = "old-category"
        };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new()
            {
                Title = "Existing Torrent",
                InfoHash = hash,
                TotalSize = 10000,
                RemainingSize = 0,
                Status = "seeding",
                Category = "radarr",
                OutputPath = "/downloads/radarr"
            }
        });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent> { existingTorrent });
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>
        {
            new() { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent", Enable = true }
        });

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(1));
        Assert.That(result.Failed, Is.EqualTo(0));

        Assert.That(existingTorrent.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(existingTorrent.Progress, Is.EqualTo(1.0));
        Assert.That(existingTorrent.Downloaded, Is.EqualTo(10000));
        Assert.That(existingTorrent.Category, Is.EqualTo("radarr"));
        Assert.That(existingTorrent.SavePath, Is.EqualTo("/downloads/radarr"));
        Assert.That(existingTorrent.SourcePath, Is.EqualTo("/downloads/radarr"));
        Assert.That(existingTorrent.ForceCompleted, Is.True);
        Assert.That(existingTorrent.Ratio, Is.EqualTo(0.5));

        _torrentService.Received(1).Update(existingTorrent);
    }

    [Test]
    public void ImportTorrent_should_map_status_from_client_status()
    {
        var hash = "eeee111122223333444455556666777788889999";
        var rawBytes = new byte[] { 0x64, 0x38, 0x3a };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetTorrentFile(hash).Returns(rawBytes);
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new()
            {
                Title = "Paused Torrent",
                InfoHash = hash,
                TotalSize = 8000,
                RemainingSize = 4000,
                Status = "paused"
            }
        });

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(new ParsedTorrent
        {
            Name = "Paused Torrent",
            TotalSize = 8000,
            PieceCount = 40,
            PieceLength = 200
        });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.Get(1).Returns(new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBittorrent",
            ClientType = "QBitTorrent",
            Enable = true
        });

        var torrent = _service.ImportTorrent(1, hash);

        Assert.That(torrent, Is.Not.Null);
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Paused));
    }

    [Test]
    public void Sync_should_serialize_concurrent_calls_using_syncLock()
    {
        var mockClient = Substitute.For<IDownloadClient>();
        var syncCount = 0;
        var maxConcurrent = 0;
        var runningCount = 0;
        var lockObj = new object();

        mockClient.GetItems().Returns(_ =>
        {
            lock (lockObj)
            {
                runningCount++;
                if (runningCount > maxConcurrent)
                {
                    maxConcurrent = runningCount;
                }
            }

            System.Threading.Thread.Sleep(50);

            lock (lockObj)
            {
                runningCount--;
                syncCount++;
            }

            return new List<DownloadClientItem>();
        });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>
        {
            new() { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent", Enable = true }
        });

        var task1 = System.Threading.Tasks.Task.Run(() => _service.Sync());
        var task2 = System.Threading.Tasks.Task.Run(() => _service.Sync());

        System.Threading.Tasks.Task.WaitAll(task1, task2);

        Assert.That(maxConcurrent, Is.EqualTo(1));
        Assert.That(syncCount, Is.EqualTo(2));
    }

    [Test]
    public void Sync_should_remap_torrent_SavePath_and_SourcePath_using_RemotePathMappingService()
    {
        var hash = "aaaa111122223333444455556666777788889999";
        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetTorrentFile(hash).Returns(new byte[] { 0x64, 0x38, 0x3a });
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new()
            {
                Title = "Test Torrent",
                InfoHash = hash,
                TotalSize = 1000,
                RemainingSize = 0,
                OutputPath = "/remote/downloads/movies/test",
                Status = "seeding"
            }
        });

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(new ParsedTorrent
        {
            Name = "Test Torrent",
            TotalSize = 1000
        });

        _remotePathMappingService.Remap("192.168.1.50", "/remote/downloads/movies/test")
            .Returns("/local/media/movies/test");

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>
        {
            new() { Id = 1, Name = "Remote qBit", ClientType = "QBitTorrent", Host = "192.168.1.50", Enable = true }
        });

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(1));
        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.InfoHash == hash &&
            t.SavePath == "/local/media/movies/test" &&
            t.SourcePath == "/local/media/movies/test"));
    }

    [Test]
    public void ImportTorrent_should_remap_torrent_SavePath_and_SourcePath_using_RemotePathMappingService()
    {
        var hash = "bbbb111122223333444455556666777788889999";
        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetTorrentFile(hash).Returns(new byte[] { 0x64, 0x38, 0x3a });
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new()
            {
                Title = "Imported Torrent",
                InfoHash = hash,
                TotalSize = 5000,
                RemainingSize = 0,
                OutputPath = @"D:\Downloads\Torrents\Imported",
                Status = "seeding"
            }
        });

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(new ParsedTorrent
        {
            Name = "Imported Torrent",
            TotalSize = 5000,
            PieceCount = 10,
            PieceLength = 500
        });

        _remotePathMappingService.Remap("qbit-box", @"D:\Downloads\Torrents\Imported")
            .Returns("/data/torrents/Imported");

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.Get(1).Returns(new DownloadClientDefinition
        {
            Id = 1,
            Name = "Windows qBit",
            ClientType = "QBitTorrent",
            Host = "qbit-box",
            Enable = true
        });

        var torrent = _service.ImportTorrent(1, hash);

        Assert.That(torrent, Is.Not.Null);
        Assert.That(torrent.SavePath, Is.EqualTo("/data/torrents/Imported"));
        Assert.That(torrent.SourcePath, Is.EqualTo("/data/torrents/Imported"));
    }

    [Test]
    public void Sync_should_prevent_overlapping_sync_sweeps()
    {
        var sync1Started = new ManualResetEventSlim(false);
        var allowSync1ToFinish = new ManualResetEventSlim(false);

        _torrentService.GetAll().Returns(x =>
        {
            sync1Started.Set();
            allowSync1ToFinish.Wait(5000);
            return new List<Torrent>();
        });

        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>
        {
            new() { Id = 1, Name = "qBit", ClientType = "QBitTorrent", Enable = true }
        });

        var task1 = System.Threading.Tasks.Task.Run(() => _service.Sync());

        Assert.That(sync1Started.Wait(5000), Is.True);

        var result2 = _service.Sync();

        allowSync1ToFinish.Set();
        task1.GetAwaiter().GetResult();

        Assert.That(result2.Added, Is.EqualTo(0));
        Assert.That(result2.Skipped, Is.EqualTo(0));
        Assert.That(result2.Failed, Is.EqualTo(0));

        _downloadClientFactory.Received(1).All();
    }

    [Test]
    public void Sync_should_reconcile_and_update_existing_torrent_status_progress_downloaded_and_speeds()
    {
        var hash = "9999111122223333444455556666777788889999";
        var existingTorrent = new Torrent
        {
            Id = 99,
            InfoHash = hash,
            Name = "Active Torrent",
            TotalSize = 10000,
            Downloaded = 4000,
            Uploaded = 1000,
            Progress = 0.4,
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 0,
            UploadSpeed = 0
        };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new()
            {
                Title = "Active Torrent",
                InfoHash = hash,
                TotalSize = 10000,
                RemainingSize = 0,
                Status = "seeding",
                DownloadSpeed = 0,
                UploadSpeed = 250000
            }
        });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent> { existingTorrent });
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>
        {
            new() { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent", Enable = true }
        });

        var result = _service.Sync();

        Assert.That(result.Updated, Is.EqualTo(1));
        Assert.That(existingTorrent.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(existingTorrent.Progress, Is.EqualTo(1.0));
        Assert.That(existingTorrent.Downloaded, Is.EqualTo(10000));
        Assert.That(existingTorrent.UploadSpeed, Is.EqualTo(250000));
        _torrentService.Received(1).Update(existingTorrent);
    }

    [Test]
    public void ImportTorrent_should_map_client_status_seeding_on_initial_import_instead_of_hardcoded_stopped()
    {
        var hash = "8888111122223333444455556666777788889999";
        var rawBytes = new byte[] { 0x64, 0x38, 0x3a };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetTorrentFile(hash).Returns(rawBytes);
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new()
            {
                Title = "Seeding Torrent",
                InfoHash = hash,
                TotalSize = 20000,
                RemainingSize = 0,
                Status = "seeding"
            }
        });

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(new ParsedTorrent
        {
            Name = "Seeding Torrent",
            TotalSize = 20000,
            PieceCount = 20,
            PieceLength = 1000
        });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.Get(1).Returns(new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBittorrent",
            ClientType = "QBitTorrent",
            Enable = true
        });

        var torrent = _service.ImportTorrent(1, hash);

        Assert.That(torrent, Is.Not.Null);
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(torrent.Progress, Is.EqualTo(1.0));
        Assert.That(torrent.Downloaded, Is.EqualTo(20000));
        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.InfoHash == hash &&
            t.Status == TorrentStatus.Seeding &&
            t.Progress == 1.0 &&
            t.Downloaded == 20000));
    }

    [Test]
    public void Sync_should_map_client_status_seeding_on_initial_sync_instead_of_hardcoded_stopped()
    {
        var hash = "7777111122223333444455556666777788889999";
        var rawBytes = new byte[] { 0x64, 0x38, 0x3a };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetTorrentFile(hash).Returns(rawBytes);
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new()
            {
                Title = "Initial Sync Seeding",
                InfoHash = hash,
                TotalSize = 30000,
                RemainingSize = 0,
                Status = "seeding"
            }
        });

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(new ParsedTorrent
        {
            Name = "Initial Sync Seeding",
            TotalSize = 30000,
            PieceCount = 30,
            PieceLength = 1000
        });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>
        {
            new() { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent", Enable = true }
        });

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(1));
        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.InfoHash == hash &&
            t.Status == TorrentStatus.Seeding &&
            t.Progress == 1.0 &&
            t.Downloaded == 30000));
    }

    [Test]
    public void Sync_should_calculate_progress_as_one_point_zero_when_remaining_size_is_zero()
    {
        var hash = "6666111122223333444455556666777788889999";
        var rawBytes = new byte[] { 0x64, 0x38, 0x3a };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetTorrentFile(hash).Returns(rawBytes);
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new()
            {
                Title = "Zero Remaining Item",
                InfoHash = hash,
                TotalSize = 0,
                RemainingSize = 0,
                Status = "seeding"
            }
        });

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(new ParsedTorrent
        {
            Name = "Zero Remaining Item",
            TotalSize = 0,
            PieceCount = 0,
            PieceLength = 0
        });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>
        {
            new() { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent", Enable = true }
        });

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(1));
        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.InfoHash == hash &&
            t.Progress == 1.0));
    }

    [Test]
    public void ImportTorrent_should_attribute_client_ownership_fallback_category_and_source_path()
    {
        var hash = "9999111122223333444455556666777788889999";
        var rawBytes = new byte[] { 0x64, 0x38, 0x3a };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetTorrentFile(hash).Returns(rawBytes);
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new()
            {
                Title = "Attributed Item",
                InfoHash = hash,
                TotalSize = 5000,
                OutputPath = "/remote/media/downloads"
            }
        });

        _torrentFileParser.Parse(Arg.Any<Stream>()).Returns(new ParsedTorrent
        {
            Name = "Attributed Item",
            TotalSize = 5000,
            PieceCount = 10,
            PieceLength = 500
        });

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.Get(7).Returns(new DownloadClientDefinition
        {
            Id = 7,
            Name = "Client 7",
            ClientType = "QBitTorrent",
            Enable = true,
            Category = "default-category",
            Host = "localhost"
        });

        var torrent = _service.ImportTorrent(7, hash);

        Assert.That(torrent, Is.Not.Null);
        Assert.That(torrent.DownloadClientId, Is.EqualTo(7));
        Assert.That(torrent.Category, Is.EqualTo("default-category"));
        Assert.That(torrent.SourcePath, Is.EqualTo("/remote/media/downloads"));

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.InfoHash == hash &&
            t.DownloadClientId == 7 &&
            t.Category == "default-category" &&
            t.SourcePath == "/remote/media/downloads"));
    }

    [Test]
    public void GetClientItems_should_isolate_library_status_by_download_client_id()
    {
        var hashOwnedByOther = "1111000000000000000000000000000000000001";
        var hashOwnedByThis = "2222000000000000000000000000000000000002";
        var hashLegacy = "3333000000000000000000000000000000000003";

        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { Id = 101, InfoHash = hashOwnedByOther, DownloadClientId = 2 },
            new() { Id = 102, InfoHash = hashOwnedByThis, DownloadClientId = 1 },
            new() { Id = 103, InfoHash = hashLegacy, DownloadClientId = null }
        });

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new() { Title = "Other Client Item", InfoHash = hashOwnedByOther },
            new() { Title = "This Client Item", InfoHash = hashOwnedByThis },
            new() { Title = "Legacy Item", InfoHash = hashLegacy }
        });

        _service.InjectedClient = mockClient;
        _downloadClientFactory.Get(1).Returns(new DownloadClientDefinition
        {
            Id = 1,
            Name = "qBittorrent",
            ClientType = "QBitTorrent",
            Enable = true
        });

        var items = _service.GetClientItems(1);

        Assert.That(items, Has.Count.EqualTo(3));
        Assert.That(items[0].IsInLibrary, Is.False);
        Assert.That(items[0].LibraryTorrentId, Is.Null);

        Assert.That(items[1].IsInLibrary, Is.True);
        Assert.That(items[1].LibraryTorrentId, Is.EqualTo(102));

        Assert.That(items[2].IsInLibrary, Is.True);
        Assert.That(items[2].LibraryTorrentId, Is.EqualTo(103));
    }

    [Test]
    public void Sync_should_record_failure_and_skip_client_when_in_backoff()
    {
        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetItems().Returns(x => throw new DownloadClientUnavailableException("Connection refused"));

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        var clientDef = new DownloadClientDefinition
        {
            Id = 1,
            Name = "Unreachable qBit",
            ClientType = "QBitTorrent",
            Enable = true
        };
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition> { clientDef });

        // First sync -> throws and sets failure status
        var result1 = _service.Sync();
        Assert.That(result1.Failed, Is.EqualTo(1));

        var status = _service.GetClientStatus(1);
        Assert.That(status, Is.Not.Null);
        Assert.That(status.IsOnline, Is.False);
        Assert.That(status.ConsecutiveFailures, Is.EqualTo(1));
        Assert.That(status.BackoffUntil, Is.Not.Null);
        Assert.That(status.BackoffUntil.Value, Is.GreaterThan(DateTime.UtcNow));

        // Clear mock calls
        mockClient.ClearReceivedCalls();

        // Second sync -> client is in backoff, so GetItems should NOT be called
        var result2 = _service.Sync();
        mockClient.DidNotReceive().GetItems();
        Assert.That(result2.Failed, Is.EqualTo(0));
    }

    [Test]
    public void Sync_should_reset_circuit_breaker_on_successful_sync()
    {
        var mockFailingClient = Substitute.For<IDownloadClient>();
        mockFailingClient.GetItems().Returns(x => throw new DownloadClientUnavailableException("Temporary glitch"));

        _service.InjectedClient = mockFailingClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        var clientDef = new DownloadClientDefinition
        {
            Id = 1,
            Name = "Glitchy qBit",
            ClientType = "QBitTorrent",
            Enable = true
        };
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition> { clientDef });

        // Trigger failure
        _service.Sync();
        var failedStatus = _service.GetClientStatus(1);
        Assert.That(failedStatus.ConsecutiveFailures, Is.EqualTo(1));

        // Reset backoff explicitly so it can try again
        _service.ResetClientStatus(1);

        // Client recovers
        var mockHealthyClient = Substitute.For<IDownloadClient>();
        mockHealthyClient.GetItems().Returns(new List<DownloadClientItem>());
        _service.InjectedClient = mockHealthyClient;
        _service.Sync();

        var recoveredStatus = _service.GetClientStatus(1);
        Assert.That(recoveredStatus.IsOnline, Is.True);
        Assert.That(recoveredStatus.ConsecutiveFailures, Is.EqualTo(0));
        Assert.That(recoveredStatus.BackoffUntil, Is.Null);
    }

    [Test]
    public void GetClientItems_should_record_failure_when_client_throws()
    {
        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetItems().Returns(x => throw new DownloadClientAuthenticationException("Invalid password"));

        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());
        _downloadClientFactory.Get(2).Returns(new DownloadClientDefinition
        {
            Id = 2,
            Name = "Auth Fail qBit",
            ClientType = "QBitTorrent",
            Enable = true
        });

        Assert.Throws<DownloadClientAuthenticationException>(() => _service.GetClientItems(2));

        var status = _service.GetClientStatus(2);
        Assert.That(status, Is.Not.Null);
        Assert.That(status.IsOnline, Is.False);
        Assert.That(status.ConsecutiveFailures, Is.EqualTo(1));
        Assert.That(status.LastErrorMessage, Does.Contain("Invalid password"));
    }

    [TestCase(1, 30)]
    [TestCase(2, 60)]
    [TestCase(3, 120)]
    [TestCase(4, 240)]
    [TestCase(5, 480)]
    [TestCase(6, 600)]
    [TestCase(10, 600)]
    public void CalculateBackoff_should_scale_exponentially_and_cap_at_max_backoff(int failures, int expectedSeconds)
    {
        var backoff = DownloadClientSyncService.CalculateBackoff(failures);
        Assert.That(backoff.TotalSeconds, Is.EqualTo(expectedSeconds));
    }

    [Test]
    public void GetAllClientItems_should_aggregate_items_from_all_enabled_clients()
    {
        var client1Def = new DownloadClientDefinition { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent", Enable = true };
        var client2Def = new DownloadClientDefinition { Id = 2, Name = "Transmission", ClientType = "Transmission", Enable = true };
        var disabledDef = new DownloadClientDefinition { Id = 3, Name = "Deluge", ClientType = "Deluge", Enable = false };

        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition> { client1Def, client2Def, disabledDef });
        _downloadClientFactory.Get(1).Returns(client1Def);
        _downloadClientFactory.Get(2).Returns(client2Def);

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new() { Title = "Linux ISO", InfoHash = "hash1", TotalSize = 1000 }
        });
        _service.InjectedClient = mockClient;
        _torrentService.GetAll().Returns(new List<Torrent>());

        var allItems = _service.GetAllClientItems();

        Assert.That(allItems, Has.Count.EqualTo(2));
        Assert.That(allItems[0].ClientId, Is.EqualTo(1));
        Assert.That(allItems[0].ClientName, Is.EqualTo("qBittorrent"));
        Assert.That(allItems[1].ClientId, Is.EqualTo(2));
        Assert.That(allItems[1].ClientName, Is.EqualTo("Transmission"));
    }
}
