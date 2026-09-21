using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Security;
using Seedarr.Http;

namespace Seedarr.Api.V1.Config;

[V1ApiController("config/general")]
public class GeneralConfigController : ConfigController<GeneralConfigResource>
{
    private static readonly HashSet<string> XmlBoundProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "BindAddress",
        "Port",
        "ApiKey",
        "AuthenticationEnabled",
        "TerminalAccessEnabled",
        "UrlBase",
        "EnableSsl",
        "SslPort",
        "SslCertPath",
        "SslKeyPath",
        "SslCertPassword",
        "RedirectHttpToHttps",
        "TrustedProxies",
        "AllowedOrigins"
    };

    private readonly IConfigFileProvider _configFileProvider;
    private readonly ICertificateManager _certificateManager;

    public GeneralConfigController(
        IConfigService configService,
        IConfigFileProvider configFileProvider,
        ICertificateManager certificateManager)
        : base(configService)
    {
        _configFileProvider = configFileProvider;
        _certificateManager = certificateManager;

        SharedValidator.RuleFor(c => c.WatchFolderScanIntervalSeconds)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.Port)
            .InclusiveBetween(1, 65535)
            .WithMessage("Port must be between 1 and 65535.");

        SharedValidator.RuleFor(c => c.SslPort)
            .InclusiveBetween(1, 65535)
            .WithMessage("SSL Port must be between 1 and 65535.");

        SharedValidator.RuleFor(c => c.SslPort)
            .NotEqual(c => c.Port)
            .WithMessage("Port and SSL Port cannot be the same.");

        SharedValidator.RuleFor(c => c.BindAddress)
            .Must(IsValidBindAddress)
            .WithMessage("Invalid BindAddress. Allowed values are '*', '0.0.0.0', '::', 'localhost', or a valid IP address.");

        SharedValidator.RuleFor(c => c.UrlBase)
            .Must(u => string.IsNullOrEmpty(u) || (u.StartsWith('/') && !u.EndsWith('/')))
            .WithMessage("UrlBase must start with '/' and not end with '/'.");
    }

    protected override GeneralConfigResource ToResource(IConfigService model)
    {
        return GeneralConfigResourceMapper.ToResource(model, _configFileProvider);
    }

    public static bool IsValidBindAddress(string bindAddress)
    {
        if (string.IsNullOrWhiteSpace(bindAddress))
        {
            return true;
        }

        var trimmed = bindAddress.Trim();
        var clean = trimmed.Trim('[', ']');
        if (clean is "*" or "0.0.0.0" or "::" or "localhost")
        {
            return true;
        }

        return IPAddress.TryParse(clean, out _);
    }

    public static string NormalizeUrlBase(string urlBase)
    {
        if (string.IsNullOrWhiteSpace(urlBase))
        {
            return string.Empty;
        }

        var trimmed = urlBase.Trim().Trim('/');
        return string.IsNullOrEmpty(trimmed) ? string.Empty : "/" + trimmed;
    }

    public override ActionResult<GeneralConfigResource> SaveConfig([FromBody] GeneralConfigResource resource)
    {
        if (resource == null)
        {
            return BadRequest("Request body cannot be empty.");
        }

        if (resource.Port < 1 || resource.Port > 65535)
        {
            return BadRequest("Port must be between 1 and 65535.");
        }

        if (resource.SslPort < 1 || resource.SslPort > 65535)
        {
            return BadRequest("SSL Port must be between 1 and 65535.");
        }

        if (resource.Port == resource.SslPort)
        {
            return BadRequest("Port and SSL Port cannot be the same.");
        }

        if (!IsValidBindAddress(resource.BindAddress))
        {
            return BadRequest("Invalid BindAddress. Allowed values are '*', '0.0.0.0', '::', 'localhost', or a valid IP address.");
        }

        resource.UrlBase = NormalizeUrlBase(resource.UrlBase);

        if (SharedValidator != null)
        {
            var validationResult = SharedValidator.Validate(resource);
            if (!validationResult.IsValid)
            {
                return BadRequest(validationResult.Errors);
            }
        }

        // If the masked API key was sent back or empty, preserve the existing value
        if (string.IsNullOrWhiteSpace(resource.ApiKey) ||
            resource.ApiKey == "(unchanged)" ||
            resource.ApiKey == GeneralConfigResourceMapper.GetMaskedApiKey(_configFileProvider.ApiKey))
        {
            resource.ApiKey = _configFileProvider.ApiKey;
        }

        if (resource.SslCertPassword == null ||
            resource.SslCertPassword == "(unchanged)" ||
            resource.SslCertPassword == GeneralConfigResourceMapper.SecretMask)
        {
            resource.SslCertPassword = _configFileProvider.SslCertPassword;
        }

        if (string.IsNullOrWhiteSpace(resource.UiTheme) && !string.IsNullOrWhiteSpace(resource.ThemeStyle))
        {
            resource.UiTheme = resource.ThemeStyle;
        }
        else if (!string.IsNullOrWhiteSpace(resource.UiTheme) && string.IsNullOrWhiteSpace(resource.ThemeStyle))
        {
            resource.ThemeStyle = resource.UiTheme;
        }

        if (string.IsNullOrWhiteSpace(resource.UiAccent) && !string.IsNullOrWhiteSpace(resource.ColorScheme))
        {
            resource.UiAccent = resource.ColorScheme;
        }
        else if (!string.IsNullOrWhiteSpace(resource.UiAccent) && string.IsNullOrWhiteSpace(resource.ColorScheme))
        {
            resource.ColorScheme = resource.UiAccent;
        }

        var xmlValues = new Dictionary<string, object>
        {
            { "BindAddress", resource.BindAddress },
            { "Port", resource.Port },
            { "ApiKey", resource.ApiKey },
            { "AuthenticationEnabled", resource.AuthenticationEnabled },
            { "TerminalAccessEnabled", resource.TerminalAccessEnabled },
            { "UrlBase", resource.UrlBase },
            { "EnableSsl", resource.EnableSsl },
            { "SslPort", resource.SslPort },
            { "SslCertPath", resource.SslCertPath ?? string.Empty },
            { "SslKeyPath", resource.SslKeyPath ?? string.Empty },
            { "SslCertPassword", resource.SslCertPassword ?? string.Empty },
            { "RedirectHttpToHttps", resource.RedirectHttpToHttps },
            { "TrustedProxies", resource.TrustedProxies ?? string.Empty },
            { "AllowedOrigins", resource.AllowedOrigins ?? string.Empty }
        };

        _configFileProvider.SaveConfigDictionary(xmlValues);

        var dbDictionary = resource.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(prop => prop.Name != "Id" && prop.Name != "ResourceName" && !XmlBoundProperties.Contains(prop.Name))
            .ToDictionary(prop => prop.Name, prop => prop.GetValue(resource, null));

        _configService.SaveConfigDictionary(dbDictionary);

        return Accepted(resource);
    }

    [HttpGet("api-key")]
    [Produces("application/json")]
    public ActionResult<ApiKeyResource> GetApiKey()
    {
        return Ok(new ApiKeyResource { ApiKey = _configFileProvider.ApiKey ?? string.Empty });
    }

    [HttpPost("test-ssl")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(SslCertificateValidationResult), 200)]
    public async Task<ActionResult<SslCertificateValidationResult>> TestSsl([FromBody] SslTestRequest request)
    {
        if (request == null)
        {
            return BadRequest();
        }

        var password = request.SslCertPassword;
        if (password == null ||
            password == "(unchanged)" ||
            password == GeneralConfigResourceMapper.SecretMask)
        {
            password = _configFileProvider.SslCertPassword;
        }

        var result = await _certificateManager.ValidateCertificateAsync(
            request.SslCertPath,
            request.SslKeyPath,
            password,
            request.BindAddress,
            request.SslPort,
            testTlsHandshake: true);

        return Ok(result);
    }
}

