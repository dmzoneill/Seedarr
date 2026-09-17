using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.RemotePathMappings;
using Seedarr.Http;

namespace Seedarr.Api.V1.RemotePathMappings;

[V1ApiController("remotepathmapping")]
public class RemotePathMappingController : Controller
{
    private readonly IRemotePathMappingService _remotePathMappingService;

    public RemotePathMappingController(IRemotePathMappingService remotePathMappingService)
    {
        _remotePathMappingService = remotePathMappingService;
    }

    [HttpGet]
    public ActionResult<List<RemotePathMappingResource>> GetAll()
    {
        var mappings = _remotePathMappingService.All();
        return Ok(mappings.Select(RemotePathMappingResourceMapper.ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<RemotePathMappingResource> Get(int id)
    {
        var mapping = _remotePathMappingService.Get(id);
        if (mapping == null)
        {
            return NotFound();
        }

        return Ok(RemotePathMappingResourceMapper.ToResource(mapping));
    }

    [HttpPost]
    public ActionResult<RemotePathMappingResource> Create([FromBody] RemotePathMappingResource resource)
    {
        if (resource == null)
        {
            return BadRequest("Request body cannot be null");
        }

        if (string.IsNullOrWhiteSpace(resource.Host))
        {
            return BadRequest("Host is required");
        }

        if (string.IsNullOrWhiteSpace(resource.RemotePath))
        {
            return BadRequest("Remote path is required");
        }

        if (string.IsNullOrWhiteSpace(resource.LocalPath))
        {
            return BadRequest("Local path is required");
        }

        var model = RemotePathMappingResourceMapper.ToModel(resource);
        var created = _remotePathMappingService.Add(model);
        return Ok(RemotePathMappingResourceMapper.ToResource(created));
    }

    [HttpPut("{id:int}")]
    [HttpPut]
    public ActionResult<RemotePathMappingResource> Update(int? id, [FromBody] RemotePathMappingResource resource)
    {
        if (resource == null)
        {
            return BadRequest("Request body cannot be null");
        }

        var targetId = id ?? resource.Id;
        if (targetId <= 0)
        {
            return BadRequest("Valid mapping ID is required");
        }

        var existing = _remotePathMappingService.Get(targetId);
        if (existing == null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(resource.Host))
        {
            return BadRequest("Host is required");
        }

        if (string.IsNullOrWhiteSpace(resource.RemotePath))
        {
            return BadRequest("Remote path is required");
        }

        if (string.IsNullOrWhiteSpace(resource.LocalPath))
        {
            return BadRequest("Local path is required");
        }

        var model = RemotePathMappingResourceMapper.ToModel(resource);
        model.Id = targetId;
        _remotePathMappingService.Update(model);

        var updated = _remotePathMappingService.Get(targetId);
        return Ok(RemotePathMappingResourceMapper.ToResource(updated));
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _remotePathMappingService.Delete(id);
        return NoContent();
    }

    [HttpPost("test")]
    public ActionResult<RemotePathMappingTestResult> Test([FromBody] RemotePathMappingTestRequest request)
    {
        if (request == null)
        {
            return BadRequest("Request body cannot be null");
        }

        var result = _remotePathMappingService.TestMapping(
            request.Host,
            request.Path,
            request.Direction ?? "remoteToLocal");

        return Ok(result);
    }
}
