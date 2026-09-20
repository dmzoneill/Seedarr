using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network;

namespace NzbDrone.Core.HealthCheck.Checks;

public class PortMappingHealthCheck : IProvideHealthCheck
{
    private readonly IConfigService _configService;
    private readonly IUpnpService _upnpService;

    public PortMappingHealthCheck(IConfigService configService, IUpnpService upnpService)
    {
        _configService = configService;
        _upnpService = upnpService;
    }

    public HealthCheckResult Check()
    {
        if (_configService == null || !_configService.UpnpEnabled)
        {
            return new HealthCheckResult(GetType(), HealthCheckResult.HealthType.Ok);
        }

        var peerPort = _configService.ListeningPort;
        var mappings = _upnpService?.GetMappings() ?? new List<PortMapping>();
        var peerMapping = mappings.FirstOrDefault(m => m.InternalPort == peerPort && string.Equals(m.Protocol, "TCP", StringComparison.OrdinalIgnoreCase));

        if (_upnpService == null || !_upnpService.IsAvailable || peerMapping == null || !peerMapping.IsActive)
        {
            var detail = !string.IsNullOrEmpty(peerMapping?.ErrorMessage)
                ? peerMapping.ErrorMessage
                : "No compatible UPnP or NAT-PMP gateway discovered or port redirection was rejected.";

            return new HealthCheckResult(
                GetType(),
                HealthCheckResult.HealthType.Warning,
                $"Port mapping is enabled, but Seedarr failed to map peer listening port {peerPort} on the router gateway: {detail}");
        }

        return new HealthCheckResult(GetType(), HealthCheckResult.HealthType.Ok);
    }
}
