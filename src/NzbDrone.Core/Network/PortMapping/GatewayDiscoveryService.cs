using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Network;

public class GatewayDiscoveryService : IGatewayDiscoveryService
{
    private readonly string _routeFilePath;
    private readonly Func<IPAddress> _networkInterfaceFallback;
    private readonly Logger _logger;

    public GatewayDiscoveryService()
        : this("/proc/net/route", null)
    {
    }

    internal GatewayDiscoveryService(string routeFilePath, Func<IPAddress> networkInterfaceFallback = null)
    {
        _routeFilePath = routeFilePath ?? "/proc/net/route";
        _networkInterfaceFallback = networkInterfaceFallback ?? GetGatewayFromNetworkInterfaces;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public IPAddress GetDefaultGateway()
    {
        if (OperatingSystem.IsLinux() || File.Exists(_routeFilePath))
        {
            try
            {
                if (File.Exists(_routeFilePath))
                {
                    var content = File.ReadAllText(_routeFilePath);
                    var gateway = ParseRouteTable(content);
                    if (gateway != null)
                    {
                        return gateway;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to read or parse route table from {0}", _routeFilePath);
            }
        }

        return _networkInterfaceFallback();
    }

    public Task<IPAddress> GetDefaultGatewayAsync()
    {
        return Task.FromResult(GetDefaultGateway());
    }

    public static IPAddress ParseRouteTable(string routeTableContent)
    {
        if (string.IsNullOrWhiteSpace(routeTableContent))
        {
            return null;
        }

        var lines = routeTableContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        IPAddress bestGateway = null;
        var bestMetric = int.MaxValue;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("Iface", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            var destinationHex = parts[1];
            var gatewayHex = parts[2];

            if (!string.Equals(destinationHex, "00000000", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(gatewayHex, "00000000", StringComparison.OrdinalIgnoreCase) || gatewayHex.Length != 8)
            {
                continue;
            }

            if (!uint.TryParse(gatewayHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var gatewayVal) || gatewayVal == 0)
            {
                continue;
            }

            var metric = 0;
            if (parts.Length > 6 && int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMetric))
            {
                metric = parsedMetric;
            }

            var b0 = (byte)(gatewayVal & 0xFF);
            var b1 = (byte)((gatewayVal >> 8) & 0xFF);
            var b2 = (byte)((gatewayVal >> 16) & 0xFF);
            var b3 = (byte)((gatewayVal >> 24) & 0xFF);

            var candidateIp = new IPAddress(new[] { b0, b1, b2, b3 });

            if (metric < bestMetric)
            {
                bestMetric = metric;
                bestGateway = candidateIp;
            }
        }

        return bestGateway;
    }

    public static IPAddress GetGatewayFromNetworkInterfaces()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var ni in interfaces.Where(i => i.OperationalStatus == OperationalStatus.Up && i.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                var ipProps = ni.GetIPProperties();
                if (ipProps == null)
                {
                    continue;
                }

                foreach (var gw in ipProps.GatewayAddresses)
                {
                    if (gw?.Address != null &&
                        !IPAddress.Any.Equals(gw.Address) &&
                        !IPAddress.IPv6Any.Equals(gw.Address) &&
                        gw.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return gw.Address;
                    }
                }
            }

            foreach (var ni in interfaces.Where(i => i.OperationalStatus == OperationalStatus.Up))
            {
                var ipProps = ni.GetIPProperties();
                if (ipProps == null)
                {
                    continue;
                }

                foreach (var gw in ipProps.GatewayAddresses)
                {
                    if (gw?.Address != null &&
                        !IPAddress.Any.Equals(gw.Address) &&
                        !IPAddress.IPv6Any.Equals(gw.Address))
                    {
                        return gw.Address;
                    }
                }
            }
        }
        catch
        {
            // Ignore network enumeration exceptions in restricted environments
        }

        return null;
    }
}
