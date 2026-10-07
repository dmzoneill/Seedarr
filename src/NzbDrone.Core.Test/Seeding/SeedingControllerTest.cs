using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Torrents;
using NzbDrone.SignalR;
using Seedarr.Api.V1.Seeding;

namespace NzbDrone.Core.Test.Seeding;

[TestFixture]
public class SeedingControllerTest
{
    private ISeedingService _seedingService;
    private ISpeedHistoryService _speedHistoryService;
    private ITorrentService _torrentService;
    private IBroadcastSignalRMessage _signalRBroadcaster;
    private SeedingController _controller;

    [SetUp]
    public void SetUp()
    {
        _seedingService = Substitute.For<ISeedingService>();
        _speedHistoryService = Substitute.For<ISpeedHistoryService>();
        _torrentService = Substitute.For<ITorrentService>();
        _signalRBroadcaster = Substitute.For<IBroadcastSignalRMessage>();
        _controller = new SeedingController(_seedingService, _speedHistoryService, _torrentService, _signalRBroadcaster);
    }

    [Test]
    public void Start_returns_not_found_when_torrent_missing()
    {
        _seedingService.Start(999).Returns(false);

        var result = _controller.Start(999);

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void Start_returns_ok_when_torrent_found()
    {
        _seedingService.Start(1).Returns(true);

        var result = _controller.Start(1);

        Assert.That(result, Is.InstanceOf<OkResult>());
    }

    [Test]
    public void Stop_returns_not_found_when_torrent_missing()
    {
        _seedingService.Stop(999).Returns(false);

        var result = _controller.Stop(999);

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void Stop_returns_ok_when_torrent_found()
    {
        _seedingService.Stop(1).Returns(true);

        var result = _controller.Stop(1);

        Assert.That(result, Is.InstanceOf<OkResult>());
    }

    [Test]
    public void GetTorrentHistory_returns_not_found_when_torrent_missing()
    {
        _torrentService.Get(999).Returns((Torrent)null);

        var result = _controller.GetTorrentHistory(999);

        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void GetTorrentHistory_returns_history_when_torrent_exists()
    {
        var history = new List<TorrentSpeedSnapshot>();
        _torrentService.Get(1).Returns(new Torrent { Id = 1 });
        _speedHistoryService.GetTorrentHistory(1).Returns(history);

        var result = _controller.GetTorrentHistory(1);

        Assert.That(result.Value, Is.SameAs(history));
    }
}
