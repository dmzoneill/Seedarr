using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Network;

public class PortMappingStatus
{
    public string Protocol { get; set; } = "None";
    public string GatewayIp { get; set; } = string.Empty;
    public string ExternalIp { get; set; } = string.Empty;
    public string RouterModel { get; set; } = string.Empty;
    public List<EnrichedPortMapping> Mappings { get; set; } = new();
    public DateTime? LastRenewalUtc { get; set; }
    public DateTime? NextRenewalUtc { get; set; }
}

public class EnrichedPortMapping
{
    public int InternalPort { get; set; }
    public int ExternalPort { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int LeaseSeconds { get; set; }
    public DateTime? ExpiryUtc { get; set; }
    public bool IsActive { get; set; }
    public string Status { get; set; } = "Inactive";
    public string ErrorMessage { get; set; }
}
