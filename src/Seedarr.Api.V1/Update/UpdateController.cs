using System;
using System.Collections.Generic;
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
    public ActionResult<List<UpdateResource>> GetUpdates()
    {
        var info = _updateService.CheckForUpdate();
        var results = new List<UpdateResource>();

        if (info.UpdateAvailable && info.LatestVersion != null)
        {
            results.Add(new UpdateResource
            {
                Version = info.LatestVersion,
                ReleaseDate = DateTime.UtcNow,
                Installed = false,
                Latest = true,
                Changes = new UpdateChanges
                {
                    New = !string.IsNullOrWhiteSpace(info.ReleaseNotes)
                        ? new List<string> { info.ReleaseNotes }
                        : new List<string>(),
                    Fixed = new List<string>()
                }
            });
        }

        results.Add(new UpdateResource
        {
            Version = info.CurrentVersion,
            ReleaseDate = DateTime.UtcNow,
            Installed = true,
            Latest = !info.UpdateAvailable,
            Changes = new UpdateChanges
            {
                New = new List<string> { "Currently running version" },
                Fixed = new List<string>()
            }
        });

        return Ok(results);
    }
}
