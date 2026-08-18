using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.TrackerBoost;
using Seedarr.Http;

namespace Seedarr.Api.V1.TrackerBoost;

[V1ApiController("trackerboost")]
[Route("api/v1/downloadplusplus")]
public class TrackerBoostController : Controller
{
    private readonly ITrackerBoostService _trackerBoostService;

    public TrackerBoostController(ITrackerBoostService trackerBoostService)
    {
        _trackerBoostService = trackerBoostService;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var summary = await _trackerBoostService.GetStatusSummaryAsync();
        return Ok(summary);
    }

    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        var settings = _trackerBoostService.GetSettings();
        return Ok(settings);
    }

    [HttpPut("settings")]
    public IActionResult UpdateSettings([FromBody] TrackerBoostSettings settings)
    {
        if (settings == null)
        {
            return BadRequest(new { message = "Invalid settings" });
        }

        _trackerBoostService.UpdateSettings(settings);
        return Ok(_trackerBoostService.GetSettings());
    }

    [HttpGet("trackers")]
    public IActionResult GetTrackers()
    {
        var trackers = _trackerBoostService.GetAllTrackers();
        return Ok(trackers);
    }

    [HttpGet("matrix")]
    public async Task<IActionResult> GetCrossMatrix()
    {
        var matrix = await _trackerBoostService.GetCrossMatrixAsync();
        return Ok(matrix);
    }

    [HttpGet("check/{torrentId:int}")]
    public async Task<IActionResult> InspectTorrentTrackers(int torrentId)
    {
        var result = await _trackerBoostService.InspectTorrentTrackersAsync(torrentId);
        return Ok(result);
    }

    [HttpGet("check-hash/{infoHash}")]
    public async Task<IActionResult> InspectHashTrackers(string infoHash, [FromQuery] string name = "")
    {
        var result = await _trackerBoostService.InspectHashTrackersAsync(infoHash, name);
        return Ok(result);
    }

    [HttpPost("trackers")]
    public IActionResult AddTracker([FromBody] AddTrackerResource resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Url))
        {
            return BadRequest(new { message = "Tracker URL is required." });
        }

        var tracker = _trackerBoostService.AddTracker(resource.Url, TrackerSourceType.Manual, "Manual Entry");
        return Ok(tracker);
    }

    [HttpDelete("trackers/{id:int}")]
    public IActionResult DeleteTracker(int id)
    {
        _trackerBoostService.DeleteTracker(id);
        return Ok(new { success = true });
    }

    [HttpPost("scan")]
    public async Task<IActionResult> ScanTrackers()
    {
        var testedCount = await _trackerBoostService.ProbeTrackerHealthAsync();
        return Ok(new { success = true, testedCount });
    }

    [HttpPost("harvest/downloads")]
    public async Task<IActionResult> HarvestFromDownloads()
    {
        var count = await _trackerBoostService.HarvestFromActiveDownloadsAsync();
        return Ok(new { success = true, harvestedCount = count });
    }

    [HttpPost("harvest/prowlarr")]
    public async Task<IActionResult> HarvestProwlarr()
    {
        var count = await _trackerBoostService.HarvestFromProwlarrAsync();
        return Ok(new { success = true, harvestedCount = count });
    }

    [HttpPost("harvest/feeds")]
    public async Task<IActionResult> HarvestFeeds()
    {
        var count = await _trackerBoostService.HarvestFromCuratedListsAsync();
        return Ok(new { success = true, harvestedCount = count });
    }

    [HttpPost("boost/{torrentId:int}")]
    public async Task<IActionResult> BoostTorrent(int torrentId, [FromQuery] bool onlyVerified = true)
    {
        var result = await _trackerBoostService.BoostTorrentAsync(torrentId, onlyVerified);
        return Ok(result);
    }

    [HttpPost("boost-hash/{infoHash}")]
    public async Task<IActionResult> BoostHash(string infoHash, [FromQuery] string name = "", [FromQuery] bool onlyVerified = true)
    {
        var result = await _trackerBoostService.BoostHashAsync(infoHash, name, onlyVerified);
        return Ok(result);
    }

    [HttpPost("inject")]
    public async Task<IActionResult> InjectTracker([FromBody] InjectTrackerResource resource)
    {
        if (resource == null || (resource.TorrentId <= 0 && string.IsNullOrWhiteSpace(resource.InfoHash)) || string.IsNullOrWhiteSpace(resource.TrackerUrl))
        {
            return BadRequest(new { message = "TorrentId or InfoHash and TrackerUrl are required." });
        }

        if (resource.TorrentId > 0)
        {
            var result = await _trackerBoostService.InjectTrackerToTorrentAsync(resource.TorrentId, resource.TrackerUrl);
            return Ok(result);
        }
        else
        {
            var result = await _trackerBoostService.InjectTrackerToHashAsync(resource.InfoHash, resource.TrackerUrl);
            return Ok(result);
        }
    }

    [HttpPost("boost-all")]
    public async Task<IActionResult> BoostAllTorrents([FromQuery] bool onlyVerified = true)
    {
        var results = await _trackerBoostService.BoostAllTorrentsAsync(onlyVerified);
        return Ok(results);
    }
}

public class AddTrackerResource
{
    public string Url { get; set; } = string.Empty;
}

public class InjectTrackerResource
{
    public int TorrentId { get; set; }
    public string InfoHash { get; set; } = string.Empty;
    public string TrackerUrl { get; set; } = string.Empty;
}
