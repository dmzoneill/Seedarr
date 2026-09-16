using System;
using System.Net;

namespace NzbDrone.Core.Validation;

public static class UrlValidator
{
    public static bool IsSafeUrl(string url, bool allowLoopback = false)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != "udp")
        {
            return false;
        }

        return IsSafeHost(uri.Host, allowLoopback);
    }

    public static bool IsSafeHost(string host, bool allowLoopback = false)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var trimmedHost = host.Trim().Trim('[', ']');

        // Cloud metadata endpoints are NEVER permitted under any circumstances
        if (string.Equals(trimmedHost, "metadata.google.internal", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmedHost, "instance-data", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Localhost hostnames
        if (string.Equals(trimmedHost, "localhost", StringComparison.OrdinalIgnoreCase) ||
            trimmedHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return allowLoopback;
        }

        if (IPAddress.TryParse(trimmedHost, out var ip))
        {
            return IsSafeIp(ip, allowLoopback);
        }

        try
        {
            var addresses = Dns.GetHostAddresses(trimmedHost);
            if (addresses.Length == 0)
            {
                return false;
            }

            foreach (var addr in addresses)
            {
                if (!IsSafeIp(addr, allowLoopback))
                {
                    return false;
                }
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    private static bool IsLoopbackIp(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (ip.Equals(IPAddress.Loopback) || ip.Equals(IPAddress.IPv6Loopback))
        {
            return true;
        }

        var bytes = ip.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 127;
    }

    private static bool IsRestrictedOrPrivateIp(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        var bytes = ip.GetAddressBytes();

        return ip.Equals(IPAddress.Any) ||
            ip.Equals(IPAddress.IPv6Any) ||
            ip.Equals(IPAddress.IPv6None) ||
            ip.IsIPv6LinkLocal ||
            ip.IsIPv6SiteLocal ||
            ip.IsIPv6Multicast ||
            (bytes.Length == 16 && bytes[0] >= 0xFC && bytes[0] <= 0xFD) || // IPv6 ULA (fc00::/7)
            (bytes.Length == 4 && bytes[0] == 0) || // 0.0.0.0/8
            (bytes.Length == 4 && bytes[0] == 10) || // RFC 1918: 10.0.0.0/8
            (bytes.Length == 4 && bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || // RFC 1918: 172.16.0.0/12
            (bytes.Length == 4 && bytes[0] == 192 && bytes[1] == 168) || // RFC 1918: 192.168.0.0/16
            (bytes.Length == 4 && bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) || // RFC 6598: CGNAT 100.64.0.0/10
            (bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254) || // Link-local / Cloud metadata (169.254.0.0/16, including 169.254.169.254)
            (bytes.Length == 4 && bytes[0] >= 224); // Multicast & Class E reserved (224.0.0.0+)
    }

    public static bool IsPrivateIp(IPAddress ip)
    {
        return IsLoopbackIp(ip) || IsRestrictedOrPrivateIp(ip);
    }

    private static bool IsSafeIp(IPAddress ip, bool allowLoopback)
    {
        if (IsLoopbackIp(ip))
        {
            return allowLoopback;
        }

        return !IsRestrictedOrPrivateIp(ip);
    }
}
