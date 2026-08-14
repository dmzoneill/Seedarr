using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Update;
using Seedarr.Http;

namespace Seedarr.Api.V1.Update;

[V1ApiController("update")]
public class UpdateController : Controller
{
    private readonly IUpdateService _updateService;

    public UpdateController(IUpdateService updateService)
    {
        _updateService = updateService;
    }

    [HttpGet]
    public ActionResult<UpdateInfo> GetUpdate()
    {
        var info = _updateService.CheckForUpdate();
        return info;
    }
}
