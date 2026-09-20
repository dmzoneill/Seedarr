// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;

namespace Seedarr.Http.Security;

public static class IpSecurityHelper
{
    private static readonly char[] Separators = { ',', ';', ' ', '\t', '\r', '\n' };

    public static bool IsTrustedProxy(IPAddress remoteIp)
    {
        return IsTrustedProxy(remoteIp, (string)null);
    }

    public static bool IsTrustedProxy(IPAddress remoteIp, string configuredProxies)
    {
        if (remoteIp == null)
        {
            return false;
        }

        var effectiveIp = remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp;

        if (IPAddress.IsLoopback(effectiveIp))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(configuredProxies))
        {
            return false;
        }

        var entries = configuredProxies.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return MatchesAny(effectiveIp, entries);
    }

    public static bool IsTrustedProxy(IPAddress remoteIp, IEnumerable<string> configuredProxies)
    {
        if (remoteIp == null)
        {
            return false;
        }

        var effectiveIp = remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp;

        if (IPAddress.IsLoopback(effectiveIp))
        {
            return true;
        }

        if (configuredProxies == null)
        {
            return false;
        }

        return MatchesAny(effectiveIp, configuredProxies);
    }

    private static bool MatchesAny(IPAddress effectiveIp, IEnumerable<string> proxyStrings)
    {
        foreach (var raw in proxyStrings)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var entry = raw.Trim();

            if (entry.Contains('/'))
            {
                if (IPNetwork.TryParse(entry, out var network))
                {
                    if (network.BaseAddress.AddressFamily == effectiveIp.AddressFamily && network.Contains(effectiveIp))
                    {
                        return true;
                    }
                }
            }
            else
            {
                if (IPAddress.TryParse(entry, out var parsedIp))
                {
                    var normalizedTarget = parsedIp.IsIPv4MappedToIPv6 ? parsedIp.MapToIPv4() : parsedIp;
                    if (effectiveIp.Equals(normalizedTarget))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
