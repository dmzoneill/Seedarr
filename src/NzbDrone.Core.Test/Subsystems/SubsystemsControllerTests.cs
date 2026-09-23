using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Telemetry;
using NzbDrone.SignalR;
using Seedarr.Api.V1.Subsystems;

namespace NzbDrone.Core.Test.Subsystems;

[TestFixture]
public class SubsystemsControllerTests
{
    private ISystemResourceService _resourceService;
    private IConfigService _configService;
    private IBroadcastSignalRMessage _signalRBroadcaster;
    private SubsystemsController _controller;

    [SetUp]
    public void SetUp()
    {
        _resourceService = Substitute.For<ISystemResourceService>();
        _configService = Substitute.For<IConfigService>();
        _signalRBroadcaster = Substitute.For<IBroadcastSignalRMessage>();

        _controller = new SubsystemsController(
            _resourceService,
            _configService,
            _signalRBroadcaster);
    }

    [Test]
    public void GetAllSubsystems_returns_all_ten_subsystems()
    {
        var result = _controller.GetAllSubsystems();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var subsystems = okResult.Value as List<SubsystemOverviewResource>;

        Assert.That(subsystems, Is.Not.Null);
        Assert.That(subsystems.Count, Is.EqualTo(10));

        var expectedIds = new[]
        {
            "bittorrent", "extractor", "mediainspector", "geoip", "blocklist",
            "networkbinding", "mediametadata", "httptransport", "ai", "simulation"
        };

        var actualIds = subsystems.Select(s => s.Id).ToList();
        Assert.That(actualIds, Is.EquivalentTo(expectedIds));
    }

    [Test]
    public void GetAllSubsystems_uses_configured_active_provider_and_maps_status()
    {
        _configService.GetValue("Subsystems_bittorrent_ActiveProvider", "MonoTorrent").Returns("LibTorrent");

        var result = _controller.GetAllSubsystems();

        var okResult = (OkObjectResult)result.Result;
        var subsystems = (List<SubsystemOverviewResource>)okResult.Value;
        var btSubsystem = subsystems.Single(s => s.Id == "bittorrent");

        Assert.That(btSubsystem.ActiveProviderId, Is.EqualTo("LibTorrent"));

        var monoProvider = btSubsystem.Providers.Single(p => p.ProviderId == "MonoTorrent");
        Assert.That(monoProvider.IsActive, Is.False);
        Assert.That(monoProvider.Status, Is.EqualTo("Ready"));

        var libTorrentProvider = btSubsystem.Providers.Single(p => p.ProviderId == "LibTorrent");
        Assert.That(libTorrentProvider.IsActive, Is.True);
        Assert.That(libTorrentProvider.Status, Is.EqualTo("Running"));
    }

    [TestCase("bittorrent", "bittorrent")]
    [TestCase("extractor", "extractor")]
    [TestCase("mediainspector", "mediainspector")]
    [TestCase("geoip", "geoip")]
    [TestCase("blocklist", "blocklist")]
    [TestCase("networkbinding", "networkbinding")]
    [TestCase("mediametadata", "mediametadata")]
    [TestCase("httptransport", "httptransport")]
    [TestCase("ai", "ai")]
    [TestCase("simulation", "simulation")]
    public void GetSubsystem_returns_subsystem_for_canonical_ids(string inputId, string expectedId)
    {
        var result = _controller.GetSubsystem(inputId);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var subsystem = okResult.Value as SubsystemOverviewResource;

        Assert.That(subsystem, Is.Not.Null);
        Assert.That(subsystem.Id, Is.EqualTo(expectedId));
        Assert.That(subsystem.Name, Is.Not.Null.And.Not.Empty);
        Assert.That(subsystem.Category, Is.Not.Null.And.Not.Empty);
        Assert.That(subsystem.Providers, Is.Not.Empty);
    }

    [TestCase("torrentengine", "bittorrent")]
    [TestCase("archiveextractor", "extractor")]
    [TestCase("inspector", "mediainspector")]
    [TestCase("binding", "networkbinding")]
    [TestCase("metadata", "mediametadata")]
    [TestCase("transport", "httptransport")]
    [TestCase("intelligence", "ai")]
    public void GetSubsystem_normalizes_aliases(string alias, string expectedId)
    {
        var result = _controller.GetSubsystem(alias);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var subsystem = (SubsystemOverviewResource)okResult.Value;

        Assert.That(subsystem.Id, Is.EqualTo(expectedId));
    }

