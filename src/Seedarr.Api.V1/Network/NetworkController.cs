using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Dht;
using NzbDrone.Core.Network;
using NzbDrone.Core.Peers;
using Seedarr.Http;

namespace Seedarr.Api.V1.Network;

[V1ApiController("network")]
public class NetworkController : Controller
{
    private readonly INetworkStatusService _networkStatusService;
    private readonly IConnectionManager _connectionManager;
    private readonly IDhtService _dhtService;
    private readonly IConfigService _configService;
    private readonly IPeerConnectionLogService _peerLogService;

    public NetworkController(
        INetworkStatusService networkStatusService,
        IConnectionManager connectionManager,
        IDhtService dhtService,
        IConfigService configService,
        IPeerConnectionLogService peerLogService)
    {
        _networkStatusService = networkStatusService;
        _connectionManager = connectionManager;
        _dhtService = dhtService;
        _configService = configService;
        _peerLogService = peerLogService;
    }

    [HttpGet("status")]
    public ActionResult<NetworkStatus> GetStatus()
    {
        return _networkStatusService.GetStatus();
    }

    [HttpGet("addresses")]
    public ActionResult GetAddresses()
    {
        var addresses = _networkStatusService.GetLocalAddresses();
        return Ok(addresses);
    }

    [HttpGet("interfaces")]
    public ActionResult<List<NetworkInterfaceResource>> GetInterfaces()
    {
        try
        {
            var ifaces = global::System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.NetworkInterfaceType != global::System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .Select(nic =>
                {
                    var isVpn = IsVpnOrTunnel(nic.Name, nic.NetworkInterfaceType, nic.Description);
                    var type = ClassifyInterfaceType(nic.Name, nic.NetworkInterfaceType, isVpn);
                    var addresses = new List<string>();

                    try
                    {
                        var ipProps = nic.GetIPProperties();
                        if (ipProps?.UnicastAddresses != null)
                        {
                            foreach (var addr in ipProps.UnicastAddresses)
                            {
                                if (addr?.Address != null &&
                                    (addr.Address.AddressFamily == global::System.Net.Sockets.AddressFamily.InterNetwork ||
                                     addr.Address.AddressFamily == global::System.Net.Sockets.AddressFamily.InterNetworkV6))
                                {
                                    addresses.Add(addr.Address.ToString());
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore exceptions reading IP properties on inactive or restricted adapters
                    }

                    return new NetworkInterfaceResource
                    {
                        Name = nic.Name,
                        Description = nic.Description ?? string.Empty,
                        Type = type,
                        Status = nic.OperationalStatus.ToString(),
                        Addresses = addresses,
                        IsVpn = isVpn
                    };
                })
                .OrderByDescending(i => i.IsVpn)
                .ThenByDescending(i => i.Status == "Up")
                .ThenBy(i => i.Name)
                .ToList();

            return Ok(ifaces);
        }
        catch
        {
            return Ok(new List<NetworkInterfaceResource>());
        }
    }

    public static bool IsVpnOrTunnel(string name, global::System.Net.NetworkInformation.NetworkInterfaceType interfaceType, string description)
    {
        if (interfaceType == global::System.Net.NetworkInformation.NetworkInterfaceType.Tunnel ||
            interfaceType == global::System.Net.NetworkInformation.NetworkInterfaceType.Ppp)
        {
            return true;
        }

        var lowerName = name?.ToLowerInvariant() ?? string.Empty;
        if (lowerName.StartsWith("tun") ||
            lowerName.StartsWith("wg") ||
            lowerName.StartsWith("tap") ||
            lowerName.StartsWith("ppp") ||
            lowerName.StartsWith("utun") ||
            lowerName.StartsWith("tailscale") ||
            lowerName.StartsWith("zt") ||
            lowerName.Contains("vpn") ||
            lowerName.Contains("wireguard") ||
            lowerName.Contains("openvpn"))
        {
            return true;
        }

        var lowerDesc = description?.ToLowerInvariant() ?? string.Empty;
        if (lowerDesc.Contains("vpn") ||
            lowerDesc.Contains("wireguard") ||
            lowerDesc.Contains("openvpn") ||
            lowerDesc.Contains("tunnel") ||
            lowerDesc.Contains("tap-windows") ||
            lowerDesc.Contains("wintun") ||
            lowerDesc.Contains("tailscale") ||
            lowerDesc.Contains("zerotier"))
        {
            return true;
        }

        return false;
    }

    public static string ClassifyInterfaceType(string name, global::System.Net.NetworkInformation.NetworkInterfaceType interfaceType, bool isVpn)
    {
        if (isVpn ||
            interfaceType == global::System.Net.NetworkInformation.NetworkInterfaceType.Tunnel ||
            interfaceType == global::System.Net.NetworkInformation.NetworkInterfaceType.Ppp)
        {
            return "Tunnel";
        }

        if (interfaceType == global::System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
        {
            return "Wireless";
        }

        var lowerName = name?.ToLowerInvariant() ?? string.Empty;
        if (lowerName.StartsWith("wlan") || lowerName.StartsWith("wl") || lowerName.StartsWith("wifi"))
        {
            return "Wireless";
        }

        if (lowerName.StartsWith("veth") ||
            lowerName.StartsWith("docker") ||
            lowerName.StartsWith("br-") ||
            lowerName.StartsWith("virbr") ||
            lowerName.StartsWith("cni") ||
            lowerName.StartsWith("flannel") ||
            lowerName.StartsWith("vmnet") ||
            lowerName.StartsWith("vboxnet") ||
            lowerName.Contains("bridge"))
        {
            return "Virtual";
        }

        return "Physical";
    }

    [HttpGet("diagnostics")]
    public ActionResult<NetworkDiagnostics> GetDiagnostics()
    {
        var status = _networkStatusService.GetStatus();
        var now = global::System.DateTime.UtcNow;
        var recentLogs = _peerLogService?.GetByTimeRange(now.AddHours(-24), now) ?? new List<NzbDrone.Core.Peers.PeerConnectionLog>();

        var encryptedCount = recentLogs.Count(l => l.IsEncrypted && l.EventType == "Connected");
        var plaintextCount = recentLogs.Count(l => !l.IsEncrypted && l.EventType == "Connected");
        var totalConnections = encryptedCount + plaintextCount;

        return Ok(new NetworkDiagnostics
        {
            LocalIp = status?.LocalIp ?? "unknown",
            ExternalIp = status?.ExternalIp,
            LocalAddresses = _networkStatusService.GetLocalAddresses() ?? new List<string>(),
            UpnpAvailable = status?.UpnpAvailable ?? false,
            ProxyEnabled = status?.ProxyEnabled ?? false,
            PortMappings = status?.PortMappings ?? new List<PortMapping>(),
            ListeningPort = _configService?.ListeningPort ?? 6881,
            ActiveConnections = _connectionManager?.ActiveCount ?? 0,
            UploadSlots = _connectionManager?.GetUploadSlotCount() ?? 0,
            DhtEnabled = _configService?.EnableDht ?? false,
            DhtNodeCount = _dhtService?.RoutingTable?.NodeCount ?? 0,
            EncryptionMode = _configService?.EncryptionMode ?? "enabled",
            EncryptedConnections = encryptedCount,
            PlaintextConnections = plaintextCount,
            EncryptionPercentage = totalConnections > 0
                ? global::System.Math.Round(encryptedCount * 100.0 / totalConnections, 1)
                : 0
        });
    }
}

public class NetworkDiagnostics
{
    public string LocalIp { get; set; }
    public string ExternalIp { get; set; }
    public List<string> LocalAddresses { get; set; }
    public bool UpnpAvailable { get; set; }
    public bool ProxyEnabled { get; set; }
    public List<PortMapping> PortMappings { get; set; }
    public int ListeningPort { get; set; }
    public int ActiveConnections { get; set; }
    public int UploadSlots { get; set; }
    public bool DhtEnabled { get; set; }
    public int DhtNodeCount { get; set; }
    public string EncryptionMode { get; set; }
    public int EncryptedConnections { get; set; }
    public int PlaintextConnections { get; set; }
    public double EncryptionPercentage { get; set; }
}

public class NetworkInterfaceResource
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Type { get; set; } // Physical, Wireless, Tunnel, Virtual
    public string Status { get; set; } // Up, Down, Testing
    public List<string> Addresses { get; set; } = new();
    public bool IsVpn { get; set; }
}
