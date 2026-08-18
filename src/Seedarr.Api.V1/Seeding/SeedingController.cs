using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Seeding;
using NzbDrone.SignalR;
using Seedarr.Http;

namespace Seedarr.Api.V1.Seeding;

[V1ApiController("seeding")]
public class SeedingController : Controller, IHandle<SeedingTickEvent>
{
    private readonly ISeedingService _seedingService;
    private readonly ISpeedHistoryService _speedHistoryService;
    private readonly IBroadcastSignalRMessage _signalRBroadcaster;

    public SeedingController(ISeedingService seedingService, ISpeedHistoryService speedHistoryService, IBroadcastSignalRMessage signalRBroadcaster)
    {
        _seedingService = seedingService;
        _speedHistoryService = speedHistoryService;
        _signalRBroadcaster = signalRBroadcaster;
    }

    public void Handle(SeedingTickEvent message)
    {
        if (!_signalRBroadcaster.IsConnected)
        {
            return;
        }

        var stats = _seedingService.GetStats();
        _signalRBroadcaster.BroadcastMessage(new SignalRMessage
        {
            Name = "seeding",
            Body = stats,
            Action = ModelAction.Updated
        });
    }

    [HttpGet("stats")]
    public ActionResult<SeedingStats> GetStats()
    {
        return _seedingService.GetStats();
    }

    [HttpGet("history")]
    public ActionResult<List<SpeedSnapshot>> GetHistory()
    {
        return _speedHistoryService.GetHistory();
    }

    [HttpGet("history/{torrentId:int}")]
    public ActionResult<List<TorrentSpeedSnapshot>> GetTorrentHistory(int torrentId)
    {
        return _speedHistoryService.GetTorrentHistory(torrentId);
    }

    [HttpPost("start/{torrentId:int}")]
    public ActionResult Start(int torrentId)
    {
        _seedingService.Start(torrentId);
        return Ok();
    }

    [HttpPost("stop/{torrentId:int}")]
    public ActionResult Stop(int torrentId)
    {
        _seedingService.Stop(torrentId);
        return Ok();
    }

    [HttpPost("start-all")]
    public ActionResult StartAll()
    {
        _seedingService.StartAll();
        return Ok();
    }

    [HttpPost("stop-all")]
    public ActionResult StopAll()
    {
        _seedingService.StopAll();
        return Ok();
    }
}
