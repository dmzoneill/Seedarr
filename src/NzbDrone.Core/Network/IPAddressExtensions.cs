using System.Net;
using System.Net.Sockets;

namespace NzbDrone.Core.Network;

public static class IPAddressExtensions
{
    public static bool IsLocalSubnet(this IPAddress ip)
    {
        if (ip == null)
        {
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();

            // RFC 1918 10.0.0.0/8 (10.0.0.0 - 10.255.255.255)
            if (bytes[0] == 10)
            {
                return true;
            }

            // RFC 1918 172.16.0.0/12 (172.16.0.0 - 172.31.255.255)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            // RFC 1918 192.168.0.0/16 (192.168.0.0 - 192.168.255.255)
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            // RFC 3927 Link-Local 169.254.0.0/16
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // RFC 4291 Link-Local fe80::/10
            if (ip.IsIPv6LinkLocal)
            {
                return true;
            }

            var bytes = ip.GetAddressBytes();

            // RFC 4193 Unique Local Address (ULA) fc00::/7 (fc00::/8, fd00::/8)
            if ((bytes[0] & 0xfe) == 0xfc)
            {
                return true;
            }

            return false;
        }

        return false;
    }

    public static bool IsLocalSubnet(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return false;
        }

        if (IPAddress.TryParse(ipAddress, out var ip))
        {
            return IsLocalSubnet(ip);
        }

        return false;
    }
}
