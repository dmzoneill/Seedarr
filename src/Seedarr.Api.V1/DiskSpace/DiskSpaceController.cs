using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.DiskSpace;
using Seedarr.Http;

namespace Seedarr.Api.V1.DiskSpace;

/// <summary>
/// Controller for disk space information.
/// </summary>
[V1ApiController("diskspace")]
public class DiskSpaceController : Controller
{
    private readonly IDiskSpaceService _diskSpaceService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskSpaceController"/> class.
    /// </summary>
    /// <param name="diskSpaceService">Disk space service.</param>
    public DiskSpaceController(IDiskSpaceService diskSpaceService)
    {
        _diskSpaceService = diskSpaceService;
    }

    /// <summary>
    /// Gets disk space information for all relevant locations.
    /// </summary>
    /// <param name="refresh">Whether to force fresh disk space evaluation bypassing TTL cache.</param>
    /// <returns>A list of disk space resources.</returns>
    [HttpGet]
    public ActionResult<List<DiskSpaceResource>> GetDiskSpace([FromQuery] bool refresh = false)
    {
        var diskSpace = _diskSpaceService.GetDiskSpace(refresh);

        return Ok(diskSpace.Select(d => new DiskSpaceResource
        {
            Path = d.Path,
            Label = d.Label,
            FreeSpace = d.FreeSpace,
            TotalSpace = d.TotalSpace,
        }).ToList());
    }
}