[V1ApiController("config/seeding")]
public class SeedingConfigController : ConfigController<SeedingConfigResource>
{
    public SeedingConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.MaxUploadSpeedKbps)
            .GreaterThanOrEqualTo(0);

        SharedValidator.RuleFor(c => c.MaxDownloadSpeedKbps)
            .GreaterThanOrEqualTo(0);

        SharedValidator.RuleFor(c => c.AltUploadSpeedKbps)
            .GreaterThanOrEqualTo(0);

        SharedValidator.RuleFor(c => c.AltDownloadSpeedKbps)
            .GreaterThanOrEqualTo(0);

        SharedValidator.RuleFor(c => c.GlobalSeedRatioLimit)
            .GreaterThanOrEqualTo(0);

        SharedValidator.RuleFor(c => c.UploadDistributionSpreadPercentage)
            .InclusiveBetween(0, 100);

        SharedValidator.RuleFor(c => c.DownloadDistributionSpreadPercentage)
            .InclusiveBetween(0, 100);

        SharedValidator.RuleFor(c => c.UploadStoppedMinPercentage)
            .InclusiveBetween(0, 100);

        SharedValidator.RuleFor(c => c.UploadStoppedMaxPercentage)
            .InclusiveBetween(0, 100)
            .GreaterThanOrEqualTo(c => c.UploadStoppedMinPercentage)
            .WithMessage("UploadStoppedMaxPercentage must be greater than or equal to UploadStoppedMinPercentage.");

        SharedValidator.RuleFor(c => c.DownloadStoppedMinPercentage)
            .InclusiveBetween(0, 100);

        SharedValidator.RuleFor(c => c.DownloadStoppedMaxPercentage)
            .InclusiveBetween(0, 100)
            .GreaterThanOrEqualTo(c => c.DownloadStoppedMinPercentage)
            .WithMessage("DownloadStoppedMaxPercentage must be greater than or equal to DownloadStoppedMinPercentage.");

        SharedValidator.RuleFor(c => c.UploadCustomIntervalMinutes)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.DownloadCustomIntervalMinutes)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.SpeedVariationMin)
            .InclusiveBetween(0.0, 1.0);

        SharedValidator.RuleFor(c => c.SpeedVariationMax)
            .InclusiveBetween(0.0, 1.0)
            .GreaterThanOrEqualTo(c => c.SpeedVariationMin)
            .WithMessage("SpeedVariationMax must be greater than or equal to SpeedVariationMin.");

        SharedValidator.RuleFor(c => c.SeedGoalReachedAction)
            .Must(action => string.IsNullOrWhiteSpace(action) || AllowedSeedGoalReachedActions.Contains(action))
            .WithMessage("SeedGoalReachedAction must be one of: Stop, Pause, RemoveTorrent, RemoveTorrentAndData.");

        SharedValidator.RuleFor(c => c.MaxActiveDownloads)
            .GreaterThanOrEqualTo(0);

        SharedValidator.RuleFor(c => c.MaxActiveSeeds)
            .GreaterThanOrEqualTo(0);

        SharedValidator.RuleFor(c => c.MaxActiveTorrents)
            .GreaterThanOrEqualTo(0);

        SharedValidator.RuleFor(c => c.SlowTorrentThresholdKbps)
            .GreaterThanOrEqualTo(0);
    }

    private static readonly HashSet<string> AllowedSeedGoalReachedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Stop", "Pause", "RemoveTorrent", "RemoveTorrentAndData"
    };

    protected override SeedingConfigResource ToResource(IConfigService model)
    {
        return SeedingConfigResourceMapper.ToResource(model);
    }
}

