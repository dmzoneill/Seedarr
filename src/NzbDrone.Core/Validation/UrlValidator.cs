using System;
using System.Net;

namespace NzbDrone.Core.Validation;

public static class UrlValidator
{
    public static bool IsSafeUrl(string url)
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

        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            return !IsPrivateIp(ip);
        }

        try
        {
            var addresses = Dns.GetHostAddresses(uri.Host);
            foreach (var addr in addresses)
            {
                if (IsPrivateIp(addr))
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

    private static bool IsPrivateIp(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();

        return ip.Equals(IPAddress.Loopback) ||
            ip.Equals(IPAddress.IPv6Loopback) ||
            ip.IsIPv6LinkLocal ||
            ip.IsIPv6SiteLocal ||
            (bytes.Length == 4 && bytes[0] == 10) ||
            (bytes.Length == 4 && bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
            (bytes.Length == 4 && bytes[0] == 192 && bytes[1] == 168) ||
            (bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254) ||
            (bytes.Length == 4 && bytes[0] == 127);
    }
}
