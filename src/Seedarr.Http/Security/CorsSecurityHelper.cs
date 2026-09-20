// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using NzbDrone.Core.Configuration;

namespace Seedarr.Http.Security;

public static class CorsSecurityHelper
{
    private static readonly char[] Separators = { ',', ';' };

    public static bool IsOriginAllowed(string origin, IConfigFileProvider configFileProvider)
    {
        return IsOriginAllowed(origin, configFileProvider?.AllowedOrigins);
    }

    public static bool IsOriginAllowed(string origin, string allowedOrigins)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            if (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(allowedOrigins))
            {
                return false;
            }

            var entries = allowedOrigins.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var normalizedOrigin = origin.Trim().TrimEnd('/');

            foreach (var entry in entries)
            {
                var trimmed = entry.Trim().TrimEnd('/');
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                if (string.Equals(normalizedOrigin, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (Uri.TryCreate(trimmed, UriKind.Absolute, out var allowedUri))
                {
                    if (string.Equals(uri.Scheme, allowedUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(uri.Authority, allowedUri.Authority, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                else
                {
                    if (string.Equals(uri.Authority, trimmed, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(uri.Host, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
