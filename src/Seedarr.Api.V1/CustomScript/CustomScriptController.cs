using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Notifications;
using Seedarr.Http;

namespace Seedarr.Api.V1.CustomScript;

/// <summary>
/// Controller for testing and diagnosing custom scripts.
/// </summary>
[V1ApiController("customscript")]
public class CustomScriptController : Controller
{
    private readonly ICustomScriptService _customScriptService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomScriptController"/> class.
    /// </summary>
    /// <param name="customScriptService">Custom script service.</param>
    public CustomScriptController(ICustomScriptService customScriptService)
    {
        _customScriptService = customScriptService;
    }

    /// <summary>
    /// Tests execution of a custom script with diagnostic capture.
    /// </summary>
    /// <param name="request">Test execution parameters.</param>
    /// <returns>Execution diagnostics including stdout, stderr, and exit code.</returns>
    [HttpPost("test")]
    public async Task<ActionResult<CustomScriptTestResult>> TestScript([FromBody] CustomScriptTestRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ScriptPath))
        {
            return Ok(new CustomScriptTestResult
            {
                Success = false,
                ExitCode = -1,
                Stderr = "Script path is required.",
                Stdout = string.Empty,
                ExecutionTimeMs = 0,
                TimedOut = false,
                ResolvedInterpreter = string.Empty,
                WorkingDirectory = string.Empty,
            });
        }

        var result = await _customScriptService.TestScriptAsync(
            request.ScriptPath,
            request.Arguments,
            string.IsNullOrWhiteSpace(request.EventType) ? "Test" : request.EventType);

        return Ok(result);
    }
}
