using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.HealthCheck;
using Seedarr.Http;

namespace Seedarr.Api.V1.Health;

[V1ApiController("health")]
public class HealthController : Controller
{
    private readonly IHealthCheckService _healthCheckService;

    public HealthController(IHealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    [HttpGet]
    public async Task<ActionResult<List<HealthCheckResult>>> GetHealth(CancellationToken cancellationToken = default)
    {
        var results = await _healthCheckService.PerformChecksAsync(cancellationToken);
        return Ok(results);
    }
}
