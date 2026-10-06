using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.Vpn;

namespace NzbDrone.Core.Network;

public class NetworkSecurityService : INetworkSecurityService
{
    private readonly IConfigService _configService;
    private readonly IEventAggregator _eventAggregator;
    private readonly IVpnKillSwitchService _vpnKillSwitchService;
    private readonly Logger _logger;

    public NetworkSecurityService(
        IConfigService configService,
        IEventAggregator eventAggregator = null,
        IVpnKillSwitchService vpnKillSwitchService = null)
    {
        _configService = configService;
        _eventAggregator = eventAggregator;
        _vpnKillSwitchService = vpnKillSwitchService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public IEnumerable<string> GetAvailableNetworkInterfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => IsInterfaceOperational(nic) &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(nic => nic.Name)
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to query network interfaces.");
            return Enumerable.Empty<string>();
        }
    }

    public bool IsInterfaceActive(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
        {
            return true;
        }

        if (_vpnKillSwitchService != null &&
            !string.IsNullOrWhiteSpace(_vpnKillSwitchService.VpnInterfaceName) &&
            string.Equals(interfaceName, _vpnKillSwitchService.VpnInterfaceName, StringComparison.OrdinalIgnoreCase))
        {
            return _vpnKillSwitchService.IsVpnInterfaceUp;
        }

        try
        {
            if (IPAddress.TryParse(interfaceName, out var targetIp))
            {
                return NetworkInterface.GetAllNetworkInterfaces().Any(nic =>
                    IsInterfaceOperational(nic) &&
                    nic.GetIPProperties()?.UnicastAddresses.Any(a => a.Address.Equals(targetIp)) == true);
            }

            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n.Id, interfaceName, StringComparison.OrdinalIgnoreCase));

            return nic != null && IsInterfaceOperational(nic);
        }
        catch
        {
            return false;
        }
    }

    public bool CheckVpnKillSwitch()
    {
        if (_vpnKillSwitchService != null)
        {
            return _vpnKillSwitchService.CheckVpnState();
        }

        return false;
    }

    private static bool IsInterfaceOperational(NetworkInterface nic)
    {
        if (nic == null)
        {
            return false;
        }

        if (nic.OperationalStatus == OperationalStatus.Up)
        {
            return true;
        }

        if (nic.OperationalStatus == OperationalStatus.Unknown &&
            nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel &&
            HasValidUnicastAddresses(nic))
        {
            return true;
        }

        return false;
    }

    private static bool HasValidUnicastAddresses(NetworkInterface nic)
    {
        var unicast = nic.GetIPProperties()?.UnicastAddresses;
        return unicast != null && unicast.Any(a =>
            !IPAddress.IsLoopback(a.Address) &&
            !a.Address.Equals(IPAddress.Any) &&
            !a.Address.Equals(IPAddress.None) &&
            !a.Address.Equals(IPAddress.IPv6Any) &&
            !a.Address.Equals(IPAddress.IPv6None) &&
            !a.Address.IsIPv6LinkLocal &&
            !a.Address.IsIPv6SiteLocal &&
            !a.Address.IsIPv6Multicast);
    }
}
