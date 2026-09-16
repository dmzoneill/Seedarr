using System;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.MediaEnrichment;
using Seedarr.Api.V1.MediaCover;

namespace NzbDrone.Core.Test.MediaCover;

[TestFixture]
public class MediaCoverSecurityTest
{
    private IMediaEnrichmentService _enrichmentService;
    private IAppFolderInfo _appFolderInfo;
    private string _tempAppData;
    private MediaCoverController _controller;

    [SetUp]
    public void SetUp()
    {
        _tempAppData = Path.Combine(Path.GetTempPath(), "seedarr_mediacover_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempAppData);

        _enrichmentService = Substitute.For<IMediaEnrichmentService>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _appFolderInfo.AppDataFolder.Returns(_tempAppData);

        _controller = new MediaCoverController(_enrichmentService, _appFolderInfo);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempAppData))
        {
            try
            {
                Directory.Delete(_tempAppData, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Test]
    public void Serialization_of_MediaMetadataResource_does_not_include_local_paths()
    {
        var resource = new MediaMetadataResource
        {
            Id = 1,
            TorrentId = 1,
            Title = "Test Movie",
            PosterUrl = "/api/v1/mediacover/1/poster.jpg",
            PosterLocalPath = "/home/daoneill/.config/Seedarr/MediaCover/1/poster.jpg",
            BackdropUrl = "/api/v1/mediacover/1/backdrop.jpg",
            BackdropLocalPath = "/home/daoneill/.config/Seedarr/MediaCover/1/backdrop.jpg"
        };

        var json = JsonSerializer.Serialize(resource);

        Assert.That(json, Does.Not.Contain("posterLocalPath"));
        Assert.That(json, Does.Not.Contain("backdropLocalPath"));
        Assert.That(json, Does.Not.Contain("/home/daoneill"));
        Assert.That(json, Does.Contain("posterUrl"));
        Assert.That(json, Does.Contain("backdropUrl"));
    }

    [Test]
    public void MediaMetadataResourceMapper_ToResource_produces_null_local_paths()
    {
        var model = new TorrentMediaMetadata
        {
            Id = 42,
            TorrentId = 100,
            Title = "Top Gun",
            Year = 2022,
            PosterLocalPath = "/root/secret/MediaCover/100/poster.jpg",
            BackdropLocalPath = "/root/secret/MediaCover/100/backdrop.jpg",
            PosterUrl = "https://example.com/poster.jpg",
            BackdropUrl = "https://example.com/backdrop.jpg"
        };

        var resource = MediaMetadataResourceMapper.ToResource(model);

        Assert.That(resource, Is.Not.Null);
        Assert.That(resource.PosterLocalPath, Is.Null);
        Assert.That(resource.BackdropLocalPath, Is.Null);
        Assert.That(resource.PosterUrl, Is.EqualTo("/api/v1/mediacover/100/poster.jpg"));
        Assert.That(resource.BackdropUrl, Is.EqualTo("/api/v1/mediacover/100/backdrop.jpg"));
    }

    [Test]
    public void MediaCoverController_serves_valid_artwork_within_MediaCover_directory()
    {
        var coverDir = Path.Combine(_tempAppData, "MediaCover", "10");
        Directory.CreateDirectory(coverDir);
        var posterFile = Path.Combine(coverDir, "poster.jpg");
        File.WriteAllBytes(posterFile, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

        _enrichmentService.GetMetadata(10).Returns(new TorrentMediaMetadata
        {
            TorrentId = 10,
            PosterLocalPath = posterFile
        });

        var result = _controller.GetPoster(10);

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var fileResult = (PhysicalFileResult)result;
        Assert.That(fileResult.FileName, Is.EqualTo(Path.GetFullPath(posterFile)));
        Assert.That(fileResult.ContentType, Is.EqualTo("image/jpeg"));
    }

    [Test]
    public void MediaCoverController_rejects_artwork_outside_MediaCover_directory()
    {
        var outsideDir = Path.Combine(Path.GetTempPath(), "seedarr_outside_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideDir);
        var outsideFile = Path.Combine(outsideDir, "poster.jpg");
        File.WriteAllBytes(outsideFile, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

        try
        {
            _enrichmentService.GetMetadata(10).Returns(new TorrentMediaMetadata
            {
                TorrentId = 10,
                PosterLocalPath = outsideFile
            });

            var result = _controller.GetPoster(10);

            Assert.That(result, Is.InstanceOf<NotFoundResult>());
        }
        finally
        {
            Directory.Delete(outsideDir, true);
        }
    }

    [TestCase("../../../../etc/passwd")]
    [TestCase("..\\..\\windows\\win.ini")]
    [TestCase("valid/../../etc/passwd")]
    [TestCase("not_valid_type.jpg")]
    [TestCase("..")]
    public void MediaCoverController_rejects_traversal_filenames(string maliciousFilename)
    {
        var result = _controller.GetCover(10, maliciousFilename);

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(-999)]
    public void MediaCoverController_rejects_invalid_torrent_ids(int invalidTorrentId)
    {
        var result = _controller.GetPoster(invalidTorrentId);

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void MediaCoverController_updates_last_access_time_when_serving_artwork()
    {
        var coverDir = Path.Combine(_tempAppData, "MediaCover", "10");
        Directory.CreateDirectory(coverDir);
        var posterFile = Path.Combine(coverDir, "poster.jpg");
        File.WriteAllBytes(posterFile, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

        var pastTime = DateTime.UtcNow.AddDays(-5);
        File.SetLastAccessTimeUtc(posterFile, pastTime);

        _enrichmentService.GetMetadata(10).Returns(new TorrentMediaMetadata
        {
            TorrentId = 10,
            PosterLocalPath = posterFile
        });

        var result = _controller.GetPoster(10);

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var lastAccess = File.GetLastAccessTimeUtc(posterFile);
        Assert.That(lastAccess, Is.GreaterThan(pastTime.AddDays(1)));
    }
}
