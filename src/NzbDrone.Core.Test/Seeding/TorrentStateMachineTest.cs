using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Seeding;

[TestFixture]
public class TorrentStateMachineTest
{
    private ITorrentEventLogService _eventLogService;
    private TorrentStateMachine _subject;

    [SetUp]
    public void Setup()
    {
        _eventLogService = Substitute.For<ITorrentEventLogService>();
        _subject = new TorrentStateMachine(_eventLogService);
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

        _subject.ApplyRatioLimit(list, 2.0);

        Assert.That(torrent1.Status, Is.EqualTo(TorrentStatus.Stopped));
        Assert.That(torrent1.UploadSpeed, Is.EqualTo(0));
        Assert.That(torrent1.DownloadSpeed, Is.EqualTo(0));
        Assert.That(torrent1.Active, Is.False);
        Assert.That(list, Has.Count.EqualTo(1));
        Assert.That(list[0].Id, Is.EqualTo(2));
        _eventLogService.Received(1).Info(1, "Seeding", Arg.Any<string>());
    }
}
