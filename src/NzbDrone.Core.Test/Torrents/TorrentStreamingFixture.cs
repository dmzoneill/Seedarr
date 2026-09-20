using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using NzbDrone.SignalR;
using Seedarr.Api.V1.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentStreamingFixture
{
    private ITorrentService _torrentService;
    private ITorrentFileService _torrentFileService;
    private ITrackerEntryService _trackerEntryService;
    private ITorrentImportService _torrentImportService;
    private IConnectionManager _connectionManager;
    private ITorrentEventLogService _eventLogService;
    private IConfigService _configService;
    private IBroadcastSignalRMessage _signalRBroadcaster;
    private TorrentResourceValidator _validator;
    private TorrentController _controller;
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_stream_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _torrentImportService = Substitute.For<ITorrentImportService>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _eventLogService = Substitute.For<ITorrentEventLogService>();
        _configService = Substitute.For<IConfigService>();
        _signalRBroadcaster = Substitute.For<IBroadcastSignalRMessage>();
        _validator = new TorrentResourceValidator();

        _controller = new TorrentController(
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentImportService,
            _connectionManager,
            _eventLogService,
            _configService,
            _signalRBroadcaster,
            _validator)
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
        _controller?.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Test]
    public void StreamFile_returns_PhysicalFileResult_with_EnableRangeProcessing_true()
    {
        const int torrentId = 1;
        const int fileId = 10;
        var mediaFile = Path.Combine(_tempDir, "sample_movie.mp4");
        File.WriteAllBytes(mediaFile, new byte[1024]);

        var torrent = new Torrent
        {
            Id = torrentId,
            SavePath = _tempDir,
            Name = "Sample Movie",
        };

        var torrentFile = new TorrentFile
        {
            Id = fileId,
            TorrentId = torrentId,
            Path = "sample_movie.mp4",
            Size = 1024,
        };

        _torrentService.Get(torrentId).Returns(torrent);
        _torrentFileService.GetByTorrentId(torrentId).Returns(new List<TorrentFile> { torrentFile });

        var result = _controller.StreamFile(torrentId, fileId);

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var physicalResult = (PhysicalFileResult)result;
        Assert.That(physicalResult.EnableRangeProcessing, Is.True);
        Assert.That(physicalResult.ContentType, Is.EqualTo("video/mp4"));
        Assert.That(physicalResult.FileName, Is.EqualTo(mediaFile));
    }

    [Test]
    public void StreamFile_rejects_path_traversal()
    {
        const int torrentId = 2;
        const int fileId = 20;

        var torrent = new Torrent
        {
            Id = torrentId,
            SavePath = _tempDir,
            Name = "Traversal Torrent",
        };

        var traversalFile = new TorrentFile
        {
            Id = fileId,
            TorrentId = torrentId,
            Path = "../secret.txt",
            Size = 512,
        };

        _torrentService.Get(torrentId).Returns(torrent);
        _torrentFileService.GetByTorrentId(torrentId).Returns(new List<TorrentFile> { traversalFile });

        var result = _controller.StreamFile(torrentId, fileId);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result;
        Assert.That(badRequest.Value, Is.EqualTo("Invalid file path"));
    }

    [Test]
    public void StreamFile_returns_NotFound_when_file_does_not_exist()
    {
        const int torrentId = 3;
        const int fileId = 30;

        var torrent = new Torrent
        {
            Id = torrentId,
            SavePath = _tempDir,
            Name = "Missing File Torrent",
        };

        var missingFile = new TorrentFile
        {
            Id = fileId,
            TorrentId = torrentId,
            Path = "non_existent_movie.mkv",
            Size = 2048,
        };

        _torrentService.Get(torrentId).Returns(torrent);
        _torrentFileService.GetByTorrentId(torrentId).Returns(new List<TorrentFile> { missingFile });

        var result = _controller.StreamFile(torrentId, fileId);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
        var notFound = (NotFoundObjectResult)result;
        Assert.That(notFound.Value, Is.EqualTo("File not found on disk"));
    }

    [Test]
    public void StreamFile_returns_NotFound_when_torrent_not_found()
    {
        _torrentService.Get(999).Returns((Torrent)null);

        var result = _controller.StreamFile(999, 1);

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void StreamFile_returns_NotFound_when_file_id_not_found()
    {
        const int torrentId = 4;
        var torrent = new Torrent
        {
            Id = torrentId,
            SavePath = _tempDir,
        };

        _torrentService.Get(torrentId).Returns(torrent);
        _torrentFileService.GetByTorrentId(torrentId).Returns(new List<TorrentFile>());

        var result = _controller.StreamFile(torrentId, 999);

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void StreamFile_returns_NotFound_when_save_path_is_null_or_empty()
    {
        const int torrentId = 5;
        const int fileId = 50;

        var torrent = new Torrent
        {
            Id = torrentId,
            SavePath = null,
            SourcePath = null,
        };

        var file = new TorrentFile
        {
            Id = fileId,
            TorrentId = torrentId,
            Path = "movie.mp4",
        };

        _torrentService.Get(torrentId).Returns(torrent);
        _torrentFileService.GetByTorrentId(torrentId).Returns(new List<TorrentFile> { file });

        var result = _controller.StreamFile(torrentId, fileId);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
        var notFound = (NotFoundObjectResult)result;
        Assert.That(notFound.Value, Is.EqualTo("Torrent save path not available"));
    }

    [TestCase("video.mkv", "video/x-matroska")]
    [TestCase("video.webm", "video/webm")]
    [TestCase("video.avi", "video/x-msvideo")]
    [TestCase("video.m4v", "video/x-m4v")]
    [TestCase("video.ts", "video/mp2t")]
    [TestCase("video.m2ts", "video/mp2t")]
    [TestCase("video.mov", "video/quicktime")]
    [TestCase("audio.mp3", "audio/mpeg")]
    [TestCase("audio.flac", "audio/flac")]
    [TestCase("audio.aac", "audio/aac")]
    [TestCase("audio.ogg", "audio/ogg")]
    [TestCase("audio.oga", "audio/ogg")]
    [TestCase("audio.opus", "audio/opus")]
    [TestCase("audio.wav", "audio/wav")]
    [TestCase("audio.m4a", "audio/mp4")]
    [TestCase("other.bin", "application/octet-stream")]
    public void StreamFile_detects_correct_mime_type(string fileName, string expectedMime)
    {
        const int torrentId = 6;
        const int fileId = 60;
        var mediaFile = Path.Combine(_tempDir, fileName);
        File.WriteAllBytes(mediaFile, new byte[128]);

        var torrent = new Torrent
        {
            Id = torrentId,
            SavePath = _tempDir,
        };

        var file = new TorrentFile
        {
            Id = fileId,
            TorrentId = torrentId,
            Path = fileName,
            Size = 128,
        };

        _torrentService.Get(torrentId).Returns(torrent);
        _torrentFileService.GetByTorrentId(torrentId).Returns(new List<TorrentFile> { file });

        var result = _controller.StreamFile(torrentId, fileId);

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var physicalResult = (PhysicalFileResult)result;
        Assert.That(physicalResult.ContentType, Is.EqualTo(expectedMime));
    }

    [Test]
    public void StreamTorrent_streams_largest_media_file_with_range_processing()
    {
        const int torrentId = 7;
        var smallFile = Path.Combine(_tempDir, "sample.mp4");
        var largeFile = Path.Combine(_tempDir, "feature.mkv");
        File.WriteAllBytes(smallFile, new byte[512]);
        File.WriteAllBytes(largeFile, new byte[4096]);

        var torrent = new Torrent
        {
            Id = torrentId,
            SavePath = _tempDir,
        };

        var files = new List<TorrentFile>
        {
            new() { Id = 71, TorrentId = torrentId, Path = "sample.mp4", Size = 512 },
            new() { Id = 72, TorrentId = torrentId, Path = "feature.mkv", Size = 4096 },
        };

        _torrentService.Get(torrentId).Returns(torrent);
        _torrentFileService.GetByTorrentId(torrentId).Returns(files);

        var result = _controller.StreamTorrent(torrentId);

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var physicalResult = (PhysicalFileResult)result;
        Assert.That(physicalResult.EnableRangeProcessing, Is.True);
        Assert.That(physicalResult.ContentType, Is.EqualTo("video/x-matroska"));
        Assert.That(physicalResult.FileName, Is.EqualTo(largeFile));
    }

    [Test]
    public void DownloadFile_returns_PhysicalFileResult_with_download_filename()
    {
        const int torrentId = 8;
        const int fileId = 80;
        var subDir = Path.Combine(_tempDir, "subfolder");
        Directory.CreateDirectory(subDir);
        var mediaFile = Path.Combine(subDir, "track.flac");
        File.WriteAllBytes(mediaFile, new byte[1024]);

        var torrent = new Torrent
        {
            Id = torrentId,
            SavePath = _tempDir,
        };

        var file = new TorrentFile
        {
            Id = fileId,
            TorrentId = torrentId,
            Path = "subfolder/track.flac",
            Size = 1024,
        };

        _torrentService.Get(torrentId).Returns(torrent);
        _torrentFileService.GetByTorrentId(torrentId).Returns(new List<TorrentFile> { file });

        var result = _controller.DownloadFile(torrentId, fileId);

        Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
        var physicalResult = (PhysicalFileResult)result;
        Assert.That(physicalResult.EnableRangeProcessing, Is.True);
        Assert.That(physicalResult.FileDownloadName, Is.EqualTo("track.flac"));
        Assert.That(physicalResult.ContentType, Is.EqualTo("audio/flac"));
    }
}
