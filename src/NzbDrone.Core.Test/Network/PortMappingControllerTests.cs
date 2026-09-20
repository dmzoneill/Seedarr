using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Network;
using Seedarr.Api.V1.Network;

namespace NzbDrone.Core.Test.Network;

[TestFixture]
public class PortMappingControllerTests
{
    private IPortMappingEngine _portMappingEngine;
    private PortMappingController _controller;

    [SetUp]
    public void SetUp()
    {
        _portMappingEngine = Substitute.For<IPortMappingEngine>();
        _controller = new PortMappingController(_portMappingEngine);
    }

    [Test]
    public async Task GetStatusAsync_should_return_ok_with_port_mapping_status()
    {
        var expectedStatus = new PortMappingStatus
        {
            Protocol = "UPnP",
            GatewayIp = "192.168.1.1",
            ExternalIp = "203.0.113.42",
            RouterModel = "UPnP-IGD Gateway",
            LastRenewalUtc = DateTime.UtcNow.AddMinutes(-30),
            NextRenewalUtc = DateTime.UtcNow.AddMinutes(30),
            Mappings = new List<EnrichedPortMapping>
            {
                new EnrichedPortMapping
                {
                    InternalPort = 6881,
                    ExternalPort = 6881,
                    Protocol = "TCP",
                    Description = "Seedarr Peer",
                    LeaseSeconds = 7200,
                    ExpiryUtc = DateTime.UtcNow.AddMinutes(90),
                    IsActive = true,
                    Status = "Active"
                }
            }
        };

        _portMappingEngine.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(expectedStatus));

