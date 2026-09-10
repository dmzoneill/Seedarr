using System;
using System.Collections.Generic;
using System.Linq;
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
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
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

        if (_vpnKillSwitchService != null)
        {
            return _vpnKillSwitchService.IsVpnInterfaceUp;
        }

        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(n.Id, interfaceName, StringComparison.OrdinalIgnoreCase));

            return nic != null && nic.OperationalStatus == OperationalStatus.Up;
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
}
