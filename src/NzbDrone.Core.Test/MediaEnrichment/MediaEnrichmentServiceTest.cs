using System;
using System.IO;
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

        _appFolderInfo.AppDataFolder.Returns(_tempDirectory);
        _configService.AutoPruneRemovedArtwork.Returns(true);

        _service = new MediaEnrichmentService(
            _repository,
            _inspector,
            _configService,
            _appFolderInfo,
            _eventAggregator,
            _arrRepository,
            _connectionFactory);
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
        _repository.Received(1).Insert(result);
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
        _repository.Received(1).Update(existing);
        _repository.DidNotReceive().Insert(Arg.Any<TorrentMediaMetadata>());
        _eventAggregator.Received(1).PublishEvent(Arg.Is<MediaEnrichedEvent>(e => e.TorrentId == 10));
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
        Assert.That(cleaned, Is.EqualTo("The Matrix 1999"));
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
}
