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
