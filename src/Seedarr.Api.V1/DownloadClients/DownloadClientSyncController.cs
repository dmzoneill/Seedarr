using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.DownloadClients.Sync;
using Seedarr.Http;

namespace Seedarr.Api.V1.DownloadClients;

[V1ApiController("downloadclientsync")]
public class DownloadClientSyncController : Controller
{
    private readonly IDownloadClientSyncService _syncService;

    public DownloadClientSyncController(IDownloadClientSyncService syncService)
    {
        _syncService = syncService;
    }

    [HttpPost("sync")]
    public ActionResult<SyncResult> Sync()
    {
        var result = _syncService.Sync();
        return Ok(result);
    }
}
