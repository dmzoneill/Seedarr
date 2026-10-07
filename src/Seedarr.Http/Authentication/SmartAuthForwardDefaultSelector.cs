// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using Seedarr.Http.Security;

namespace Seedarr.Http.Authentication;

/// <summary>
/// Chooses the concrete authentication scheme for SmartAuth policy routing.
/// </summary>
public static class SmartAuthForwardDefaultSelector
{
    public const string SessionCookieName = "Seedarr_Auth";
    public const string CookieScheme = "Cookies";

    public static string SelectScheme(HttpContext httpContext)
    {
        var req = httpContext.Request;
        var configFileProvider = httpContext.RequestServices.GetService<IConfigFileProvider>();

        // 0. When authentication is disabled, automatically grant local access
        if (configFileProvider != null && !configFileProvider.AuthenticationEnabled)
        {
            return ApiKeyAuthenticationOptions.DefaultScheme;
        }

        // 1. API Key present in header, query parameter, or Bearer token
        var hasApiKeyHeader = (req.Headers.TryGetValue("X-Api-Key", out var headerKey) && !string.IsNullOrWhiteSpace(headerKey)) ||
            (req.Headers.TryGetValue("ApiKey", out var headerKey2) && !string.IsNullOrWhiteSpace(headerKey2));
        var hasApiKeyQuery = (req.Query.TryGetValue("apikey", out var qKey) && !string.IsNullOrWhiteSpace(qKey)) ||
            (req.Query.TryGetValue("access_token", out var qToken) && !string.IsNullOrWhiteSpace(qToken)) ||
            (req.Query.TryGetValue("api_key", out var qApiKey) && !string.IsNullOrWhiteSpace(qApiKey)) ||
            (req.Query.TryGetValue("token", out var qToken2) && !string.IsNullOrWhiteSpace(qToken2));
        var hasBearerToken = req.Headers.TryGetValue("Authorization", out var authHeader) &&
            authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(authHeader.ToString()["Bearer ".Length..].Trim());

        if (hasApiKeyHeader || hasApiKeyQuery || hasBearerToken)
        {
            return ApiKeyAuthenticationOptions.DefaultScheme;
        }

        // 2. HTTP Basic Auth header
        if (req.Headers.TryGetValue("Authorization", out var basicHeader) &&
            basicHeader.ToString().StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(basicHeader.ToString()["Basic ".Length..].Trim()))
        {
            return BasicAuthenticationOptions.DefaultScheme;
        }

        // 3. Cookie for interactive browser session or login flow (before ForwardAuth so proxy headers do not override)
        if (req.Cookies.ContainsKey(SessionCookieName) ||
            req.Path.StartsWithSegments("/login") ||
            req.Path.StartsWithSegments("/auth"))
        {
            return CookieScheme;
        }

        // 4. Forward-Auth reverse proxy headers
        var hasForwardAuthHeaders = (req.Headers.TryGetValue("Remote-User", out var rUser) && !string.IsNullOrWhiteSpace(rUser)) ||
            (req.Headers.TryGetValue("X-authentik-username", out var aUser) && !string.IsNullOrWhiteSpace(aUser)) ||
            (req.Headers.TryGetValue("X-Forwarded-User", out var fUser) && !string.IsNullOrWhiteSpace(fUser));

        if (hasForwardAuthHeaders)
        {
            var remoteIp = req.HttpContext.Connection.RemoteIpAddress;
            var configTrustedProxies = configFileProvider?.TrustedProxies;
            var idpRepo = httpContext.RequestServices.GetService<IIdentityProviderRepository>();
            var forwardAuthIdp = idpRepo?.GetEnabled()?.FirstOrDefault(p => p.ProviderType == IdentityProviderType.ForwardAuth);
            var isForwardAuthEnabled = forwardAuthIdp != null && forwardAuthIdp.IsEnabled;

            if (isForwardAuthEnabled && remoteIp != null)
            {
                var idpProxies = forwardAuthIdp?.TrustedProxies;
                var combinedProxies = !string.IsNullOrWhiteSpace(idpProxies)
                    ? (!string.IsNullOrWhiteSpace(configTrustedProxies) ? $"{configTrustedProxies},{idpProxies}" : idpProxies)
                    : configTrustedProxies;

                if (IpSecurityHelper.IsTrustedProxy(remoteIp, combinedProxies))
                {
                    return ForwardAuthOptions.DefaultScheme;
                }
            }
        }

        // 5. Default to ApiKey handler
        return ApiKeyAuthenticationOptions.DefaultScheme;
    }
}
