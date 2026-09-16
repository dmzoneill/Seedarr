using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NLog;
using NzbDrone.Core.Configuration;

namespace Seedarr.Http.Security;

public class CsrfProtectionMiddleware
{
    private static readonly string[] AmbientCookieNames = new[]
    {
        "Seedarr_Auth",
        "SeedarrAuth",
        "SID",
        ".AspNetCore.Cookies",
        "AuthToken",
        "_session_id",
        "deluge-session",
    };

    private static readonly string[] DefaultAuthBypassPaths = new[]
    {
        "/auth/login",
        "/auth/callback",
        "/auth/authenticate",
        "/api/v1/auth/login",
        "/api/v1/auth/callback",
        "/api/v2/auth/login",
        "/api/auth/authenticate",
    };

    private static readonly string[] DefaultRpcBypassPaths = new[]
    {
        "/json",
        "/gui",
        "/jsonrpc",
        "/api/v2",
        "/transmission/rpc",
        "/webapi",
        "/rpc",
        "/RPC2",
        "/RPC1",
        "/aria2",
        "/api/v1/arrconnections/webhook",
        "/api/v1/webhook",
    };

    private readonly RequestDelegate _next;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public CsrfProtectionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public static bool HasAmbientAuthCookie(IRequestCookieCollection cookies)
    {
        if (cookies == null)
        {
            return false;
        }

        foreach (var cookieName in AmbientCookieNames)
        {
            if (cookies.ContainsKey(cookieName))
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasVerifiedNonAmbientCredential(HttpContext context, IConfigFileProvider configFileProvider)
    {
        configFileProvider ??= context.RequestServices?.GetService(typeof(IConfigFileProvider)) as IConfigFileProvider;
        var masterApiKey = configFileProvider?.ApiKey;

        if (string.IsNullOrWhiteSpace(masterApiKey))
        {
            return false;
        }

        // 1. Check X-Api-Key header
        if (context.Request.Headers.TryGetValue("X-Api-Key", out var headerKey) && !string.IsNullOrWhiteSpace(headerKey))
        {
            if (RpcAuthenticationHelper.FixedTimeEquals(headerKey.ToString().Trim(), masterApiKey))
            {
                return true;
            }
        }

        // 2. Check ApiKey header
        if (context.Request.Headers.TryGetValue("ApiKey", out var customApiKey) && !string.IsNullOrWhiteSpace(customApiKey))
        {
            if (RpcAuthenticationHelper.FixedTimeEquals(customApiKey.ToString().Trim(), masterApiKey))
            {
                return true;
            }
        }

        // 3. Check Authorization header (Bearer or Basic)
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeaderVal) && !string.IsNullOrWhiteSpace(authHeaderVal))
        {
            var authHeader = authHeaderVal.ToString().Trim();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader["Bearer ".Length..].Trim();
                if (RpcAuthenticationHelper.FixedTimeEquals(token, masterApiKey))
                {
                    return true;
                }
            }
            else if (authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var creds = Encoding.UTF8.GetString(Convert.FromBase64String(authHeader["Basic ".Length..].Trim()));
                    var parts = creds.Split(':', 2);
                    var username = parts[0];
                    var password = parts.Length > 1 ? parts[1] : string.Empty;

                    if (RpcAuthenticationHelper.FixedTimeEquals(password, masterApiKey) ||
                        RpcAuthenticationHelper.FixedTimeEquals(username, masterApiKey))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Invalid base64, fall through
                }
            }
        }

        // 4. Validated query parameter: only if the key is actually valid (mere presence NEVER bypasses)
        if (context.Request.Query.TryGetValue("apikey", out var queryKey) && !string.IsNullOrWhiteSpace(queryKey))
        {
            if (RpcAuthenticationHelper.FixedTimeEquals(queryKey.ToString().Trim(), masterApiKey))
            {
                return true;
            }
        }

        if (context.Request.Query.TryGetValue("api_key", out var queryKey2) && !string.IsNullOrWhiteSpace(queryKey2))
        {
            if (RpcAuthenticationHelper.FixedTimeEquals(queryKey2.ToString().Trim(), masterApiKey))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsAuthPath(string path, string urlBase = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        // Normalize path by stripping urlBase if present
        if (!string.IsNullOrEmpty(urlBase) && path.StartsWith(urlBase, StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(urlBase.Length);
            if (!path.StartsWith('/'))
            {
                path = "/" + path;
            }
        }

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var bypassPath in DefaultAuthBypassPaths)
        {
            if (path.Equals(bypassPath, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(bypassPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsRpcPath(string path, string urlBase = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        // Normalize path by stripping urlBase if present
        if (!string.IsNullOrEmpty(urlBase) && path.StartsWith(urlBase, StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(urlBase.Length);
            if (!path.StartsWith('/'))
            {
                path = "/" + path;
            }
        }

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var bypassPath in DefaultRpcBypassPaths)
        {
            if (path.Equals(bypassPath, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(bypassPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public async Task InvokeAsync(HttpContext context, IConfigService configService, IConfigFileProvider configFileProvider = null)
    {
        if (configService != null && configService.CsrfProtectionEnabled)
        {
            var method = context.Request.Method;

            // Safe methods don't mutate state
            if (!HttpMethods.IsGet(method) &&
                !HttpMethods.IsHead(method) &&
                !HttpMethods.IsOptions(method) &&
                !HttpMethods.IsTrace(method))
            {
                var path = context.Request.Path.Value ?? string.Empty;
                var hasAuthCookie = HasAmbientAuthCookie(context.Request.Cookies);
                var isAuthPath = IsAuthPath(path, context.Request.PathBase.Value);
                var isRpcPath = IsRpcPath(path, context.Request.PathBase.Value);
                var hasVerifiedCredential = HasVerifiedNonAmbientCredential(context, configFileProvider);

                // CSRF bypass rules:
                // 1. Non-ambient credential verified (e.g. valid API key or bearer token)
                // 2. Authentication paths (e.g. login endpoints)
                // 3. RPC paths ONLY IF ambient session cookies are NOT present
                var shouldBypass = hasVerifiedCredential ||
                    isAuthPath ||
                    (isRpcPath && !hasAuthCookie);

                if (!shouldBypass)
                {
                    // 1. Check Sec-Fetch-Site (Modern browser defense)
                    if (context.Request.Headers.TryGetValue("Sec-Fetch-Site", out var secFetchSite) &&
                        string.Equals(secFetchSite.ToString(), "cross-site", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Warn("CSRF blocked: cross-site Sec-Fetch-Site on {0} {1}", method, context.Request.Path);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync("CSRF check failed: cross-site request blocked.");
                        return;
                    }

                    // 2. Determine effective host and scheme (handling reverse proxies)
                    var effectiveScheme = context.Request.Scheme;
                    if (context.Request.Headers.TryGetValue("X-Forwarded-Proto", out var fwdProto) && !string.IsNullOrWhiteSpace(fwdProto))
                    {
                        effectiveScheme = fwdProto.ToString().Split(',')[0].Trim();
                    }

                    var effectiveHost = context.Request.Host;
                    if (context.Request.Headers.TryGetValue("X-Forwarded-Host", out var fwdHost) && !string.IsNullOrWhiteSpace(fwdHost))
                    {
                        var rawFwdHost = fwdHost.ToString().Split(',')[0].Trim();
                        effectiveHost = HostString.FromUriComponent(rawFwdHost);
                    }

                    if (context.Request.Headers.TryGetValue("X-Forwarded-Port", out var fwdPort) &&
                        int.TryParse(fwdPort.ToString().Split(',')[0].Trim(), out var parsedPort))
                    {
                        effectiveHost = new HostString(effectiveHost.Host, parsedPort);
                    }

                    // 3. Check Origin and Referer headers (for browser requests)
                    var hasOrigin = context.Request.Headers.TryGetValue("Origin", out var originHeader) &&
                        !string.IsNullOrWhiteSpace(originHeader);
                    var hasReferer = context.Request.Headers.TryGetValue("Referer", out var refererHeader) &&
                        !string.IsNullOrWhiteSpace(refererHeader);

                    if (!hasOrigin && !hasReferer)
                    {
                        if (hasAuthCookie)
                        {
                            _logger.Warn("CSRF blocked: missing both Origin and Referer headers on authenticated session {0} {1}", method, context.Request.Path);
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.ContentType = "text/plain";
                            await context.Response.WriteAsync("CSRF check failed: missing Origin and Referer.");
                            return;
                        }
                    }

                    if (hasOrigin)
                    {
                        if (!IsOriginAllowed(originHeader.ToString(), effectiveHost, effectiveScheme))
                        {
                            _logger.Warn("CSRF blocked: invalid Origin '{0}' on {1} {2}", originHeader, method, context.Request.Path);
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.ContentType = "text/plain";
                            await context.Response.WriteAsync("CSRF check failed: invalid Origin.");
                            return;
                        }
                    }
                    else if (hasReferer)
                    {
                        if (!IsOriginAllowed(refererHeader.ToString(), effectiveHost, effectiveScheme))
                        {
                            _logger.Warn("CSRF blocked: invalid Referer '{0}' on {1} {2}", refererHeader, method, context.Request.Path);
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.ContentType = "text/plain";
                            await context.Response.WriteAsync("CSRF check failed: invalid Referer.");
                            return;
                        }
                    }
                }
            }
        }

        await _next(context);
    }

    public static bool IsOriginAllowed(string originOrReferer, HostString requestHost)
    {
        return IsOriginAllowed(originOrReferer, requestHost, null);
    }

    public static bool IsOriginAllowed(string originOrReferer, HostString requestHost, string requestScheme)
    {
        if (!Uri.TryCreate(originOrReferer, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // Scheme check: If request is HTTPS, an HTTP origin should not be allowed
        if (!string.IsNullOrEmpty(requestScheme) &&
            string.Equals(requestScheme, "https", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var effectiveRequestScheme = !string.IsNullOrEmpty(requestScheme) ? requestScheme : uri.Scheme;
        var isHttps = string.Equals(effectiveRequestScheme, "https", StringComparison.OrdinalIgnoreCase);

        var effectiveRequestPort = requestHost.Port ?? (isHttps ? 443 : 80);

        // Reverse-proxy normalization: if the request scheme is HTTPS but requestHost.Port was HTTP default 80
        // (e.g. reverse proxy terminated TLS and forwarded over internal HTTP 80 without rewriting port),
        // treat effective request port as HTTPS default 443.
        if (isHttps && effectiveRequestPort == 80)
        {
            effectiveRequestPort = 443;
        }

        var effectiveOriginPort = uri.Port > 0 ? uri.Port : (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);

        // Check if both ports are standard default ports for HTTP (80) or HTTPS (443)
        var isOriginStandardPort = (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) && effectiveOriginPort == 443) ||
                                   (string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) && effectiveOriginPort == 80);
        var isRequestStandardPort = (isHttps && effectiveRequestPort == 443) ||
                                    (!isHttps && effectiveRequestPort == 80);

        if (effectiveOriginPort != effectiveRequestPort)
        {
            if (!(isOriginStandardPort && isRequestStandardPort && string.Equals(uri.Scheme, effectiveRequestScheme, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        // If origin host matches request host
        if (string.Equals(uri.Host, requestHost.Host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Loopback / localhost match
        var isOriginLoopback = IsLoopbackHost(uri.Host);
        var isRequestLoopback = IsLoopbackHost(requestHost.Host);

        if (isOriginLoopback && isRequestLoopback)
        {
            return true;
        }

        return false;
    }

    private static bool IsLoopbackHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("[::1]", StringComparison.OrdinalIgnoreCase);
    }
}
