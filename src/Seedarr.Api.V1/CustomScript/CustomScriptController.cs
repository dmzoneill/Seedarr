using System;
using System.Collections.Generic;
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
    private static readonly string[] AllowedScriptDirectories = OperatingSystem.IsWindows()
        ? new[] { @"C:\Program Files\Seedarr\Scripts", @"C:\ProgramData\Seedarr\Scripts", @"C:\scripts" }
        : new[] { "/usr/local/bin", "/usr/bin", "/opt/seedarr/scripts", "/var/lib/seedarr/scripts", "/etc/seedarr/scripts", "/scripts" };

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
                Stderr = $"Custom script path '{fullPath}' is not permitted. Scripts must be located within authorized directories ({string.Join(", ", AllowedScriptDirectories)}).",
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

    public static bool IsInAllowedDirectory(string fullPath, string customScriptsDir = null)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        var allowed = new List<string>(AllowedScriptDirectories);
        if (!string.IsNullOrWhiteSpace(customScriptsDir))
        {
            allowed.Add(customScriptsDir);
        }

        foreach (var dir in allowed)
        {
            var fullDir = Path.GetFullPath(dir);
            if (!fullDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                fullDir += Path.DirectorySeparatorChar;
            }

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (fullPath.StartsWith(fullDir, comparison) || string.Equals(fullPath, Path.GetFullPath(dir), comparison))
            {
                return true;
            }
        }

        return false;
    }
}
