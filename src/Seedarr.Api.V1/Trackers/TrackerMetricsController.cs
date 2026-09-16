using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Trackers.Metrics;
using Seedarr.Http;

namespace Seedarr.Api.V1.Trackers;

[V1ApiController("trackermetrics")]
public class TrackerMetricsController : Controller
{
    private readonly ITrackerMetricService _trackerMetricService;

    public TrackerMetricsController(ITrackerMetricService trackerMetricService)
    {
        _trackerMetricService = trackerMetricService;
    }

    [HttpGet]
    public ActionResult<List<TrackerMetricResource>> GetAll()
    {
        var metrics = _trackerMetricService.GetAllMetrics();
        return Ok(metrics.Select(TrackerMetricResourceMapper.ToResource).ToList());
    }

    [HttpGet("summary")]
    public ActionResult<TrackerMetricsSummary> GetSummary()
    {
        var summary = _trackerMetricService.GetSummary();
        return Ok(summary);
    }

    [HttpGet("{id:int}")]
    public ActionResult<TrackerMetricResource> Get(int id)
    {
        var metric = _trackerMetricService.GetMetric(id);
        if (metric == null)
        {
            return NotFound();
        }

        return Ok(TrackerMetricResourceMapper.ToResource(metric));
    }

    [HttpGet("{id:int}/history")]
    public ActionResult<List<TrackerMetricSnapshot>> GetHistory(int id, [FromQuery] int hours = 24, [FromQuery] int limit = 500)
    {
        if (hours <= 0)
        {
            return BadRequest("Hours must be greater than 0.");
        }

        if (hours > 168)
        {
            return BadRequest("Hours cannot exceed 168 (7 days).");
        }

        if (limit <= 0 || limit > 2000)
        {
            return BadRequest("Limit must be between 1 and 2000.");
        }

        var history = _trackerMetricService.GetHistory(id, hours, limit);
        return Ok(history);
    }

    [HttpPost("{id:int}/reset")]
    public ActionResult Reset(int id)
    {
        _trackerMetricService.ResetMetrics(id);
        return Ok(new { success = true, message = "Tracker metrics reset." });
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _trackerMetricService.DeleteMetric(id);
        return Ok(new { success = true, message = "Tracker metric deleted." });
    }
}
