using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.DownloadPlusPlus;
using Seedarr.Http;

namespace Seedarr.Api.V1.DownloadPlusPlus;

[V1ApiController("downloadplusplus")]
public class DownloadPlusPlusController : Controller
{
    private readonly IDownloadPlusPlusService _downloadPlusPlusService;

    public DownloadPlusPlusController(IDownloadPlusPlusService downloadPlusPlusService)
    {
        _downloadPlusPlusService = downloadPlusPlusService;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var summary = await _downloadPlusPlusService.GetStatusSummaryAsync();
        return Ok(summary);
    }

    [HttpGet("trackers")]
    public IActionResult GetTrackers()
    {
        var trackers = _downloadPlusPlusService.GetAllTrackers();
        return Ok(trackers);
    }

    [HttpGet("check/{torrentId:int}")]
    public async Task<IActionResult> InspectTorrentTrackers(int torrentId)
    {
        var result = await _downloadPlusPlusService.InspectTorrentTrackersAsync(torrentId);
        return Ok(result);
    }

    [HttpGet("check-hash/{infoHash}")]
    public async Task<IActionResult> InspectHashTrackers(string infoHash, [FromQuery] string name = "")
    {
        var result = await _downloadPlusPlusService.InspectHashTrackersAsync(infoHash, name);
        return Ok(result);
    }

    [HttpPost("trackers")]
    public IActionResult AddTracker([FromBody] AddTrackerResource resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Url))
        {
            return BadRequest(new { message = "Tracker URL is required." });
        }

        var tracker = _downloadPlusPlusService.AddTracker(resource.Url, TrackerSourceType.Manual, "Manual Entry");
        return Ok(tracker);
    }

    [HttpDelete("trackers/{id:int}")]
    public IActionResult DeleteTracker(int id)
    {
        _downloadPlusPlusService.DeleteTracker(id);
        return Ok(new { success = true });
    }

    [HttpPost("scan")]
    public async Task<IActionResult> ScanTrackers()
    {
        var testedCount = await _downloadPlusPlusService.ProbeTrackerHealthAsync();
        return Ok(new { success = true, testedCount });
    }

    [HttpPost("harvest/prowlarr")]
    public async Task<IActionResult> HarvestProwlarr()
    {
        var count = await _downloadPlusPlusService.HarvestFromProwlarrAsync();
        return Ok(new { success = true, harvestedCount = count });
    }

    [HttpPost("harvest/feeds")]
    public async Task<IActionResult> HarvestFeeds()
    {
        var count = await _downloadPlusPlusService.HarvestFromCuratedListsAsync();
        return Ok(new { success = true, harvestedCount = count });
    }

    [HttpPost("boost/{torrentId:int}")]
    public async Task<IActionResult> BoostTorrent(int torrentId)
    {
        var result = await _downloadPlusPlusService.BoostTorrentAsync(torrentId);
        return Ok(result);
    }

    [HttpPost("boost-hash/{infoHash}")]
    public async Task<IActionResult> BoostHash(string infoHash, [FromQuery] string name = "")
    {
        var result = await _downloadPlusPlusService.BoostHashAsync(infoHash, name);
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
            var result = await _downloadPlusPlusService.InjectTrackerToTorrentAsync(resource.TorrentId, resource.TrackerUrl);
            return Ok(result);
        }
        else
        {
            var result = await _downloadPlusPlusService.InjectTrackerToHashAsync(resource.InfoHash, resource.TrackerUrl);
            return Ok(result);
        }
    }

    [HttpPost("boost-all")]
    public async Task<IActionResult> BoostAllTorrents()
    {
        var results = await _downloadPlusPlusService.BoostAllTorrentsAsync();
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
