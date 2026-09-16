using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.SignalR;
using Seedarr.Api.V1.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentControllerTest
{
    private ITorrentService _torrentService;
    private ITorrentFileService _torrentFileService;
    private ITrackerEntryService _trackerEntryService;
    private ITorrentImportService _torrentImportService;
    private IConnectionManager _connectionManager;
    private ITorrentEventLogService _eventLogService;
    private IConfigService _configService;
    private IBroadcastSignalRMessage _signalRBroadcaster;
    private ICategoryService _categoryService;
    private TorrentResourceValidator _validator;
    private TorrentController _controller;

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
        _categoryService = Substitute.For<ICategoryService>();
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
            _validator,
            categoryService: _categoryService);
    }

    [Test]
    public void AddTracker_returns_BadRequest_when_torrent_is_private()
    {
        const int torrentId = 10;
        var torrent = new Torrent
        {
            Id = torrentId,
            IsPrivate = true,
            Name = "Private Torrent",
        };

        _torrentService.Get(torrentId).Returns(torrent);

        var resource = new AddTorrentTrackerResource
        {
            Url = "udp://tracker.openbittorrent.com:6969/announce",
            Tier = 1,
        };

        var result = _controller.AddTracker(torrentId, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        _trackerEntryService.DidNotReceive().Add(Arg.Any<TrackerEntry>());
    }

    [Test]
    public void AddTracker_succeeds_when_torrent_is_not_private()
    {
        const int torrentId = 11;
        var torrent = new Torrent
        {
            Id = torrentId,
            IsPrivate = false,
            Name = "Public Torrent",
        };

        _torrentService.Get(torrentId).Returns(torrent);
        _trackerEntryService.GetByTorrentId(torrentId).Returns(new List<TrackerEntry>());
        _trackerEntryService.Add(Arg.Any<TrackerEntry>()).Returns(callInfo =>
        {
            var entry = callInfo.Arg<TrackerEntry>();
            entry.Id = 99;
            return entry;
        });

        var resource = new AddTorrentTrackerResource
        {
            Url = "udp://tracker.openbittorrent.com:6969/announce",
            Tier = 1,
        };

        var result = _controller.AddTracker(torrentId, resource);

        Assert.That(result.Result, Is.InstanceOf<CreatedAtActionResult>());
        _trackerEntryService.Received(1).Add(Arg.Is<TrackerEntry>(t => t.TorrentId == torrentId && t.Url == resource.Url));
    }

    [Test]
    public void UpdateTracker_returns_BadRequest_when_resource_is_null()
    {
        var result = _controller.UpdateTracker(1, 1, null);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void UpdateTracker_returns_NotFound_when_torrent_does_not_exist()
    {
        _torrentService.Get(999).Returns((Torrent)null);

        var result = _controller.UpdateTracker(999, 1, new UpdateTorrentTrackerResource { Tier = 2 });

        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void UpdateTracker_returns_NotFound_when_tracker_does_not_exist()
    {
        const int torrentId = 10;
        _torrentService.Get(torrentId).Returns(new Torrent { Id = torrentId });
        _trackerEntryService.GetByTorrentId(torrentId).Returns(new List<TrackerEntry>());

        var result = _controller.UpdateTracker(torrentId, 999, new UpdateTorrentTrackerResource { Tier = 2 });

        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void UpdateTracker_updates_tier_and_enabled_and_updates_torrent_tracker_url()
    {
        const int torrentId = 10;
        var torrent = new Torrent
        {
            Id = torrentId,
            TrackerUrl = "http://tracker1.com/announce",
        };

        var tracker1 = new TrackerEntry
        {
            Id = 1,
            TorrentId = torrentId,
            Url = "http://tracker1.com/announce",
            Tier = 0,
            Enabled = true,
        };
        var tracker2 = new TrackerEntry
        {
            Id = 2,
            TorrentId = torrentId,
            Url = "http://tracker2.com/announce",
            Tier = 1,
            Enabled = true,
        };

        _torrentService.Get(torrentId).Returns(torrent);
        _trackerEntryService.GetByTorrentId(torrentId).Returns(new List<TrackerEntry> { tracker1, tracker2 });

        var updateResource = new UpdateTorrentTrackerResource
        {
            Tier = 5,
            Enabled = false,
        };

        var result = _controller.UpdateTracker(torrentId, 1, updateResource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _trackerEntryService.Received(1).Update(Arg.Is<TrackerEntry>(t => t.Id == 1 && t.Tier == 5 && !t.Enabled));

        // Since tracker1 is now disabled, tracker2 (enabled, tier 1) becomes the primary tracker.
        Assert.That(torrent.TrackerUrl, Is.EqualTo("http://tracker2.com/announce"));
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public void Create_with_existing_torrent_merges_trackers_and_returns_Ok()
    {
        const string infoHash = "0123456789abcdef0123456789abcdef01234567";
        var existing = new Torrent
        {
            Id = 50,
            Name = "Existing",
            InfoHash = infoHash,
        };

        _torrentService.GetByInfoHash(infoHash).Returns(existing);
        _trackerEntryService.GetByTorrentId(50).Returns(new List<TrackerEntry>
        {
            new() { Id = 1, TorrentId = 50, Url = "http://existing-tracker.com/announce", Tier = 0 }
        });

        var resource = new TorrentResource
        {
            Name = "Existing",
            InfoHash = infoHash,
            Trackers = new List<string> { "http://existing-tracker.com/announce", "http://new-tracker.com/announce" }
        };

        var result = _controller.Create(resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
        _trackerEntryService.Received(1).Add(Arg.Is<TrackerEntry>(t => t.TorrentId == 50 && t.Url == "http://new-tracker.com/announce"));
    }

    [TestCase("ftp://tracker.example.com/announce")]
    [TestCase("invalid-scheme")]
    [TestCase("javascript:alert(1)")]
    [TestCase("file:///etc/passwd")]
    public void AddTracker_returns_BadRequest_when_url_scheme_is_invalid(string url)
    {
        var resource = new AddTorrentTrackerResource
        {
            Url = url,
            Tier = 1,
        };

        var result = _controller.AddTracker(1, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Invalid tracker URL. Must be an HTTP, HTTPS, or UDP URL."));
        _trackerEntryService.DidNotReceive().Add(Arg.Any<TrackerEntry>());
    }

    [TestCase("http://tracker.example.com/announce")]
    [TestCase("https://tracker.example.com/announce")]
    [TestCase("udp://tracker.example.com:6969/announce")]
    public void AddTracker_accepts_valid_url_schemes(string url)
    {
        const int torrentId = 15;
        var torrent = new Torrent
        {
            Id = torrentId,
            IsPrivate = false,
            Name = "Public Torrent",
        };

        _torrentService.Get(torrentId).Returns(torrent);
        _trackerEntryService.GetByTorrentId(torrentId).Returns(new List<TrackerEntry>());
        _trackerEntryService.Add(Arg.Any<TrackerEntry>()).Returns(callInfo => callInfo.Arg<TrackerEntry>());

        var resource = new AddTorrentTrackerResource
        {
            Url = url,
            Tier = 1,
        };

        var result = _controller.AddTracker(torrentId, resource);

        Assert.That(result.Result, Is.InstanceOf<CreatedAtActionResult>());
        _trackerEntryService.Received(1).Add(Arg.Is<TrackerEntry>(t => t.TorrentId == torrentId && t.Url == url));
    }

    [Test]
    public void BulkAction_populates_SucceededIds_and_FailedIds()
    {
        var torrent1 = new Torrent { Id = 1, Name = "Torrent 1" };
        _torrentService.Get(1).Returns(torrent1);
        _torrentService.Get(2).Returns((Torrent)null);

        var resource = new BulkTorrentActionResource
        {
            Action = "start",
            TorrentIds = new List<int> { 1, 2 },
        };

        var result = _controller.BulkAction(resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var bulkResult = (BulkActionResult)okResult.Value;

        Assert.That(bulkResult.SuccessCount, Is.EqualTo(1));
        Assert.That(bulkResult.FailedCount, Is.EqualTo(1));
        Assert.That(bulkResult.SucceededIds, Does.Contain(1));
        Assert.That(bulkResult.FailedIds, Does.ContainKey(2));
    }

    [Test]
    public void BulkAction_setCategory_returns_BadRequest_when_categoryId_is_invalid()
    {
        _categoryService.Get(999).Returns((Category)null);

        var resource = new BulkTorrentActionResource
        {
            Action = "setCategory",
            CategoryId = 999,
            TorrentIds = new List<int> { 1, 2 },
        };

        var result = _controller.BulkAction(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Category with ID 999 not found."));
    }
}
