using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Network;
using Seedarr.Http;

namespace Seedarr.Api.V1.Network;

[V1ApiController("portmapping")]
[Authorize(Policy = Policies.Reader)]
public class PortMappingController : Controller
{
    private readonly IPortMappingEngine _portMappingEngine;

    public PortMappingController(IPortMappingEngine portMappingEngine)
    {
        _portMappingEngine = portMappingEngine;
    }

    [HttpGet]
    public async Task<ActionResult<PortMappingStatus>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await _portMappingEngine.GetStatusAsync(cancellationToken);
        return Ok(status);
    }

    [HttpPost("refresh")]
    [Authorize(Policy = Policies.Operator)]
    public async Task<ActionResult<PortMappingStatus>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var status = await _portMappingEngine.RefreshAsync(cancellationToken);
        return Ok(status);
    }
}
