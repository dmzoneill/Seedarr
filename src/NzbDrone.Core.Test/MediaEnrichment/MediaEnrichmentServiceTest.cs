using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.MediaEnrichment;

[TestFixture]
public class MediaEnrichmentServiceTest
{
    private ITorrentMediaMetadataRepository _repository;
    private IMediaContainerInspector _inspector;
    private IConfigService _configService;
    private IAppFolderInfo _appFolderInfo;
    private IEventAggregator _eventAggregator;
    private IArrConnectionRepository _arrRepository;
    private IArrConnectionFactory _connectionFactory;
    private ITmdbMetadataProvider _tmdbProvider;
    private MediaEnrichmentService _service;
    private string _tempDirectory;

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "seedarr_enrichment_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _repository = Substitute.For<ITorrentMediaMetadataRepository>();
        _inspector = Substitute.For<IMediaContainerInspector>();
        _configService = Substitute.For<IConfigService>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _arrRepository = Substitute.For<IArrConnectionRepository>();
        _connectionFactory = Substitute.For<IArrConnectionFactory>();
        _tmdbProvider = Substitute.For<ITmdbMetadataProvider>();

        _appFolderInfo.AppDataFolder.Returns(_tempDirectory);
        _configService.AutoPruneRemovedArtwork.Returns(true);
        _repository.Upsert(Arg.Any<TorrentMediaMetadata>()).Returns(x => x.Arg<TorrentMediaMetadata>());

