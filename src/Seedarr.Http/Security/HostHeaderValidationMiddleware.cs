using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NLog;
using NzbDrone.Core.Configuration;

namespace Seedarr.Http.Security;

public class HostHeaderValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public HostHeaderValidationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IConfigService configService)
    {
        if (configService != null && configService.HostHeaderValidationEnabled)
        {
            var hostHeader = context.Request.Host.Value;
            if (string.IsNullOrWhiteSpace(hostHeader))
            {
                hostHeader = context.Request.Host.Host;
            }

            if (string.IsNullOrWhiteSpace(hostHeader) || !IsHostAllowed(hostHeader, configService.AllowedHosts))
            {
                _logger.Warn("Blocked request with disallowed Host header: '{0}' from {1}", hostHeader, context.Connection.RemoteIpAddress);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("Invalid Host header.");
                return;
            }
        }

        await _next(context);
    }

    public static bool IsHostAllowed(string host, string allowedHostsConfig)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var cleanHost = host.Trim();

        // Strip port and extract inner host / IP
        if (cleanHost.StartsWith('['))
        {
            var closeBracket = cleanHost.IndexOf(']');
            if (closeBracket > 0)
            {
                cleanHost = cleanHost.Substring(1, closeBracket - 1).Trim();
            }
        }
        else
        {
            var colonIndex = cleanHost.IndexOf(':');
            if (colonIndex >= 0 && colonIndex == cleanHost.LastIndexOf(':'))
            {
                var portPart = cleanHost.Substring(colonIndex + 1);
                if (int.TryParse(portPart, out var port) && port >= 0 && port <= 65535)
                {
                    cleanHost = cleanHost.Substring(0, colonIndex).Trim();
                }
            }
        }

        // Loopback and container network local domains are always allowed
        if (cleanHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            cleanHost.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            cleanHost.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
            cleanHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            cleanHost.Equals("seedarr", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check configured allowed hosts
        if (!string.IsNullOrWhiteSpace(allowedHostsConfig))
        {
            var allowed = allowedHostsConfig.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pattern in allowed)
            {
                var trimmedPattern = pattern.Trim();
                if (trimmedPattern == "*")
                {
                    return true;
                }

                if (trimmedPattern.Equals(host.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    trimmedPattern.Equals(cleanHost, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var patternHost = trimmedPattern;
                if (patternHost.StartsWith('['))
                {
                    var closeBracket = patternHost.IndexOf(']');
                    if (closeBracket > 0)
                    {
                        patternHost = patternHost.Substring(1, closeBracket - 1).Trim();
                    }
                }
                else
                {
                    var colonIndex = patternHost.IndexOf(':');
                    if (colonIndex >= 0 && colonIndex == patternHost.LastIndexOf(':'))
                    {
                        var portPart = patternHost.Substring(colonIndex + 1);
                        if (int.TryParse(portPart, out var p) && p >= 0 && p <= 65535)
                        {
                            patternHost = patternHost.Substring(0, colonIndex).Trim();
                        }
                    }
                }

                if (patternHost.Equals(cleanHost, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Wildcard subdomain and apex domain matching (e.g. *.example.com, *.local, *.lan or .example.com)
                if (patternHost.StartsWith("*.", StringComparison.OrdinalIgnoreCase))
                {
                    var domain = patternHost.Substring(2);
                    if (cleanHost.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                        cleanHost.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                else if (patternHost.StartsWith(".", StringComparison.OrdinalIgnoreCase))
                {
                    var domain = patternHost.Substring(1);
                    if (cleanHost.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                        cleanHost.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }

        // Allow local LAN IPs (IPv4 private, link-local, CGNAT/Tailscale, and IPv6 loopback, link-local, ULA, site-local)
        if (IPAddress.TryParse(cleanHost, out var ip))
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            if (IPAddress.IsLoopback(ip))
            {
                return true;
            }

            var bytes = ip.GetAddressBytes();
            if (bytes.Length == 4)
            {
                // 10.0.0.0/8
                if (bytes[0] == 10)
                {
                    return true;
                }

                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                {
                    return true;
                }

                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168)
                {
                    return true;
                }

                // 169.254.0.0/16 (Link Local, RFC 3927)
                if (bytes[0] == 169 && bytes[1] == 254)
                {
                    return true;
                }

                // 100.64.0.0/10 (Carrier-Grade NAT / Tailscale, RFC 6598)
                if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                {
                    return true;
                }
            }
            else if (bytes.Length == 16)
            {
                // IPv6 Link-Local (fe80::/10) or Site-Local (fec0::/10)
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                {
                    return true;
                }

                // IPv6 Unique Local Address (fc00::/7 / fd00::/8, RFC 4193)
                if ((bytes[0] & 0xfe) == 0xfc)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
