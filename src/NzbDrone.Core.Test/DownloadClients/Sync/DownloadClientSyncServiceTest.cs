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
}
