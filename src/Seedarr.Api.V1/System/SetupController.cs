using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Configuration;
using Seedarr.Http;

namespace Seedarr.Api.V1.System;

[V1ApiController("system/setup")]
public class SetupController : Controller
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly IConfigService _configService;
    private readonly IConfigFileProvider _configFileProvider;

    public SetupController(IConfigService configService, IConfigFileProvider configFileProvider = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _configFileProvider = configFileProvider;
    }

    [HttpGet("status")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SetupStatusResource), StatusCodes.Status200OK)]
    public ActionResult<SetupStatusResource> GetStatus()
    {
        try
        {
            var isSetupCompleted = _configService.IsSetupCompleted;
            var isAuthEnabled = _configService.AuthenticationEnabled;
            var hasAdminUser = isSetupCompleted ||
                !string.IsNullOrWhiteSpace(_configService.GetValue("AdminUsername", string.Empty)) ||
                !string.IsNullOrWhiteSpace(_configService.GetValue("AdminPassword", string.Empty));

            return Ok(new SetupStatusResource
            {
                IsSetupCompleted = isSetupCompleted,
                IsAuthEnabled = isAuthEnabled,
                HasAdminUser = hasAdminUser,
            });
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to retrieve setup status; returning default status");
            return Ok(new SetupStatusResource
            {
                IsSetupCompleted = false,
                IsAuthEnabled = false,
                HasAdminUser = false,
            });
        }
    }

    [HttpPost("complete")]
    [AllowAnonymous]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult Complete([FromBody] SetupCompleteRequest request)
    {
        if (_configService.IsSetupCompleted)
        {
            if (User?.Identity?.IsAuthenticated != true)
            {
                return Unauthorized(new { error = "Setup has already been completed. Authentication is required to reconfigure setup." });
            }
        }

        var (isValid, errorMessage) = ValidatePassword(request?.Password);
        if (!isValid)
        {
            return BadRequest(new { error = errorMessage });
        }

        var adminUsername = string.IsNullOrWhiteSpace(request?.Username) ? "admin" : request.Username.Trim();

        _configService.SaveConfigDictionary(new Dictionary<string, object>
        {
            { "AdminUsername", adminUsername },
            { "AdminPassword", request.Password },
            { "AuthenticationEnabled", true },
            { "IsSetupCompleted", true },
        });

        _configService.AuthenticationEnabled = true;
        _configService.IsSetupCompleted = true;

        if (_configFileProvider != null)
        {
            _configFileProvider.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "AuthenticationEnabled", true },
                { "IsSetupCompleted", true },
            });
            _configFileProvider.SetIsSetupCompleted(true);
        }

        return Ok(new
        {
            message = "Setup completed successfully.",
            isSetupCompleted = true,
            isAuthEnabled = true,
        });
    }

    public static (bool IsValid, string ErrorMessage) ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 10)
        {
            return (false, "Password must be at least 10 characters long.");
        }

        if (!password.Any(char.IsUpper))
        {
            return (false, "Password must contain at least one uppercase letter.");
        }

        if (!password.Any(char.IsLower))
        {
            return (false, "Password must contain at least one lowercase letter.");
        }

        if (!password.Any(char.IsDigit) && !password.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            return (false, "Password must contain at least one digit or special character.");
        }

        return (true, null);
    }
}
