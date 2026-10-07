// Copyright (c) FeedItOut. All rights reserved.

using Microsoft.AspNetCore.Http;
using NzbDrone.Core.Configuration;

namespace Seedarr.Http.Security;

public static class RequestHostResolver
{
    public static string GetEffectiveHostHeader(HttpContext context, IConfigFileProvider configFileProvider)
    {
        var effectiveHost = context.Request.Host;

        if (IpSecurityHelper.IsTrustedProxy(context.Connection.RemoteIpAddress, configFileProvider?.TrustedProxies))
        {
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
        }

        var hostHeader = effectiveHost.Value;
        if (string.IsNullOrWhiteSpace(hostHeader))
        {
            hostHeader = effectiveHost.Host;
        }

        return hostHeader;
    }
}
