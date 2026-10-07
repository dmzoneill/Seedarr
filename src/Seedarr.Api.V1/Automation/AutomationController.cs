#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using NzbDrone.Common.Serializer;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Automation;
using NzbDrone.SignalR;
using Seedarr.Http;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Automation;

[V1ApiController("automation")]
[Authorize(Policy = Policies.Reader)]
public class AutomationController : RestControllerWithSignalR<AutomationScriptResource, AutomationScript>
{
    private readonly IAutomationService _automationService;
    private readonly IAutomationMarketplaceService _marketplaceService;
    private readonly IBroadcastSignalRMessage _signalRBroadcaster;

    public AutomationController(
        IAutomationService automationService,
        IAutomationMarketplaceService marketplaceService,
        IBroadcastSignalRMessage signalRBroadcaster)
        : base(signalRBroadcaster)
    {
        _automationService = automationService;
        _marketplaceService = marketplaceService;
        _signalRBroadcaster = signalRBroadcaster;
    }

    public const int MaxListLogPreviewCharacters = 500;

    [HttpGet]
    public ActionResult<List<AutomationScriptResource>> GetAll()
    {
        return _automationService.GetAll().Select(s => ToResource(s, truncateLog: true)).ToList();
    }

    [HttpGet("{id:int}")]
    public ActionResult<AutomationScriptResource> Get(int id)
    {
        var script = _automationService.Get(id);
        if (script == null)
        {
            return NotFound();
        }

        return ToResource(script);
    }

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public ActionResult<AutomationScriptResource> Create([FromBody] AutomationScriptResource resource)
    {
        if (resource == null)
        {
            return BadRequest("Request body cannot be null");
        }

        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return BadRequest("Script name is required.");
        }

