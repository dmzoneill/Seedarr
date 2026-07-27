using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using Seedarr.Http;

namespace Seedarr.Api.V1.Config;

[V1ApiController("config/general")]
public class GeneralConfigController : Controller
{
    private readonly IConfigService _configService;
    private readonly IConfigFileProvider _configFileProvider;

    public GeneralConfigController(IConfigService configService, IConfigFileProvider configFileProvider)
    {
        _configService = configService;
        _configFileProvider = configFileProvider;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            autoStart = _configService.AutoStart,
            themeStyle = _configService.ThemeStyle,
            colorScheme = _configService.ColorScheme,
            watchFolderEnabled = _configService.WatchFolderEnabled,
            watchFolderPath = _configService.WatchFolderPath,
            watchFolderScanIntervalSeconds = _configService.WatchFolderScanIntervalSeconds,
            watchFolderAutoStartTorrents = _configService.WatchFolderAutoStartTorrents,
            watchFolderDeleteAddedTorrents = _configService.WatchFolderDeleteAddedTorrents,
            port = _configFileProvider.Port,
            bindAddress = _configFileProvider.BindAddress,
            urlBase = _configFileProvider.UrlBase,
            authenticationEnabled = _configFileProvider.AuthenticationEnabled,
            apiKey = _configFileProvider.ApiKey
        });
    }

    [HttpPut]
    public IActionResult Save([FromBody] Dictionary<string, object> config)
    {
        _configService.SaveConfigDictionary(config);
        return Get();
    }
}

[V1ApiController("config/seeding")]
public class SeedingConfigController : Controller
{
    private readonly IConfigService _configService;

    public SeedingConfigController(IConfigService configService)
    {
        _configService = configService;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            maxUploadSpeedKbps = _configService.MaxUploadSpeedKbps,
            maxDownloadSpeedKbps = _configService.MaxDownloadSpeedKbps,
            alternativeSpeedEnabled = _configService.AlternativeSpeedEnabled,
            altUploadSpeedKbps = _configService.AltUploadSpeedKbps,
            altDownloadSpeedKbps = _configService.AltDownloadSpeedKbps,
            globalSeedRatioLimit = _configService.GlobalSeedRatioLimit,
            uploadDistributionAlgorithm = _configService.UploadDistributionAlgorithm,
            uploadDistributionSpreadPercentage = _configService.UploadDistributionSpreadPercentage,
            uploadRedistributionMode = _configService.UploadRedistributionMode,
            uploadCustomIntervalMinutes = _configService.UploadCustomIntervalMinutes,
            uploadStoppedMinPercentage = _configService.UploadStoppedMinPercentage,
            uploadStoppedMaxPercentage = _configService.UploadStoppedMaxPercentage,
            downloadDistributionAlgorithm = _configService.DownloadDistributionAlgorithm,
            downloadDistributionSpreadPercentage = _configService.DownloadDistributionSpreadPercentage,
            downloadRedistributionMode = _configService.DownloadRedistributionMode,
            downloadCustomIntervalMinutes = _configService.DownloadCustomIntervalMinutes,
            downloadStoppedMinPercentage = _configService.DownloadStoppedMinPercentage,
            downloadStoppedMaxPercentage = _configService.DownloadStoppedMaxPercentage,
            speedVariationMin = _configService.SpeedVariationMin,
            speedVariationMax = _configService.SpeedVariationMax
        });
    }

    [HttpPut]
    public IActionResult Save([FromBody] Dictionary<string, object> config)
    {
        _configService.SaveConfigDictionary(config);
        return Get();
    }
}

[V1ApiController("config/network")]
public class NetworkConfigController : Controller
{
    private readonly IConfigService _configService;

    public NetworkConfigController(IConfigService configService)
    {
        _configService = configService;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            listeningPort = _configService.ListeningPort,
            upnpEnabled = _configService.UpnpEnabled,
            maxGlobalConnections = _configService.MaxGlobalConnections,
            maxPerTorrentConnections = _configService.MaxPerTorrentConnections,
            maxUploadSlots = _configService.MaxUploadSlots,
            proxyType = _configService.ProxyType,
            proxyHost = _configService.ProxyHost,
            proxyPort = _configService.ProxyPort,
            proxyAuthEnabled = _configService.ProxyAuthEnabled,
            proxyUsername = _configService.ProxyUsername,
            proxyPassword = _configService.ProxyPassword
        });
    }

    [HttpPut]
    public IActionResult Save([FromBody] Dictionary<string, object> config)
    {
        _configService.SaveConfigDictionary(config);
        return Get();
    }
}

