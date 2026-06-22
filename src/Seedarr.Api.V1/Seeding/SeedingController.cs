using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Seeding;
using Seedarr.Http;

namespace Seedarr.Api.V1.Seeding;

[V1ApiController("seeding")]
public class SeedingController : Controller
{
    private readonly ISeedingService _seedingService;
    private readonly ISpeedHistoryService _speedHistoryService;

    public SeedingController(ISeedingService seedingService, ISpeedHistoryService speedHistoryService)
    {
        _seedingService = seedingService;
        _speedHistoryService = speedHistoryService;
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
