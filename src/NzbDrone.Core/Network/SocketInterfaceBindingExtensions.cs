using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using NLog;

namespace NzbDrone.Core.Network;

public static class SocketInterfaceBindingExtensions
{
    private const int SO_BINDTODEVICE = 25; // Linux socket level
    private const int IP_UNICAST_IF = 31;   // Windows IP level
    private const int IPV6_UNICAST_IF = 31; // Windows IPv6 level
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public static void BindToNetworkInterface(this Socket socket, string interfaceName, IPAddress localIp = null, int port = 0)
    {
        if (socket == null)
        {
            throw new ArgumentNullException(nameof(socket));
        }

        if (string.IsNullOrWhiteSpace(interfaceName))
        {
            if (localIp != null && !socket.IsBound)
            {
                socket.Bind(new IPEndPoint(localIp, port));
            }

            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                var ifaceBytes = Encoding.ASCII.GetBytes(interfaceName);
                socket.SetSocketOption(SocketOptionLevel.Socket, (SocketOptionName)SO_BINDTODEVICE, ifaceBytes);
            }
            catch (SocketException)
            {
                // In unprivileged Docker containers lacking CAP_NET_RAW, SO_BINDTODEVICE may throw EPERM; fallback gracefully
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase));
                if (nic != null)
                {
                    if (socket.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        var index = (int)nic.GetIPProperties().GetIPv6Properties().Index;
                        socket.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)IPV6_UNICAST_IF, IPAddress.HostToNetworkOrder(index));
                    }
                    else
                    {
                        var index = (int)nic.GetIPProperties().GetIPv4Properties().Index;
                        socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)IP_UNICAST_IF, IPAddress.HostToNetworkOrder(index));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to bind socket option to interface name {0}", interfaceName);
            }
        }

        if (localIp != null && !socket.IsBound)
        {
            socket.Bind(new IPEndPoint(localIp, port));
        }
    }
}