[V1ApiController("config/bittorrent")]
public class BitTorrentConfigController : Controller
{
    private readonly IConfigService _configService;

    public BitTorrentConfigController(IConfigService configService)
    {
        _configService = configService;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            enableDht = _configService.EnableDht,
            enablePex = _configService.EnablePex,
            enableLpd = _configService.EnableLpd,
            encryptionMode = _configService.EncryptionMode,
            bitTorrentUserAgent = _configService.BitTorrentUserAgent,
            peerIdPrefix = _configService.PeerIdPrefix,
            announceIntervalSeconds = _configService.AnnounceIntervalSeconds,
            minAnnounceIntervalSeconds = _configService.MinAnnounceIntervalSeconds,
            scrapeIntervalSeconds = _configService.ScrapeIntervalSeconds
        });
    }

    [HttpPut]
    public IActionResult Save([FromBody] Dictionary<string, object> config)
    {
        _configService.SaveConfigDictionary(config);
        return Get();
    }
}

[V1ApiController("config/peerprotocol")]
public class PeerProtocolConfigController : Controller
{
    private readonly IConfigService _configService;

    public PeerProtocolConfigController(IConfigService configService)
    {
        _configService = configService;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            handshakeTimeoutSeconds = _configService.HandshakeTimeoutSeconds,
            messageReadTimeoutSeconds = _configService.MessageReadTimeoutSeconds,
            keepAliveIntervalSeconds = _configService.KeepAliveIntervalSeconds,
            peerContactIntervalSeconds = _configService.PeerContactIntervalSeconds,
            udpTrackerTimeoutSeconds = _configService.UdpTrackerTimeoutSeconds,
            httpTrackerTimeoutSeconds = _configService.HttpTrackerTimeoutSeconds,
            peerRequestCount = _configService.PeerRequestCount,
            seederUploadActivityProbability = _configService.SeederUploadActivityProbability,
            peerIdleChance = _configService.PeerIdleChance,
            peerDropoutProbability = _configService.PeerDropoutProbability,
            connectionRotationPercentage = _configService.ConnectionRotationPercentage
        });
    }

    [HttpPut]
    public IActionResult Save([FromBody] Dictionary<string, object> config)
    {
        _configService.SaveConfigDictionary(config);
        return Get();
    }
}

[V1ApiController("config/protocols")]
public class ProtocolsConfigController : Controller
{
    private readonly IConfigService _configService;

    public ProtocolsConfigController(IConfigService configService)
    {
        _configService = configService;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            extensionUtMetadata = _configService.ExtensionUtMetadata,
            extensionUtPex = _configService.ExtensionUtPex,
            extensionLtDontHave = _configService.ExtensionLtDontHave,
            extensionFastExtension = _configService.ExtensionFastExtension,
            utpEnabled = _configService.UtpEnabled,
            tcpFallback = _configService.TcpFallback,
            transportConnectionTimeoutSeconds = _configService.TransportConnectionTimeoutSeconds,
            pexInterval = _configService.PexInterval,
            pexMaxPeersPerMessage = _configService.PexMaxPeersPerMessage,
            multiTrackerEnabled = _configService.MultiTrackerEnabled,
            multiTrackerFailoverEnabled = _configService.MultiTrackerFailoverEnabled,
            announceToAllTiers = _configService.AnnounceToAllTiers,
            announceToAllInTier = _configService.AnnounceToAllInTier,
            failoverMaxConsecutiveFailures = _configService.FailoverMaxConsecutiveFailures,
            failoverBackoffBaseSeconds = _configService.FailoverBackoffBaseSeconds,
            failoverMaxBackoffSeconds = _configService.FailoverMaxBackoffSeconds,
            dhtRoutingTableSize = _configService.DhtRoutingTableSize,
            dhtAnnouncementInterval = _configService.DhtAnnouncementInterval,
            dhtBootstrapTimeout = _configService.DhtBootstrapTimeout,
            dhtQueryTimeout = _configService.DhtQueryTimeout,
            dhtMaxNodes = _configService.DhtMaxNodes,
            dhtBucketSize = _configService.DhtBucketSize,
            dhtConcurrentQueries = _configService.DhtConcurrentQueries,
            dhtAutoBootstrap = _configService.DhtAutoBootstrap,
            dhtRateLimitEnabled = _configService.DhtRateLimitEnabled,
            dhtMaxQueriesPerSecond = _configService.DhtMaxQueriesPerSecond
        });
    }

    [HttpPut]
    public IActionResult Save([FromBody] Dictionary<string, object> config)
    {
        _configService.SaveConfigDictionary(config);
        return Get();
    }
}

