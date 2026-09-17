using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.Vpn;

namespace NzbDrone.Core.Network;

public class NetworkStatus
{
    public string LocalIp { get; set; }
    public string BoundInterface { get; set; }
    public string BoundIp { get; set; }
    public string PhysicalIp { get; set; }
    public string ExternalIp { get; set; }
    public bool IsVpnKillSwitchActive { get; set; }
    public bool UpnpAvailable { get; set; }
    public bool ProxyEnabled { get; set; }
    public List<PortMapping> PortMappings { get; set; } = new();
}

public class PortTestResult
{
    public int Port { get; set; }
    public string ExternalIp { get; set; }
    public bool IsOpen { get; set; }
    public string ErrorMessage { get; set; }
    public TimeSpan ResponseTime { get; set; }
    public double ResponseTimeMs => Math.Round(ResponseTime.TotalMilliseconds, 1);
}

public interface INetworkStatusService
{
    NetworkStatus GetStatus();
    List<string> GetLocalAddresses();
    Task<PortTestResult> TestPortAsync(int port, CancellationToken cancellationToken = default);
}

public class NetworkStatusService : INetworkStatusService, IHandle<UpnpMappingCreatedEvent>
{
    private readonly IUpnpService _upnpService;
    private readonly IExternalIpService _externalIpService;
    private readonly IProxySettingsProvider _proxySettings;
    private readonly IConfigService _configService;
    private readonly IVpnKillSwitchService _vpnKillSwitchService;
    private readonly Logger _logger;

    public Func<List<string>> LocalAddressesResolver { get; set; }

    public NetworkStatusService(
        IUpnpService upnpService,
        IExternalIpService externalIpService,
        IProxySettingsProvider proxySettings,
        IConfigService configService = null,
        IVpnKillSwitchService vpnKillSwitchService = null)
    {
        _upnpService = upnpService;
        _externalIpService = externalIpService;
        _proxySettings = proxySettings;
        _configService = configService;
        _vpnKillSwitchService = vpnKillSwitchService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public NetworkStatus GetStatus()
    {
        var localAddresses = GetLocalAddresses();
        _logger.Debug("Local addresses: {0}", string.Join(", ", localAddresses));

        var configuredInterface = _configService?.BindInterface?.Trim();
        var hasDedicatedInterface = !string.IsNullOrWhiteSpace(configuredInterface) &&
                                    !configuredInterface.Equals("Any", StringComparison.OrdinalIgnoreCase) &&
                                    !configuredInterface.Equals("all", StringComparison.OrdinalIgnoreCase) &&
                                    !configuredInterface.Equals("*", StringComparison.OrdinalIgnoreCase) &&
                                    !configuredInterface.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) &&
                                    !configuredInterface.Equals("::", StringComparison.OrdinalIgnoreCase);

        string vpnIp = null;
        if (hasDedicatedInterface)
        {
            if (_vpnKillSwitchService != null)
            {
                var ip = _vpnKillSwitchService.GetVpnInterfaceIpAddress(AddressFamily.InterNetwork);
                if (ip != null)
                {
                    vpnIp = ip.ToString();
                }
            }

            if (string.IsNullOrEmpty(vpnIp) && IPAddress.TryParse(configuredInterface, out var parsedIp))
            {
                vpnIp = parsedIp.ToString();
            }
        }

        var physicalIp = localAddresses.FirstOrDefault(ip => !string.Equals(ip, vpnIp, StringComparison.OrdinalIgnoreCase))
                         ?? localAddresses.FirstOrDefault()
                         ?? "unknown";

        string boundInterface;
        string boundIp;
        string localIp;

        if (hasDedicatedInterface && !string.IsNullOrEmpty(vpnIp))
        {
            boundInterface = _configService.BindInterface;
            boundIp = vpnIp;
            localIp = boundIp;
        }
        else
        {
            boundInterface = "Any";
            boundIp = physicalIp;
            localIp = physicalIp;
        }

        var isVpnKillSwitchActive = _vpnKillSwitchService != null
            ? _vpnKillSwitchService.IsKillSwitchEnabled
            : (_configService?.EnableVpnKillSwitch ?? false);

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
            LocalIp = localIp,
            BoundInterface = boundInterface,
            BoundIp = boundIp,
            PhysicalIp = physicalIp,
            ExternalIp = externalIp,
            IsVpnKillSwitchActive = isVpnKillSwitchActive,
            UpnpAvailable = _upnpService.IsAvailable,
            ProxyEnabled = _proxySettings.IsEnabled,
            PortMappings = _upnpService.GetMappings()
        };
    }

    public void Handle(UpnpMappingCreatedEvent message)
    {
        _logger.Info("UPnP port mapping created for external port {0}", message.ExternalPort);
    }

    public virtual List<string> GetLocalAddresses()
    {
        if (LocalAddressesResolver != null)
        {
            return LocalAddressesResolver();
        }

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

    public async Task<PortTestResult> TestPortAsync(int port, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var status = GetStatus();
        var externalIp = status?.ExternalIp;

        if (string.IsNullOrWhiteSpace(externalIp))
        {
            try
            {
                externalIp = await _externalIpService.GetExternalIpAsync(cancellationToken);
            }
            catch
            {
                // Fallback
            }
        }

        if (string.IsNullOrWhiteSpace(externalIp))
        {
            externalIp = "127.0.0.1";
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(6));

        var result = new PortTestResult
        {
            Port = port,
            ExternalIp = externalIp,
        };

        try
        {
            using var client = new TcpClient();
            using (cts.Token.Register(() => client.Close()))
            {
                if (IPAddress.TryParse(externalIp, out var ipAddr))
                {
                    await client.ConnectAsync(ipAddr, port, cts.Token);
                }
                else
                {
                    await client.ConnectAsync(externalIp, port, cts.Token);
                }
            }

            result.IsOpen = client.Connected;
            result.ErrorMessage = null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result.IsOpen = false;
            result.ErrorMessage = "Port reachability test timed out after 6 seconds.";
        }
        catch (SocketException ex)
        {
            result.IsOpen = false;
            result.ErrorMessage = ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => "Connection refused by host.",
                SocketError.TimedOut => "Port reachability test timed out.",
                SocketError.HostUnreachable => "Host unreachable.",
                SocketError.NetworkUnreachable => "Network unreachable.",
                _ => ex.Message
            };
        }
        catch (Exception ex)
        {
            result.IsOpen = false;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            sw.Stop();
            result.ResponseTime = sw.Elapsed;
        }

        return result;
    }
}
