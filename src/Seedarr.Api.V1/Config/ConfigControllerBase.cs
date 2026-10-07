using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using Seedarr.Http.REST;
using Seedarr.Http.REST.Attributes;

namespace Seedarr.Api.V1.Config;

public abstract class ConfigController<TResource> : Controller
    where TResource : RestResource, new()
{
    protected readonly IConfigService _configService;
    protected ResourceValidator<TResource> SharedValidator { get; set; }

    protected ConfigController(IConfigService configService)
    {
        _configService = configService;
        SharedValidator = new ResourceValidator<TResource>();
    }

    [HttpGet]
    [Authorize(Policy = Policies.Reader)]
    [Produces("application/json")]
    public TResource GetConfig()
    {
        var resource = ToResource(_configService);
        resource.Id = 1;

        return resource;
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = Policies.Reader)]
    [Produces("application/json")]
    public TResource GetConfigById(int id)
    {
        return GetConfig();
    }

    private const int ConfigResourceId = 1;

    [RestPutById]
    [Authorize(Policy = Policies.AdminOnly)]
    [Consumes("application/json")]
    [Produces("application/json")]
    public virtual ActionResult<TResource> SaveConfig(int? id, [FromBody] TResource resource)
    {
        var idValidationError = ValidateSaveConfigId(id, resource);
        if (idValidationError != null)
        {
            return idValidationError;
        }

        if (resource == null)
        {
            return BadRequest("Request body cannot be empty.");
        }

        if (SharedValidator != null)
        {
            var result = SharedValidator.Validate(resource);
            if (!result.IsValid)
            {
                return BadRequest(result.Errors);
            }
        }

        var dictionary = resource.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(prop => prop.Name != "Id" && prop.Name != "ResourceName")
            .ToDictionary(prop => prop.Name, prop => prop.GetValue(resource, null));

        try
        {
            _configService.SaveConfigDictionary(dictionary);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return Problem(detail: ex.Message, title: "Failed to save configuration.", statusCode: 500);
        }

        return Accepted(resource);
    }

    protected ActionResult<TResource>? ValidateSaveConfigId(int? routeId, TResource? resource)
    {
        if (routeId.HasValue && routeId.Value != ConfigResourceId)
        {
            return BadRequest($"Config resource id must be {ConfigResourceId}.");
        }

        if (resource == null)
        {
            return null;
        }

        if (resource.Id != 0 && resource.Id != ConfigResourceId)
        {
            return BadRequest($"Config resource id must be {ConfigResourceId}.");
        }

        if (routeId.HasValue && resource.Id != 0 && resource.Id != routeId.Value)
        {
            return BadRequest("Route id does not match resource id.");
        }

        return null;
    }

    protected abstract TResource ToResource(IConfigService model);
}
