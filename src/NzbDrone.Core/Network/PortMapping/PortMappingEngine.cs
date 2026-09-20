using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Network.Pcp;
using Open.Nat;

namespace NzbDrone.Core.Network;

public class PortMappingEngine : IPortMappingEngine
{
    public const int NatPmpPort = 5351;
    public static readonly int[] DefaultRetryIntervalsMs = new[] { 250, 500, 1000 };

    private readonly IGatewayDiscoveryService _gatewayDiscoveryService;
    private readonly IUpnpService _upnpService;
    private readonly IPcpClient _pcpClient;
    private readonly Func<IPAddress, CancellationToken, Task<PortMappingProtocol>> _udpProber;
    private readonly Func<CancellationToken, Task<bool>> _upnpProber;
    private readonly Logger _logger;

    public PortMappingProtocol CurrentProtocol { get; private set; } = PortMappingProtocol.None;
    public IPcpClient PcpClient => _pcpClient;

    public PortMappingEngine(
        IGatewayDiscoveryService gatewayDiscoveryService,
        IUpnpService upnpService = null,
        Func<IPAddress, CancellationToken, Task<PortMappingProtocol>> udpProber = null,
        Func<CancellationToken, Task<bool>> upnpProber = null,
        IPcpClient pcpClient = null)
    {
        _gatewayDiscoveryService = gatewayDiscoveryService;
        _upnpService = upnpService;
        _pcpClient = pcpClient;
        _logger = LogManager.GetCurrentClassLogger();
        _udpProber = udpProber ?? ProbeUdpAsync;
        _upnpProber = upnpProber ?? ProbeUpnpAsync;
    }

    public void DemoteToNatPmp()
    {
        CurrentProtocol = PortMappingProtocol.NatPmp;
        _logger.Info("Port mapping protocol demoted from PCP to NAT-PMP");
    }

    public async Task<PcpMappingResult> CreatePcpMappingAsync(
        IPAddress gateway,
        IPAddress clientIp,
        int internalPort,
        int suggestedExternalPort,
        PortMappingTransport protocol,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        if (_pcpClient == null)
        {
            throw new InvalidOperationException("PCP client is not configured.");
        }

        var result = await _pcpClient.CreateMappingAsync(gateway, clientIp, internalPort, suggestedExternalPort, protocol, lifetime, cancellationToken);
        if (result.ResultCode == PcpResultCode.UnsupportedVersion)
        {
            DemoteToNatPmp();
        }

        return result;
    }

    public async Task<PortMappingProtocol> DetectProtocolAsync(CancellationToken cancellationToken = default)
    {
        IPAddress gateway = null;
        try
        {
            gateway = await _gatewayDiscoveryService.GetDefaultGatewayAsync();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to resolve default gateway");
        }

        if (gateway != null)
        {
            try
            {
                var udpProtocol = await _udpProber(gateway, cancellationToken);
                if (udpProtocol == PortMappingProtocol.Pcp || udpProtocol == PortMappingProtocol.NatPmp)
                {
                    CurrentProtocol = udpProtocol;
                    _logger.Info("Port mapping protocol negotiated via UDP 5351: {0}", CurrentProtocol);
                    return CurrentProtocol;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "UDP 5351 probe failed");
            }
        }
        else
        {
            _logger.Debug("No default gateway found; skipping UDP 5351 probe");
        }

        try
        {
            var upnpAvailable = await _upnpProber(cancellationToken);
            if (upnpAvailable)
            {
                CurrentProtocol = PortMappingProtocol.Upnp;
                _logger.Info("Port mapping protocol negotiated via UPnP-IGD");
                return CurrentProtocol;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "UPnP probe failed");
        }

        CurrentProtocol = PortMappingProtocol.None;
        _logger.Info("No port mapping protocol available");
        return CurrentProtocol;
    }

    public Task<PortMappingProtocol> ProbeAsync(CancellationToken cancellationToken = default)
    {
        return DetectProtocolAsync(cancellationToken);
    }

    public PortMappingProtocol DetectProtocol()
    {
        return DetectProtocolAsync().GetAwaiter().GetResult();
    }

    public async Task<PortMappingProtocol> ProbeUdpAsync(IPAddress gatewayAddress, CancellationToken cancellationToken)
    {
        if (gatewayAddress == null)
        {
            return PortMappingProtocol.None;
        }

        var probePayload = new byte[] { 0x00, 0x00 };
        var endpoint = new IPEndPoint(gatewayAddress, NatPmpPort);

        using var udpClient = new UdpClient(gatewayAddress.AddressFamily);

        foreach (var timeoutMs in DefaultRetryIntervalsMs)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await udpClient.SendAsync(probePayload, endpoint, cancellationToken);

                using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveCts.CancelAfter(timeoutMs);

                var result = await udpClient.ReceiveAsync(receiveCts.Token);
                if (result.Buffer != null && result.Buffer.Length > 0)
                {
                    var protocol = ParseProbeResponse(result.Buffer);
                    if (protocol != PortMappingProtocol.None)
                    {
                        return protocol;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.Debug("UDP probe to {0}:{1} timed out after {2}ms, retrying...", gatewayAddress, NatPmpPort, timeoutMs);
            }
            catch (SocketException ex)
            {
                _logger.Debug(ex, "Socket exception during UDP probe to {0}:{1}", gatewayAddress, NatPmpPort);
                break;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error during UDP probe to {0}:{1}", gatewayAddress, NatPmpPort);
                break;
            }
        }

        return PortMappingProtocol.None;
    }