[V1ApiController("config/network")]
public class NetworkConfigController : ConfigController<NetworkConfigResource>
{
    private readonly IConfigFileProvider _configFileProvider;
    private readonly IProxyTestService _proxyTestService;

    public NetworkConfigController(
        IConfigService configService,
        IConfigFileProvider configFileProvider = null,
        IProxyTestService proxyTestService = null)
        : base(configService)
    {
        _configFileProvider = configFileProvider;
        _proxyTestService = proxyTestService ?? new ProxyTestService(configService);

        SharedValidator.RuleFor(c => c.ListeningPort)
            .InclusiveBetween(1024, 65535)
            .WithMessage("Listening port must be a non-privileged port between 1024 and 65535.");

        SharedValidator.RuleFor(c => c.ListeningPort)
            .Must((resource, port) =>
            {
                if (_configFileProvider == null)
                {
                    return true;
                }

                if (_configFileProvider.Port > 0 && port == _configFileProvider.Port)
                {
                    return false;
                }

                if (_configFileProvider.SslPort > 0 && port == _configFileProvider.SslPort)
                {
                    return false;
                }

                return true;
            })
            .WithMessage("Listening port cannot conflict with web server Port or SslPort.");

        SharedValidator.RuleFor(c => c.MaxGlobalConnections)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.MaxPerTorrentConnections)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.MaxPerTorrentConnections)
            .LessThanOrEqualTo(c => c.MaxGlobalConnections)
            .WithMessage("Max Connections Per Torrent cannot exceed Maximum Global Connections.");

        SharedValidator.RuleFor(c => c.MaxUploadSlots)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.MaxConnectionsPerIp)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.MaximumHalfOpenConnections)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.PeerDscp)
            .InclusiveBetween(0, 63);

        SharedValidator.RuleFor(c => c.PeerTos)
            .InclusiveBetween(0, 255);

        SharedValidator.RuleFor(c => c.ProxyPort)
            .InclusiveBetween(1, 65535);

        SharedValidator.RuleFor(c => c.VpnStabilizationDelaySeconds)
            .GreaterThanOrEqualTo(0);
    }

    protected override NetworkConfigResource ToResource(IConfigService model)
    {
        return NetworkConfigResourceMapper.ToResource(model);
    }

    public override ActionResult<NetworkConfigResource> SaveConfig([FromBody] NetworkConfigResource resource)
    {
        if (resource == null)
        {
            return BadRequest("Request body cannot be empty.");
        }

        if (resource.ListeningPort < 1024 || resource.ListeningPort > 65535)
        {
            return BadRequest("Listening port must be a non-privileged port between 1024 and 65535.");
        }

        if (_configFileProvider != null)
        {
            if (_configFileProvider.Port > 0 && resource.ListeningPort == _configFileProvider.Port)
            {
                return BadRequest("Listening port cannot conflict with web server Port.");
            }

            if (_configFileProvider.SslPort > 0 && resource.ListeningPort == _configFileProvider.SslPort)
            {
                return BadRequest("Listening port cannot conflict with web server SslPort.");
            }
        }

        // If the masked proxy password was sent back, preserve the existing value
        if (resource.ProxyPassword == null ||
            resource.ProxyPassword == "(unchanged)" ||
            resource.ProxyPassword == NetworkConfigResourceMapper.SecretMask)
        {
            resource.ProxyPassword = _configService.ProxyPassword;
        }

        return base.SaveConfig(resource);
    }

    [HttpPost("test-proxy")]
    [HttpPost("/api/v1/config/proxy/test")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ProxyTestResult), 200)]
    public async Task<ActionResult<ProxyTestResult>> TestProxy([FromBody] ProxyTestRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return BadRequest(new ProxyTestResult
            {
                Success = false,
                Message = "Request body cannot be empty."
            });
        }

        var result = await _proxyTestService.TestProxyAsync(request, cancellationToken);
        return Ok(result);
    }
}

