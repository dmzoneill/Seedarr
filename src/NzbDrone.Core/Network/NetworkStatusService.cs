using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NLog;

namespace NzbDrone.Core.Network;

public class NetworkStatus
{
    public string LocalIp { get; set; }
    public string ExternalIp { get; set; }
    public bool UpnpAvailable { get; set; }
    public bool ProxyEnabled { get; set; }
    public List<PortMapping> PortMappings { get; set; } = new();
}

public interface INetworkStatusService
{
    NetworkStatus GetStatus();
    List<string> GetLocalAddresses();
}

public class NetworkStatusService : INetworkStatusService
{
    private readonly IUpnpService _upnpService;
    private readonly IProxySettingsProvider _proxySettings;
    private readonly Logger _logger;

    public NetworkStatusService(IUpnpService upnpService, IProxySettingsProvider proxySettings)
    {
        _upnpService = upnpService;
        _proxySettings = proxySettings;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public NetworkStatus GetStatus()
    {
        var localAddresses = GetLocalAddresses();
        _logger.Debug("Local addresses: {0}", string.Join(", ", localAddresses));

        return new NetworkStatus
        {
            LocalIp = localAddresses.FirstOrDefault() ?? "unknown",
            ExternalIp = _upnpService.ExternalIp,
            UpnpAvailable = _upnpService.IsAvailable,
            ProxyEnabled = _proxySettings.IsEnabled,
            PortMappings = _upnpService.GetMappings()
        };
    }

    public List<string> GetLocalAddresses()
    {
        var addresses = new List<string>();

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var iface in interfaces)
            {
                if (iface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var properties = iface.GetIPProperties();
                foreach (var addr in properties.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        addresses.Add(addr.Address.ToString());
                    }
                }
            }
        }
        catch
        {
            addresses.Add(IPAddress.Loopback.ToString());
        }

        return addresses;
    }
}
