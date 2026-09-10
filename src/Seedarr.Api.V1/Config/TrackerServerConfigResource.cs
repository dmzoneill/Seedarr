using NzbDrone.Core.Configuration;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Config;

public class TrackerServerConfigResource : RestResource
{
    public bool TrackerServerEnabled { get; set; }
    public bool TrackerHttpEnabled { get; set; } = true;
    public int TrackerHttpPort { get; set; } = 9696;
    public bool TrackerUdpEnabled { get; set; } = true;
    public int TrackerUdpPort { get; set; } = 9696;
    public string TrackerBindAddress { get; set; } = "0.0.0.0";
    public int TrackerAnnounceInterval { get; set; } = 1800;
    public int TrackerMaxPeersPerAnnounce { get; set; } = 50;
    public bool TrackerEnableScrape { get; set; } = true;
    public bool TrackerPrivateMode { get; set; }
    public bool TrackerLogAnnounces { get; set; }
    public int TrackerRateLimitPerMinute { get; set; } = 60;
}

public static class TrackerServerConfigResourceMapper
{
    public static TrackerServerConfigResource ToResource(IConfigService model)
    {
        return new TrackerServerConfigResource
        {
            TrackerServerEnabled = model.TrackerServerEnabled,
            TrackerHttpEnabled = model.TrackerHttpEnabled,
            TrackerHttpPort = model.TrackerHttpPort,
            TrackerUdpEnabled = model.TrackerUdpEnabled,
            TrackerUdpPort = model.TrackerUdpPort,
            TrackerBindAddress = model.TrackerBindAddress,
            TrackerAnnounceInterval = model.TrackerAnnounceInterval,
            TrackerMaxPeersPerAnnounce = model.TrackerMaxPeersPerAnnounce,
            TrackerEnableScrape = model.TrackerEnableScrape,
            TrackerPrivateMode = model.TrackerPrivateMode,
            TrackerLogAnnounces = model.TrackerLogAnnounces,
            TrackerRateLimitPerMinute = model.TrackerRateLimitPerMinute,
        };
    }
}
