using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ArrIntegration;
using Seedarr.Http;

namespace Seedarr.Api.V1.ArrIntegration;

[V1ApiController("arrsync")]
public class ArrSyncController : Controller
{
    private readonly IArrSyncService _arrSyncService;

    public ArrSyncController(IArrSyncService arrSyncService)
    {
        _arrSyncService = arrSyncService;
    }

    [HttpPost("sync")]
    public ActionResult<SyncResult> Sync()
    {
        var result = _arrSyncService.Sync();
        return result;
    }
}
