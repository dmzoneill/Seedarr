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
        ArgumentNullException.ThrowIfNull(socket);

        string physicalInterfaceName = null;
        NetworkInterface matchingNic = null;

        if (!string.IsNullOrWhiteSpace(interfaceName))
        {
            if (IPAddress.TryParse(interfaceName, out var parsedIfaceIp))
            {
                localIp ??= parsedIfaceIp;
                try
                {
                    matchingNic = NetworkInterface.GetAllNetworkInterfaces()
                        .FirstOrDefault(n =>
                        {
                            try
                            {
                                return n.GetIPProperties()?.UnicastAddresses?.Any(u => u.Address.Equals(parsedIfaceIp)) == true;
                            }
                            catch
                            {
                                return false;
                            }
                        });
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to enumerate network interfaces for IP {0}", parsedIfaceIp);
                }

                physicalInterfaceName = matchingNic?.Name;
            }
            else
            {
                physicalInterfaceName = interfaceName;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        matchingNic = NetworkInterface.GetAllNetworkInterfaces()
                            .FirstOrDefault(n => string.Equals(n.Name, physicalInterfaceName, StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(n.Id, physicalInterfaceName, StringComparison.OrdinalIgnoreCase));
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Failed to enumerate network interfaces for name {0}", physicalInterfaceName);
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(physicalInterfaceName))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    var ifaceBytes = Encoding.ASCII.GetBytes(physicalInterfaceName);
                    socket.SetSocketOption(SocketOptionLevel.Socket, (SocketOptionName)SO_BINDTODEVICE, ifaceBytes);
                }
                catch (SocketException)
                {
                    // In unprivileged Docker containers lacking CAP_NET_RAW, SO_BINDTODEVICE may throw EPERM; fallback gracefully
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && matchingNic != null)
            {
                try
                {
                    if (socket.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        var index = (int)matchingNic.GetIPProperties().GetIPv6Properties().Index;
                        socket.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)IPV6_UNICAST_IF, IPAddress.HostToNetworkOrder(index));
                    }
                    else
                    {
                        var index = (int)matchingNic.GetIPProperties().GetIPv4Properties().Index;
                        socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)IP_UNICAST_IF, IPAddress.HostToNetworkOrder(index));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to bind socket option to interface name {0}", physicalInterfaceName);
                }
            }
        }

        if (localIp != null && !socket.IsBound)
        {
            BindLocalEndpoint(socket, localIp, port);
        }
    }

    private static void BindLocalEndpoint(Socket socket, IPAddress localIp, int port)
    {
        var targetIp = NormalizeAddressForSocket(socket, localIp);
        if (targetIp == null)
        {
            return;
        }

        try
        {
            socket.Bind(new IPEndPoint(targetIp, port));
        }
        catch (SocketException ex)
        {
            _logger.Warn(ex, "Failed to bind socket to local endpoint {0}:{1}", targetIp, port);
        }
    }

    private static IPAddress NormalizeAddressForSocket(Socket socket, IPAddress localIp)
    {
        if (socket.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (localIp.AddressFamily == AddressFamily.InterNetwork || localIp.IsIPv4MappedToIPv6)
            {
                try
                {
                    socket.DualMode = true;
                }
                catch
                {
                    // DualMode may not be supported on all socket types or OS configurations
                }
            }

            if (localIp.AddressFamily == AddressFamily.InterNetwork)
            {
                return localIp.Equals(IPAddress.Any) ? IPAddress.IPv6Any : localIp.MapToIPv6();
            }

            return localIp;
        }

        if (socket.AddressFamily == AddressFamily.InterNetwork)
        {
            if (localIp.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (localIp.Equals(IPAddress.IPv6Any))
                {
                    return IPAddress.Any;
                }

                if (localIp.IsIPv4MappedToIPv6)
                {
                    return localIp.MapToIPv4();
                }

                _logger.Warn("Cannot bind IPv6 address {0} to IPv4 socket", localIp);
                return null;
            }

            return localIp;
        }

        return localIp;
    }
}
