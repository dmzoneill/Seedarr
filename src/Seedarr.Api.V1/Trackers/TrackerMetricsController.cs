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
    public ActionResult<List<TrackerMetricSnapshot>> GetHistory(int id, [FromQuery] int hours = 24)
    {
        var history = _trackerMetricService.GetHistory(id, hours);
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
