// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using Seedarr.Http;

namespace Seedarr.Api.V1.Auth;

[V1ApiController("auth")]
public class AuthController : ControllerBase
{
    private readonly IIdentityProviderService _identityProviderService;
    private readonly IConfigFileProvider _configFileProvider;
    private readonly ISessionRevocationService _sessionRevocationService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public AuthController(
        IIdentityProviderService identityProviderService,
        IConfigFileProvider configFileProvider,
        ISessionRevocationService sessionRevocationService = null)
    {
        _identityProviderService = identityProviderService;
        _configFileProvider = configFileProvider;
        _sessionRevocationService = sessionRevocationService;
    }

    [HttpGet("providers")]
    [AllowAnonymous]
    public ActionResult<List<AuthProviderResource>> GetProviders([FromQuery] string returnUrl = null)
    {
        var providers = new List<AuthProviderResource>();
        var basePath = GetEffectivePathBase();
        string safeReturnUrl = null;
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            safeReturnUrl = SanitizeRedirectUrl(returnUrl, basePath);
        }

        var enabledProviders = _identityProviderService.GetEnabled();
        foreach (var p in enabledProviders)
        {
            var loginUrl = $"{basePath}/api/v1/auth/login/{p.ProviderId}";
            if (!string.IsNullOrWhiteSpace(safeReturnUrl))
            {
                loginUrl += $"?returnUrl={Uri.EscapeDataString(safeReturnUrl)}";
            }

            providers.Add(new AuthProviderResource
            {
                Id = p.Id,
                ProviderId = p.ProviderId,
                Name = p.Name,
                ProviderType = p.ProviderType,
                IconUrl = p.IconUrl,
                ButtonText = p.ButtonText ?? $"Sign in with {p.Name}",
                LoginUrl = loginUrl,
            });
        }

