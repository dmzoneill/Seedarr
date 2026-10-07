// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Developer.Debugger;
using Seedarr.Http;

namespace Seedarr.Api.V1.System;

[V1ApiController("system/developer/debugger")]
[Authorize(Policy = Policies.AdminOnly)]
public class SystemDeveloperDebuggerController : Controller
{
    private readonly IDeveloperDebuggerService _debuggerService;

    public SystemDeveloperDebuggerController(IDeveloperDebuggerService debuggerService = null)
    {
        _debuggerService = debuggerService;
    }

    [HttpGet("status")]
    public ActionResult<DebuggerStatusReport> GetStatus()
    {
        if (_debuggerService == null)
        {
            return new DebuggerStatusReport();
        }

        return Ok(_debuggerService.GetStatus());
    }

    [HttpGet("tracepoints")]
    public ActionResult<IReadOnlyList<TracepointDefinition>> GetTracepoints()
    {
        if (_debuggerService == null)
        {
            return new List<TracepointDefinition>();
        }

        return Ok(_debuggerService.GetTracepoints());
    }

    [HttpPost("tracepoints")]
    public ActionResult<TracepointDefinition> AddTracepoint([FromBody] TracepointDefinition tracepoint)
    {
        if (_debuggerService == null)
        {
            return BadRequest("Debugger service not registered.");
        }

        var created = _debuggerService.AddTracepoint(tracepoint ?? new TracepointDefinition());
        return Ok(created);
    }

    [HttpDelete("tracepoints/{id}")]
    public ActionResult RemoveTracepoint(string id)
    {
        if (_debuggerService == null)
        {
            return NotFound();
        }

        var removed = _debuggerService.RemoveTracepoint(id);
        return removed ? NoContent() : NotFound();
    }

    [HttpDelete("tracepoints")]
    public ActionResult ClearTracepoints()
    {
        _debuggerService?.ClearTracepoints();
        return NoContent();
    }

    [HttpGet("snapshots")]
    public ActionResult<IReadOnlyList<TracepointSnapshot>> GetSnapshots([FromQuery] int limit = 50)
    {
        if (_debuggerService == null)
        {
            return new List<TracepointSnapshot>();
        }

        return Ok(_debuggerService.GetSnapshots(limit));
    }

    [HttpPost("snapshots")]
    public ActionResult<TracepointSnapshot> RecordSnapshot([FromBody] TracepointSnapshot snapshot)
    {
        if (_debuggerService == null)
        {
            return BadRequest("Debugger service not registered.");
        }

        var toRecord = snapshot ?? new TracepointSnapshot();
        _debuggerService.RecordSnapshot(toRecord);
        return Ok(toRecord);
    }

    [HttpDelete("snapshots")]
    public ActionResult ClearSnapshots()
    {
        _debuggerService?.ClearSnapshots();
        return NoContent();
    }

    [HttpGet("files")]
    public ActionResult<IReadOnlyList<DebuggerSourceFileItem>> GetFiles()
    {
        if (_debuggerService == null)
        {
            return new List<DebuggerSourceFileItem>();
        }

        return Ok(_debuggerService.GetKnownSourceFiles());
    }

    [HttpGet("source")]
    public ActionResult<DebuggerSourceCodeResponse> GetSource([FromQuery] string path)
    {
        if (_debuggerService == null)
        {
            return new DebuggerSourceCodeResponse { FilePath = path, Exists = false, Content = "// Debugger service not registered" };
        }

        return Ok(_debuggerService.GetSourceCode(path));
    }
}
