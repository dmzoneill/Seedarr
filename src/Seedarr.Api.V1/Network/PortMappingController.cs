using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Network;
using Seedarr.Http;

namespace Seedarr.Api.V1.Network;

[V1ApiController("portmapping")]
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
    public async Task<ActionResult<PortMappingStatus>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var status = await _portMappingEngine.RefreshAsync(cancellationToken);
        return Ok(status);
    }
}
