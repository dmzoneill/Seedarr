using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Plugins;
using Seedarr.Http;

namespace Seedarr.Api.V1.Plugins;

[V1ApiController("plugins")]
[Route("api/v1/plugins")]
[Route("api/v1/plugin")]
public class PluginController : ControllerBase
{
    private readonly IPluginService _pluginService;

    public PluginController(IPluginService pluginService)
    {
        _pluginService = pluginService;
    }

    [HttpGet]
    public ActionResult<List<PluginResource>> GetAll()
    {
        var plugins = _pluginService.GetAll();
        var resources = plugins.Select(PluginResourceMapper.ToResource).ToList();
        return Ok(resources);
    }

    [HttpPost("{id}/enable")]
    public ActionResult<PluginResource> Enable(string id)
    {
        var plugin = _pluginService.Enable(id);
        if (plugin == null)
        {
            return NotFound($"Plugin '{id}' not found.");
        }

        return Ok(PluginResourceMapper.ToResource(plugin));
    }

    [HttpPost("{id}/disable")]
    public ActionResult<PluginResource> Disable(string id)
    {
        var plugin = _pluginService.Disable(id);
        if (plugin == null)
        {
            return NotFound($"Plugin '{id}' not found.");
        }

        return Ok(PluginResourceMapper.ToResource(plugin));
    }
}
