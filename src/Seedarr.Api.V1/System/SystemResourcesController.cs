using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Telemetry;
using Seedarr.Http;

namespace Seedarr.Api.V1.System;

[V1ApiController("system/resources")]
[Authorize(Policy = Policies.Reader)]
public class SystemResourcesController : ControllerBase
{
    private readonly ISystemResourceService _resourceService;

    public SystemResourcesController(ISystemResourceService resourceService)
    {
        _resourceService = resourceService;
    }

    [HttpGet]
    public async Task<ActionResult<SystemResourceTelemetrySnapshot>> GetFullSnapshot(CancellationToken cancellationToken = default)
    {
        var snapshot = await _resourceService.GetFullTelemetrySnapshotAsync(cancellationToken);
        return Ok(snapshot);
    }

    [HttpGet("host")]
    public ActionResult<HostProcessResourceMetrics> GetHostMetrics()
    {
        return Ok(_resourceService.GetHostMetrics());
    }

    [HttpGet("engine")]
    public ActionResult<TorrentEngineMetrics> GetEngineMetrics()
    {
        return Ok(_resourceService.GetTorrentEngineMetrics());
    }

    [HttpGet("subsystems")]
    public async Task<ActionResult<List<SubsystemTelemetryReport>>> GetSubsystemsTelemetry(CancellationToken cancellationToken = default)
    {
        var subsystems = await _resourceService.GetSubsystemTelemetryAsync(null, cancellationToken);
        return Ok(subsystems);
    }

    [HttpGet("torrents")]
    public ActionResult<IReadOnlyList<TorrentResourceMetrics>> GetPerTorrentMetrics()
    {
        return Ok(_resourceService.GetPerTorrentMetrics());
    }

    [HttpGet("torrents/{id:int}")]
    public ActionResult<TorrentResourceMetrics> GetTorrentMetrics(int id)
    {
        var metrics = _resourceService.GetTorrentMetrics(id);
        if (metrics == null)
        {
            return NotFound(new { error = $"Torrent id {id} was not found in active engine session." });
        }

        return Ok(metrics);
    }
}
