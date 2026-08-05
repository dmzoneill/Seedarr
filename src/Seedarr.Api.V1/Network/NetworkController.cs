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
    private readonly DhtService _dhtService;
    private readonly IConfigService _configService;
    private readonly IPeerConnectionLogService _peerLogService;

    public NetworkController(
        INetworkStatusService networkStatusService,
        IConnectionManager connectionManager,
        DhtService dhtService,
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

    [HttpGet("diagnostics")]
    public ActionResult<NetworkDiagnostics> GetDiagnostics()
    {
        var status = _networkStatusService.GetStatus();
        var now = global::System.DateTime.UtcNow;
        var recentLogs = _peerLogService.GetByTimeRange(now.AddHours(-24), now);

        var encryptedCount = recentLogs.Count(l => l.IsEncrypted && l.EventType == "Connected");
        var plaintextCount = recentLogs.Count(l => !l.IsEncrypted && l.EventType == "Connected");
        var totalConnections = encryptedCount + plaintextCount;

        return Ok(new NetworkDiagnostics
        {
            LocalIp = status.LocalIp,
            ExternalIp = status.ExternalIp,
            LocalAddresses = _networkStatusService.GetLocalAddresses(),
            UpnpAvailable = status.UpnpAvailable,
            ProxyEnabled = status.ProxyEnabled,
            PortMappings = status.PortMappings,
            ListeningPort = _configService.ListeningPort,
            ActiveConnections = _connectionManager.ActiveCount,
            UploadSlots = _connectionManager.GetUploadSlotCount(),
            DhtEnabled = _configService.EnableDht,
            DhtNodeCount = _dhtService.RoutingTable.NodeCount,
            EncryptionMode = _configService.EncryptionMode,
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