        return Ok(providers);
    }

    [HttpPost("login")]
    [Consumes("application/json")]
    [AllowAnonymous]
    public async Task<ActionResult<CurrentUserResource>> Login([FromBody] LoginRequestResource request, [FromQuery] string returnUrl = null)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Password or API key is required" });
        }

        var masterApiKey = _configFileProvider.ApiKey;
        var enteredUser = request.Username?.Trim();
        var enteredPass = request.Password;

        var passwordMatches = !string.IsNullOrWhiteSpace(masterApiKey) && FixedTimeEquals(enteredPass, masterApiKey);
        var isValid = !_configFileProvider.AuthenticationEnabled || passwordMatches;

        if (!isValid)
        {
            await Task.Delay(300);
            return Unauthorized(new { error = "Invalid credentials. Please verify your username and password or API key." });
        }

        var usernameMatchesApiKey = !string.IsNullOrWhiteSpace(masterApiKey) && FixedTimeEquals(enteredUser, masterApiKey);
        var username = string.IsNullOrWhiteSpace(enteredUser) || usernameMatchesApiKey
            ? "admin"
            : enteredUser;
        var sessionId = Guid.NewGuid().ToString("N");
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "1"),
            new(ClaimTypes.Name, username),
            new("DisplayName", username == "admin" ? "Administrator" : username),
            new(ClaimTypes.Role, "Admin"),
            new("SessionId", sessionId),
        };

        var identity = new ClaimsIdentity(claims, "Cookies");
        var principal = new ClaimsPrincipal(identity);

        var now = DateTimeOffset.UtcNow;
        var authProps = new AuthenticationProperties
        {
            IsPersistent = request.RememberMe,
            IssuedUtc = now,
            ExpiresUtc = request.RememberMe ? now.AddDays(30) : now.AddHours(8),
        };

        await HttpContext.SignInAsync("Cookies", principal, authProps);

        var requestedUrl = returnUrl ?? request.ReturnUrl;
        var safeReturnUrl = SanitizeRedirectUrl(requestedUrl, GetEffectivePathBase());

        return Ok(new CurrentUserResource
        {
            Id = 1,
            Identifier = username,
            Username = username,
            DisplayName = username == "admin" ? "Administrator" : username,
            Roles = new List<string> { "Admin" },
            IsAuthenticated = true,
            ReturnUrl = safeReturnUrl,
        });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<ActionResult> Logout()
    {
        var user = User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            var authResult = await HttpContext.AuthenticateAsync("Cookies");
            if (authResult?.Succeeded == true && authResult.Principal != null)
            {
                user = authResult.Principal;
            }
        }

        var sessionId = user?.FindFirst("SessionId")?.Value;
        var username = user?.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            _sessionRevocationService?.RevokeSession(sessionId);
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            _sessionRevocationService?.RevokeSession(username);
        }

        await HttpContext.SignOutAsync("Cookies");
        return Ok(new { message = "Logged out successfully" });
    }

    [HttpGet("me")]
    [AllowAnonymous]
    public ActionResult<CurrentUserResource> GetCurrentUser()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            if (!_configFileProvider.AuthenticationEnabled)
            {
                return Ok(new CurrentUserResource
                {
                    Username = "admin",
                    DisplayName = "Administrator",
                    Roles = new List<string> { "Admin" },
                    IsAuthenticated = true,
                });
            }

            return Ok(new CurrentUserResource
            {
                IsAuthenticated = false,
            });
        }

        var username = User.Identity?.Name ?? "User";
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        var displayName = User.FindFirst("DisplayName")?.Value ?? username;
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        if (roles.Count == 0)
        {
            roles.Add("Admin");
        }

        return Ok(new CurrentUserResource
        {
            Username = username,
            Email = email,
            DisplayName = displayName,
            Roles = roles,
            IsAuthenticated = true,
        });
    }

    [HttpGet("login/{providerId}")]
    [AllowAnonymous]
    public ActionResult ChallengeProvider(string providerId, [FromQuery] string returnUrl = "/")
    {
        var safeReturnUrl = SanitizeRedirectUrl(returnUrl, GetEffectivePathBase());
        var schemeName = $"Oidc_{providerId}";
        var props = new AuthenticationProperties
        {
            RedirectUri = safeReturnUrl,
        };

        return Challenge(props, schemeName);
    }

    public static bool IsLocalUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        // Prohibit any backslash characters completely
        if (url.Contains('\\') || url.Contains("%5c", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Must start with single slash, not double slash or /@
        if (!url.StartsWith('/') || url.StartsWith("//", StringComparison.Ordinal) || url.StartsWith("/@", StringComparison.Ordinal))
        {
            return false;
        }

        // Disallow control characters
        for (var i = 0; i < url.Length; i++)
        {
            if (char.IsControl(url[i]))
            {
                return false;
            }
        }

        return Uri.TryCreate(url, UriKind.Relative, out var uri) && !uri.OriginalString.StartsWith("//", StringComparison.Ordinal);
    }

    public static string NormalizePathBase(string pathBase)
    {
        if (string.IsNullOrWhiteSpace(pathBase))
        {
            return string.Empty;
        }

        var trimmed = pathBase.Trim().TrimEnd('/');
        if (!trimmed.StartsWith('/'))
        {
            trimmed = "/" + trimmed;
        }

        return trimmed == "/" ? string.Empty : trimmed;
    }

    public static string SanitizeRedirectUrl(string url, string pathBase = null)
    {
        var cleanBase = NormalizePathBase(pathBase);

        if (!IsLocalUrl(url))
        {
            return string.IsNullOrEmpty(cleanBase) ? "/" : cleanBase + "/";
        }

        if (string.IsNullOrEmpty(cleanBase))
        {
            return url;
        }

        if (url.Equals(cleanBase, StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith(cleanBase + "/", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (url == "/")
        {
            return cleanBase + "/";
        }

        return cleanBase + url;
    }

    private string GetEffectivePathBase()
    {
        if (Request?.PathBase.HasValue == true && !string.IsNullOrWhiteSpace(Request.PathBase.Value))
        {
            return NormalizePathBase(Request.PathBase.Value);
        }

        if (_configFileProvider != null && !string.IsNullOrWhiteSpace(_configFileProvider.UrlBase))
        {
            return NormalizePathBase(_configFileProvider.UrlBase);
        }

        return string.Empty;
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        Span<byte> hashA = stackalloc byte[32];
        Span<byte> hashB = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(a), hashA);
        SHA256.HashData(Encoding.UTF8.GetBytes(b), hashB);
        return CryptographicOperations.FixedTimeEquals(hashA, hashB);
    }
}