        _service = new MediaEnrichmentService(
            _repository,
            _inspector,
            _configService,
            _appFolderInfo,
            _eventAggregator,
            _arrRepository,
            _connectionFactory,
            _tmdbProvider);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Test]
    public async Task EnrichTorrentAsync_WhenTorrentNull_ReturnsNull()
    {
        var result = await _service.EnrichTorrentAsync(null);
        Assert.That(result, Is.Null);
    }

    [TestCase("tv", "Severance.S02E01.2160p.ATVP.WEB-DL", "Sonarr")]
    [TestCase("sonarr-shows", "The.Bear.S03E01.1080p.WEB-DL", "Sonarr")]
    [TestCase("shows", "Game.of.Thrones.Season.1.1080p", "Sonarr")]
    [TestCase("series", "Chernobyl.Season.1.Complete", "Sonarr")]
    [TestCase("radarr-movies", "Oppenheimer.2023.2160p.UHD.BluRay", "Radarr")]
    [TestCase("movies", "Dune.Part.Two.2024.1080p.Remux", "Radarr")]
    [TestCase("films", "The.Godfather.1972.2160p", "Radarr")]
    [TestCase("music", "Pink.Floyd-The.Dark.Side.Of.The.Moon.1973.FLAC", "Lidarr")]
    [TestCase("lidarr-albums", "Daft.Punk-Discovery.2001.FLAC", "Lidarr")]
    [TestCase("albums", "Radiohead-OK.Computer.1997.MP3", "Lidarr")]
    [TestCase("books", "Stephen.King-The.Shining.EPUB", "Readarr")]
    [TestCase("readarr-ebooks", "Frank.Herbert-Dune.MOBI", "Readarr")]
    [TestCase("whisparr", "Scene.Title.2024.1080p", "Whisparr")]
    [TestCase("adult", "Scene.Title.2024.1080p", "Whisparr")]
    [TestCase("other", "Random.Archive.Release.zip", "Unknown")]
    public async Task EnrichTorrentAsync_ArrTypeClassification_CorrectlyMapsToServarrApp(
        string label, string name, string expectedArrType)
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = name,
            Label = label,
        };

        _repository.GetByTorrentId(42).Returns((TorrentMediaMetadata)null);

        var result = await _service.EnrichTorrentAsync(torrent);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.TorrentId, Is.EqualTo(42));
        Assert.That(result.Title, Is.EqualTo(name));
        Assert.That(result.ArrType, Is.EqualTo(expectedArrType));
        _repository.Received(1).Upsert(result);
        _repository.DidNotReceive().Insert(Arg.Any<TorrentMediaMetadata>());
        _repository.DidNotReceive().Update(Arg.Any<TorrentMediaMetadata>());
        _eventAggregator.Received(1).PublishEvent(Arg.Is<MediaEnrichedEvent>(e => e.TorrentId == 42));
    }

    [Test]
    public async Task EnrichTorrentAsync_WhenMetadataAlreadyExists_UpdatesExistingRecord()
    {
        var torrent = new Torrent
        {
            Id = 10,
            Name = "Existing.Movie.2024.1080p",
            Label = "movies",
        };

        var existing = new TorrentMediaMetadata
        {
            Id = 5,
            TorrentId = 10,
            Title = "Existing Movie",
        };

        _repository.GetByTorrentId(10).Returns(existing);

        var result = await _service.EnrichTorrentAsync(torrent);

        Assert.That(result, Is.SameAs(existing));
        _repository.Received(1).Upsert(existing);
        _repository.DidNotReceive().Insert(Arg.Any<TorrentMediaMetadata>());
        _repository.DidNotReceive().Update(Arg.Any<TorrentMediaMetadata>());
        _eventAggregator.Received(1).PublishEvent(Arg.Is<MediaEnrichedEvent>(e => e.TorrentId == 10));
    }

    [Test]
    public async Task EnrichTorrentAsync_ConcurrentCallsForSameTorrent_CompleteSuccessfullyAndUpsertMetadata()
    {
        var torrent = new Torrent
        {
            Id = 55,
            Name = "Concurrent.Movie.2024.1080p",
            Label = "movies",
        };

        var tasks = System.Linq.Enumerable.Range(0, 10).Select(_ => _service.EnrichTorrentAsync(torrent));
        var results = await Task.WhenAll(tasks);

        Assert.That(results, Has.Length.EqualTo(10));
        foreach (var r in results)
        {
            Assert.That(r, Is.Not.Null);
            Assert.That(r.TorrentId, Is.EqualTo(55));
        }

        _repository.Received().Upsert(Arg.Is<TorrentMediaMetadata>(m => m.TorrentId == 55));
    }

    [Test]
    public async Task DeleteMetadata_PrunesDatabaseAndArtworkCache()
    {
        var meta = new TorrentMediaMetadata
        {
            Id = 1,
            TorrentId = 42,
            PosterLocalPath = Path.Combine(_tempDirectory, "poster.jpg"),
            BackdropLocalPath = Path.Combine(_tempDirectory, "backdrop.jpg"),
        };

        // Create dummy artwork files
        await File.WriteAllBytesAsync(meta.PosterLocalPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
        await File.WriteAllBytesAsync(meta.BackdropLocalPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

        _repository.GetByTorrentId(42).Returns(meta);

        _service.DeleteMetadata(42);

        _repository.Received(1).DeleteByTorrentId(42);
        Assert.That(File.Exists(meta.PosterLocalPath), Is.False);
        Assert.That(File.Exists(meta.BackdropLocalPath), Is.False);
    }

    [Test]
    public void Handle_TorrentDeletedEvent_DeletesMetadata()
    {
        var meta = new TorrentMediaMetadata { Id = 1, TorrentId = 77 };
        _repository.GetByTorrentId(77).Returns(meta);

        _service.Handle(new TorrentDeletedEvent(77));

        _repository.Received(1).DeleteByTorrentId(77);
    }

    [Test]
    public async Task CacheArtworkAsync_WhenLocalValidImage_CopiesToMediaCoverDir()
    {
        var sourceImage = Path.Combine(_tempDirectory, "source.jpg");
        // Valid JPEG header
        var validJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        await File.WriteAllBytesAsync(sourceImage, validJpeg);

        var cachedPath = await _service.CacheArtworkAsync(sourceImage, 101, "poster");

        Assert.That(cachedPath, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(cachedPath), Is.True);
        Assert.That(cachedPath, Does.Contain("101"));
        Assert.That(cachedPath, Does.EndWith("poster.jpg"));
    }

    [Test]
    public void CleanReleaseTitle_StripsSceneTagsAndExtensions()
    {
        var cleaned = MediaEnrichmentService.CleanReleaseTitle("The.Matrix.1999.2160p.UHD.BluRay.x265-GROUP.mkv");
        Assert.That(cleaned, Is.EqualTo("The Matrix"));
    }

    [Test]
    public void CleanReleaseTitle_ProtectsSensitiveKeywordsAndAnimeBrackets()
    {
        Assert.That(MediaEnrichmentService.CleanReleaseTitle("Charlotte's Web (2006)"), Is.EqualTo("Charlotte's Web"));
        Assert.That(MediaEnrichmentService.CleanReleaseTitle("A Season in Hell (1991)"), Is.EqualTo("A Season in Hell"));
        Assert.That(MediaEnrichmentService.CleanReleaseTitle("The Complete Walk (2016)"), Is.EqualTo("The Complete Walk"));
        Assert.That(MediaEnrichmentService.CleanReleaseTitle("[SubsPlease] Sousou no Frieren - 01 (1080p) [F2E4A9B1].mkv"), Is.EqualTo("Sousou no Frieren"));
    }

    [Test]
    public void ExtractYear_FindsValidYear()
    {
        var year = MediaEnrichmentService.ExtractYear("Inception.2010.1080p.BluRay");
        Assert.That(year, Is.EqualTo(2010));
    }

    [Test]
    public void IsValidImage_ValidatesHeaders()
    {
        Assert.That(MediaEnrichmentService.IsValidImage(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }), Is.True); // JPEG
        Assert.That(MediaEnrichmentService.IsValidImage(new byte[] { 0x89, 0x50, 0x4E, 0x47 }), Is.True); // PNG
        Assert.That(MediaEnrichmentService.IsValidImage(new byte[] { 0x00, 0x00, 0x00, 0x00 }), Is.False); // Invalid
    }

    [Test]
    public async Task EnrichTorrentAsync_WhenArrReturnsNoMetadata_FallsBackToTmdbProvider()
    {
        _arrRepository.All().Returns(new List<ArrConnectionDefinition>());

        var tmdbMeta = new TorrentMediaMetadata
        {
            Title = "Oppenheimer",
            Year = 2023,
            Overview = "The story of J. Robert Oppenheimer.",
            Rating = 8.9,
            PosterUrl = "https://image.tmdb.org/t/p/w500/poster.jpg",
            BackdropUrl = "https://image.tmdb.org/t/p/original/backdrop.jpg",
            Genres = "Biography, Drama, History",
            Cast = "Cillian Murphy, Emily Blunt",
            TmdbId = "872585",
            ArrType = "Radarr",
        };

        _tmdbProvider.LookupMediaAsync(
            Arg.Any<string>(),
            Arg.Any<int?>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>()).Returns(tmdbMeta);

        var torrent = new Torrent
        {
            Id = 88,
            Name = "Oppenheimer.2023.2160p.UHD",
            Label = "radarr-movies",
        };

        var result = await _service.EnrichTorrentAsync(torrent);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Title, Is.EqualTo("Oppenheimer"));
        Assert.That(result.Year, Is.EqualTo(2023));
        Assert.That(result.Rating, Is.EqualTo(8.9));
        Assert.That(result.Genres, Is.EqualTo("Biography, Drama, History"));
        Assert.That(result.Cast, Is.EqualTo("Cillian Murphy, Emily Blunt"));
        Assert.That(result.TmdbId, Is.EqualTo("872585"));
        _repository.Received(1).Upsert(Arg.Is<TorrentMediaMetadata>(m => m.Title == "Oppenheimer" && m.TmdbId == "872585"));
    }

    [Test]
    public async Task EnrichTorrentAsync_WhenTorrentHasImdbId_PassesImdbIdToTmdbProvider()
    {
        _arrRepository.All().Returns(new List<ArrConnectionDefinition>());

        _tmdbProvider.LookupMediaAsync(
            Arg.Any<string>(),
            Arg.Any<int?>(),
            "tt1375666",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>()).Returns(new TorrentMediaMetadata
        {
            Title = "Inception",
            Year = 2010,
            ImdbId = "tt1375666",
            TmdbId = "27205",
        });

        var torrent = new Torrent
        {
            Id = 89,
            Name = "Inception.2010.tt1375666.1080p",
            Label = "movies",
        };

        var result = await _service.EnrichTorrentAsync(torrent);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Title, Is.EqualTo("Inception"));
        Assert.That(result.ImdbId, Is.EqualTo("tt1375666"));
        await _tmdbProvider.Received(1).LookupMediaAsync(
            Arg.Any<string>(),
            Arg.Any<int?>(),
            "tt1375666",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExtractImdbId_ExtractsFromTitleAndNfoFile()
    {
        var idFromTitle = MediaEnrichmentService.ExtractImdbId("Fight.Club.1999.tt0137523.mkv");
        Assert.That(idFromTitle, Is.EqualTo("tt0137523"));

        var nfoPath = Path.Combine(_tempDirectory, "movie.nfo");
        await File.WriteAllTextAsync(nfoPath, "Movie details at https://www.imdb.com/title/tt0137523/ enjoy!");

        var idFromNfo = MediaEnrichmentService.ExtractImdbId("Fight.Club.1999.mkv", nfoPath);
        Assert.That(idFromNfo, Is.EqualTo("tt0137523"));
    }

    [Test]
    public async Task EnrichTorrentAsync_QueriesArrConnectionAsyncMethods()
    {
        var mockProvider = Substitute.For<IArrConnection>();
        mockProvider.GetDownloadHistoryAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ArrDownloadRecord>
            {
                new ArrDownloadRecord { InfoHash = "hash123", MediaId = 999 }
            }));
        mockProvider.GetMediaDetailsAsync(999, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MediaMetadata
            {
                Title = "Arr Show Title",
                Year = 2024,
                MediaType = "series"
            }));

        var service = new TestableMediaEnrichmentService(
            _repository,
            _inspector,
            _configService,
            _appFolderInfo,
            _eventAggregator,
            _arrRepository,
            _connectionFactory,
            mockProvider);

        _arrRepository.All().Returns(new List<ArrConnectionDefinition>
        {
            new ArrConnectionDefinition { Id = 1, Name = "Sonarr", ArrType = "Sonarr", Enable = true }
        });

        var torrent = new Torrent { Id = 99, Name = "Test.Show", InfoHash = "hash123" };
        var result = await service.EnrichTorrentAsync(torrent);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Title, Is.EqualTo("Arr Show Title"));
        await mockProvider.Received(1).GetDownloadHistoryAsync(Arg.Any<CancellationToken>());
        await mockProvider.Received(1).GetMediaDetailsAsync(999, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CacheArtworkAsync_InvalidOrTraversalType_ReturnsNull()
    {
        var sourceImage = Path.Combine(_tempDirectory, "source.jpg");
        var validJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        await File.WriteAllBytesAsync(sourceImage, validJpeg);

        Assert.That(await _service.CacheArtworkAsync(sourceImage, 101, "../poster"), Is.Null);
        Assert.That(await _service.CacheArtworkAsync(sourceImage, 101, "../../etc/passwd"), Is.Null);
        Assert.That(await _service.CacheArtworkAsync(sourceImage, 101, "invalid_type"), Is.Null);
        Assert.That(await _service.CacheArtworkAsync(sourceImage, 101, string.Empty), Is.Null);
        Assert.That(await _service.CacheArtworkAsync(sourceImage, 101, null), Is.Null);
    }

    [Test]
    public async Task CacheArtworkAsync_StreamExceeding15Mb_AbortsAndDeletesTempFile()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new RepeatingStream(16 * 1024 * 1024)),
        });
        using var httpClient = new HttpClient(handler);
        var service = new MediaEnrichmentService(
            _repository,
            _inspector,
            _configService,
            _appFolderInfo,
            _eventAggregator,
            _arrRepository,
            _connectionFactory,
            httpClient);

        var result = await service.CacheArtworkAsync("https://example.com/poster.jpg", 102, "poster");

        Assert.That(result, Is.Null);
        var cacheDir = Path.Combine(_tempDirectory, "MediaCover", "102");
        if (Directory.Exists(cacheDir))
        {
            var tmpFiles = Directory.EnumerateFiles(cacheDir, "*.tmp.*").ToList();
            Assert.That(tmpFiles, Is.Empty);
            Assert.That(File.Exists(Path.Combine(cacheDir, "poster.jpg")), Is.False);
        }
    }

    [Test]
    public async Task CacheArtworkAsync_CorruptedMagicBytes_QuarantinesOrDeletesTempFile()
    {
        var invalidBytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(invalidBytes),
        });
        using var httpClient = new HttpClient(handler);
        var service = new MediaEnrichmentService(
            _repository,
            _inspector,
            _configService,
            _appFolderInfo,
            _eventAggregator,
            _arrRepository,
            _connectionFactory,
            httpClient);

        var result = await service.CacheArtworkAsync("https://example.com/poster.jpg", 103, "poster");

        Assert.That(result, Is.Null);
        var cacheDir = Path.Combine(_tempDirectory, "MediaCover", "103");
        if (Directory.Exists(cacheDir))
        {
            var tmpFiles = Directory.EnumerateFiles(cacheDir, "*.tmp.*").ToList();
            Assert.That(tmpFiles, Is.Empty);
            Assert.That(File.Exists(Path.Combine(cacheDir, "poster.jpg")), Is.False);
        }
    }

    [Test]
    public async Task CacheArtworkAsync_CleansUpExistingFormat_WhenNewFormatCached()
    {
        var cacheDir = Path.Combine(_tempDirectory, "MediaCover", "104");
        Directory.CreateDirectory(cacheDir);
        var existingJpg = Path.Combine(cacheDir, "poster.jpg");
        await File.WriteAllBytesAsync(existingJpg, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 });
        Assert.That(File.Exists(existingJpg), Is.True);

        var validWebp = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x20, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x20 };
        var sourceWebp = Path.Combine(_tempDirectory, "poster.webp");
        await File.WriteAllBytesAsync(sourceWebp, validWebp);

        var result = await _service.CacheArtworkAsync(sourceWebp, 104, "poster");

        Assert.That(result, Is.Not.Null);
        Assert.That(File.Exists(result), Is.True);
        Assert.That(result, Does.EndWith("poster.webp"));
        Assert.That(File.Exists(existingJpg), Is.False);
    }

    [Test]
    public async Task EnrichTorrentAsync_WhenExistingPosterPathMissingOnDisk_ReCachesArtwork()
    {
        var validJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        var sourcePoster = Path.Combine(_tempDirectory, "source_poster.jpg");
        await File.WriteAllBytesAsync(sourcePoster, validJpeg);

        var missingPath = Path.Combine(_tempDirectory, "non_existent_poster.jpg");
        var existing = new TorrentMediaMetadata
        {
            Id = 1,
            TorrentId = 105,
            Title = "Test Torrent",
            PosterUrl = sourcePoster,
            PosterLocalPath = missingPath,
        };

        _repository.GetByTorrentId(105).Returns(existing);

        var torrent = new Torrent { Id = 105, Name = "Test Torrent" };
        var result = await _service.EnrichTorrentAsync(torrent);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.PosterLocalPath, Is.Not.EqualTo(missingPath));
        Assert.That(File.Exists(result.PosterLocalPath), Is.True);
    }

    [Test]
    public async Task EnrichTorrentAsync_SampleFileExclusion_ChoosesPrimaryMediaFileForInspection()
    {
        var torrentDir = Path.Combine(_tempDirectory, "Matrix_Release");
        Directory.CreateDirectory(torrentDir);

        var mainFile = Path.Combine(torrentDir, "The.Matrix.1999.1080p.mkv");
        var sampleFile = Path.Combine(torrentDir, "sample.mkv");
        var nfoFile = Path.Combine(torrentDir, "matrix.nfo");

        await File.WriteAllBytesAsync(mainFile, new byte[10000]);
        await File.WriteAllBytesAsync(sampleFile, new byte[1000]);
        await File.WriteAllBytesAsync(nfoFile, new byte[100]);

        var torrent = new Torrent { Id = 201, Name = "The.Matrix.1999.1080p.BluRay.x264-GRP" };

        var result = await _service.EnrichTorrentAsync(torrent, sampleFile);

        Assert.That(result, Is.Not.Null);
        _inspector.Received(1).InspectFile(mainFile);
        _inspector.DidNotReceive().InspectFile(sampleFile);
    }

    [Test]
    public async Task EnrichTorrentAsync_WhenDirectoryPassed_SelectsPrimaryMediaFileAndIgnoresSample()
    {
        var torrentDir = Path.Combine(_tempDirectory, "Gladiator_Release");
        Directory.CreateDirectory(torrentDir);

        var mainFile = Path.Combine(torrentDir, "Gladiator.2000.1080p.mkv");
        var sampleFile = Path.Combine(torrentDir, "gladiator-sample.mkv");

        await File.WriteAllBytesAsync(mainFile, new byte[10000]);
        await File.WriteAllBytesAsync(sampleFile, new byte[1000]);

        var torrent = new Torrent { Id = 202, Name = "Gladiator.2000.Extended.1080p.BluRay.x264-GRP" };

        var result = await _service.EnrichTorrentAsync(torrent, torrentDir);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Edition, Is.EqualTo("Extended"));
        _inspector.Received(1).InspectFile(mainFile);
        _inspector.DidNotReceive().InspectFile(sampleFile);
    }

    private class RepeatingStream : Stream
    {
        private readonly long _length;
        private long _position;

        public RepeatingStream(long length)
        {
            _length = length;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = _length - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            var toRead = (int)Math.Min(count, remaining);
            Array.Fill(buffer, (byte)0x55, offset, toRead);
            _position += toRead;
            return toRead;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private class TestableMediaEnrichmentService : MediaEnrichmentService
    {
        private readonly IArrConnection _mockProvider;

        public TestableMediaEnrichmentService(
            ITorrentMediaMetadataRepository repository,
            IMediaContainerInspector inspector,
            IConfigService configService,
            IAppFolderInfo appFolderInfo,
            IEventAggregator eventAggregator,
            IArrConnectionRepository arrRepository,
            IArrConnectionFactory connectionFactory,
            IArrConnection mockProvider)
            : base(repository, inspector, configService, appFolderInfo, eventAggregator, arrRepository, connectionFactory)
        {
            _mockProvider = mockProvider;
        }

        protected override IArrConnection CreateProvider(ArrConnectionDefinition definition) => _mockProvider;
    }
}