    [TestCase("BITTORRENT")]
    [TestCase("TorrentEngine")]
    [TestCase("GeoIP")]
    [TestCase("NetworkBinding")]
    [TestCase("AI")]
    public void GetSubsystem_is_case_insensitive(string inputId)
    {
        var result = _controller.GetSubsystem(inputId);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var subsystem = (SubsystemOverviewResource)okResult.Value;

        Assert.That(subsystem, Is.Not.Null);
        Assert.That(subsystem.Id, Is.EqualTo(inputId.ToLowerInvariant().Replace("torrentengine", "bittorrent")));
    }

    [TestCase("nonexistent")]
    [TestCase("unknown")]
    [TestCase("random123")]
    public void GetSubsystem_returns_not_found_for_invalid_subsystem(string invalidId)
    {
        var result = _controller.GetSubsystem(invalidId);

        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
        var notFound = (NotFoundObjectResult)result.Result;
        Assert.That(notFound.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public void GetSubsystem_returns_not_found_when_id_is_null()
    {
        var result = _controller.GetSubsystem(null);

        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void SwitchProvider_returns_bad_request_when_request_is_null()
    {
        var result = _controller.SwitchProvider("bittorrent", null);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var value = badRequest.Value as SwitchSubsystemProviderResult;
        Assert.That(value, Is.Not.Null);
        Assert.That(value.Success, Is.False);
        Assert.That(value.Error, Is.EqualTo("ProviderId is required."));
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase(null)]
    public void SwitchProvider_returns_bad_request_when_provider_id_is_missing(string providerId)
    {
        var request = new SwitchSubsystemProviderRequest { ProviderId = providerId };
        var result = _controller.SwitchProvider("bittorrent", request);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var value = (SwitchSubsystemProviderResult)badRequest.Value;
        Assert.That(value.Success, Is.False);
        Assert.That(value.Error, Is.EqualTo("ProviderId is required."));
    }

    [Test]
    public void SwitchProvider_updates_config_and_returns_success()
    {
        _configService.GetValue("Subsystems_bittorrent_ActiveProvider", "MonoTorrent").Returns("MonoTorrent");

        var request = new SwitchSubsystemProviderRequest { ProviderId = "LibTorrent" };
        var result = _controller.SwitchProvider("torrentengine", request);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var value = okResult.Value as SwitchSubsystemProviderResult;

        Assert.That(value, Is.Not.Null);
        Assert.That(value.Success, Is.True);
        Assert.That(value.SubsystemId, Is.EqualTo("bittorrent"));
        Assert.That(value.PreviousProvider, Is.EqualTo("MonoTorrent"));
        Assert.That(value.ActiveProvider, Is.EqualTo("LibTorrent"));
        Assert.That(value.Message, Does.Contain("Successfully switched bittorrent provider to LibTorrent"));

        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            d.ContainsKey("Subsystems_bittorrent_ActiveProvider") &&
            (string)d["Subsystems_bittorrent_ActiveProvider"] == "LibTorrent"));
    }

    [Test]
    public void SwitchProvider_broadcasts_signalr_message()
    {
        _configService.GetValue("Subsystems_geoip_ActiveProvider", "MaxMindGeoIp").Returns("MaxMindGeoIp");

        var request = new SwitchSubsystemProviderRequest { ProviderId = "CustomGeoIp" };
        _controller.SwitchProvider("geoip", request);

        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(msg =>
            msg.Name == "subsystemSwitched" &&
            msg.Action == ModelAction.Updated &&
            msg.Body is SwitchSubsystemProviderResult &&
            ((SwitchSubsystemProviderResult)msg.Body).ActiveProvider == "CustomGeoIp"));
    }

    [Test]
    public void SwitchProvider_does_not_throw_when_signalr_broadcaster_is_null()
    {
        var controllerWithoutSignalR = new SubsystemsController(_resourceService, _configService, null);
        var request = new SwitchSubsystemProviderRequest { ProviderId = "Ollama" };

        Assert.DoesNotThrow(() =>
        {
            var result = controllerWithoutSignalR.SwitchProvider("ai", request);
            Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        });
    }