        var model = ToModel(resource);
        var added = _automationService.Add(model);
        return ToResource(added);
    }

    [HttpPut("{id:int}")]
    [HttpPut]
    [Authorize(Policy = Policies.AdminOnly)]
    public ActionResult<AutomationScriptResource> Update([FromBody] JsonElement body, int? id = null)
    {
        if (body.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return BadRequest("Request body cannot be null");
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            return BadRequest("Request body must be a JSON object");
        }

        var presentPropertyKeys = body.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var resource = JsonSerializer.Deserialize<AutomationScriptResource>(body, STJson.GetSerializerSettings());
        if (resource == null)
        {
            return BadRequest("Request body cannot be null");
        }

        return UpdateScript(resource, id, presentPropertyKeys);
    }

    [NonAction]
    public ActionResult<AutomationScriptResource> Update(int id, AutomationScriptResource resource) => UpdateScript(resource, id, null);

    private ActionResult<AutomationScriptResource> UpdateScript(
        AutomationScriptResource resource,
        int? id,
        IReadOnlySet<string>? presentPropertyKeys)
    {
        if (resource == null)
        {
            return BadRequest("Request body cannot be null");
        }

        if (id.HasValue)
        {
            if (resource.Id > 0 && resource.Id != id.Value)
            {
                return BadRequest($"ID mismatch: URL ID {id.Value} does not match resource ID {resource.Id}.");
            }

            if (resource.Id == 0)
            {
                resource.Id = id.Value;
            }
        }

        if (resource.Id <= 0)
        {
            return NotFound("Automation script not found");
        }

        var existing = _automationService.Get(resource.Id);
        if (existing == null)
        {
            return NotFound("Automation script not found");
        }

        var name = presentPropertyKeys != null && !presentPropertyKeys.Contains("name")
            ? existing.Name
            : resource.Name;

        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest("Script name is required.");
        }

        var model = presentPropertyKeys == null
            ? ToModel(resource)
            : AutomationScriptUpdateMerger.Merge(existing, resource, presentPropertyKeys);

        model.Id = existing.Id;
        model.Name = name;
        model.LastExecutedAt = existing.LastExecutedAt;
        model.LastExecutionStatus = existing.LastExecutionStatus;
        model.LastExecutionLog = existing.LastExecutionLog;

        var updated = _automationService.Update(model);
        return Ok(ToResource(updated));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public ActionResult Delete(int id)
    {
        if (_automationService.Get(id) == null)
        {
            return NotFound();
        }

        _automationService.Delete(id);
        return Ok();
    }

    [HttpPost("{id:int}/run")]
    [HttpPost("{id:int}/execute")]
    [Authorize(Policy = Policies.AdminOnly)]
    public ActionResult<AutomationExecutionResult> Run(int id, [FromQuery] int? torrentId = null)
    {
        var result = _automationService.ExecuteScript(id, torrentId);
        _signalRBroadcaster.BroadcastMessage(new SignalRMessage
        {
            Name = "AutomationExecuted",
            Action = NzbDrone.Core.Datastore.ModelAction.Updated,
            Body = new
            {
                ScriptId = id,
                TorrentId = torrentId,
                Success = result.Success,
                ExecutionTimeMs = result.ExecutionTimeMs,
                OutputLog = result.OutputLog,
                Error = result.Error,
                Result = result
            }
        });
        return Ok(result);
    }

    [HttpPost("test")]
    [Authorize(Policy = Policies.AdminOnly)]
    public ActionResult<AutomationExecutionResult> Test([FromBody] AutomationTestRequestResource request)
    {
        if (request?.Script == null)
        {
            return BadRequest("Script definition is required.");
        }

        var model = ToModel(request.Script);
        var result = _automationService.TestScript(model, request.TorrentId, request.CustomInputs);
        _signalRBroadcaster.BroadcastMessage(new SignalRMessage
        {
            Name = "AutomationTriggerEvaluated",
            Action = NzbDrone.Core.Datastore.ModelAction.Updated,
            Body = new
            {
                ScriptId = request.Script.Id,
                ScriptName = request.Script.Name,
                Trigger = request.Script.Trigger.ToString(),
                TorrentId = request.TorrentId,
                Success = result.Success,
                ExecutionTimeMs = result.ExecutionTimeMs,
                OutputLog = result.OutputLog,
                Error = result.Error,
                Result = result
            }
        });
        return Ok(result);
    }

    [HttpGet("marketplace")]
    public ActionResult<List<AutomationMarketplaceTemplate>> GetMarketplaceTemplates()
    {
        return Ok(_marketplaceService.GetTemplates());
    }

    [HttpPost("marketplace/install")]
    [Authorize(Policy = Policies.AdminOnly)]
    public ActionResult<AutomationScriptResource> InstallMarketplaceTemplate([FromBody] InstallMarketplaceTemplateRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.TemplateId))
        {
            return BadRequest("Template ID is required.");
        }

        try
        {
            var script = _marketplaceService.InstallTemplate(request.TemplateId, request.CustomName, request.CustomInputs);
            var added = _automationService.Add(script);
            return ToResource(added);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    protected override AutomationScriptResource GetResourceById(AutomationScript model)
    {
        return ToResource(model);
    }

    private static AutomationScriptResource ToResource(AutomationScript model, bool truncateLog = false)
    {
        if (model == null)
        {
            return null!;
        }

        var log = model.LastExecutionLog;
        if (truncateLog && log != null && log.Length > MaxListLogPreviewCharacters)
        {
            log = string.Concat(log.AsSpan(0, MaxListLogPreviewCharacters), "...");
        }

        return new AutomationScriptResource
        {
            Id = model.Id,
            Name = model.Name,
            Description = model.Description,
            Trigger = model.Trigger,
            Language = model.Language,
            Code = model.Code,
            InputsJson = model.InputsJson,
            IsEnabled = model.IsEnabled,
            TargetCategories = model.TargetCategories ?? new List<string>(),
            TargetTagIds = model.TargetTagIds ?? new List<int>(),
            CreatedAt = model.CreatedAt,
            LastExecutedAt = model.LastExecutedAt,
            LastExecutionStatus = model.LastExecutionStatus,
            LastExecutionLog = log,
        };
    }

    private static AutomationScript ToModel(AutomationScriptResource resource)
    {
        return new AutomationScript
        {
            Id = resource.Id,
            Name = resource.Name ?? string.Empty,
            Description = resource.Description,
            Trigger = resource.Trigger,
            Language = resource.Language,
            Code = resource.Code ?? string.Empty,
            InputsJson = resource.InputsJson,
            IsEnabled = resource.IsEnabled,
            TargetCategories = resource.TargetCategories ?? new List<string>(),
            TargetTagIds = resource.TargetTagIds ?? new List<int>(),
            CreatedAt = resource.CreatedAt,
            LastExecutedAt = resource.LastExecutedAt,
            LastExecutionStatus = resource.LastExecutionStatus,
            LastExecutionLog = resource.LastExecutionLog,
        };
    }
}
