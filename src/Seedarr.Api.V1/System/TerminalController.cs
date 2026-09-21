#pragma warning disable SA1117
#pragma warning disable IDE0007

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NLog;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Terminal;
using Seedarr.Http;

namespace Seedarr.Api.V1.System;

[V1ApiController("system/terminal")]
[Authorize(Policy = Policies.AdminOnly)]
public class TerminalController : ControllerBase
{
    public const int MinCols = TerminalDimensionValidator.MinCols;
    public const int MaxCols = TerminalDimensionValidator.MaxCols;
    public const int MinRows = TerminalDimensionValidator.MinRows;
    public const int MaxRows = TerminalDimensionValidator.MaxRows;

    private readonly ITerminalService _terminalService;
    private readonly IConfigFileProvider _configFileProvider;
    private readonly Logger _logger;

    public TerminalController(ITerminalService terminalService = null, IConfigFileProvider configFileProvider = null)
    {
        _terminalService = terminalService;
        _configFileProvider = configFileProvider;
        _logger = LogManager.GetCurrentClassLogger();
    }

    [HttpGet("status")]
    [HttpGet]
    [ProducesResponseType(typeof(TerminalStatusResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetStatus()
    {
        var accessCheck = CheckAccess();
        if (accessCheck != null)
        {
            return accessCheck;
        }

        return Ok(new TerminalStatusResource
        {
            TerminalAccessEnabled = _configFileProvider?.TerminalAccessEnabled ?? true,
            ActiveSessions = _terminalService?.ActiveSessionIds ?? Array.Empty<string>()
        });
    }

    [HttpPost("session")]
    [HttpPost]
    [ProducesResponseType(typeof(TerminalSessionResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> StartSession(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] TerminalSessionRequest request = null,
        [FromQuery] int? cols = null,
        [FromQuery] int? rows = null,
        [FromQuery] string connectionId = null)
    {
        var accessCheck = CheckAccess();
        if (accessCheck != null)
        {
            return accessCheck;
        }

        var effectiveCols = cols ?? request?.Cols ?? 80;
        var effectiveRows = rows ?? request?.Rows ?? 24;
        var effectiveConnectionId = !string.IsNullOrWhiteSpace(connectionId)
            ? connectionId
            : (!string.IsNullOrWhiteSpace(request?.ConnectionId) ? request.ConnectionId : Guid.NewGuid().ToString("N"));

        if (!TerminalDimensionValidator.Validate(effectiveCols, effectiveRows, out var dimensionError))
        {
            return BadRequest(dimensionError);
        }

        var clientIp = GetClientIp();
        var user = GetUserIdentity();

        _logger.Info("Terminal session initiation: connectionId '{0}', user '{1}', clientIp '{2}', dimensions {3}x{4}",
            effectiveConnectionId, user, clientIp, effectiveCols, effectiveRows);

        if (_terminalService != null)
        {
            await _terminalService.StartSessionAsync(
                effectiveConnectionId,
                effectiveCols,
                effectiveRows,
                _ => Task.CompletedTask,
                null,
                HttpContext?.RequestAborted ?? default);
        }

        return Ok(new TerminalSessionResource
        {
            ConnectionId = effectiveConnectionId,
            Cols = effectiveCols,
            Rows = effectiveRows,
            CreatedAt = DateTime.UtcNow
        });
    }

    [HttpPost("resize")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult Resize(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] TerminalResizeRequest request = null,
        [FromQuery] int? cols = null,
        [FromQuery] int? rows = null,
        [FromQuery] string connectionId = null)
    {
        var accessCheck = CheckAccess();
        if (accessCheck != null)
        {
            return accessCheck;
        }

        var effectiveCols = cols ?? request?.Cols ?? 80;
        var effectiveRows = rows ?? request?.Rows ?? 24;
        var effectiveConnectionId = !string.IsNullOrWhiteSpace(connectionId)
            ? connectionId
            : request?.ConnectionId;

        if (!TerminalDimensionValidator.Validate(effectiveCols, effectiveRows, out var dimensionError))
        {
            return BadRequest(dimensionError);
        }

        if (string.IsNullOrWhiteSpace(effectiveConnectionId))
        {
            return BadRequest("Connection ID is required.");
        }

        _terminalService?.Resize(effectiveConnectionId, effectiveCols, effectiveRows);

        return Ok(new { connectionId = effectiveConnectionId, cols = effectiveCols, rows = effectiveRows });
    }

    [HttpDelete("session/{connectionId}")]
    [HttpDelete("{connectionId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CloseSession(string connectionId)
    {
        var accessCheck = CheckAccess();
        if (accessCheck != null)
        {
            return accessCheck;
        }

        if (string.IsNullOrWhiteSpace(connectionId))
        {
            return BadRequest("Connection ID is required.");
        }

        if (_terminalService != null)
        {
            await _terminalService.CloseSessionAsync(connectionId);
        }

        return NoContent();
    }

    private ActionResult CheckAccess()
    {
        if (_configFileProvider != null && !_configFileProvider.TerminalAccessEnabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Terminal access is disabled in system configuration.");
        }

        if (User?.Identity?.IsAuthenticated == true && !User.IsInRole(Roles.Admin))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Admin role is required to access terminal.");
        }

        return null;
    }

    private string GetClientIp()
    {
        var remoteIp = HttpContext?.Connection?.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            return remoteIp;
        }

        if (HttpContext?.Request?.Headers != null &&
            HttpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) &&
            !string.IsNullOrWhiteSpace(forwardedFor))
        {
            return forwardedFor.ToString().Split(',')[0].Trim();
        }

        return "unknown";
    }

    private string GetUserIdentity()
    {
        if (User?.Identity?.Name != null && !string.IsNullOrWhiteSpace(User.Identity.Name))
        {
            return User.Identity.Name;
        }

        var nameClaim = User?.FindFirst(ClaimTypes.Name)?.Value ??
                        User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrWhiteSpace(nameClaim))
        {
            return nameClaim;
        }

        return "anonymous";
    }
}
