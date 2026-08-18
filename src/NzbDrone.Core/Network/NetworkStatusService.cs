using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NLog;
using NzbDrone.Core.Messaging.Events;

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

public class NetworkStatusService : INetworkStatusService, IHandle<UpnpMappingCreatedEvent>
{
    private readonly IUpnpService _upnpService;
    private readonly IExternalIpService _externalIpService;
    private readonly IProxySettingsProvider _proxySettings;
    private readonly Logger _logger;

    public NetworkStatusService(IUpnpService upnpService, IExternalIpService externalIpService, IProxySettingsProvider proxySettings)
    {
        _upnpService = upnpService;
        _externalIpService = externalIpService;
        _proxySettings = proxySettings;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public NetworkStatus GetStatus()
    {
        var localAddresses = GetLocalAddresses();
        _logger.Debug("Local addresses: {0}", string.Join(", ", localAddresses));

        var externalIp = _upnpService.ExternalIp;

        if (string.IsNullOrEmpty(externalIp))
        {
            externalIp = _externalIpService.CachedIp;

            if (string.IsNullOrEmpty(externalIp))
            {
                _ = _externalIpService.GetExternalIpAsync();
            }
        }

        return new NetworkStatus
        {
            LocalIp = localAddresses.FirstOrDefault() ?? "unknown",
            ExternalIp = externalIp,
            UpnpAvailable = _upnpService.IsAvailable,
            ProxyEnabled = _proxySettings.IsEnabled,
            PortMappings = _upnpService.GetMappings()
        };
    }

    public void Handle(UpnpMappingCreatedEvent message)
    {
        _logger.Info("UPnP port mapping created for external port {0}", message.ExternalPort);
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
