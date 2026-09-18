using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Network;

namespace NzbDrone.Core.Test.Network;

[TestFixture]
public class GatewayDiscoveryServiceTests
{
    private string _tempRouteFile;

    [TearDown]
    public void TearDown()
    {
        if (_tempRouteFile != null && File.Exists(_tempRouteFile))
        {
            try
            {
                File.Delete(_tempRouteFile);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Test]
    public void ParseRouteTable_should_parse_valid_hex_gateway()
    {
        var routeContent =
            "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n" +
            "wlp9s0f0\t00000000\tFE01A8C0\t0003\t0\t0\t600\t00000000\t0\t0\t0\n" +
            "virbr1\t000A0A0A\t00000000\t0001\t0\t0\t0\t00FFFFFF\t0\t0\t0\n";

        var gateway = GatewayDiscoveryService.ParseRouteTable(routeContent);

        Assert.That(gateway, Is.Not.Null);
        Assert.That(gateway.ToString(), Is.EqualTo("192.168.1.254"));
    }

    [Test]
    public void ParseRouteTable_should_select_default_route_ignoring_non_default_routes()
    {
        var routeContent =
            "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n" +
            "eth0\t0001A8C0\t00000000\t0001\t0\t0\t100\t00FFFFFF\t0\t0\t0\n" +
            "eth0\t00000000\t010010AC\t0003\t0\t0\t100\t00000000\t0\t0\t0\n";

        var gateway = GatewayDiscoveryService.ParseRouteTable(routeContent);

        Assert.That(gateway, Is.Not.Null);
        Assert.That(gateway.ToString(), Is.EqualTo("172.16.0.1"));
    }

    [Test]
    public void ParseRouteTable_should_select_default_route_with_lowest_metric()
    {
        var routeContent =
            "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n" +
            "wlan0\t00000000\tFE01A8C0\t0003\t0\t0\t600\t00000000\t0\t0\t0\n" +
            "eth0\t00000000\t0101A8C0\t0003\t0\t0\t100\t00000000\t0\t0\t0\n";

        var gateway = GatewayDiscoveryService.ParseRouteTable(routeContent);

        Assert.That(gateway, Is.Not.Null);
        Assert.That(gateway.ToString(), Is.EqualTo("192.168.1.1"));
    }

    [Test]
    public void ParseRouteTable_should_ignore_destination_00000000_with_gateway_00000000()
    {
        var routeContent =
            "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n" +
            "eth0\t00000000\t00000000\t0001\t0\t0\t0\t00000000\t0\t0\t0\n";

        var gateway = GatewayDiscoveryService.ParseRouteTable(routeContent);

        Assert.That(gateway, Is.Null);
    }

    [Test]
    public void ParseRouteTable_should_return_null_when_content_is_empty_or_whitespace_or_null()
    {
        Assert.That(GatewayDiscoveryService.ParseRouteTable(null), Is.Null);
        Assert.That(GatewayDiscoveryService.ParseRouteTable(""), Is.Null);
        Assert.That(GatewayDiscoveryService.ParseRouteTable("   \r\n   "), Is.Null);
    }

    [Test]
    public void ParseRouteTable_should_return_null_when_no_default_route_exists()
    {
        var routeContent =
            "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n" +
            "virbr1\t000A0A0A\t00000000\t0001\t0\t0\t0\t00FFFFFF\t0\t0\t0\n" +
            "virbr0\t007AA8C0\t00000000\t0001\t0\t0\t0\t00FFFFFF\t0\t0\t0\n";

        var gateway = GatewayDiscoveryService.ParseRouteTable(routeContent);

        Assert.That(gateway, Is.Null);
    }

    [Test]
    public void GetDefaultGateway_should_fallback_when_route_file_does_not_exist()
    {
        var fallbackIp = IPAddress.Parse("10.0.0.1");
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new GatewayDiscoveryService(nonExistentPath, () => fallbackIp);

        var result = service.GetDefaultGateway();

        Assert.That(result, Is.EqualTo(fallbackIp));
    }

    [Test]
    public void GetDefaultGateway_should_fallback_when_route_file_has_no_default_route()
    {
        var fallbackIp = IPAddress.Parse("10.0.0.1");
        _tempRouteFile = Path.GetTempFileName();
        File.WriteAllText(_tempRouteFile, "Iface\tDestination\tGateway\neth0\t000A0A0A\t00000000\n");

        var service = new GatewayDiscoveryService(_tempRouteFile, () => fallbackIp);

        var result = service.GetDefaultGateway();

        Assert.That(result, Is.EqualTo(fallbackIp));
    }

    [Test]
    public void GetDefaultGateway_should_use_route_file_when_valid_default_route_exists()
    {
        var fallbackIp = IPAddress.Parse("192.168.1.1");
        _tempRouteFile = Path.GetTempFileName();
        File.WriteAllText(_tempRouteFile, "Iface\tDestination\tGateway\tFlags\tRefCnt\tUse\tMetric\neth0\t00000000\t0100000A\t0003\t0\t0\t0\n");

        var service = new GatewayDiscoveryService(_tempRouteFile, () => fallbackIp);

        var result = service.GetDefaultGateway();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ToString(), Is.EqualTo("10.0.0.1"));
    }

    [Test]
    public async Task GetDefaultGatewayAsync_should_return_expected_gateway()
    {
        var fallbackIp = IPAddress.Parse("10.0.0.1");
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new GatewayDiscoveryService(nonExistentPath, () => fallbackIp);

        var result = await service.GetDefaultGatewayAsync();

        Assert.That(result, Is.EqualTo(fallbackIp));
    }

    [Test]
    public void ParseProbeResponse_should_return_Pcp_when_version_is_2()
    {
        var response = new byte[] { 2, 0x80, 0, 0 };

        var protocol = PortMappingEngine.ParseProbeResponse(response);

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.Pcp));
    }

    [Test]
    public void ParseProbeResponse_should_return_Pcp_when_unsupported_version_indicates_version_2()
    {
        var responseWithVers2 = new byte[] { 2, 0x80, 0, 1 };
        var responseWithVersInPayload = new byte[] { 0, 0x80, 0, 1, 2 };

        var protocol1 = PortMappingEngine.ParseProbeResponse(responseWithVers2);
        var protocol2 = PortMappingEngine.ParseProbeResponse(responseWithVersInPayload);

        Assert.That(protocol1, Is.EqualTo(PortMappingProtocol.Pcp));
        Assert.That(protocol2, Is.EqualTo(PortMappingProtocol.Pcp));
    }

    [Test]
    public void ParseProbeResponse_should_return_NatPmp_when_version_0_and_result_code_0()
    {
        var response = new byte[] { 0, 0x80, 0, 0, 0, 0, 0, 10, 192, 168, 1, 100 };

        var protocol = PortMappingEngine.ParseProbeResponse(response);

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.NatPmp));
    }

    [Test]
    public void ParseProbeResponse_should_return_None_when_result_code_is_not_success()
    {
        var response = new byte[] { 0, 0x80, 0, 2 };

        var protocol = PortMappingEngine.ParseProbeResponse(response);

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.None));
    }

    [Test]
    public void ParseProbeResponse_should_return_None_when_response_is_null_or_empty_or_too_short()
    {
        Assert.That(PortMappingEngine.ParseProbeResponse(null), Is.EqualTo(PortMappingProtocol.None));
        Assert.That(PortMappingEngine.ParseProbeResponse(Array.Empty<byte>()), Is.EqualTo(PortMappingProtocol.None));
        Assert.That(PortMappingEngine.ParseProbeResponse(new byte[] { 0 }), Is.EqualTo(PortMappingProtocol.None));
    }

    [Test]
    public async Task DetectProtocolAsync_should_select_Pcp_when_udp_prober_returns_Pcp()
    {
        var gatewayService = Substitute.For<IGatewayDiscoveryService>();
        gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(IPAddress.Parse("192.168.1.1")));

        var upnpProberCalled = false;
        var engine = new PortMappingEngine(
            gatewayService,
            udpProber: (gw, ct) => Task.FromResult(PortMappingProtocol.Pcp),
            upnpProber: ct =>
            {
                upnpProberCalled = true;
                return Task.FromResult(true);
            });

        var protocol = await engine.DetectProtocolAsync();

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.Pcp));
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.Pcp));
        Assert.That(upnpProberCalled, Is.False);
    }

    [Test]
    public async Task DetectProtocolAsync_should_select_NatPmp_when_udp_prober_returns_NatPmp()
    {
        var gatewayService = Substitute.For<IGatewayDiscoveryService>();
        gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(IPAddress.Parse("192.168.1.1")));

        var upnpProberCalled = false;
        var engine = new PortMappingEngine(
            gatewayService,
            udpProber: (gw, ct) => Task.FromResult(PortMappingProtocol.NatPmp),
            upnpProber: ct =>
            {
                upnpProberCalled = true;
                return Task.FromResult(true);
            });

        var protocol = await engine.DetectProtocolAsync();

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.NatPmp));
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.NatPmp));
        Assert.That(upnpProberCalled, Is.False);
    }

    [Test]
    public async Task DetectProtocolAsync_should_fallback_to_Upnp_when_udp_prober_times_out_and_upnp_is_available()
    {
        var gatewayService = Substitute.For<IGatewayDiscoveryService>();
        gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(IPAddress.Parse("192.168.1.1")));

        var engine = new PortMappingEngine(
            gatewayService,
            udpProber: (gw, ct) => Task.FromResult(PortMappingProtocol.None),
            upnpProber: ct => Task.FromResult(true));

        var protocol = await engine.DetectProtocolAsync();

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.Upnp));
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.Upnp));
    }

    [Test]
    public async Task DetectProtocolAsync_should_fallback_to_None_when_udp_prober_times_out_and_upnp_is_unavailable()
    {
        var gatewayService = Substitute.For<IGatewayDiscoveryService>();
        gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(IPAddress.Parse("192.168.1.1")));

        var engine = new PortMappingEngine(
            gatewayService,
            udpProber: (gw, ct) => Task.FromResult(PortMappingProtocol.None),
            upnpProber: ct => Task.FromResult(false));

        var protocol = await engine.DetectProtocolAsync();

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.None));
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.None));
    }

    [Test]
    public async Task DetectProtocolAsync_should_fallback_to_Upnp_when_gateway_is_null_and_upnp_is_available()
    {
        var gatewayService = Substitute.For<IGatewayDiscoveryService>();
        gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult<IPAddress>(null));

        var udpProberCalled = false;
        var engine = new PortMappingEngine(
            gatewayService,
            udpProber: (gw, ct) =>
            {
                udpProberCalled = true;
                return Task.FromResult(PortMappingProtocol.NatPmp);
            },
            upnpProber: ct => Task.FromResult(true));

        var protocol = await engine.DetectProtocolAsync();

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.Upnp));
        Assert.That(udpProberCalled, Is.False);
    }

    [Test]
    public async Task DetectProtocolAsync_should_fallback_to_None_when_gateway_is_null_and_upnp_is_unavailable()
    {
        var gatewayService = Substitute.For<IGatewayDiscoveryService>();
        gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult<IPAddress>(null));

        var engine = new PortMappingEngine(
            gatewayService,
            udpProber: (gw, ct) => Task.FromResult(PortMappingProtocol.NatPmp),
            upnpProber: ct => Task.FromResult(false));

        var protocol = await engine.DetectProtocolAsync();

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.None));
    }

    [Test]
    public void DetectProtocol_synchronous_wrapper_should_return_detected_protocol()
    {
        var gatewayService = Substitute.For<IGatewayDiscoveryService>();
        gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(IPAddress.Parse("192.168.1.1")));

        var engine = new PortMappingEngine(
            gatewayService,
            udpProber: (gw, ct) => Task.FromResult(PortMappingProtocol.NatPmp),
            upnpProber: ct => Task.FromResult(false));

        var protocol = engine.DetectProtocol();

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.NatPmp));
    }
}
