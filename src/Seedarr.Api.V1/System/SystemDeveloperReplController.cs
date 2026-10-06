// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Developer.Repl;
using Seedarr.Http;

namespace Seedarr.Api.V1.System;

[V1ApiController("system/developer/repl")]
[Authorize(Policy = Policies.AdminOnly)]
public class SystemDeveloperReplController : Controller
{
    private readonly IDeveloperReplService _replService;

    public SystemDeveloperReplController(IDeveloperReplService replService = null)
    {
        _replService = replService;
    }

    [HttpPost("eval")]
    public ActionResult<ReplExecutionResponse> Evaluate([FromBody] ReplExecutionRequest request)
    {
        if (_replService == null)
        {
            return new ReplExecutionResponse
            {
                Success = false,
                ErrorMessage = "Developer REPL service is not registered.",
            };
        }

        var response = _replService.Execute(request ?? new ReplExecutionRequest());
        return Ok(response);
    }

    [HttpGet("history")]
    public ActionResult<IReadOnlyList<ReplHistoryEntry>> GetHistory([FromQuery] int limit = 50)
    {
        if (_replService == null)
        {
            return new List<ReplHistoryEntry>();
        }

        return Ok(_replService.GetHistory(limit));
    }

    [HttpDelete("history")]
    public ActionResult ClearHistory()
    {
        _replService?.ClearHistory();
        return NoContent();
    }

    [HttpDelete("session")]
    public ActionResult ResetSession()
    {
        _replService?.ResetSession();
        return NoContent();
    }
}
