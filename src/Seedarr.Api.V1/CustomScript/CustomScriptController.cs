using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Notifications;
using Seedarr.Http;

namespace Seedarr.Api.V1.CustomScript;

/// <summary>
/// Controller for testing and diagnosing custom scripts.
/// </summary>
[V1ApiController("customscript")]
[Authorize(Policy = Policies.AdminOnly)]
public class CustomScriptController : Controller
{
    private readonly ICustomScriptService _customScriptService;
    private readonly IConfigService _configService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomScriptController"/> class.
    /// </summary>
    /// <param name="customScriptService">Custom script service.</param>
    /// <param name="configService">Configuration service.</param>
    public CustomScriptController(ICustomScriptService customScriptService, IConfigService configService = null)
    {
        _customScriptService = customScriptService;
        _configService = configService;
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

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(request.ScriptPath.Trim());
        }
        catch (Exception ex)
        {
            return Ok(new CustomScriptTestResult
            {
                Success = false,
                ExitCode = -1,
                Stderr = $"Invalid script path: {ex.Message}",
                Stdout = string.Empty,
                ExecutionTimeMs = 0,
                TimedOut = false,
                ResolvedInterpreter = string.Empty,
                WorkingDirectory = string.Empty,
            });
        }

        if (!IsInAllowedDirectory(fullPath, _configService?.CustomScriptsDirectory))
        {
            return Ok(new CustomScriptTestResult
            {
                Success = false,
                ExitCode = -1,
                Stderr = $"Custom script path '{fullPath}' is not permitted. Scripts must be located within authorized directories ({CustomScriptPathPolicy.AllowedDirectoriesDescription}).",
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

    public static bool IsInAllowedDirectory(string fullPath, string customScriptsDir = null) =>
        CustomScriptPathPolicy.IsInAllowedDirectory(fullPath, customScriptsDir);
}
