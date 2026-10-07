// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Seedarr.Http.Security;

public static class ForwardedHeadersSecurityHelper
{
    private static readonly char[] Separators = { ',', ';', ' ', '\t', '\r', '\n' };

    /// <summary>
    /// Restricts forwarded header processing to loopback and configured trusted proxies.
    /// Cleared trust lists would otherwise accept spoofed X-Forwarded-* from any client.
    /// </summary>
    public static void ApplyTrustedProxyConfiguration(ForwardedHeadersOptions options, string trustedProxies)
    {
#pragma warning disable ASPDEPR005
        options.KnownNetworks.Clear();
#pragma warning restore ASPDEPR005
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownProxies.Add(IPAddress.IPv6Loopback);

        if (string.IsNullOrWhiteSpace(trustedProxies))
        {
            return;
        }

        foreach (var raw in trustedProxies.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var entry = raw.Trim();
            if (string.IsNullOrEmpty(entry))
            {
                continue;
            }

            if (entry.Contains('/'))
            {
                if (System.Net.IPNetwork.TryParse(entry, out var network))
                {
                    options.KnownIPNetworks.Add(network);
                }
            }
            else if (IPAddress.TryParse(entry, out var parsedIp))
            {
                options.KnownProxies.Add(parsedIp.IsIPv4MappedToIPv6 ? parsedIp.MapToIPv4() : parsedIp);
            }
        }
    }
}
