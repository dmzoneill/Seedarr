using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Seeding;

[TestFixture]
public class SeedingServiceTest
{
    private ITorrentService _torrentService;
    private IConfigService _configService;
    private IEventAggregator _eventAggregator;
    private SeedingService _service;

    [SetUp]
    public void Setup()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _configService = Substitute.For<IConfigService>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _service = new SeedingService(_torrentService, _configService, _eventAggregator);
    }

    [Test]
    public void Start_should_set_torrent_to_seeding_when_complete()
    {
        var torrent = new Torrent { Id = 1, Name = "Test", Status = TorrentStatus.Stopped, Progress = 1.0 };
        _torrentService.Get(1).Returns(torrent);

        _service.Start(1);

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(torrent.Active, Is.True);
        Assert.That(torrent.ForceStart, Is.True);
        Assert.That(torrent.LastActive, Is.Not.Null);
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public void Start_should_set_torrent_to_seeding_when_force_completed()
    {
        var torrent = new Torrent { Id = 1, Name = "Test", Status = TorrentStatus.Stopped, ForceCompleted = true };
        _torrentService.Get(1).Returns(torrent);

        _service.Start(1);

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(torrent.Active, Is.True);
        Assert.That(torrent.ForceStart, Is.True);
        Assert.That(torrent.LastActive, Is.Not.Null);
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public void Start_should_set_torrent_to_downloading_when_incomplete()
    {
        var torrent = new Torrent { Id = 1, Name = "Test", Status = TorrentStatus.Stopped, Progress = 0.5 };
        _torrentService.Get(1).Returns(torrent);

        _service.Start(1);

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Downloading));
        Assert.That(torrent.Active, Is.True);
        Assert.That(torrent.ForceStart, Is.True);
        Assert.That(torrent.LastActive, Is.Not.Null);
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public void Start_should_publish_started_event()
    {
        var torrent = new Torrent { Id = 1, Name = "Test" };
        _torrentService.Get(1).Returns(torrent);

        _service.Start(1);

        _eventAggregator.Received(1).PublishEvent(Arg.Is<SeedingStartedEvent>(e => e.TorrentId == 1));
    }

    [Test]
    public void Start_should_not_update_when_torrent_not_found()
    {
        _torrentService.Get(99).Returns((Torrent)null);

        _service.Start(99);

        _torrentService.DidNotReceive().Update(Arg.Any<Torrent>());
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<SeedingStartedEvent>());
    }

    [Test]
    public void Stop_should_set_torrent_to_stopped_when_found()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Test",
            Status = TorrentStatus.Seeding,
            Active = true,
            ForceStart = true,
            UploadSpeed = 1024,
            DownloadSpeed = 2048
        };
        _torrentService.Get(1).Returns(torrent);

        _service.Stop(1);

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Stopped));
        Assert.That(torrent.Active, Is.False);
        Assert.That(torrent.ForceStart, Is.False);
        Assert.That(torrent.UploadSpeed, Is.EqualTo(0));
        Assert.That(torrent.DownloadSpeed, Is.EqualTo(0));
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public void Stop_should_set_downloading_torrent_to_stopped_when_found()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Test",
            Status = TorrentStatus.Downloading,
            Active = true,
            ForceStart = true,
            UploadSpeed = 1024,
            DownloadSpeed = 2048
        };
        _torrentService.Get(1).Returns(torrent);

        _service.Stop(1);

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Stopped));
        Assert.That(torrent.Active, Is.False);
        Assert.That(torrent.ForceStart, Is.False);
        Assert.That(torrent.UploadSpeed, Is.EqualTo(0));
        Assert.That(torrent.DownloadSpeed, Is.EqualTo(0));
        _torrentService.Received(1).Update(torrent);
    }

    [Test]
    public void Stop_should_publish_stopped_event()
    {
        var torrent = new Torrent { Id = 1, Name = "Test" };
        _torrentService.Get(1).Returns(torrent);

        _service.Stop(1);

        _eventAggregator.Received(1).PublishEvent(Arg.Is<SeedingStoppedEvent>(e => e.TorrentId == 1));
    }

    [Test]
    public void Stop_should_not_update_when_torrent_not_found()
    {
        _torrentService.Get(99).Returns((Torrent)null);

        _service.Stop(99);

        _torrentService.DidNotReceive().Update(Arg.Any<Torrent>());
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<SeedingStoppedEvent>());
    }

    [Test]
    public void StartAll_should_enable_autostart_when_disabled()
    {
        _configService.AutoStart.Returns(false);
        _torrentService.GetAll().Returns(new List<Torrent>());

        _service.StartAll();

        _configService.Received(1).SaveConfigDictionary(
            Arg.Is<Dictionary<string, object>>(d => (bool)d["AutoStart"] == true));
    }

    [Test]
    public void StartAll_should_not_enable_autostart_when_already_enabled()
    {
        _configService.AutoStart.Returns(true);
        _torrentService.GetAll().Returns(new List<Torrent>());

        _service.StartAll();

        _configService.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public void StartAll_should_start_stopped_queued_and_paused_torrents_with_correct_status()
    {
        var stoppedComplete = new Torrent { Id = 1, Name = "StoppedComplete", Status = TorrentStatus.Stopped, Progress = 1.0 };
        var stoppedIncomplete = new Torrent { Id = 2, Name = "StoppedIncomplete", Status = TorrentStatus.Stopped, Progress = 0.5 };
        var queued = new Torrent { Id = 3, Name = "Queued", Status = TorrentStatus.Queued, Progress = 0.0 };
        var pausedComplete = new Torrent { Id = 4, Name = "PausedComplete", Status = TorrentStatus.Paused, ForceCompleted = true };
        var pausedIncomplete = new Torrent { Id = 5, Name = "PausedIncomplete", Status = TorrentStatus.Paused, Progress = 0.2 };
        var seeding = new Torrent { Id = 6, Name = "Seeding", Status = TorrentStatus.Seeding };
        var downloading = new Torrent { Id = 7, Name = "Downloading", Status = TorrentStatus.Downloading };

        _configService.AutoStart.Returns(true);
        _torrentService.GetAll().Returns(new List<Torrent>
        {
            stoppedComplete,
            stoppedIncomplete,
            queued,
            pausedComplete,
            pausedIncomplete,
            seeding,
            downloading
        });

        _service.StartAll();

        Assert.That(stoppedComplete.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(stoppedComplete.Active, Is.True);
        Assert.That(stoppedComplete.LastActive, Is.Not.Null);

        Assert.That(stoppedIncomplete.Status, Is.EqualTo(TorrentStatus.Downloading));
        Assert.That(stoppedIncomplete.Active, Is.True);
        Assert.That(stoppedIncomplete.LastActive, Is.Not.Null);

        Assert.That(queued.Status, Is.EqualTo(TorrentStatus.Downloading));
        Assert.That(queued.Active, Is.True);
        Assert.That(queued.LastActive, Is.Not.Null);

        Assert.That(pausedComplete.Status, Is.EqualTo(TorrentStatus.Seeding));
        Assert.That(pausedComplete.Active, Is.True);
        Assert.That(pausedComplete.LastActive, Is.Not.Null);

        Assert.That(pausedIncomplete.Status, Is.EqualTo(TorrentStatus.Downloading));
        Assert.That(pausedIncomplete.Active, Is.True);
        Assert.That(pausedIncomplete.LastActive, Is.Not.Null);

        _torrentService.Received(1).Update(stoppedComplete);
        _torrentService.Received(1).Update(stoppedIncomplete);
        _torrentService.Received(1).Update(queued);
        _torrentService.Received(1).Update(pausedComplete);
        _torrentService.Received(1).Update(pausedIncomplete);

        _torrentService.DidNotReceive().Update(seeding);
        _torrentService.DidNotReceive().Update(downloading);
    }

    [Test]
    public void StartAll_should_publish_event_per_started_torrent()
    {
        var stopped = new Torrent { Id = 1, Name = "S1", Status = TorrentStatus.Stopped };
        var queued = new Torrent { Id = 2, Name = "S2", Status = TorrentStatus.Queued };
        _configService.AutoStart.Returns(true);
        _torrentService.GetAll().Returns(new List<Torrent> { stopped, queued });

        _service.StartAll();

        _eventAggregator.Received(1).PublishEvent(Arg.Is<SeedingStartedEvent>(e => e.TorrentId == 1));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<SeedingStartedEvent>(e => e.TorrentId == 2));
    }

    [Test]
    public void StopAll_should_disable_autostart_when_enabled()
    {
        _configService.AutoStart.Returns(true);
        _torrentService.GetAll().Returns(new List<Torrent>());

        _service.StopAll();

        _configService.Received(1).SaveConfigDictionary(
            Arg.Is<Dictionary<string, object>>(d => (bool)d["AutoStart"] == false));
    }

    [Test]
    public void StopAll_should_not_disable_autostart_when_already_disabled()
    {
        _configService.AutoStart.Returns(false);
        _torrentService.GetAll().Returns(new List<Torrent>());

        _service.StopAll();

        _configService.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public void StopAll_should_stop_seeding_downloading_and_active_torrents()
    {
        var seeding = new Torrent
        {
            Id = 1,
            Name = "Seeding",
            Status = TorrentStatus.Seeding,
            ForceStart = true,
            Active = true,
            UploadSpeed = 5000,
            DownloadSpeed = 1000
        };
        var downloading = new Torrent
        {
            Id = 2,
            Name = "Downloading",
            Status = TorrentStatus.Downloading,
            ForceStart = true,
            Active = true,
            UploadSpeed = 200,
            DownloadSpeed = 8000
        };
        var stopped = new Torrent { Id = 3, Name = "Stopped", Status = TorrentStatus.Stopped, Active = false };
        var paused = new Torrent { Id = 4, Name = "Paused", Status = TorrentStatus.Paused, Active = false };

        _configService.AutoStart.Returns(false);
        _torrentService.GetAll().Returns(new List<Torrent> { seeding, downloading, stopped, paused });

        _service.StopAll();

        Assert.That(seeding.Status, Is.EqualTo(TorrentStatus.Stopped));
        Assert.That(seeding.ForceStart, Is.False);
        Assert.That(seeding.Active, Is.False);
        Assert.That(seeding.UploadSpeed, Is.EqualTo(0));
        Assert.That(seeding.DownloadSpeed, Is.EqualTo(0));

        Assert.That(downloading.Status, Is.EqualTo(TorrentStatus.Stopped));
        Assert.That(downloading.ForceStart, Is.False);
        Assert.That(downloading.Active, Is.False);
        Assert.That(downloading.UploadSpeed, Is.EqualTo(0));
        Assert.That(downloading.DownloadSpeed, Is.EqualTo(0));

        _torrentService.Received(1).Update(seeding);
        _torrentService.Received(1).Update(downloading);
        _torrentService.DidNotReceive().Update(stopped);
        _torrentService.DidNotReceive().Update(paused);
    }

    [Test]
    public void StopAll_should_publish_event_per_stopped_torrent()
    {
        var s1 = new Torrent { Id = 1, Name = "S1", Status = TorrentStatus.Seeding };
        var d1 = new Torrent { Id = 2, Name = "D1", Status = TorrentStatus.Downloading };
        _configService.AutoStart.Returns(false);
        _torrentService.GetAll().Returns(new List<Torrent> { s1, d1 });

        _service.StopAll();

        _eventAggregator.Received(1).PublishEvent(Arg.Is<SeedingStoppedEvent>(e => e.TorrentId == 1));
        _eventAggregator.PublishEvent(Arg.Is<SeedingStoppedEvent>(e => e.TorrentId == 2));
    }

    [Test]
    public void GetStats_should_return_correct_active_count()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Uploaded = 100, Downloaded = 50, Ratio = 2.0 },
            new Torrent { Id = 2, Status = TorrentStatus.Stopped, Uploaded = 200, Downloaded = 100, Ratio = 1.0 },
            new Torrent { Id = 3, Status = TorrentStatus.Seeding, Uploaded = 300, Downloaded = 150, Ratio = 3.0 }
        };
        _torrentService.GetAll().Returns(torrents);

        var stats = _service.GetStats();

        Assert.That(stats.ActiveTorrents, Is.EqualTo(2));
    }

    [Test]
    public void GetStats_should_sum_all_uploaded_and_downloaded()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Uploaded = 100, Downloaded = 50, Ratio = 2.0 },
            new Torrent { Id = 2, Status = TorrentStatus.Stopped, Uploaded = 200, Downloaded = 100, Ratio = 1.0 }
        };
        _torrentService.GetAll().Returns(torrents);

        var stats = _service.GetStats();

        Assert.That(stats.TotalUploaded, Is.EqualTo(300));
        Assert.That(stats.TotalDownloaded, Is.EqualTo(150));
    }

    [Test]
    public void GetStats_should_average_ratio_of_active_only()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Ratio = 2.0 },
            new Torrent { Id = 2, Status = TorrentStatus.Seeding, Ratio = 4.0 },
            new Torrent { Id = 3, Status = TorrentStatus.Stopped, Ratio = 100.0 }
        };
        _torrentService.GetAll().Returns(torrents);

        var stats = _service.GetStats();

        Assert.That(stats.AverageRatio, Is.EqualTo(3.0));
    }

    [Test]
    public void GetStats_should_return_zero_ratio_when_no_active_torrents()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());

        var stats = _service.GetStats();

        Assert.That(stats.ActiveTorrents, Is.EqualTo(0));
        Assert.That(stats.AverageRatio, Is.EqualTo(0));
    }
}