[V1ApiController("config/bittorrent")]
public class BitTorrentConfigController : ConfigController<BitTorrentConfigResource>
{
    public BitTorrentConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.AnnounceIntervalSeconds)
            .GreaterThanOrEqualTo(60);

        SharedValidator.RuleFor(c => c.MinAnnounceIntervalSeconds)
            .GreaterThanOrEqualTo(30);

        SharedValidator.RuleFor(c => c.MinAnnounceIntervalSeconds)
            .LessThanOrEqualTo(c => c.AnnounceIntervalSeconds)
            .WithMessage("Min Announce Interval cannot be greater than Announce Interval.");

        SharedValidator.RuleFor(c => c.ScrapeIntervalSeconds)
            .GreaterThanOrEqualTo(60);
        SharedValidator.RuleFor(c => c.EncryptionMode)
            .Must(m => !string.IsNullOrEmpty(m) && m.ToLowerInvariant() is "disabled" or "enabled" or "required" or "forced")
            .WithMessage("EncryptionMode must be one of: disabled, enabled, required, forced.");

        SharedValidator.RuleFor(c => c.CustomScriptTimeoutSeconds)
            .InclusiveBetween(5, 3600)
            .WithMessage("CustomScriptTimeoutSeconds must be between 5 and 3600 seconds.");

        SharedValidator.RuleFor(c => c.PeerIdPrefix)
            .MaximumLength(12)
            .WithMessage("PeerIdPrefix cannot exceed 12 characters.");

        SharedValidator.RuleFor(c => c.OnDownloadCompleteScript)
            .Must(IsValidScriptPath)
            .WithMessage("OnDownloadCompleteScript must be a valid absolute path.");

        SharedValidator.RuleFor(c => c.OnSeedGoalReachedScript)
            .Must(IsValidScriptPath)
            .WithMessage("OnSeedGoalReachedScript must be a valid absolute path.");

        SharedValidator.RuleFor(c => c.ScriptTorrentDoneFilename)
            .Must(IsValidScriptPath)
            .WithMessage("ScriptTorrentDoneFilename must be a valid absolute path.");

        SharedValidator.RuleFor(c => c.ScriptTorrentAddedFilename)
            .Must(IsValidScriptPath)
            .WithMessage("ScriptTorrentAddedFilename must be a valid absolute path.");

        SharedValidator.RuleFor(c => c.ScriptTorrentDoneSeedingFilename)
            .Must(IsValidScriptPath)
            .WithMessage("ScriptTorrentDoneSeedingFilename must be a valid absolute path.");
    }

    private static bool IsValidScriptPath(string scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            return true;
        }

        var (path, _) = CustomScriptService.ParseSettings(scriptPath);
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0)
        {
            return false;
        }

        try
        {
            if (Path.IsPathRooted(path))
            {
                return true;
            }

            if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            {
                return true;
            }

            if (path.StartsWith(@"\\") || path.StartsWith("//"))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    protected override BitTorrentConfigResource ToResource(IConfigService model)
    {
        return BitTorrentConfigResourceMapper.ToResource(model);
    }
}

