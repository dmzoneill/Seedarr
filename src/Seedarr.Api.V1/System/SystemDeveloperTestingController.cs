// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Developer.Testing;
using Seedarr.Http;

namespace Seedarr.Api.V1.System;

[V1ApiController("system/developer/testing")]
public class SystemDeveloperTestingController : Controller
{
    private readonly IDeveloperTestRunner _testRunner;

    public SystemDeveloperTestingController(IDeveloperTestRunner testRunner = null)
    {
        _testRunner = testRunner;
    }

    [HttpGet("tests")]
    public ActionResult<IReadOnlyList<DeveloperTestItem>> GetTests()
    {
        if (_testRunner == null)
        {
            return new List<DeveloperTestItem>();
        }

        return Ok(_testRunner.DiscoverTests());
    }

    [HttpPost("run")]
    public async Task<ActionResult<TestExecutionResponse>> RunTests([FromBody] TestExecutionRequest request)
    {
        if (_testRunner == null)
        {
            return new TestExecutionResponse();
        }

        var response = await _testRunner.RunTestsAsync(request ?? new TestExecutionRequest { RunAll = true });
        return Ok(response);
    }

    [HttpPost("run/{testId}")]
    public async Task<ActionResult> RunSingleTest(string testId)
    {
        if (_testRunner == null)
        {
            var skippedJson = global::System.Text.Json.JsonSerializer.Serialize(new DeveloperTestResult { TestId = testId, Status = "Skipped" });
            return Content(skippedJson, "application/json");
        }

        var result = await _testRunner.RunTestAsync(testId);
        var json = global::System.Text.Json.JsonSerializer.Serialize(result);
        return Content(json, "application/json");
    }

    [HttpGet("history")]
    public ActionResult<IReadOnlyList<DeveloperTestResult>> GetHistory([FromQuery] int limit = 50)
    {
        if (_testRunner == null)
        {
            return new List<DeveloperTestResult>();
        }

        return Ok(_testRunner.GetRecentResults(limit));
    }

    [HttpDelete("history")]
    public ActionResult ClearHistory()
    {
        _testRunner?.ClearHistory();
        return NoContent();
    }
}