    public async Task<bool> ProbeUpnpAsync(CancellationToken cancellationToken)
    {
        if (_upnpService != null && _upnpService.IsAvailable)
        {
            return true;
        }

        try
        {
            var discoverer = new NatDiscoverer();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            var device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);
            return device != null;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "UPnP discovery probe failed or timed out");
            return false;
        }
    }

    public static PortMappingProtocol ParseProbeResponse(byte[] response)
    {
        if (response == null || response.Length == 0)
        {
            return PortMappingProtocol.None;
        }

        if (response[0] == 2)
        {
            return PortMappingProtocol.Pcp;
        }

        if (response.Length >= 4)
        {
            var version = response[0];
            var resultCode = (ushort)((response[2] << 8) | response[3]);

            if (version == 0 && resultCode == 0)
            {
                return PortMappingProtocol.NatPmp;
            }

            if (resultCode == 1)
            {
                for (var i = 0; i < response.Length; i++)
                {
                    if (response[i] == 2)
                    {
                        return PortMappingProtocol.Pcp;
                    }
                }
            }
        }

        return PortMappingProtocol.None;
    }

    public async Task<PortMappingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var gatewayIp = string.Empty;
        try
        {
            var gw = await _gatewayDiscoveryService.GetDefaultGatewayAsync();
            if (gw != null)
            {
                gatewayIp = gw.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to resolve default gateway for status");
        }

        var protocol = CurrentProtocol;
        if (protocol == PortMappingProtocol.None && _upnpService != null && _upnpService.IsAvailable)
        {
            protocol = PortMappingProtocol.Upnp;
        }

        var protocolStr = protocol switch
        {
            PortMappingProtocol.Pcp => "PCP",
            PortMappingProtocol.NatPmp => "NAT-PMP",
            PortMappingProtocol.Upnp => "UPnP",
            _ => "None"
        };

        var externalIp = _pcpClient?.ExternalAddress?.ToString();
        if (string.IsNullOrWhiteSpace(externalIp))
        {
            externalIp = _upnpService?.ExternalIp ?? string.Empty;
        }

        var routerModel = _upnpService?.RouterModel;
        if (string.IsNullOrWhiteSpace(routerModel))
        {
            routerModel = protocol switch
            {
                PortMappingProtocol.Upnp => "UPnP-IGD Gateway",
                PortMappingProtocol.NatPmp => "NAT-PMP Gateway",
                PortMappingProtocol.Pcp => "PCP Gateway",
                _ => string.Empty
            };
        }

        var mappings = new List<EnrichedPortMapping>();

        if (_pcpClient != null && _pcpClient.ActiveMappings.Count > 0)
        {
            foreach (var m in _pcpClient.ActiveMappings)
            {
                mappings.Add(new EnrichedPortMapping
                {
                    InternalPort = m.InternalPort,
                    ExternalPort = m.AssignedExternalPort,
                    Protocol = m.Protocol.ToString().ToUpperInvariant(),
                    Description = "PCP Mapping",
                    LeaseSeconds = (int)m.Lifetime.TotalSeconds,
                    ExpiryUtc = m.CreatedAtUtc.Add(m.Lifetime),
                    IsActive = m.IsActive,
                    Status = m.IsActive ? "Active" : "Inactive"
                });
            }
        }

        if (_upnpService != null)
        {
            var rawMappings = _upnpService.GetMappings();
            if (rawMappings != null)
            {
                foreach (var m in rawMappings)
                {
                    mappings.Add(new EnrichedPortMapping
                    {
                        InternalPort = m.InternalPort,
                        ExternalPort = m.ExternalPort,
                        Protocol = m.Protocol,
                        Description = m.Description,
                        LeaseSeconds = m.LeaseSeconds,
                        ExpiryUtc = m.ExpiryUtc,
                        IsActive = m.IsActive,
                        Status = m.IsActive ? "Active" : "Inactive",
                        ErrorMessage = m.ErrorMessage
                    });
                }
            }
        }

        return new PortMappingStatus
        {
            Protocol = protocolStr,
            GatewayIp = gatewayIp,
            ExternalIp = externalIp,
            RouterModel = routerModel,
            Mappings = mappings,
            LastRenewalUtc = _upnpService?.LastRenewalUtc,
            NextRenewalUtc = _upnpService?.NextRenewalUtc
        };
    }

    public async Task<PortMappingStatus> RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await DetectProtocolAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "PortMappingEngine: Protocol detection failed during refresh");
        }

        if (_upnpService != null)
        {
            try
            {
                await _upnpService.CreateMappings(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "PortMappingEngine: UPnP mapping refresh failed");
            }
        }

        return await GetStatusAsync(cancellationToken);
    }
}