    [TestCase("bittorrent", "MonoTorrent")]
    [TestCase("torrentengine", "LibTorrent")]
    [TestCase("geoip", "MaxMindGeoIp")]
    [TestCase("ai", "Ollama")]
    public void ProbeProvider_returns_healthy_probe_result(string subsystemId, string providerId)
    {
        var result = _controller.ProbeProvider(subsystemId, providerId);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var probe = okResult.Value as SubsystemProbeResult;

        Assert.That(probe, Is.Not.Null);
        Assert.That(probe.ProviderId, Is.EqualTo(providerId));
        Assert.That(probe.IsHealthy, Is.True);
        Assert.That(probe.StatusMessage, Does.Contain(providerId));
        Assert.That(probe.DependencyChecks, Is.Not.Empty);
        Assert.That(probe.Warnings, Is.Empty);
    }

    [Test]
    public async Task GetSubsystemsMetrics_returns_metrics_from_resource_service()
    {
        var telemetryList = new List<SubsystemTelemetryReport>
        {
            new() { SubsystemId = "bittorrent" },
            new() { SubsystemId = "geoip" }
        };

        _resourceService.GetSubsystemTelemetryAsync(null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(telemetryList));

        var result = await _controller.GetSubsystemsMetrics();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var reports = okResult.Value as List<SubsystemTelemetryReport>;

        Assert.That(reports, Is.Not.Null);
        Assert.That(reports.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task GetSubsystemMetrics_returns_metric_when_subsystem_found()
    {
        var telemetryList = new List<SubsystemTelemetryReport>
        {
            new() { SubsystemId = "bittorrent" }
        };

        _resourceService.GetSubsystemTelemetryAsync("bittorrent", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(telemetryList));

        var result = await _controller.GetSubsystemMetrics("torrentengine");

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var report = okResult.Value as SubsystemTelemetryReport;

        Assert.That(report, Is.Not.Null);
        Assert.That(report.SubsystemId, Is.EqualTo("bittorrent"));
    }

    [Test]
    public async Task GetSubsystemMetrics_returns_not_found_when_telemetry_absent()
    {
        _resourceService.GetSubsystemTelemetryAsync("bittorrent", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<SubsystemTelemetryReport>()));

        var result = await _controller.GetSubsystemMetrics("bittorrent");

        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
        var notFound = (NotFoundObjectResult)result.Result;
        Assert.That(notFound.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public void Subsystem_matrix_capabilities_verification()
    {
        var result = _controller.GetAllSubsystems();
        var okResult = (OkObjectResult)result.Result;
        var subsystems = (List<SubsystemOverviewResource>)okResult.Value;

        var aiSubsystem = subsystems.Single(s => s.Id == "ai");
        Assert.That(aiSubsystem.Providers.Count, Is.EqualTo(4));
        Assert.That(aiSubsystem.Providers.Select(p => p.ProviderId), Is.EquivalentTo(new[] { "RuleHeuristic", "Ollama", "Gemini", "Onnx" }));

        var ruleProvider = aiSubsystem.Providers.Single(p => p.ProviderId == "RuleHeuristic");
        Assert.That(ruleProvider.Capabilities["supportsReleaseNameParsing"], Is.EqualTo(true));
        Assert.That(ruleProvider.Capabilities["supportsCloudLlm"], Is.EqualTo(false));

        var ollamaProvider = aiSubsystem.Providers.Single(p => p.ProviderId == "Ollama");
        Assert.That(ollamaProvider.Capabilities["supportsNaturalLanguageSearch"], Is.EqualTo(true));
        Assert.That(ollamaProvider.Capabilities["supportsDiagnosticCopilot"], Is.EqualTo(true));

        var netSubsystem = subsystems.Single(s => s.Id == "networkbinding");
        var socketProvider = netSubsystem.Providers.Single(p => p.ProviderId == "SocketBindToDevice");
        Assert.That(socketProvider.Capabilities["supportsVpnKillSwitch"], Is.EqualTo(true));

        var blocklistSubsystem = subsystems.Single(s => s.Id == "blocklist");
        var filterProvider = blocklistSubsystem.Providers.Single(p => p.ProviderId == "PeerBlocklistFilter");
        Assert.That(filterProvider.Capabilities["supportsIPv4"], Is.EqualTo(true));
        Assert.That(filterProvider.Capabilities["supportsIPv6"], Is.EqualTo(true));
    }
}
