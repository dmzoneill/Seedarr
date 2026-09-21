using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Seeding;

[TestFixture]
public class TorrentStateMachineTest
{
    private ITorrentEventLogService _eventLogService;
    private ITorrentService _torrentService;
    private TorrentStateMachine _subject;

    [SetUp]
    public void Setup()
    {
        _eventLogService = Substitute.For<ITorrentEventLogService>();
        _torrentService = Substitute.For<ITorrentService>();
        _subject = new TorrentStateMachine(_eventLogService, null, new Lazy<ITorrentService>(() => _torrentService));
    }

    [Test]
    public void HandleForceCompleted_switches_downloading_to_seeding()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "TestTorrent",
            Status = TorrentStatus.Downloading,
            ForceCompleted = true
        };

        var result = _subject.HandleForceCompleted(torrent);

        Assert.That(result, Is.True);
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
        _eventLogService.Received(1).Info(1, "Seeding", Arg.Any<string>());
    }

    [Test]
    public void HandleForceCompleted_returns_false_when_not_force_completed()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            ForceCompleted = false
        };

        var result = _subject.HandleForceCompleted(torrent);

        Assert.That(result, Is.False);
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Downloading));
    }

    [Test]
    public void CheckDownloadThreshold_switches_to_seeding_when_threshold_reached()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "TestTorrent",
            Status = TorrentStatus.Downloading,
            Progress = 0.8,
            Threshold = 80
        };

        var result = _subject.CheckDownloadThreshold(torrent, 1.0);

        Assert.That(result, Is.True);
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
        _eventLogService.Received(1).Info(1, "Seeding", Arg.Any<string>());
    }

    [Test]
    public void CheckDownloadThreshold_uses_default_when_torrent_threshold_zero()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "TestTorrent",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            Threshold = 0
        };

        var result = _subject.CheckDownloadThreshold(torrent, 0.5);

        Assert.That(result, Is.True);
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
    }

    [Test]
    public void CheckDownloadThreshold_does_not_switch_when_below_threshold()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Downloading,
            Progress = 0.49,
            Threshold = 50
        };

        var result = _subject.CheckDownloadThreshold(torrent, 1.0);

        Assert.That(result, Is.False);
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Downloading));
    }

    [Test]
    public void ApplyRatioLimit_stops_torrents_reaching_limit()
    {
        var torrent1 = new Torrent { Id = 1, Status = TorrentStatus.Seeding, Ratio = 2.5, UploadSpeed = 100, DownloadSpeed = 50, Active = true };
        var torrent2 = new Torrent { Id = 2, Status = TorrentStatus.Seeding, Ratio = 1.0, UploadSpeed = 100, DownloadSpeed = 50, Active = true };
        var list = new List<Torrent> { torrent1, torrent2 };

        var stopped = _subject.ApplyRatioLimit(list, 2.0);

        Assert.That(torrent1.Status, Is.EqualTo(TorrentStatus.Stopped));
        Assert.That(torrent1.UploadSpeed, Is.EqualTo(0));
        Assert.That(torrent1.DownloadSpeed, Is.EqualTo(0));
        Assert.That(torrent1.Active, Is.False);
        Assert.That(list, Has.Count.EqualTo(1));
        Assert.That(list[0].Id, Is.EqualTo(2));
        Assert.That(stopped, Has.Count.EqualTo(1));
        Assert.That(stopped[0].Id, Is.EqualTo(1));
        _eventLogService.Received(1).Info(1, "Seeding", Arg.Any<string>());
    }

    [Test]
    public void ApplyRatioLimit_pauses_torrents_when_action_is_Pause()
    {
        var torrent1 = new Torrent { Id = 1, Status = TorrentStatus.Seeding, Ratio = 2.5, UploadSpeed = 100, DownloadSpeed = 50, Active = true };
        var torrent2 = new Torrent { Id = 2, Status = TorrentStatus.Seeding, Ratio = 1.0, UploadSpeed = 100, DownloadSpeed = 50, Active = true };
        var list = new List<Torrent> { torrent1, torrent2 };

        var paused = _subject.ApplyRatioLimit(list, 2.0, "Pause");

        Assert.That(torrent1.Status, Is.EqualTo(TorrentStatus.Paused));
        Assert.That(torrent1.UploadSpeed, Is.EqualTo(0));
        Assert.That(torrent1.DownloadSpeed, Is.EqualTo(0));
        Assert.That(torrent1.Active, Is.False);
        Assert.That(list, Has.Count.EqualTo(1));
        Assert.That(list[0].Id, Is.EqualTo(2));
        Assert.That(paused, Has.Count.EqualTo(1));
        Assert.That(paused[0].Id, Is.EqualTo(1));
        _torrentService.DidNotReceiveWithAnyArgs().Delete(default, default);
    }

    [Test]
    public void ApplyRatioLimit_removes_torrent_without_data_when_action_is_RemoveTorrent()
    {
        var torrent = new Torrent { Id = 1, Status = TorrentStatus.Seeding, Ratio = 2.5, UploadSpeed = 100, DownloadSpeed = 50, Active = true };
        var list = new List<Torrent> { torrent };

        var stopped = _subject.ApplyRatioLimit(list, 2.0, "RemoveTorrent");

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Stopped));
        Assert.That(list, Is.Empty);
        Assert.That(stopped, Is.Empty);
        _torrentService.Received(1).Delete(1, false);
    }

    [Test]
    public void ApplyRatioLimit_removes_torrent_with_data_when_action_is_RemoveTorrentAndData()
    {
        var torrent = new Torrent { Id = 1, Status = TorrentStatus.Seeding, Ratio = 2.5, UploadSpeed = 100, DownloadSpeed = 50, Active = true };
        var list = new List<Torrent> { torrent };

        var stopped = _subject.ApplyRatioLimit(list, 2.0, "RemoveTorrentAndData");

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Stopped));
        Assert.That(list, Is.Empty);
        Assert.That(stopped, Is.Empty);
        _torrentService.Received(1).Delete(1, true);
    }

    [Test]
    public void ApplyRatioLimit_returns_empty_when_limit_is_zero_or_negative()
    {
        var torrent = new Torrent { Id = 1, Status = TorrentStatus.Seeding, Ratio = 2.5 };
        var list = new List<Torrent> { torrent };

        var stopped = _subject.ApplyRatioLimit(list, 0.0);

        Assert.That(stopped, Is.Empty);
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(list, Has.Count.EqualTo(1));
    }

    [Test]
    public void ApplyRatioLimit_stops_torrents_reaching_per_torrent_ratio_limit()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            Ratio = 1.6,
            RatioLimit = 1.5,
            UploadSpeed = 100,
            Active = true
        };
        var list = new List<Torrent> { torrent };

        var stopped = _subject.ApplyRatioLimit(list, 0.0);

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Stopped));
        Assert.That(stopped, Has.Count.EqualTo(1));
        Assert.That(list, Is.Empty);
    }

    [Test]
    public void ApplyRatioLimit_per_torrent_ratio_limit_overrides_global_ratio_limit_when_below_torrent_limit()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            Ratio = 2.5,
            RatioLimit = 3.0,
            UploadSpeed = 100,
            Active = true
        };
        var list = new List<Torrent> { torrent };

        // Global limit is 2.0, but per-torrent limit is 3.0, current ratio 2.5 -> should NOT stop
        var stopped = _subject.ApplyRatioLimit(list, 2.0);

        Assert.That(stopped, Is.Empty);
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(list, Has.Count.EqualTo(1));
    }

    [Test]
    public void ApplyRatioLimit_stops_torrents_reaching_per_torrent_seeding_time_limit()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            Ratio = 0.5,
            SeedingTime = 3605,
            SeedingTimeLimit = 3600,
            UploadSpeed = 100,
            Active = true
        };
        var list = new List<Torrent> { torrent };

        var stopped = _subject.ApplyRatioLimit(list, 0.0);

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Stopped));
        Assert.That(stopped, Has.Count.EqualTo(1));
        Assert.That(list, Is.Empty);
    }

    [Test]
    public void ApplyRatioLimit_stops_torrents_reaching_category_target_ratio()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var categoryService = Substitute.For<ICategoryService>();
        categoryService.GetByName("Movies").Returns(new Category { Name = "Movies", TargetRatio = 1.75, AutoStop = true });
        var subject = new TorrentStateMachine(_eventLogService, eventAggregator, new Lazy<ITorrentService>(() => _torrentService), categoryService);

        var torrent = new Torrent
        {
            Id = 1,
            Category = "Movies",
            Status = TorrentStatus.Seeding,
            Ratio = 1.8,
            UploadSpeed = 100,
            DownloadSpeed = 50,
            Active = true
        };
        var list = new List<Torrent> { torrent };

        var stopped = subject.ApplyRatioLimit(list, 0.0);

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Stopped));
        Assert.That(torrent.UploadSpeed, Is.EqualTo(0));
        Assert.That(torrent.DownloadSpeed, Is.EqualTo(0));
        Assert.That(torrent.Active, Is.False);
        Assert.That(stopped, Has.Count.EqualTo(1));
        eventAggregator.Received(1).PublishEvent(Arg.Any<TorrentSeedGoalReachedEvent>());
        eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentRatioReachedEvent>(e => e.Ratio == 1.8));
    }

    [Test]
    public void ApplyRatioLimit_stops_torrents_reaching_category_target_seed_time()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var categoryService = Substitute.For<ICategoryService>();
        categoryService.GetByName("TV").Returns(new Category { Name = "TV", TargetSeedTimeMinutes = 60, AutoStop = true });
        var subject = new TorrentStateMachine(_eventLogService, eventAggregator, new Lazy<ITorrentService>(() => _torrentService), categoryService);

        var torrent = new Torrent
        {
            Id = 1,
            Category = "TV",
            Status = TorrentStatus.Seeding,
            Ratio = 0.5,
            SeedingTime = 3660, // 61 minutes
            UploadSpeed = 100,
            DownloadSpeed = 50,
            Active = true
        };
        var list = new List<Torrent> { torrent };

        var stopped = subject.ApplyRatioLimit(list, 0.0);

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Stopped));
        Assert.That(torrent.UploadSpeed, Is.EqualTo(0));
        Assert.That(torrent.DownloadSpeed, Is.EqualTo(0));
        Assert.That(torrent.Active, Is.False);
        Assert.That(stopped, Has.Count.EqualTo(1));
        eventAggregator.Received(1).PublishEvent(Arg.Any<TorrentSeedGoalReachedEvent>());
        eventAggregator.Received(1).PublishEvent(Arg.Any<TorrentSeedingTimeReachedEvent>());
    }

    [Test]
    public void ApplyRatioLimit_does_not_stop_torrents_when_category_autostop_disabled()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var categoryService = Substitute.For<ICategoryService>();
        categoryService.GetByName("Movies").Returns(new Category
        {
            Name = "Movies",
            TargetRatio = 1.75,
            TargetSeedTimeMinutes = 60,
            AutoStop = false
        });
        var subject = new TorrentStateMachine(_eventLogService, eventAggregator, new Lazy<ITorrentService>(() => _torrentService), categoryService);

        var torrent = new Torrent
        {
            Id = 1,
            Category = "Movies",
            Status = TorrentStatus.Seeding,
            Ratio = 2.0,
            SeedingTime = 7200,
            UploadSpeed = 100,
            Active = true
        };
        var list = new List<Torrent> { torrent };

        // Global ratio limit is 1.5, category has TargetRatio 1.75 & TargetSeedTimeMinutes 60, but AutoStop is false
        var stopped = subject.ApplyRatioLimit(list, 1.5);

        Assert.That(stopped, Is.Empty);
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(list, Has.Count.EqualTo(1));
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<TorrentSeedGoalReachedEvent>());
    }

    [Test]
    public void ApplyRatioLimit_category_target_ratio_overrides_global_ratio_limit()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var categoryService = Substitute.For<ICategoryService>();
        categoryService.GetByName("Movies").Returns(new Category
        {
            Name = "Movies",
            TargetRatio = 3.0,
            AutoStop = true
        });
        var subject = new TorrentStateMachine(_eventLogService, eventAggregator, new Lazy<ITorrentService>(() => _torrentService), categoryService);

        var torrent = new Torrent
        {
            Id = 1,
            Category = "Movies",
            Status = TorrentStatus.Seeding,
            Ratio = 2.5,
            UploadSpeed = 100,
            Active = true
        };
        var list = new List<Torrent> { torrent };

        // Global limit is 2.0, but category target ratio is 3.0, current ratio 2.5 -> should NOT stop
        var stopped = subject.ApplyRatioLimit(list, 2.0);

        Assert.That(stopped, Is.Empty);
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(list, Has.Count.EqualTo(1));

        // When ratio reaches category target ratio 3.0 -> should stop
        torrent.Ratio = 3.0;
        stopped = subject.ApplyRatioLimit(list, 2.0);

        Assert.That(stopped, Has.Count.EqualTo(1));
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Stopped));
        eventAggregator.Received(1).PublishEvent(Arg.Any<TorrentSeedGoalReachedEvent>());
        eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentRatioReachedEvent>(e => e.Ratio == 3.0));
    }

    [Test]
    public void ApplyRatioLimit_falls_back_to_global_ratio_limit_when_category_has_no_target_ratio()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var categoryService = Substitute.For<ICategoryService>();
        categoryService.GetByName("Movies").Returns(new Category
        {
            Name = "Movies",
            TargetRatio = 0.0,
            AutoStop = true
        });
        var subject = new TorrentStateMachine(_eventLogService, eventAggregator, new Lazy<ITorrentService>(() => _torrentService), categoryService);

        var torrent = new Torrent
        {
            Id = 1,
            Category = "Movies",
            Status = TorrentStatus.Seeding,
            Ratio = 2.5,
            UploadSpeed = 100,
            Active = true
        };
        var list = new List<Torrent> { torrent };

        // Global limit is 2.0, category has AutoStop = true but TargetRatio = 0 -> falls back to global 2.0 -> stops
        var stopped = subject.ApplyRatioLimit(list, 2.0);

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Stopped));
        Assert.That(stopped, Has.Count.EqualTo(1));
        Assert.That(list, Is.Empty);
        eventAggregator.Received(1).PublishEvent(Arg.Any<TorrentSeedGoalReachedEvent>());
    }

    [Test]
    public void ApplyRatioLimit_publishes_seeding_time_reached_event_when_seed_time_limit_reached()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var subject = new TorrentStateMachine(_eventLogService, eventAggregator, new Lazy<ITorrentService>(() => _torrentService));

        var torrent = new Torrent
        {
            Id = 1,
            Status = TorrentStatus.Seeding,
            Ratio = 0.5,
            SeedingTime = 3600,
            SeedingTimeLimit = 3600
        };
        var list = new List<Torrent> { torrent };

        subject.ApplyRatioLimit(list, 0.0);

        eventAggregator.Received(1).PublishEvent(Arg.Any<TorrentSeedingTimeReachedEvent>());
        eventAggregator.Received(1).PublishEvent(Arg.Any<TorrentSeedGoalReachedEvent>());
    }
}