[V1ApiController("config/peerprotocol")]
public class PeerProtocolConfigController : ConfigController<PeerProtocolConfigResource>
{
    public PeerProtocolConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.HandshakeTimeoutSeconds)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.MessageReadTimeoutSeconds)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.KeepAliveIntervalSeconds)
            .GreaterThanOrEqualTo(30);

        SharedValidator.RuleFor(c => c.PeerRequestCount)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.SeederUploadActivityProbability)
            .InclusiveBetween(0.0, 1.0);

        SharedValidator.RuleFor(c => c.PeerIdleChance)
            .InclusiveBetween(0.0, 1.0);

        SharedValidator.RuleFor(c => c.PeerDropoutProbability)
            .InclusiveBetween(0.0, 1.0);

        SharedValidator.RuleFor(c => c.ConnectionRotationPercentage)
            .InclusiveBetween(0.0, 1.0);
    }

    protected override PeerProtocolConfigResource ToResource(IConfigService model)
    {
        return PeerProtocolConfigResourceMapper.ToResource(model);
    }
}

[V1ApiController("config/protocols")]
public class ProtocolsConfigController : ConfigController<ProtocolsConfigResource>
{
    public ProtocolsConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.TransportConnectionTimeoutSeconds)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.PexInterval)
            .GreaterThanOrEqualTo(10);

        SharedValidator.RuleFor(c => c.PexMaxPeersPerMessage)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.FailoverMaxConsecutiveFailures)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.DhtBucketSize)
            .GreaterThanOrEqualTo(1);

        SharedValidator.RuleFor(c => c.DhtMaxQueriesPerSecond)
            .GreaterThanOrEqualTo(1);
    }

    protected override ProtocolsConfigResource ToResource(IConfigService model)
    {
        return ProtocolsConfigResourceMapper.ToResource(model);
    }
}

