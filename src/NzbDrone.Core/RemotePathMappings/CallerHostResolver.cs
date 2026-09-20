using System;
using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace NzbDrone.Core.RemotePathMappings;

public class CallerHostResolver : ICallerHostResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, (string Hostname, DateTime Expiry)> _ipToHostCache = new(StringComparer.OrdinalIgnoreCase);

    public string ResolveHost(HttpContext httpContext)
    {
        if (httpContext == null)
        {
            return "localhost";
        }

        string xForwardedFor = null;
        string xRealIp = null;
        string xForwardedHost = null;
        string hostHeader = null;

        if (httpContext.Request?.Headers != null)
        {
            if (httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var fwdForVal))
            {
                xForwardedFor = fwdForVal.ToString();
            }

            if (httpContext.Request.Headers.TryGetValue("X-Real-IP", out var realIpVal))
            {
                xRealIp = realIpVal.ToString();
            }

            if (httpContext.Request.Headers.TryGetValue("X-Forwarded-Host", out var fwdHostVal))
            {
                xForwardedHost = fwdHostVal.ToString();
            }

            if (httpContext.Request.Headers.TryGetValue("Host", out var hostVal))
            {
                hostHeader = hostVal.ToString();
            }
        }

        var remoteIp = httpContext.Connection?.RemoteIpAddress?.ToString();
        var resolved = ResolveFromHeaders(xForwardedFor, xRealIp, xForwardedHost, remoteIp);
        if ((string.IsNullOrWhiteSpace(resolved) || resolved == "localhost" || resolved == "127.0.0.1" || resolved == "::1") && !string.IsNullOrWhiteSpace(hostHeader))
        {
            var cleanHostHeader = RemotePathMappingService.ExtractIpOrHostname(hostHeader);
            if (!string.IsNullOrWhiteSpace(cleanHostHeader) && cleanHostHeader != "localhost" && cleanHostHeader != "127.0.0.1" && cleanHostHeader != "::1")
            {
                return cleanHostHeader;
            }
        }

        return resolved;
    }

    public string ResolveFromHeaders(string xForwardedFor, string xRealIp, string xForwardedHost, string remoteIp = null)
    {
        // 1. X-Forwarded-For (first entry or client IP)
        if (!string.IsNullOrWhiteSpace(xForwardedFor))
        {
            var firstEntry = xForwardedFor.Split(',')[0].Trim();
            var clientIp = RemotePathMappingService.ExtractIpOrHostname(firstEntry);
            if (!string.IsNullOrEmpty(clientIp))
            {
                return clientIp;
            }
        }

        // 2. X-Real-IP
        if (!string.IsNullOrWhiteSpace(xRealIp))
        {
            var clientIp = RemotePathMappingService.ExtractIpOrHostname(xRealIp.Trim());
            if (!string.IsNullOrEmpty(clientIp))
            {
                return clientIp;
            }
        }

        // 3. X-Forwarded-Host
        if (!string.IsNullOrWhiteSpace(xForwardedHost))
        {
            var clientHost = RemotePathMappingService.ExtractIpOrHostname(xForwardedHost.Trim());
            if (!string.IsNullOrEmpty(clientHost))
            {
                return clientHost;
            }
        }

        // 4. Direct socket IP from connection context
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            var clientIp = RemotePathMappingService.ExtractIpOrHostname(remoteIp.Trim());
            if (!string.IsNullOrEmpty(clientIp))
            {
                return clientIp;
            }
        }

        return "localhost";
    }

    public string TryResolveHostname(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return ipAddress;
        }

        var clean = RemotePathMappingService.ExtractIpOrHostname(ipAddress);
        if (string.IsNullOrEmpty(clean))
        {
            return ipAddress;
        }

        if (_ipToHostCache.TryGetValue(clean, out var entry) && entry.Expiry > DateTime.UtcNow)
        {
            return entry.Hostname;
        }

        if (IPAddress.TryParse(clean, out var ip))
        {
            try
            {
                var hostEntry = Dns.GetHostEntry(ip);
                if (!string.IsNullOrWhiteSpace(hostEntry.HostName))
                {
                    _ipToHostCache[clean] = (hostEntry.HostName, DateTime.UtcNow.Add(CacheTtl));
                    return hostEntry.HostName;
                }
            }
            catch
            {
                // Fallback and avoid re-querying within TTL
                _ipToHostCache[clean] = (clean, DateTime.UtcNow.Add(CacheTtl));
            }
        }

        return clean;
    }
}