[V1ApiController("config/simulation")]
public class SimulationConfigController : Controller
{
    private readonly IConfigService _configService;

    public SimulationConfigController(IConfigService configService)
    {
        _configService = configService;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            clientBehaviorEngineEnabled = _configService.ClientBehaviorEngineEnabled,
            primaryClient = _configService.PrimaryClient,
            behaviorVariation = _configService.BehaviorVariation,
            clientProfileSwitching = _configService.ClientProfileSwitching,
            switchClientProbability = _configService.SwitchClientProbability,
            trafficPatternProfile = _configService.TrafficPatternProfile,
            realisticVariations = _configService.RealisticVariations,
            timeBasedPatterns = _configService.TimeBasedPatterns,
            swarmIntelligenceEnabled = _configService.SwarmIntelligenceEnabled,
            swarmAdaptationRate = _configService.SwarmAdaptationRate,
            swarmPeerAnalysisDepth = _configService.SwarmPeerAnalysisDepth
        });
    }

    [HttpPut]
    public IActionResult Save([FromBody] Dictionary<string, object> config)
    {
        _configService.SaveConfigDictionary(config);
        return Get();
    }
}

[V1ApiController("config/trackerserver")]
public class TrackerServerConfigController : Controller
{
    private readonly IConfigService _configService;

    public TrackerServerConfigController(IConfigService configService)
    {
        _configService = configService;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            trackerServerEnabled = _configService.TrackerServerEnabled,
            trackerHttpEnabled = _configService.TrackerHttpEnabled,
            trackerHttpPort = _configService.TrackerHttpPort,
            trackerUdpEnabled = _configService.TrackerUdpEnabled,
            trackerUdpPort = _configService.TrackerUdpPort,
            trackerBindAddress = _configService.TrackerBindAddress,
            trackerAnnounceInterval = _configService.TrackerAnnounceInterval,
            trackerMaxPeersPerAnnounce = _configService.TrackerMaxPeersPerAnnounce,
            trackerEnableScrape = _configService.TrackerEnableScrape,
            trackerPrivateMode = _configService.TrackerPrivateMode,
            trackerLogAnnounces = _configService.TrackerLogAnnounces,
            trackerRateLimitPerMinute = _configService.TrackerRateLimitPerMinute
        });
    }

    [HttpPut]
    public IActionResult Save([FromBody] Dictionary<string, object> config)
    {
        _configService.SaveConfigDictionary(config);
        return Get();
    }
}

[V1ApiController("config/scheduler")]
public class SchedulerConfigController : Controller
{
    private readonly IConfigService _configService;

    public SchedulerConfigController(IConfigService configService)
    {
        _configService = configService;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            schedulerEnabled = _configService.SchedulerEnabled,
            schedulerStartHour = _configService.SchedulerStartHour,
            schedulerStartMinute = _configService.SchedulerStartMinute,
            schedulerEndHour = _configService.SchedulerEndHour,
            schedulerEndMinute = _configService.SchedulerEndMinute,
            schedulerMonday = _configService.SchedulerMonday,
            schedulerTuesday = _configService.SchedulerTuesday,
            schedulerWednesday = _configService.SchedulerWednesday,
            schedulerThursday = _configService.SchedulerThursday,
            schedulerFriday = _configService.SchedulerFriday,
            schedulerSaturday = _configService.SchedulerSaturday,
            schedulerSunday = _configService.SchedulerSunday
        });
    }

    [HttpPut]
    public IActionResult Save([FromBody] Dictionary<string, object> config)
    {
        _configService.SaveConfigDictionary(config);
        return Get();
    }
}

[V1ApiController("config/advanced")]
public class AdvancedConfigController : Controller
{
    private readonly IConfigService _configService;

    public AdvancedConfigController(IConfigService configService)
    {
        _configService = configService;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            logToFile = _configService.LogToFile,
            fileLogLevel = _configService.FileLogLevel,
            debugMode = _configService.DebugMode,
            uiRefreshRateSec = _configService.UiRefreshRateSec
        });
    }

    [HttpPut]
    public IActionResult Save([FromBody] Dictionary<string, object> config)
    {
        _configService.SaveConfigDictionary(config);
        return Get();
    }
}