[V1ApiController("config/simulation")]
public class SimulationConfigController : ConfigController<SimulationConfigResource>
{
    private static readonly string[] AllowedTrafficProfiles = { "conservative", "balanced", "aggressive", "off" };
    private static readonly string[] AllowedPrimaryClients = { "qbittorrent", "deluge", "transmission", "utorrent", "biglybt" };

    public SimulationConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.BehaviorVariation)
            .InclusiveBetween(0.0, 1.0);

        SharedValidator.RuleFor(c => c.SwitchClientProbability)
            .InclusiveBetween(0.0, 1.0);

        SharedValidator.RuleFor(c => c.SwarmAdaptationRate)
            .InclusiveBetween(0.0, 1.0);

        SharedValidator.RuleFor(c => c.SwarmPeerAnalysisDepth)
            .InclusiveBetween(1, 500);

        SharedValidator.RuleFor(c => c.TrafficPatternProfile)
            .Must(p => string.IsNullOrWhiteSpace(p) || AllowedTrafficProfiles.Contains(p.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("TrafficPatternProfile must be one of: conservative, balanced, aggressive, off.");

        SharedValidator.RuleFor(c => c.PrimaryClient)
            .Must(c => string.IsNullOrWhiteSpace(c) || AllowedPrimaryClients.Contains(c.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("PrimaryClient must be one of: qbittorrent, deluge, transmission, utorrent, biglybt.");
    }

    protected override SimulationConfigResource ToResource(IConfigService model)
    {
        return SimulationConfigResourceMapper.ToResource(model);
    }
}

[V1ApiController("config/trackerserver")]
public class TrackerServerConfigController : ConfigController<TrackerServerConfigResource>
{
    public TrackerServerConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.TrackerHttpPort)
            .InclusiveBetween(1, 65535);

        SharedValidator.RuleFor(c => c.TrackerUdpPort)
            .InclusiveBetween(1, 65535);

        SharedValidator.RuleFor(c => c.TrackerAnnounceInterval)
            .InclusiveBetween(60, 86400);

        SharedValidator.RuleFor(c => c.TrackerMaxPeersPerAnnounce)
            .InclusiveBetween(1, 200);

        SharedValidator.RuleFor(c => c.TrackerRateLimitPerMinute)
            .GreaterThanOrEqualTo(1);
    }

    protected override TrackerServerConfigResource ToResource(IConfigService model)
    {
        return TrackerServerConfigResourceMapper.ToResource(model);
    }
}

[V1ApiController("config/scheduler")]
public class SchedulerConfigController : ConfigController<SchedulerConfigResource>
{
    public SchedulerConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.SchedulerStartHour)
            .InclusiveBetween(0, 23);

        SharedValidator.RuleFor(c => c.SchedulerStartMinute)
            .InclusiveBetween(0, 59);

        SharedValidator.RuleFor(c => c.SchedulerEndHour)
            .InclusiveBetween(0, 23);

        SharedValidator.RuleFor(c => c.SchedulerEndMinute)
            .InclusiveBetween(0, 59);

        SharedValidator.RuleFor(c => c.TimeZone)
            .Must(IsValidTimeZone)
            .WithMessage("Invalid TimeZone identifier.");
    }

    private static bool IsValidTimeZone(string timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
        {
            return true;
        }

        try
        {
            return TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _);
        }
        catch
        {
            return false;
        }
    }

    protected override SchedulerConfigResource ToResource(IConfigService model)
    {
        return SchedulerConfigResourceMapper.ToResource(model);
    }
}

[V1ApiController("config/advanced")]
public class AdvancedConfigController : ConfigController<AdvancedConfigResource>
{
    public AdvancedConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.UiRefreshRateSec)
            .GreaterThanOrEqualTo(1);
    }

    protected override AdvancedConfigResource ToResource(IConfigService model)
    {
        return AdvancedConfigResourceMapper.ToResource(model);
    }
}