        var result = await _controller.GetStatusAsync();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        Assert.That(okResult.Value, Is.SameAs(expectedStatus));
    }

    [Test]
    public async Task RefreshAsync_should_trigger_refresh_and_return_updated_status()
    {
        var refreshedStatus = new PortMappingStatus
        {
            Protocol = "NAT-PMP",
            GatewayIp = "10.0.0.1",
            ExternalIp = "198.51.100.25",
            RouterModel = "NAT-PMP Gateway",
            LastRenewalUtc = DateTime.UtcNow,
            NextRenewalUtc = DateTime.UtcNow.AddHours(1),
            Mappings = new List<EnrichedPortMapping>
            {
                new EnrichedPortMapping
                {
                    InternalPort = 51413,
                    ExternalPort = 51413,
                    Protocol = "TCP",
                    Description = "Seedarr Peer",
                    LeaseSeconds = 3600,
                    ExpiryUtc = DateTime.UtcNow.AddHours(1),
                    IsActive = true,
                    Status = "Active"
                }
            }
        };

        _portMappingEngine.RefreshAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(refreshedStatus));

        var result = await _controller.RefreshAsync();

        await _portMappingEngine.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        Assert.That(okResult.Value, Is.SameAs(refreshedStatus));
    }

    [Test]
    public async Task PortMappingEngine_GetStatusAsync_should_populate_telemetry_correctly()
    {
        var gatewayService = Substitute.For<IGatewayDiscoveryService>();
        gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(IPAddress.Parse("192.168.1.254")));

        var upnpService = Substitute.For<IUpnpService>();
        upnpService.IsAvailable.Returns(true);
        upnpService.ExternalIp.Returns("198.51.100.99");
        upnpService.RouterModel.Returns("Netgear Nighthawk");
        var now = DateTime.UtcNow;
        upnpService.LastRenewalUtc.Returns(now);
        upnpService.NextRenewalUtc.Returns(now.AddHours(1));
        upnpService.GetMappings().Returns(new List<PortMapping>
        {
            new PortMapping
            {
                InternalPort = 6881,
                ExternalPort = 6881,
                Protocol = "TCP",
                Description = "Seedarr Peer",
                IsActive = true,
                LeaseSeconds = 7200,
                ExpiryUtc = now.AddHours(2)
            }
        });

        var engine = new PortMappingEngine(
            gatewayService,
            upnpService,
            udpProber: (gw, ct) => Task.FromResult(PortMappingProtocol.Upnp),
            upnpProber: ct => Task.FromResult(true));

        var status = await engine.GetStatusAsync();

        Assert.That(status.Protocol, Is.EqualTo("UPnP"));
        Assert.That(status.GatewayIp, Is.EqualTo("192.168.1.254"));
        Assert.That(status.ExternalIp, Is.EqualTo("198.51.100.99"));
        Assert.That(status.RouterModel, Is.EqualTo("Netgear Nighthawk"));
        Assert.That(status.Mappings.Count, Is.EqualTo(1));
        Assert.That(status.Mappings[0].InternalPort, Is.EqualTo(6881));
        Assert.That(status.Mappings[0].ExternalPort, Is.EqualTo(6881));
        Assert.That(status.Mappings[0].Protocol, Is.EqualTo("TCP"));
        Assert.That(status.Mappings[0].Description, Is.EqualTo("Seedarr Peer"));
        Assert.That(status.Mappings[0].LeaseSeconds, Is.EqualTo(7200));
        Assert.That(status.Mappings[0].IsActive, Is.True);
        Assert.That(status.Mappings[0].Status, Is.EqualTo("Active"));
    }

    [Test]
    public async Task PortMappingEngine_RefreshAsync_should_trigger_probe_and_mapping_creation()
    {
        var gatewayService = Substitute.For<IGatewayDiscoveryService>();
        gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(IPAddress.Parse("192.168.1.1")));

        var upnpService = Substitute.For<IUpnpService>();
        upnpService.IsAvailable.Returns(true);
        upnpService.ExternalIp.Returns("198.51.100.1");

        var engine = new PortMappingEngine(
            gatewayService,
            upnpService,
            udpProber: (gw, ct) => Task.FromResult(PortMappingProtocol.Pcp),
            upnpProber: ct => Task.FromResult(false));

        var status = await engine.RefreshAsync();

        await upnpService.Received(1).CreateMappings(Arg.Any<CancellationToken>());
        Assert.That(status.Protocol, Is.EqualTo("PCP"));
        Assert.That(status.GatewayIp, Is.EqualTo("192.168.1.1"));
    }

    [Test]
    public void PortMappingHealthCheck_should_return_ok_when_upnp_disabled()
    {
        var configService = Substitute.For<IConfigService>();
        configService.UpnpEnabled.Returns(false);
        var upnpService = Substitute.For<IUpnpService>();

        var healthCheck = new PortMappingHealthCheck(configService, upnpService);
        var result = healthCheck.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void PortMappingHealthCheck_should_return_ok_when_peer_port_mapped_successfully()
    {
        var configService = Substitute.For<IConfigService>();
        configService.UpnpEnabled.Returns(true);
        configService.ListeningPort.Returns(6881);

        var upnpService = Substitute.For<IUpnpService>();
        upnpService.IsAvailable.Returns(true);
        upnpService.GetMappings().Returns(new List<PortMapping>
        {
            new PortMapping
            {
                InternalPort = 6881,
                ExternalPort = 6881,
                Protocol = "TCP",
                IsActive = true
            }
        });

        var healthCheck = new PortMappingHealthCheck(configService, upnpService);
        var result = healthCheck.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void PortMappingHealthCheck_should_return_warning_when_upnp_service_unavailable()
    {
        var configService = Substitute.For<IConfigService>();
        configService.UpnpEnabled.Returns(true);
        configService.ListeningPort.Returns(6881);

        var upnpService = Substitute.For<IUpnpService>();
        upnpService.IsAvailable.Returns(false);
        upnpService.GetMappings().Returns(new List<PortMapping>());

        var healthCheck = new PortMappingHealthCheck(configService, upnpService);
        var result = healthCheck.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Message, Does.Contain("Seedarr failed to map peer listening port 6881"));
    }

    [Test]
    public void PortMappingHealthCheck_should_return_warning_when_peer_mapping_failed()
    {
        var configService = Substitute.For<IConfigService>();
        configService.UpnpEnabled.Returns(true);
        configService.ListeningPort.Returns(6881);

        var upnpService = Substitute.For<IUpnpService>();
        upnpService.IsAvailable.Returns(true);
        upnpService.GetMappings().Returns(new List<PortMapping>
        {
            new PortMapping
            {
                InternalPort = 6881,
                ExternalPort = 6881,
                Protocol = "TCP",
                IsActive = false,
                ErrorMessage = "Conflict in mapping entry (Error 718)"
            }
        });

        var healthCheck = new PortMappingHealthCheck(configService, upnpService);
        var result = healthCheck.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Message, Does.Contain("Conflict in mapping entry (Error 718)"));
    }
}
