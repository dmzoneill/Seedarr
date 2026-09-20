using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Subtitles;
using NzbDrone.Core.Torrents;
using NzbDrone.SignalR;
using Seedarr.Api.V1.Subtitles;
using Seedarr.Api.V1.Torrents;

namespace NzbDrone.Core.Test.Subtitles;

[TestFixture]
public class SubtitleEndpointTest
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
    private ISubtitleDiscoveryService _subtitleDiscoveryService;
    private ISubtitleConversionService _subtitleConversionService;
    private TorrentController _controller;
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _torrentImportService = Substitute.For<ITorrentImportService>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _eventLogService = Substitute.For<ITorrentEventLogService>();
        _configService = Substitute.For<IConfigService>();
        _signalRBroadcaster = Substitute.For<IBroadcastSignalRMessage>();
        _validator = new TorrentResourceValidator();
        _subtitleDiscoveryService = new SubtitleDiscoveryService();
        _subtitleConversionService = new SubtitleConversionService();

        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_sub_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);

        _controller = new TorrentController(
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentImportService,
            _connectionManager,
            _eventLogService,
            _configService,
            _signalRBroadcaster,
            _validator,
            subtitleDiscoveryService: _subtitleDiscoveryService,
            subtitleConversionService: _subtitleConversionService);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Test]
    public void GetFileSubtitles_ReturnsDiscoveredSubtitleTracks()
    {
        var torrent = new Torrent { Id = 10, Name = "Test Movie", SavePath = _tempDir };
        var videoFile = new TorrentFile { Id = 101, TorrentId = 10, Path = "Movie.mkv" };
        var subFile = new TorrentFile { Id = 102, TorrentId = 10, Path = "Movie.en.srt" };

        _torrentService.Get(10).Returns(torrent);
        _torrentFileService.GetByTorrentId(10).Returns(new List<TorrentFile> { videoFile, subFile });

        var actionResult = _controller.GetFileSubtitles(10, 101);
        var okResult = actionResult.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        var list = okResult.Value as List<SubtitleTrackResource>;
        Assert.That(list, Is.Not.Null);
        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0].Language, Is.EqualTo("en"));
        Assert.That(list[0].Url, Does.Contain("/subtitles/1.vtt"));
    }

    [Test]
    public void GetSubtitleTrack_ConvertsSrtToWebVtt()
    {
        var torrent = new Torrent { Id = 10, Name = "Test Movie", SavePath = _tempDir };
        var videoFile = new TorrentFile { Id = 101, TorrentId = 10, Path = "Movie.mkv" };
        var subFile = new TorrentFile { Id = 102, TorrentId = 10, Path = "Movie.en.srt" };

        var srtContent = "1\n00:00:01,000 --> 00:00:03,000\nHello from subtitle test\n";
        File.WriteAllText(Path.Combine(_tempDir, "Movie.en.srt"), srtContent, Encoding.UTF8);

        _torrentService.Get(10).Returns(torrent);
        _torrentFileService.GetByTorrentId(10).Returns(new List<TorrentFile> { videoFile, subFile });

        var actionResult = _controller.GetSubtitleTrack(10, 101, "1");
        var contentResult = actionResult as ContentResult;

        Assert.That(contentResult, Is.Not.Null);
        Assert.That(contentResult.ContentType, Is.EqualTo("text/vtt; charset=utf-8"));
        Assert.That(contentResult.Content, Does.StartWith("WEBVTT\n\n"));
        Assert.That(contentResult.Content, Does.Contain("00:00:01.000 --> 00:00:03.000"));
        Assert.That(contentResult.Content, Does.Contain("Hello from subtitle test"));
    }

    [Test]
    public void GetSubtitleTrack_ReturnsNotFound_WhenTorrentMissing()
    {
        _torrentService.Get(999).Returns((Torrent)null);

        var actionResult = _controller.GetSubtitleTrack(999, 1, "1");
        Assert.That(actionResult, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void GetSubtitleTrack_ReturnsNotFound_WhenSubtitleFileMissingOnDisk()
    {
        var torrent = new Torrent { Id = 10, Name = "Test Movie", SavePath = _tempDir };
        var videoFile = new TorrentFile { Id = 101, TorrentId = 10, Path = "Movie.mkv" };
        var subFile = new TorrentFile { Id = 102, TorrentId = 10, Path = "Movie.en.srt" };

        _torrentService.Get(10).Returns(torrent);
        _torrentFileService.GetByTorrentId(10).Returns(new List<TorrentFile> { videoFile, subFile });

        var actionResult = _controller.GetSubtitleTrack(10, 101, "1");
        Assert.That(actionResult, Is.InstanceOf<NotFoundObjectResult>());
    }
}
