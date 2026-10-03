using System;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.MediaEnrichment;
using Seedarr.Api.V1.MediaCover;

namespace NzbDrone.Core.Test.MediaCover;

[TestFixture]
public class MediaCoverControllerTest
{
    private static readonly JsonSerializerOptions CamelCaseOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private IMediaEnrichmentService _enrichmentService;
    private IAppFolderInfo _appFolderInfo;
    private string _tempAppData;
    private MediaCoverController _controller;

    [SetUp]
    public void SetUp()
    {
        _tempAppData = Path.Combine(Path.GetTempPath(), "seedarr_mediacover_cache_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempAppData);

        _enrichmentService = Substitute.For<IMediaEnrichmentService>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _appFolderInfo.AppDataFolder.Returns(_tempAppData);

        _controller = new MediaCoverController(_enrichmentService, _appFolderInfo)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
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

    private string CreateTestArtwork(int torrentId, string filename = "poster.jpg", byte[] content = null)
    {
        var coverDir = Path.Combine(_tempAppData, "MediaCover", torrentId.ToString());
        Directory.CreateDirectory(coverDir);
        var artworkFile = Path.Combine(coverDir, filename);
        File.WriteAllBytes(artworkFile, content ?? new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x12, 0x34 });
        return artworkFile;
    }

    [Test]
    public void ServeArtwork_sets_cache_control_etag_and_last_modified_headers()
    {
        var posterFile = CreateTestArtwork(10, "poster.jpg");
        var fileInfo = new FileInfo(posterFile);
        var expectedLastModified = fileInfo.LastWriteTimeUtc.ToString("R");
        var expectedEtag = $"\"{fileInfo.LastWriteTimeUtc.Ticks:x}-{fileInfo.Length:x}\"";

        _enrichmentService.GetMetadata(10).Returns(new TorrentMediaMetadata
        {
            TorrentId = 10,
            PosterLocalPath = posterFile,
        });

        var result = _controller.GetPoster(10);

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        Assert.That(_controller.Response.Headers["Cache-Control"].ToString(), Is.EqualTo("public, max-age=604800, must-revalidate"));
        Assert.That(_controller.Response.Headers["ETag"].ToString(), Is.EqualTo(expectedEtag));
        Assert.That(_controller.Response.Headers["Last-Modified"].ToString(), Is.EqualTo(expectedLastModified));
    }

    [Test]
    public void ServeArtwork_returns_304_when_if_none_match_matches_etag()
    {
        var posterFile = CreateTestArtwork(11, "poster.jpg");
        var fileInfo = new FileInfo(posterFile);
        var expectedEtag = $"\"{fileInfo.LastWriteTimeUtc.Ticks:x}-{fileInfo.Length:x}\"";

        _enrichmentService.GetMetadata(11).Returns(new TorrentMediaMetadata
        {
            TorrentId = 11,
            PosterLocalPath = posterFile,
        });

        _controller.Request.Headers.IfNoneMatch = expectedEtag;

        var result = _controller.GetPoster(11);

        Assert.That(result, Is.InstanceOf<StatusCodeResult>());
        var statusResult = (StatusCodeResult)result;
        Assert.That(statusResult.StatusCode, Is.EqualTo(StatusCodes.Status304NotModified));
        Assert.That(_controller.Response.Headers["ETag"].ToString(), Is.EqualTo(expectedEtag));
        Assert.That(_controller.Response.Headers["Cache-Control"].ToString(), Is.EqualTo("public, max-age=604800, must-revalidate"));
    }

    [Test]
    public void ServeArtwork_returns_304_when_if_none_match_is_wildcard()
    {
        var posterFile = CreateTestArtwork(12, "poster.jpg");

        _enrichmentService.GetMetadata(12).Returns(new TorrentMediaMetadata
        {
            TorrentId = 12,
            PosterLocalPath = posterFile,
        });

        _controller.Request.Headers.IfNoneMatch = "*";

        var result = _controller.GetPoster(12);

        Assert.That(result, Is.InstanceOf<StatusCodeResult>());
        var statusResult = (StatusCodeResult)result;
        Assert.That(statusResult.StatusCode, Is.EqualTo(StatusCodes.Status304NotModified));
    }

    [Test]
    public void ServeArtwork_returns_304_when_if_none_match_contains_weak_etag()
    {
        var posterFile = CreateTestArtwork(13, "poster.jpg");
        var fileInfo = new FileInfo(posterFile);
        var expectedEtag = $"\"{fileInfo.LastWriteTimeUtc.Ticks:x}-{fileInfo.Length:x}\"";

        _enrichmentService.GetMetadata(13).Returns(new TorrentMediaMetadata
        {
            TorrentId = 13,
            PosterLocalPath = posterFile,
        });

        _controller.Request.Headers.IfNoneMatch = $"W/{expectedEtag}";

        var result = _controller.GetPoster(13);

        Assert.That(result, Is.InstanceOf<StatusCodeResult>());
        var statusResult = (StatusCodeResult)result;
        Assert.That(statusResult.StatusCode, Is.EqualTo(StatusCodes.Status304NotModified));
    }

    [Test]
    public void ServeArtwork_returns_304_when_if_modified_since_matches_file_timestamp()
    {
        var posterFile = CreateTestArtwork(14, "poster.jpg");
        var fileInfo = new FileInfo(posterFile);
        var lastModifiedUtc = fileInfo.LastWriteTimeUtc;

        _enrichmentService.GetMetadata(14).Returns(new TorrentMediaMetadata
        {
            TorrentId = 14,
            PosterLocalPath = posterFile,
        });

        _controller.Request.Headers.IfModifiedSince = lastModifiedUtc.ToString("R");

        var result = _controller.GetPoster(14);

        Assert.That(result, Is.InstanceOf<StatusCodeResult>());
        var statusResult = (StatusCodeResult)result;
        Assert.That(statusResult.StatusCode, Is.EqualTo(StatusCodes.Status304NotModified));
    }

    [Test]
    public void ServeArtwork_returns_304_when_if_modified_since_is_after_file_timestamp()
    {
        var posterFile = CreateTestArtwork(15, "poster.jpg");
        var fileInfo = new FileInfo(posterFile);

        _enrichmentService.GetMetadata(15).Returns(new TorrentMediaMetadata
        {
            TorrentId = 15,
            PosterLocalPath = posterFile,
        });

        _controller.Request.Headers.IfModifiedSince = fileInfo.LastWriteTimeUtc.AddDays(1).ToString("R");

        var result = _controller.GetPoster(15);

        Assert.That(result, Is.InstanceOf<StatusCodeResult>());
        var statusResult = (StatusCodeResult)result;
        Assert.That(statusResult.StatusCode, Is.EqualTo(StatusCodes.Status304NotModified));
    }

    [Test]
    public void ServeArtwork_returns_physical_file_when_if_none_match_does_not_match()
    {
        var posterFile = CreateTestArtwork(16, "poster.jpg");

        _enrichmentService.GetMetadata(16).Returns(new TorrentMediaMetadata
        {
            TorrentId = 16,
            PosterLocalPath = posterFile,
        });

        _controller.Request.Headers.IfNoneMatch = "\"mismatched-etag\"";

        var result = _controller.GetPoster(16);

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var fileResult = (PhysicalFileResult)result;
        Assert.That(fileResult.FileName, Is.EqualTo(Path.GetFullPath(posterFile)));
    }

    [Test]
    public void ServeArtwork_returns_physical_file_when_if_modified_since_is_older()
    {
        var posterFile = CreateTestArtwork(17, "poster.jpg");
        var fileInfo = new FileInfo(posterFile);

        _enrichmentService.GetMetadata(17).Returns(new TorrentMediaMetadata
        {
            TorrentId = 17,
            PosterLocalPath = posterFile,
        });

        _controller.Request.Headers.IfModifiedSince = fileInfo.LastWriteTimeUtc.AddDays(-2).ToString("R");

        var result = _controller.GetPoster(17);

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var fileResult = (PhysicalFileResult)result;
        Assert.That(fileResult.FileName, Is.EqualTo(Path.GetFullPath(posterFile)));
    }

    [Test]
    public void ServeArtwork_rejects_paths_outside_AppDataFolder_with_NotFound()
    {
        var outsideDir = Path.Combine(Path.GetTempPath(), "seedarr_v1_outside_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideDir);
        var outsideFile = Path.Combine(outsideDir, "poster.jpg");
        File.WriteAllBytes(outsideFile, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

        try
        {
            _enrichmentService.GetMetadata(20).Returns(new TorrentMediaMetadata
            {
                TorrentId = 20,
                PosterLocalPath = outsideFile,
            });

            var result = _controller.GetPoster(20);

            Assert.That(result, Is.InstanceOf<NotFoundResult>());
        }
        finally
        {
            Directory.Delete(outsideDir, true);
        }
    }

    [Test]
    public void ServeArtwork_rejects_paths_when_AppFolderInfo_is_null_with_NotFound()
    {
        var controllerWithoutAppFolder = new MediaCoverController(_enrichmentService, null)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        _enrichmentService.GetMetadata(21).Returns(new TorrentMediaMetadata
        {
            TorrentId = 21,
            PosterLocalPath = "/etc/passwd",
        });

        var result = controllerWithoutAppFolder.GetPoster(21);
        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [TestCase("../../../../../etc/passwd")]
    [TestCase("MediaCover/../../etc/passwd")]
    public void ServeArtwork_rejects_traversal_paths_with_NotFound(string traversalPath)
    {
        _enrichmentService.GetMetadata(22).Returns(new TorrentMediaMetadata
        {
            TorrentId = 22,
            PosterLocalPath = traversalPath,
        });

        var result = _controller.GetPoster(22);
        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void MediaMetadataResource_serialization_does_not_leak_local_paths()
    {
        var resource = new MediaMetadataResource
        {
            Id = 1,
            TorrentId = 1,
            Title = "Secret Movie",
            PosterUrl = "/api/v1/mediacover/1/poster.jpg",
            PosterLocalPath = "/root/secret/MediaCover/1/poster.jpg",
            BackdropUrl = "/api/v1/mediacover/1/backdrop.jpg",
            BackdropLocalPath = "/root/secret/MediaCover/1/backdrop.jpg",
        };

        var json = JsonSerializer.Serialize(resource, CamelCaseOptions);

        Assert.That(json, Does.Not.Contain("posterLocalPath"));
        Assert.That(json, Does.Not.Contain("backdropLocalPath"));
        Assert.That(json, Does.Not.Contain("/root/secret"));
        Assert.That(json, Does.Contain("posterUrl"));
        Assert.That(json, Does.Contain("backdropUrl"));
    }

    [Test]
    public void ServeArtwork_fanart_falls_back_to_backdrop_when_fanart_not_found()
    {
        var backdropFile = CreateTestArtwork(30, "backdrop.jpg");
        _enrichmentService.GetMetadata(30).Returns(new TorrentMediaMetadata { TorrentId = 30 });

        var result = _controller.GetCover(30, "fanart.jpg");

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var fileResult = (PhysicalFileResult)result;
        Assert.That(fileResult.FileName, Is.EqualTo(Path.GetFullPath(backdropFile)));
    }

    [Test]
    public void ServeArtwork_fanart_falls_back_to_background_when_backdrop_not_found()
    {
        var backgroundFile = CreateTestArtwork(31, "background.png");
        _enrichmentService.GetMetadata(31).Returns(new TorrentMediaMetadata { TorrentId = 31 });

        var result = _controller.GetCover(31, "fanart.png");

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var fileResult = (PhysicalFileResult)result;
        Assert.That(fileResult.FileName, Is.EqualTo(Path.GetFullPath(backgroundFile)));
    }

    [Test]
    public void ServeArtwork_backdrop_falls_back_to_fanart_when_backdrop_not_found()
    {
        var fanartFile = CreateTestArtwork(32, "fanart.jpg");
        _enrichmentService.GetMetadata(32).Returns(new TorrentMediaMetadata { TorrentId = 32 });

        var result = _controller.GetBackdrop(32);

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var fileResult = (PhysicalFileResult)result;
        Assert.That(fileResult.FileName, Is.EqualTo(Path.GetFullPath(fanartFile)));
    }

    [Test]
    public void ServeArtwork_backdrop_falls_back_to_background_when_fanart_not_found()
    {
        var backgroundFile = CreateTestArtwork(33, "background.jpg");
        _enrichmentService.GetMetadata(33).Returns(new TorrentMediaMetadata { TorrentId = 33 });

        var result = _controller.GetCover(33, "backdrop.jpg");

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var fileResult = (PhysicalFileResult)result;
        Assert.That(fileResult.FileName, Is.EqualTo(Path.GetFullPath(backgroundFile)));
    }

    [Test]
    public void ServeArtwork_thumb_falls_back_to_poster_when_thumb_not_found()
    {
        var posterFile = CreateTestArtwork(34, "poster.jpg");
        _enrichmentService.GetMetadata(34).Returns(new TorrentMediaMetadata { TorrentId = 34 });

        var result = _controller.GetCover(34, "thumb.jpg");

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var fileResult = (PhysicalFileResult)result;
        Assert.That(fileResult.FileName, Is.EqualTo(Path.GetFullPath(posterFile)));
    }

    [Test]
    public void ServeArtwork_thumb_falls_back_to_cover_when_poster_not_found()
    {
        var coverFile = CreateTestArtwork(35, "cover.jpg");
        _enrichmentService.GetMetadata(35).Returns(new TorrentMediaMetadata { TorrentId = 35 });

        var result = _controller.GetCover(35, "thumb.jpg");

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var fileResult = (PhysicalFileResult)result;
        Assert.That(fileResult.FileName, Is.EqualTo(Path.GetFullPath(coverFile)));
    }

    [Test]
    public void ServeArtwork_thumb_falls_back_to_folder_when_cover_not_found()
    {
        var folderFile = CreateTestArtwork(36, "folder.png");
        _enrichmentService.GetMetadata(36).Returns(new TorrentMediaMetadata { TorrentId = 36 });

        var result = _controller.GetCover(36, "thumb.png");

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var fileResult = (PhysicalFileResult)result;
        Assert.That(fileResult.FileName, Is.EqualTo(Path.GetFullPath(folderFile)));
    }

    [Test]
    public void ServeArtwork_banner_falls_back_to_season_banner_when_banner_not_found()
    {
        var seasonBannerFile = CreateTestArtwork(37, "season-banner.jpg");
        _enrichmentService.GetMetadata(37).Returns(new TorrentMediaMetadata { TorrentId = 37 });

        var result = _controller.GetBanner(37);

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var fileResult = (PhysicalFileResult)result;
        Assert.That(fileResult.FileName, Is.EqualTo(Path.GetFullPath(seasonBannerFile)));
    }

    [Test]
    public void ServeArtwork_banner_falls_back_to_fanart_when_season_banner_not_found()
    {
        var fanartFile = CreateTestArtwork(38, "fanart.jpg");
        _enrichmentService.GetMetadata(38).Returns(new TorrentMediaMetadata { TorrentId = 38 });

        var result = _controller.GetCover(38, "banner.jpg");

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var fileResult = (PhysicalFileResult)result;
        Assert.That(fileResult.FileName, Is.EqualTo(Path.GetFullPath(fanartFile)));
    }

    [Test]
    public void ServeArtwork_banner_falls_back_to_backdrop_when_fanart_not_found()
    {
        var backdropFile = CreateTestArtwork(39, "backdrop.jpg");
        _enrichmentService.GetMetadata(39).Returns(new TorrentMediaMetadata { TorrentId = 39 });

        var result = _controller.GetBanner(39);

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var fileResult = (PhysicalFileResult)result;
        Assert.That(fileResult.FileName, Is.EqualTo(Path.GetFullPath(backdropFile)));
    }
}
