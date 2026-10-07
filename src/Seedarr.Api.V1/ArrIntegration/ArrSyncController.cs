using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Authentication;
using Seedarr.Http;

namespace Seedarr.Api.V1.ArrIntegration;

[V1ApiController("arrsync")]
[Authorize(Policy = Policies.Reader)]
public class ArrSyncController : Controller
{
    private readonly IArrSyncService _arrSyncService;

    public ArrSyncController(IArrSyncService arrSyncService)
    {
        _arrSyncService = arrSyncService;
    }

    [HttpPost("sync")]
    [Authorize(Policy = Policies.AdminOnly)]
    public ActionResult<SyncResult> Sync()
    {
        var result = _arrSyncService.Sync();
        return result;
    }
}
