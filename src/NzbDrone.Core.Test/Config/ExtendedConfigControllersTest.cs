using System.Collections.Generic;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Api.V1.Config;

namespace NzbDrone.Core.Test.Config;

[TestFixture]
public class ExtendedConfigControllersTest
{
    private IConfigService _configService;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
    }

    [Test]
    public void PeerProtocol_GetConfig_sets_resource_id()
    {
        var controller = new PeerProtocolConfigController(_configService);
        var resource = controller.GetConfig();

        Assert.That(resource.Id, Is.EqualTo(1));
    }

    [Test]
    public void PeerProtocol_GetConfigById_returns_not_found_for_invalid_id()
    {
        var controller = new PeerProtocolConfigController(_configService);
        var result = controller.GetConfigById(2);

        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void PeerProtocol_SaveConfig_rejects_invalid_handshake_timeout()
    {
        var controller = new PeerProtocolConfigController(_configService);
        var resource = ValidPeerProtocolResource();
        resource.HandshakeTimeoutSeconds = 0;

        var result = controller.SaveConfig(null, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void PeerProtocol_SaveConfig_success_persists_config()
    {
        var controller = new PeerProtocolConfigController(_configService);
        var resource = ValidPeerProtocolResource();

        var result = controller.SaveConfig(null, resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        _configService.Received(1).SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public void Protocols_SaveConfig_rejects_invalid_pex_interval()
    {
        var controller = new ProtocolsConfigController(_configService);
        var resource = ValidProtocolsResource();
        resource.PexInterval = 5;

        var result = controller.SaveConfig(null, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var errors = (IList<ValidationFailure>)((BadRequestObjectResult)result.Result!).Value!;
        Assert.That(errors, Has.Some.Matches<ValidationFailure>(e => e.PropertyName == nameof(ProtocolsConfigResource.PexInterval)));
    }

    [Test]
    public void Protocols_SaveConfig_success()
    {
        var controller = new ProtocolsConfigController(_configService);
        var result = controller.SaveConfig(null, ValidProtocolsResource());

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
    }

    [TestCase("not-a-profile")]
    [TestCase("turbo")]
    public void Simulation_SaveConfig_rejects_invalid_traffic_profile(string profile)
    {
        var controller = new SimulationConfigController(_configService);
        var resource = ValidSimulationResource();
        resource.TrafficPatternProfile = profile;

        var result = controller.SaveConfig(null, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase("deluge")]
    [TestCase("QBITTORRENT")]
    public void Simulation_SaveConfig_accepts_valid_primary_client(string client)
    {
        var controller = new SimulationConfigController(_configService);
        var resource = ValidSimulationResource();
        resource.PrimaryClient = client;

        var result = controller.SaveConfig(null, resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
    }

    [Test]
    public void TrackerServer_SaveConfig_rejects_announce_interval_below_minimum()
    {
        var controller = new TrackerServerConfigController(_configService);
        var resource = new TrackerServerConfigResource { TrackerAnnounceInterval = 30 };

        var result = controller.SaveConfig(null, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void TrackerServer_SaveConfig_success_with_defaults()
    {
        var controller = new TrackerServerConfigController(_configService);
        var resource = new TrackerServerConfigResource();

        var result = controller.SaveConfig(null, resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        _configService.Received(1).SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public void ConfigControllers_SaveConfig_rejects_null_body()
    {
        var controller = new TrackerServerConfigController(_configService);
        var result = controller.SaveConfig(null, null!);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void BitTorrent_SaveConfig_rejects_relative_script_path()
    {
        var controller = new BitTorrentConfigController(_configService);
        var resource = ValidBitTorrentResource();
        resource.OnDownloadCompleteScript = "scripts/on-complete.sh";

        var result = controller.SaveConfig(null, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void BitTorrent_SaveConfig_accepts_absolute_script_path()
    {
        var controller = new BitTorrentConfigController(_configService);
        var resource = ValidBitTorrentResource();
        resource.OnDownloadCompleteScript = "/usr/local/bin/on-complete.sh";

        var result = controller.SaveConfig(null, resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
    }

    [Test]
    public void Network_SaveConfig_preserves_masked_proxy_password()
    {
        _configService.ProxyPassword.Returns("stored-proxy-secret");
        var controller = new NetworkConfigController(_configService);
        var resource = ValidNetworkResource();
        resource.ProxyPassword = NetworkConfigResourceMapper.SecretMask;

        var result = controller.SaveConfig(null, resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        _configService.Received().SaveConfigDictionary(
            Arg.Is<Dictionary<string, object>>(d => (string)d[nameof(NetworkConfigResource.ProxyPassword)] == "stored-proxy-secret"));
    }

    private static PeerProtocolConfigResource ValidPeerProtocolResource()
    {
        return new PeerProtocolConfigResource
        {
            HandshakeTimeoutSeconds = 10,
            MessageReadTimeoutSeconds = 10,
            KeepAliveIntervalSeconds = 60,
            PeerContactIntervalSeconds = 5,
            UdpTrackerTimeoutSeconds = 5,
            HttpTrackerTimeoutSeconds = 5,
            PeerRequestCount = 5,
            SeederUploadActivityProbability = 0.5,
            PeerIdleChance = 0.1,
            PeerDropoutProbability = 0.1,
            ConnectionRotationPercentage = 0.2,
        };
    }

    private static ProtocolsConfigResource ValidProtocolsResource()
    {
        return new ProtocolsConfigResource
        {
            TransportConnectionTimeoutSeconds = 10,
            PexInterval = 60,
            PexMaxPeersPerMessage = 10,
            FailoverMaxConsecutiveFailures = 3,
            DhtBucketSize = 8,
            DhtMaxQueriesPerSecond = 10,
        };
    }

    private static SimulationConfigResource ValidSimulationResource()
    {
        return new SimulationConfigResource
        {
            BehaviorVariation = 0.5,
            SwitchClientProbability = 0.1,
            SwarmAdaptationRate = 0.2,
            SwarmPeerAnalysisDepth = 10,
            PrimaryClient = "qbittorrent",
            TrafficPatternProfile = "balanced",
        };
    }

    private static BitTorrentConfigResource ValidBitTorrentResource()
    {
        return new BitTorrentConfigResource
        {
            EncryptionMode = "enabled",
            AnnounceIntervalSeconds = 1800,
            MinAnnounceIntervalSeconds = 60,
            ScrapeIntervalSeconds = 900,
            CustomScriptTimeoutSeconds = 60,
        };
    }

    private static NetworkConfigResource ValidNetworkResource()
    {
        return new NetworkConfigResource
        {
            ListeningPort = 6881,
            MaxGlobalConnections = 200,
            MaxPerTorrentConnections = 50,
            MaxUploadSlots = 4,
            MaxConnectionsPerIp = 5,
            MaximumHalfOpenConnections = 50,
            PeerDscp = 0,
            PeerTos = 0,
            ProxyPort = 0,
        };
    }
}
