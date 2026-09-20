using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
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
    private ITrackerAnnounceService _trackerAnnounceService;
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
        _trackerAnnounceService = Substitute.For<ITrackerAnnounceService>();

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
            trackerAnnounceService: _trackerAnnounceService,
            categoryService: _categoryService);
    }

    [TearDown]
    public void TearDown()
    {
        _controller?.Dispose();
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

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
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

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
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

    [Test]
    public void BulkAction_setCategory_propagates_category_bandwidth_limits_when_torrent_has_no_limits()
    {
        var category = new Category
        {
            Id = 5,
            Name = "LimitedCategory",
            DefaultDownloadLimit = 5000,
            DefaultUploadLimit = 2000,
        };
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Torrent 1",
            DownloadLimit = 0,
            UploadLimit = -1,
        };

        _categoryService.Get(5).Returns(category);
        _torrentService.Get(1).Returns(torrent);

        var resource = new BulkTorrentActionResource
        {
            Action = "setCategory",
            CategoryId = 5,
            TorrentIds = new List<int> { 1 },
        };

        var result = _controller.BulkAction(resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        Assert.That(torrent.Category, Is.EqualTo("LimitedCategory"));
        Assert.That(torrent.DownloadLimit, Is.EqualTo(5000));
        Assert.That(torrent.UploadLimit, Is.EqualTo(2000));
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public void BulkAction_setCategory_preserves_custom_limits_when_torrent_already_has_limits()
    {
        var category = new Category
        {
            Id = 5,
            Name = "LimitedCategory",
            DefaultDownloadLimit = 5000,
            DefaultUploadLimit = 2000,
        };
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Torrent 1",
            DownloadLimit = 8000,
            UploadLimit = 4000,
        };

        _categoryService.Get(5).Returns(category);
        _torrentService.Get(1).Returns(torrent);

        var resource = new BulkTorrentActionResource
        {
            Action = "setCategory",
            CategoryId = 5,
            TorrentIds = new List<int> { 1 },
        };

        var result = _controller.BulkAction(resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        Assert.That(torrent.Category, Is.EqualTo("LimitedCategory"));
        Assert.That(torrent.DownloadLimit, Is.EqualTo(8000));
        Assert.That(torrent.UploadLimit, Is.EqualTo(4000));
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public void BulkAction_setCategory_with_string_category_updates_category_and_propagates_limits()
    {
        var category = new Category
        {
            Id = 5,
            Name = "Movies",
            DefaultDownloadLimit = 5000,
            DefaultUploadLimit = 2000,
        };
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Torrent 1",
            DownloadLimit = 0,
            UploadLimit = -1,
        };

        _categoryService.GetByName("Movies").Returns(category);
        _torrentService.Get(1).Returns(torrent);

        var resource = new BulkTorrentActionResource
        {
            Action = "setCategory",
            Category = "Movies",
            TorrentIds = new List<int> { 1 },
        };

        var result = _controller.BulkAction(resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        Assert.That(torrent.Category, Is.EqualTo("Movies"));
        Assert.That(torrent.DownloadLimit, Is.EqualTo(5000));
        Assert.That(torrent.UploadLimit, Is.EqualTo(2000));
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public void BulkAction_setCategory_with_string_category_and_no_matching_category_still_sets_category()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Torrent 1",
        };

        _categoryService.GetByName("CustomCategory").Returns((Category)null);
        _torrentService.Get(1).Returns(torrent);

        var resource = new BulkTorrentActionResource
        {
            Action = "setCategory",
            Category = "CustomCategory",
            TorrentIds = new List<int> { 1 },
        };

        var result = _controller.BulkAction(resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        Assert.That(torrent.Category, Is.EqualTo("CustomCategory"));
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public void BulkAction_addtags_adds_tags_to_selected_torrents()
    {
        var torrent1 = new Torrent { Id = 1, Name = "Torrent 1", TagIds = new List<int> { 1 } };
        var torrent2 = new Torrent { Id = 2, Name = "Torrent 2", TagIds = new List<int> { 2 } };

        _torrentService.Get(1).Returns(torrent1);
        _torrentService.Get(2).Returns(torrent2);

        var resource = new BulkTorrentActionResource
        {
            Action = "addtags",
            TagIds = new List<int> { 2, 3 },
            TorrentIds = new List<int> { 1, 2 },
        };

        var result = _controller.BulkAction(resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var bulkResult = (BulkActionResult)((OkObjectResult)result.Result).Value;
        Assert.That(bulkResult.SuccessCount, Is.EqualTo(2));
        Assert.That(bulkResult.FailedCount, Is.EqualTo(0));

        Assert.That(torrent1.TagIds, Is.EquivalentTo(new[] { 1, 2, 3 }));
        Assert.That(torrent2.TagIds, Is.EquivalentTo(new[] { 2, 3 }));
        _torrentService.Received(1).Update(torrent1);
        _torrentService.Received(1).Update(torrent2);
    }

    [Test]
    public void BulkAction_removetags_removes_tags_from_selected_torrents()
    {
        var torrent1 = new Torrent { Id = 1, Name = "Torrent 1", TagIds = new List<int> { 1, 2, 3 } };
        var torrent2 = new Torrent { Id = 2, Name = "Torrent 2", TagIds = new List<int> { 2, 4 } };

        _torrentService.Get(1).Returns(torrent1);
        _torrentService.Get(2).Returns(torrent2);

        var resource = new BulkTorrentActionResource
        {
            Action = "removetags",
            TagIds = new List<int> { 2, 3 },
            TorrentIds = new List<int> { 1, 2 },
        };

        var result = _controller.BulkAction(resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var bulkResult = (BulkActionResult)((OkObjectResult)result.Result).Value;
        Assert.That(bulkResult.SuccessCount, Is.EqualTo(2));
        Assert.That(bulkResult.FailedCount, Is.EqualTo(0));

        Assert.That(torrent1.TagIds, Is.EquivalentTo(new[] { 1 }));
        Assert.That(torrent2.TagIds, Is.EquivalentTo(new[] { 4 }));
        _torrentService.Received(1).Update(torrent1);
        _torrentService.Received(1).Update(torrent2);
    }

    [Test]
    public void BulkAction_wraps_in_transaction_and_commits_on_success()
    {
        var mainDatabase = Substitute.For<IMainDatabase>();
        var connection = Substitute.For<IDbConnection>();
        var tx = Substitute.For<IDbTransaction>();
        mainDatabase.OpenConnection().Returns(connection);
        connection.BeginTransaction().Returns(tx);

        var controller = new TorrentController(
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentImportService,
            _connectionManager,
            _eventLogService,
            _configService,
            _signalRBroadcaster,
            new TorrentResourceValidator(),
            categoryService: _categoryService,
            mainDatabase: mainDatabase);

        var torrent = new Torrent { Id = 1, Name = "Torrent 1", TagIds = new List<int>() };
        _torrentService.Get(1).Returns(torrent);

        var resource = new BulkTorrentActionResource
        {
            Action = "addtags",
            TagIds = new List<int> { 5 },
            TorrentIds = new List<int> { 1 },
        };

        var result = controller.BulkAction(resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var bulkResult = (BulkActionResult)((OkObjectResult)result.Result).Value;
        Assert.That(bulkResult.SuccessCount, Is.EqualTo(1));
        Assert.That(bulkResult.FailedCount, Is.EqualTo(0));

        mainDatabase.Received(1).OpenConnection();
        connection.Received(1).BeginTransaction();
        tx.Received(1).Commit();
        tx.DidNotReceive().Rollback();
    }

    [Test]
    public void BulkAction_rolls_back_transaction_on_failure()
    {
        var mainDatabase = Substitute.For<IMainDatabase>();
        var connection = Substitute.For<IDbConnection>();
        var tx = Substitute.For<IDbTransaction>();
        mainDatabase.OpenConnection().Returns(connection);
        connection.BeginTransaction().Returns(tx);

        var controller = new TorrentController(
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentImportService,
            _connectionManager,
            _eventLogService,
            _configService,
            _signalRBroadcaster,
            new TorrentResourceValidator(),
            categoryService: _categoryService,
            mainDatabase: mainDatabase);

        var torrent1 = new Torrent { Id = 1, Name = "Torrent 1", TagIds = new List<int>() };
        _torrentService.Get(1).Returns(torrent1);
        _torrentService.Get(2).Returns((Torrent)null);

        var resource = new BulkTorrentActionResource
        {
            Action = "addtags",
            TagIds = new List<int> { 5 },
            TorrentIds = new List<int> { 1, 2 },
        };

        var result = controller.BulkAction(resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var bulkResult = (BulkActionResult)((OkObjectResult)result.Result).Value;
        Assert.That(bulkResult.SuccessCount, Is.EqualTo(0));
        Assert.That(bulkResult.FailedCount, Is.EqualTo(1));

        mainDatabase.Received(1).OpenConnection();
        connection.Received(1).BeginTransaction();
        tx.Received(1).Rollback();
        tx.DidNotReceive().Commit();
        Assert.That(bulkResult.Errors, Does.Contain("Bulk action rolled back due to failures."));
    }

    [Test]
    public void Create_with_category_and_no_save_path_resolves_category_save_path()
    {
        _configService.TorrentSaveDirectory.Returns("/downloads/default");
        _categoryService.GetSavePathForCategory("Movies", "/downloads/default").Returns("/downloads/movies");
        _torrentService.GetByInfoHash(Arg.Any<string>()).Returns((Torrent)null);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(callInfo => callInfo.Arg<Torrent>());

        var resource = new TorrentResource
        {
            Name = "Movie Torrent",
            InfoHash = "1234567890abcdef1234567890abcdef12345678",
            Category = "Movies",
            SavePath = null,
        };

        var result = _controller.Create(resource);

        Assert.That(result.Result, Is.InstanceOf<CreatedResult>());
        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.Category == "Movies" &&
            t.SavePath == "/downloads/movies" &&
            t.SourcePath == "/downloads/movies"));
    }

    [Test]
    public void Create_with_custom_save_path_overrides_category_save_path()
    {
        _configService.TorrentSaveDirectory.Returns("/downloads/default");
        _categoryService.GetSavePathForCategory("Movies", "/downloads/default").Returns("/downloads/movies");
        _torrentService.GetByInfoHash(Arg.Any<string>()).Returns((Torrent)null);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(callInfo => callInfo.Arg<Torrent>());

        var resource = new TorrentResource
        {
            Name = "Movie Torrent",
            InfoHash = "1234567890abcdef1234567890abcdef12345678",
            Category = "Movies",
            SavePath = "/custom/movies/dir",
        };

        var result = _controller.Create(resource);

        Assert.That(result.Result, Is.InstanceOf<CreatedResult>());
        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.Category == "Movies" &&
            t.SavePath == "/custom/movies/dir"));
    }

    [Test]
    public void Create_magnet_with_category_and_no_save_path_resolves_category_save_path()
    {
        var imported = new Torrent { Id = 1, Name = "Imported Magnet" };
        _torrentImportService.ImportFromMagnet(Arg.Any<string>()).Returns(imported);
        _configService.TorrentSaveDirectory.Returns("/downloads/default");
        _categoryService.GetSavePathForCategory("TV", "/downloads/default").Returns("/downloads/tv");

        var resource = new TorrentResource
        {
            MagnetLink = "magnet:?xt=urn:btih:1234567890abcdef1234567890abcdef12345678&dn=Test",
            Category = "TV",
            SavePath = null,
        };

        var result = _controller.Create(resource);

        Assert.That(result.Result, Is.InstanceOf<CreatedResult>());
        Assert.That(imported.Category, Is.EqualTo("TV"));
        Assert.That(imported.SavePath, Is.EqualTo("/downloads/tv"));
        Assert.That(imported.SourcePath, Is.EqualTo("/downloads/tv"));
        _torrentService.Received(1).Update(imported);
    }

    [Test]
    public void Handle_rapid_consecutive_torrent_updates_coalesce_into_throttled_broadcasts()
    {
        _signalRBroadcaster.IsConnected.Returns(true);
        _trackerEntryService.GetByTorrentId(1).Returns(new List<TrackerEntry>());

        for (var i = 0; i < 10; i++)
        {
            var torrent = new Torrent
            {
                Id = 1,
                Name = "Rapid Torrent",
                Downloaded = i * 1024,
                Progress = i * 10.0,
            };

            _controller.Handle(new ModelEvent<Torrent>(torrent, ModelAction.Updated));
        }

        // Only 1 broadcast immediately (leading edge); subsequent 9 are throttled
        Assert.That(_controller.PendingUpdatesCount, Is.EqualTo(1));
        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Any<SignalRMessage>());

        _controller.Flush();

        Assert.That(_controller.PendingUpdatesCount, Is.EqualTo(0));
        _signalRBroadcaster.Received(2).BroadcastMessage(Arg.Any<SignalRMessage>());
        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Updated &&
            ((TorrentResource)m.Body).Progress == 90.0));
    }

    [Test]
    public void Handle_distinct_torrent_ids_are_all_broadcasted()
    {
        _signalRBroadcaster.IsConnected.Returns(true);
        _trackerEntryService.GetByTorrentId(Arg.Any<int>()).Returns(new List<TrackerEntry>());

        var t1 = new Torrent { Id = 1, Name = "Torrent 1" };
        var t2 = new Torrent { Id = 2, Name = "Torrent 2" };
        var t3 = new Torrent { Id = 3, Name = "Torrent 3" };

        _controller.Handle(new ModelEvent<Torrent>(t1, ModelAction.Updated));
        _controller.Handle(new ModelEvent<Torrent>(t2, ModelAction.Updated));
        _controller.Handle(new ModelEvent<Torrent>(t3, ModelAction.Updated));

        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m => ((TorrentResource)m.Body).Id == 1));
        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m => ((TorrentResource)m.Body).Id == 2));
        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m => ((TorrentResource)m.Body).Id == 3));
        _signalRBroadcaster.Received(3).BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public void Handle_create_and_delete_events_are_processed_cleanly()
    {
        _signalRBroadcaster.IsConnected.Returns(true);
        _trackerEntryService.GetByTorrentId(Arg.Any<int>()).Returns(new List<TrackerEntry>());

        var torrent = new Torrent { Id = 42, Name = "Lifecycle Torrent" };

        _controller.Handle(new ModelEvent<Torrent>(torrent, ModelAction.Created));
        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Created &&
            ((TorrentResource)m.Body).Id == 42));

        _controller.Handle(new ModelEvent<Torrent>(torrent, ModelAction.Updated));
        Assert.That(_controller.PendingUpdatesCount, Is.EqualTo(1));

        _controller.Handle(new ModelEvent<Torrent>(torrent, ModelAction.Deleted));
        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Deleted &&
            ((TorrentResource)m.Body).Id == 42));
        Assert.That(_controller.PendingUpdatesCount, Is.EqualTo(0));

        _controller.Flush();
        _signalRBroadcaster.DidNotReceive().BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Updated &&
            ((TorrentResource)m.Body).Id == 42));
    }

    [Test]
    public void GetResourceById_caches_metadata_and_trackers_without_hammering_external_tables()
    {
        var mediaService = Substitute.For<IMediaEnrichmentService>();
        mediaService.GetMetadata(100).Returns(new TorrentMediaMetadata { TorrentId = 100, Title = "Cached Movie" });

        using var controller = new TorrentController(
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentImportService,
            _connectionManager,
            _eventLogService,
            _configService,
            _signalRBroadcaster,
            _validator,
            mediaEnrichmentService: mediaService,
            coalesceWindow: TimeSpan.Zero);

        _signalRBroadcaster.IsConnected.Returns(true);
        _trackerEntryService.GetByTorrentId(100).Returns(new List<TrackerEntry>());

        var torrent = new Torrent { Id = 100, Name = "Movie Torrent" };

        controller.Handle(new ModelEvent<Torrent>(torrent, ModelAction.Updated));
        controller.Handle(new ModelEvent<Torrent>(torrent, ModelAction.Updated));
        controller.Handle(new ModelEvent<Torrent>(torrent, ModelAction.Updated));

        mediaService.Received(1).GetMetadata(100);
        _trackerEntryService.Received(1).GetByTorrentId(100);
    }

    [Test]
    public void GetPieceMap_returns_compressed_RLE_bitmask_and_rarity()
    {
        const int torrentId = 42;
        const string infoHash = "1234567890abcdef1234567890abcdef12345678";
        var torrent = new Torrent
        {
            Id = torrentId,
            InfoHash = infoHash,
            PieceCount = 6,
            PieceLength = 16384
        };

        _torrentService.Get(torrentId).Returns(torrent);

        var pieceStorage = Substitute.For<IPieceStorage>();
        var piecePicker = Substitute.For<NzbDrone.Core.Torrents.IPiecePicker>();

        pieceStorage.GetVerifiedPieces(infoHash).Returns(new[] { true, true, false, false, false, false });
        pieceStorage.GetCorruptedPieces(infoHash).Returns(new HashSet<int> { 3 });
        piecePicker.GetActivePieces(infoHash).Returns(new HashSet<int> { 2 });

        var peerSeed = Substitute.For<PeerConnection>((System.Net.Sockets.TcpClient)null, (NzbDrone.Core.Peers.Encryption.IDhKeyPool)null);
        peerSeed.IsSeed.Returns(true);

        var peerLeech = Substitute.For<PeerConnection>((System.Net.Sockets.TcpClient)null, (NzbDrone.Core.Peers.Encryption.IDhKeyPool)null);
        peerLeech.IsSeed.Returns(false);
        peerLeech.PeerPieces.Returns(new[] { true, true, false, false, false, false });

        _connectionManager.GetConnections(infoHash).Returns(new List<PeerConnection> { peerSeed, peerLeech });

        using var controller = new TorrentController(
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentImportService,
            _connectionManager,
            _eventLogService,
            _configService,
            _signalRBroadcaster,
            _validator,
            categoryService: _categoryService,
            pieceStorage: pieceStorage,
            piecePicker: piecePicker);

        var result = controller.GetPieceMap(torrentId.ToString());

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var pieceMap = okResult.Value as PieceMapResource;

        Assert.That(pieceMap, Is.Not.Null);
        Assert.That(pieceMap.TorrentId, Is.EqualTo(torrentId));
        Assert.That(pieceMap.InfoHash, Is.EqualTo(infoHash));
        Assert.That(pieceMap.TotalPieces, Is.EqualTo(6));
        Assert.That(pieceMap.PieceLength, Is.EqualTo(16384));

        // Spans: 2x verified (2), 1x in-flight (1), 1x corrupted (3), 2x missing (0)
        Assert.That(pieceMap.Spans.Count, Is.EqualTo(4));
        Assert.That(pieceMap.Spans[0].Count, Is.EqualTo(2));
        Assert.That(pieceMap.Spans[0].State, Is.EqualTo(2));
        Assert.That(pieceMap.Spans[1].Count, Is.EqualTo(1));
        Assert.That(pieceMap.Spans[1].State, Is.EqualTo(1));
        Assert.That(pieceMap.Spans[2].Count, Is.EqualTo(1));
        Assert.That(pieceMap.Spans[2].State, Is.EqualTo(3));
        Assert.That(pieceMap.Spans[3].Count, Is.EqualTo(2));
        Assert.That(pieceMap.Spans[3].State, Is.EqualTo(0));

        // RleSpans [[count, state], ...]
        Assert.That(pieceMap.RleSpans.Count, Is.EqualTo(4));
        Assert.That(pieceMap.RleSpans[0], Is.EqualTo(new[] { 2, 2 }));
        Assert.That(pieceMap.RleSpans[1], Is.EqualTo(new[] { 1, 1 }));
        Assert.That(pieceMap.RleSpans[2], Is.EqualTo(new[] { 1, 3 }));
        Assert.That(pieceMap.RleSpans[3], Is.EqualTo(new[] { 2, 0 }));

        // Rarity: [2, 2, 1, 1, 1, 1]
        Assert.That(pieceMap.Rarity, Is.EqualTo(new[] { 2, 2, 1, 1, 1, 1 }));
        // RaritySpans: [[2, 2], [4, 1]]
        Assert.That(pieceMap.RaritySpans.Count, Is.EqualTo(2));
        Assert.That(pieceMap.RaritySpans[0], Is.EqualTo(new[] { 2, 2 }));
        Assert.That(pieceMap.RaritySpans[1], Is.EqualTo(new[] { 4, 1 }));
    }

    [Test]
    public void GetPieceMap_returns_NotFound_when_torrent_does_not_exist()
    {
        _torrentService.Get(999).Returns((Torrent)null);
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _controller.GetPieceMap("999");
        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void Announce_returns_429_with_RetryAfter_header_when_all_trackers_are_rate_limited()
    {
        const int torrentId = 15;
        var torrent = new Torrent
        {
            Id = torrentId,
            Name = "RateLimited.Movie",
            InfoHash = "1234567890123456789012345678901234567890"
        };
        _torrentService.Get(torrentId).Returns(torrent);

        var httpContext = new DefaultHttpContext();
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        var rateLimitedResults = new List<TrackerAnnounceResult>
        {
            new()
            {
                TrackerId = 1,
                Url = "http://tracker.example.com/announce",
                Success = false,
                FailureReason = "Rate limited: minimum announce interval of 60s not elapsed (retry in 45s)",
                RetryAfterSeconds = 45
            }
        };

        _trackerAnnounceService.AnnounceTorrent(torrent, force: true).Returns(rateLimitedResults);

        var result = _controller.Announce(torrentId);

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        var objResult = (ObjectResult)result;
        Assert.That(objResult.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
        Assert.That(httpContext.Response.Headers.ContainsKey("Retry-After"), Is.True);
        Assert.That(httpContext.Response.Headers["Retry-After"].ToString(), Is.EqualTo("45"));
    }

    [Test]
    public void AnnounceTracker_returns_429_with_RetryAfter_header_when_rate_limited()
    {
        const int torrentId = 16;
        const int trackerId = 2;
        var torrent = new Torrent
        {
            Id = torrentId,
            Name = "SingleTracker.RateLimit",
            InfoHash = "abcdefabcdefabcdefabcdefabcdefabcdefabcd"
        };
        var tracker = new TrackerEntry
        {
            Id = trackerId,
            TorrentId = torrentId,
            Url = "http://tracker.example.com/announce",
            Enabled = true
        };

        _torrentService.Get(torrentId).Returns(torrent);
        _trackerEntryService.GetByTorrentId(torrentId).Returns(new List<TrackerEntry> { tracker });

        var httpContext = new DefaultHttpContext();
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        var rateLimitedResult = new TrackerAnnounceResult
        {
            TrackerId = trackerId,
            Url = tracker.Url,
            Success = false,
            FailureReason = "Rate limited: minimum announce interval of 120s not elapsed (retry in 60s)",
            RetryAfterSeconds = 60
        };

        _trackerAnnounceService.AnnounceTracker(torrent, tracker, force: true).Returns(rateLimitedResult);

        var result = _controller.AnnounceTracker(torrentId, trackerId);

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        var objResult = (ObjectResult)result;
        Assert.That(objResult.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
        Assert.That(httpContext.Response.Headers.ContainsKey("Retry-After"), Is.True);
        Assert.That(httpContext.Response.Headers["Retry-After"].ToString(), Is.EqualTo("60"));
    }
}
