using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Pcp;

namespace NzbDrone.Core.Test.Network;

[TestFixture]
public class PortMappingEngineTests
{
    private IGatewayDiscoveryService _gatewayService;
    private IUpnpService _upnpService;
    private IPcpClient _pcpClient;

    [SetUp]
    public void SetUp()
    {
        _gatewayService = Substitute.For<IGatewayDiscoveryService>();
        _upnpService = Substitute.For<IUpnpService>();
        _pcpClient = Substitute.For<IPcpClient>();
    }

    [Test]
    public void Constructor_initializes_with_default_protocol_none_and_exposes_pcp_client()
    {
        var engine = new PortMappingEngine(_gatewayService, _upnpService, null, null, _pcpClient);

        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.None));
        Assert.That(engine.PcpClient, Is.SameAs(_pcpClient));
    }

    [Test]
    public void DemoteToNatPmp_sets_current_protocol_to_natpmp()
    {
        var engine = new PortMappingEngine(_gatewayService, _upnpService, null, null, _pcpClient);

        engine.DemoteToNatPmp();

        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.NatPmp));
    }

    [Test]
    public void CreatePcpMappingAsync_throws_InvalidOperationException_when_pcp_client_is_null()
    {
        var engine = new PortMappingEngine(_gatewayService, _upnpService, null, null, null);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await engine.CreatePcpMappingAsync(
                IPAddress.Loopback,
                IPAddress.Loopback,
                6881,
                6881,
                PortMappingTransport.Tcp,
                TimeSpan.FromHours(1));
        });

        Assert.That(ex.Message, Does.Contain("PCP client is not configured"));
    }

    [Test]
    public async Task CreatePcpMappingAsync_delegates_to_pcp_client_and_returns_result()
    {
        var gateway = IPAddress.Parse("192.168.1.1");
        var clientIp = IPAddress.Parse("192.168.1.50");
        var expectedResult = new PcpMappingResult
        {
            Success = true,
            ResultCode = PcpResultCode.Success,
            InternalPort = 6881,
            ExternalPort = 16881,
            Protocol = PortMappingTransport.Tcp,
            Lifetime = TimeSpan.FromHours(1)
        };

        _pcpClient.CreateMappingAsync(
            gateway,
            clientIp,
            6881,
            6881,
            PortMappingTransport.Tcp,
            TimeSpan.FromHours(1),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedResult));

        var engine = new PortMappingEngine(_gatewayService, _upnpService, null, null, _pcpClient);

        var result = await engine.CreatePcpMappingAsync(
            gateway,
            clientIp,
            6881,
            6881,
            PortMappingTransport.Tcp,
            TimeSpan.FromHours(1));

        Assert.That(result, Is.EqualTo(expectedResult));
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.None));
    }

    [Test]
    public async Task CreatePcpMappingAsync_demotes_to_natpmp_when_UnsupportedVersion_result_received()
    {
        var gateway = IPAddress.Parse("192.168.1.1");
        var clientIp = IPAddress.Parse("192.168.1.50");
        var unsupportedVersionResult = new PcpMappingResult
        {
            Success = false,
            ResultCode = PcpResultCode.UnsupportedVersion,
            ErrorMessage = "Unsupported PCP version"
        };

        _pcpClient.CreateMappingAsync(
            gateway,
            clientIp,
            6881,
            6881,
            PortMappingTransport.Tcp,
            TimeSpan.FromHours(1),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(unsupportedVersionResult));

        var engine = new PortMappingEngine(
            _gatewayService,
            _upnpService,
            (_, _) => Task.FromResult(PortMappingProtocol.Pcp),
            null,
            _pcpClient);

        await engine.DetectProtocolAsync();
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.Pcp));

        var result = await engine.CreatePcpMappingAsync(
            gateway,
            clientIp,
            6881,
            6881,
            PortMappingTransport.Tcp,
            TimeSpan.FromHours(1));

        Assert.That(result.ResultCode, Is.EqualTo(PcpResultCode.UnsupportedVersion));
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.NatPmp));
    }

    [Test]
    public async Task CreatePcpMappingAsync_does_not_demote_on_other_error_codes()
    {
        var gateway = IPAddress.Parse("192.168.1.1");
        var clientIp = IPAddress.Parse("192.168.1.50");
        var quotaErrorResult = new PcpMappingResult
        {
            Success = false,
            ResultCode = PcpResultCode.UserExceededQuota,
            ErrorMessage = "Quota exceeded"
        };

        _pcpClient.CreateMappingAsync(
            gateway,
            clientIp,
            6881,
            6881,
            PortMappingTransport.Tcp,
            TimeSpan.FromHours(1),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(quotaErrorResult));

        var engine = new PortMappingEngine(
            _gatewayService,
            _upnpService,
            (_, _) => Task.FromResult(PortMappingProtocol.Pcp),
            null,
            _pcpClient);

        await engine.DetectProtocolAsync();
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.Pcp));

        var result = await engine.CreatePcpMappingAsync(
            gateway,
            clientIp,
            6881,
            6881,
            PortMappingTransport.Tcp,
            TimeSpan.FromHours(1));

        Assert.That(result.ResultCode, Is.EqualTo(PcpResultCode.UserExceededQuota));
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.Pcp));
    }

    [Test]
    public async Task DetectProtocolAsync_negotiates_pcp_when_udp_probe_returns_pcp()
    {
        _gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(IPAddress.Parse("192.168.1.1")));

        var engine = new PortMappingEngine(
            _gatewayService,
            _upnpService,
            udpProber: (_, _) => Task.FromResult(PortMappingProtocol.Pcp),
            upnpProber: _ => Task.FromResult(false),
            _pcpClient);

        var protocol = await engine.DetectProtocolAsync();

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.Pcp));
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.Pcp));
    }

    [Test]
    public async Task DetectProtocolAsync_negotiates_natpmp_when_udp_probe_returns_natpmp()
    {
        _gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(IPAddress.Parse("192.168.1.1")));

        var engine = new PortMappingEngine(
            _gatewayService,
            _upnpService,
            udpProber: (_, _) => Task.FromResult(PortMappingProtocol.NatPmp),
            upnpProber: _ => Task.FromResult(false),
            _pcpClient);

        var protocol = await engine.DetectProtocolAsync();

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.NatPmp));
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.NatPmp));
    }

    [Test]
    public async Task DetectProtocolAsync_negotiates_upnp_when_udp_probe_returns_none_and_upnp_probe_succeeds()
    {
        _gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(IPAddress.Parse("192.168.1.1")));

        var engine = new PortMappingEngine(
            _gatewayService,
            _upnpService,
            udpProber: (_, _) => Task.FromResult(PortMappingProtocol.None),
            upnpProber: _ => Task.FromResult(true),
            _pcpClient);

        var protocol = await engine.DetectProtocolAsync();

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.Upnp));
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.Upnp));
    }

    [Test]
    public async Task DetectProtocolAsync_negotiates_upnp_when_gateway_discovery_returns_null()
    {
        _gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult<IPAddress>(null));

        var engine = new PortMappingEngine(
            _gatewayService,
            _upnpService,
            udpProber: (_, _) => Task.FromResult(PortMappingProtocol.Pcp),
            upnpProber: _ => Task.FromResult(true),
            _pcpClient);

        var protocol = await engine.DetectProtocolAsync();

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.Upnp));
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.Upnp));
    }

    [Test]
    public async Task DetectProtocolAsync_negotiates_upnp_when_gateway_discovery_throws()
    {
        _gatewayService.GetDefaultGatewayAsync().Returns<Task<IPAddress>>(_ => throw new InvalidOperationException("Gateway lookup failed"));

        var engine = new PortMappingEngine(
            _gatewayService,
            _upnpService,
            udpProber: (_, _) => Task.FromResult(PortMappingProtocol.Pcp),
            upnpProber: _ => Task.FromResult(true),
            _pcpClient);

        var protocol = await engine.DetectProtocolAsync();

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.Upnp));
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.Upnp));
    }

    [Test]
    public async Task DetectProtocolAsync_returns_none_when_both_probers_fail_or_throw()
    {
        _gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(IPAddress.Parse("192.168.1.1")));

        var engine = new PortMappingEngine(
            _gatewayService,
            _upnpService,
            udpProber: (_, _) => throw new SocketException(10060),
            upnpProber: _ => throw new TimeoutException("UPnP timeout"),
            _pcpClient);

        var protocol = await engine.DetectProtocolAsync();

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.None));
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.None));
    }

    [Test]
    public async Task ProbeAsync_delegates_to_DetectProtocolAsync()
    {
        _gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(IPAddress.Parse("192.168.1.1")));

        var engine = new PortMappingEngine(
            _gatewayService,
            _upnpService,
            udpProber: (_, _) => Task.FromResult(PortMappingProtocol.Pcp),
            upnpProber: _ => Task.FromResult(false),
            _pcpClient);

        var protocol = await engine.ProbeAsync();

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.Pcp));
    }

    [Test]
    public void DetectProtocol_synchronously_negotiates_protocol()
    {
        _gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(IPAddress.Parse("192.168.1.1")));

        var engine = new PortMappingEngine(
            _gatewayService,
            _upnpService,
            udpProber: (_, _) => Task.FromResult(PortMappingProtocol.NatPmp),
            upnpProber: _ => Task.FromResult(false),
            _pcpClient);

        var protocol = engine.DetectProtocol();

        Assert.That(protocol, Is.EqualTo(PortMappingProtocol.NatPmp));
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.NatPmp));
    }

    [Test]
    public void ParseProbeResponse_returns_none_for_null_or_empty_buffer()
    {
        Assert.That(PortMappingEngine.ParseProbeResponse(null), Is.EqualTo(PortMappingProtocol.None));
        Assert.That(PortMappingEngine.ParseProbeResponse(Array.Empty<byte>()), Is.EqualTo(PortMappingProtocol.None));
    }

    [Test]
    public void ParseProbeResponse_identifies_pcp_when_first_byte_is_two()
    {
        var response = new byte[] { 2, 0, 0, 0 };
        Assert.That(PortMappingEngine.ParseProbeResponse(response), Is.EqualTo(PortMappingProtocol.Pcp));
    }

    [Test]
    public void ParseProbeResponse_identifies_natpmp_when_version_and_result_are_zero()
    {
        var response = new byte[] { 0, 0x80, 0, 0 };
        Assert.That(PortMappingEngine.ParseProbeResponse(response), Is.EqualTo(PortMappingProtocol.NatPmp));
    }

    [Test]
    public void ParseProbeResponse_identifies_pcp_when_natpmp_unsupported_version_contains_byte_two()
    {
        var response = new byte[] { 0, 0x80, 0, 1, 0, 0, 2, 0 };
        Assert.That(PortMappingEngine.ParseProbeResponse(response), Is.EqualTo(PortMappingProtocol.Pcp));
    }

    [Test]
    public void ParseProbeResponse_returns_none_when_natpmp_unsupported_version_lacks_byte_two()
    {
        var response = new byte[] { 0, 0x80, 0, 1, 0, 0, 0, 0 };
        Assert.That(PortMappingEngine.ParseProbeResponse(response), Is.EqualTo(PortMappingProtocol.None));
    }

    [Test]
    public void ParseProbeResponse_returns_none_for_unrecognized_short_or_invalid_buffers()
    {
        var shortBuffer = new byte[] { 1, 0, 0 };
        var unknownProtocol = new byte[] { 5, 0, 0, 0 };

        Assert.That(PortMappingEngine.ParseProbeResponse(shortBuffer), Is.EqualTo(PortMappingProtocol.None));
        Assert.That(PortMappingEngine.ParseProbeResponse(unknownProtocol), Is.EqualTo(PortMappingProtocol.None));
    }

    [Test]
    public async Task ProbeUdpAsync_returns_none_when_gateway_address_is_null()
    {
        var engine = new PortMappingEngine(_gatewayService, _upnpService, null, null, _pcpClient);

        var result = await engine.ProbeUdpAsync(null, CancellationToken.None);

        Assert.That(result, Is.EqualTo(PortMappingProtocol.None));
    }

    [Test]
    public async Task ProbeUdpAsync_returns_none_when_cancellation_token_is_cancelled()
    {
        var engine = new PortMappingEngine(_gatewayService, _upnpService, null, null, _pcpClient);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await engine.ProbeUdpAsync(IPAddress.Loopback, cts.Token);

        Assert.That(result, Is.EqualTo(PortMappingProtocol.None));
    }

    [Test]
    public async Task ProbeUpnpAsync_returns_true_when_upnp_service_is_available()
    {
        _upnpService.IsAvailable.Returns(true);
        var engine = new PortMappingEngine(_gatewayService, _upnpService, null, null, _pcpClient);

        var result = await engine.ProbeUpnpAsync(CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task ProbeUpnpAsync_returns_false_when_service_unavailable_and_cancellation_requested()
    {
        _upnpService.IsAvailable.Returns(false);
        var engine = new PortMappingEngine(_gatewayService, _upnpService, null, null, _pcpClient);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await engine.ProbeUpnpAsync(cts.Token);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task GetStatusAsync_formats_protocol_string_and_populates_gateway()
    {
        var gatewayIp = IPAddress.Parse("192.168.1.1");
        _gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(gatewayIp));

        var engine = new PortMappingEngine(
            _gatewayService,
            _upnpService,
            udpProber: (_, _) => Task.FromResult(PortMappingProtocol.Pcp),
            upnpProber: _ => Task.FromResult(false),
            _pcpClient);

        await engine.DetectProtocolAsync();
        var status = await engine.GetStatusAsync();

        Assert.That(status.Protocol, Is.EqualTo("PCP"));
        Assert.That(status.GatewayIp, Is.EqualTo("192.168.1.1"));
        Assert.That(status.RouterModel, Is.EqualTo("PCP Gateway"));
    }

    [Test]
    public async Task GetStatusAsync_falls_back_to_upnp_protocol_when_engine_protocol_none_but_upnp_available()
    {
        _gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult<IPAddress>(null));
        _upnpService.IsAvailable.Returns(true);
        _upnpService.ExternalIp.Returns("203.0.113.5");
        _upnpService.RouterModel.Returns("Custom UPnP Router");

        var engine = new PortMappingEngine(_gatewayService, _upnpService, null, null, _pcpClient);
        var status = await engine.GetStatusAsync();

        Assert.That(status.Protocol, Is.EqualTo("UPnP"));
        Assert.That(status.ExternalIp, Is.EqualTo("203.0.113.5"));
        Assert.That(status.RouterModel, Is.EqualTo("Custom UPnP Router"));
    }

    [Test]
    public async Task GetStatusAsync_aggregates_pcp_and_upnp_mappings()
    {
        var now = DateTime.UtcNow;
        var pcpMapping = new PcpActiveMapping
        {
            InternalPort = 6881,
            AssignedExternalPort = 16881,
            Protocol = PortMappingTransport.Tcp,
            Lifetime = TimeSpan.FromSeconds(3600),
            CreatedAtUtc = now,
            IsActive = true
        };

        _pcpClient.ActiveMappings.Returns(new List<PcpActiveMapping> { pcpMapping });
        _pcpClient.ExternalAddress.Returns(IPAddress.Parse("198.51.100.1"));

        var upnpMapping = new PortMapping
        {
            InternalPort = 51413,
            ExternalPort = 51413,
            Protocol = "UDP",
            Description = "Seedarr UPnP",
            LeaseSeconds = 7200,
            ExpiryUtc = now.AddHours(2),
            IsActive = false,
            ErrorMessage = "Port conflicted"
        };

        _upnpService.GetMappings().Returns(new List<PortMapping> { upnpMapping });
        _upnpService.LastRenewalUtc.Returns(now.AddMinutes(-10));
        _upnpService.NextRenewalUtc.Returns(now.AddMinutes(50));

        var engine = new PortMappingEngine(_gatewayService, _upnpService, null, null, _pcpClient);
        var status = await engine.GetStatusAsync();

        Assert.That(status.ExternalIp, Is.EqualTo("198.51.100.1"));
        Assert.That(status.LastRenewalUtc, Is.EqualTo(now.AddMinutes(-10)));
        Assert.That(status.NextRenewalUtc, Is.EqualTo(now.AddMinutes(50)));
        Assert.That(status.Mappings.Count, Is.EqualTo(2));

        var enrichedPcp = status.Mappings[0];
        Assert.That(enrichedPcp.InternalPort, Is.EqualTo(6881));
        Assert.That(enrichedPcp.ExternalPort, Is.EqualTo(16881));
        Assert.That(enrichedPcp.Protocol, Is.EqualTo("TCP"));
        Assert.That(enrichedPcp.Description, Is.EqualTo("PCP Mapping"));
        Assert.That(enrichedPcp.LeaseSeconds, Is.EqualTo(3600));
        Assert.That(enrichedPcp.IsActive, Is.True);
        Assert.That(enrichedPcp.Status, Is.EqualTo("Active"));

        var enrichedUpnp = status.Mappings[1];
        Assert.That(enrichedUpnp.InternalPort, Is.EqualTo(51413));
        Assert.That(enrichedUpnp.ExternalPort, Is.EqualTo(51413));
        Assert.That(enrichedUpnp.Protocol, Is.EqualTo("UDP"));
        Assert.That(enrichedUpnp.Description, Is.EqualTo("Seedarr UPnP"));
        Assert.That(enrichedUpnp.IsActive, Is.False);
        Assert.That(enrichedUpnp.Status, Is.EqualTo("Inactive"));
        Assert.That(enrichedUpnp.ErrorMessage, Is.EqualTo("Port conflicted"));
    }

    [Test]
    public async Task GetStatusAsync_handles_gateway_discovery_exception_gracefully()
    {
        _gatewayService.GetDefaultGatewayAsync().Returns<Task<IPAddress>>(_ => throw new InvalidOperationException("Gateway unreachable"));

        var engine = new PortMappingEngine(_gatewayService, _upnpService, null, null, _pcpClient);
        var status = await engine.GetStatusAsync();

        Assert.That(status.GatewayIp, Is.Empty);
    }

    [Test]
    public async Task RefreshAsync_detects_protocol_and_refreshes_upnp_mappings()
    {
        _gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(IPAddress.Parse("192.168.1.1")));
        _upnpService.CreateMappings(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var engine = new PortMappingEngine(
            _gatewayService,
            _upnpService,
            udpProber: (_, _) => Task.FromResult(PortMappingProtocol.NatPmp),
            upnpProber: _ => Task.FromResult(false),
            _pcpClient);

        var status = await engine.RefreshAsync();

        Assert.That(status.Protocol, Is.EqualTo("NAT-PMP"));
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.NatPmp));
        await _upnpService.Received(1).CreateMappings(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefreshAsync_handles_exceptions_during_detection_and_mapping_gracefully()
    {
        _gatewayService.GetDefaultGatewayAsync().Returns<Task<IPAddress>>(_ => throw new InvalidOperationException("Network down"));
        _upnpService.CreateMappings(Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new TimeoutException("UPnP failure"));

        var engine = new PortMappingEngine(
            _gatewayService,
            _upnpService,
            udpProber: (_, _) => throw new SocketException(10054),
            upnpProber: _ => throw new TimeoutException("UPnP probe timeout"),
            _pcpClient);

        var status = await engine.RefreshAsync();

        Assert.That(status.Protocol, Is.EqualTo("None"));
    }

    [Test]
    public void PcpPacket_GenerateNonce_returns_unique_12_byte_buffers()
    {
        var nonce1 = PcpPacket.GenerateNonce();
        var nonce2 = PcpPacket.GenerateNonce();

        Assert.That(nonce1.Length, Is.EqualTo(12));
        Assert.That(nonce2.Length, Is.EqualTo(12));
        Assert.That(nonce1, Is.Not.EqualTo(nonce2));
    }

    [Test]
    public void PcpPacket_ConvertIpTo16Bytes_handles_null_none_ipv4_and_ipv6()
    {
        var nullBytes = PcpPacket.ConvertIpTo16Bytes(null);
        var noneBytes = PcpPacket.ConvertIpTo16Bytes(IPAddress.None);

        Assert.That(nullBytes.Length, Is.EqualTo(16));
        Assert.That(noneBytes.Length, Is.EqualTo(16));
        Assert.That(nullBytes, Is.EqualTo(new byte[16]));
        Assert.That(noneBytes, Is.EqualTo(new byte[16]));

        var ipv4 = IPAddress.Parse("192.168.1.100");
        var ipv4Bytes = PcpPacket.ConvertIpTo16Bytes(ipv4);
        Assert.That(ipv4Bytes.Length, Is.EqualTo(16));
        var roundTrippedIpv4 = PcpPacket.Convert16BytesToIp(ipv4Bytes);
        Assert.That(roundTrippedIpv4, Is.EqualTo(ipv4));

        var ipv6 = IPAddress.Parse("2001:db8::1");
        var ipv6Bytes = PcpPacket.ConvertIpTo16Bytes(ipv6);
        Assert.That(ipv6Bytes.Length, Is.EqualTo(16));
        var roundTrippedIpv6 = PcpPacket.Convert16BytesToIp(ipv6Bytes);
        Assert.That(roundTrippedIpv6, Is.EqualTo(ipv6));
    }

    [Test]
    public void PcpPacket_Convert16BytesToIp_throws_when_buffer_less_than_16_bytes()
    {
        var shortBuffer = new byte[15];
        var ex = Assert.Throws<ArgumentException>(() => PcpPacket.Convert16BytesToIp(shortBuffer));
        Assert.That(ex.Message, Does.Contain("16 bytes required"));
    }

    [Test]
    public void PcpPacket_CreateMapRequest_throws_for_invalid_arguments()
    {
        var validIp = IPAddress.Parse("192.168.1.10");

        Assert.Throws<ArgumentNullException>(() =>
            PcpPacket.CreateMapRequest(null, 6881, 6881, PortMappingTransport.Tcp, 3600));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PcpPacket.CreateMapRequest(validIp, -1, 6881, PortMappingTransport.Tcp, 3600));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PcpPacket.CreateMapRequest(validIp, 70000, 6881, PortMappingTransport.Tcp, 3600));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PcpPacket.CreateMapRequest(validIp, 6881, -1, PortMappingTransport.Tcp, 3600));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PcpPacket.CreateMapRequest(validIp, 6881, 70000, PortMappingTransport.Tcp, 3600));

        var invalidNonce = new byte[10];
        Assert.Throws<ArgumentException>(() =>
            PcpPacket.CreateMapRequest(validIp, 6881, 6881, PortMappingTransport.Tcp, 3600, invalidNonce));
    }

    [Test]
    public void PcpPacket_CreateDeleteMapRequest_encodes_zero_lifetime_and_suggested_port()
    {
        var clientIp = IPAddress.Parse("192.168.1.20");
        var nonce = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

        var packet = PcpPacket.CreateDeleteMapRequest(clientIp, 8080, PortMappingTransport.Tcp, nonce);

        Assert.That(packet.Length, Is.EqualTo(PcpPacket.TotalPacketLength));
        Assert.That(packet[0], Is.EqualTo(PcpPacket.Version));
        Assert.That(packet[1], Is.EqualTo(PcpPacket.OpcodeMap));

        var lifetime = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(4, 4));
        var internalPort = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(40, 2));
        var suggestedPort = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(42, 2));

        Assert.That(lifetime, Is.EqualTo(0));
        Assert.That(internalPort, Is.EqualTo(8080));
        Assert.That(suggestedPort, Is.EqualTo(0));

        Assert.Throws<ArgumentException>(() =>
            PcpPacket.CreateDeleteMapRequest(clientIp, 8080, PortMappingTransport.Tcp, null));

        Assert.Throws<ArgumentException>(() =>
            PcpPacket.CreateDeleteMapRequest(clientIp, 8080, PortMappingTransport.Tcp, new byte[11]));
    }

    [Test]
    public void PcpPacket_ParseMapResponse_parses_natpmp_short_packets()
    {
        var natPmp4ByteResponse = new byte[] { 0, 0x81, 0, 1 };
        var response4 = PcpPacket.ParseMapResponse(natPmp4ByteResponse);

        Assert.That(response4.Version, Is.EqualTo(0));
        Assert.That(response4.Opcode, Is.EqualTo(0x81));
        Assert.That(response4.ResultCode, Is.EqualTo(PcpResultCode.UnsupportedVersion));
        Assert.That(response4.Epoch, Is.EqualTo(0));

        var natPmp8ByteResponse = new byte[] { 0, 0x81, 0, 0, 0, 0, 0, 120 };
        var response8 = PcpPacket.ParseMapResponse(natPmp8ByteResponse);

        Assert.That(response8.Version, Is.EqualTo(0));
        Assert.That(response8.ResultCode, Is.EqualTo(PcpResultCode.Success));
        Assert.That(response8.Epoch, Is.EqualTo(120));
    }

    [Test]
    public void PcpPacket_ParseMapResponse_throws_when_buffer_less_than_header_length_for_pcp()
    {
        var shortPcpPacket = new byte[] { 2, 0x81, 0, 0, 0, 0, 0, 0 };

        var ex = Assert.Throws<PcpException>(() => PcpPacket.ParseMapResponse(shortPcpPacket));
        Assert.That(ex.Message, Does.Contain("too short"));
    }

    [Test]
    public void PcpPacket_ParseMapResponse_throws_when_opcode_is_invalid()
    {
        var buffer = new byte[PcpPacket.TotalPacketLength];
        buffer[0] = 2;
        buffer[1] = 99; // Invalid opcode

        var ex = Assert.Throws<PcpException>(() => PcpPacket.ParseMapResponse(buffer));
        Assert.That(ex.Message, Does.Contain("Invalid opcode"));
    }

    [Test]
    public void PcpPacket_ParseMapResponse_throws_when_expected_nonce_is_missing_in_short_success_packet()
    {
        var buffer = new byte[PcpPacket.HeaderLength]; // 24 bytes, lacks nonce
        buffer[0] = 2;
        buffer[1] = PcpPacket.OpcodeMapResponse;
        buffer[2] = 0;
        buffer[3] = (byte)PcpResultCode.Success;

        var expectedNonce = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

        var ex = Assert.Throws<PcpException>(() => PcpPacket.ParseMapResponse(buffer, expectedNonce));
        Assert.That(ex.Message, Does.Contain("too short to contain expected mapping nonce"));
    }

    [TestCase(PcpResultCode.Success, "Success")]
    [TestCase(PcpResultCode.UnsupportedVersion, "Unsupported PCP version")]
    [TestCase(PcpResultCode.NotAuthorized, "Not authorized")]
    [TestCase(PcpResultCode.MalformedRequest, "Malformed request")]
    [TestCase(PcpResultCode.UnsupportedOpcode, "Unsupported opcode")]
    [TestCase(PcpResultCode.UnsupportedOption, "Unsupported option")]
    [TestCase(PcpResultCode.MalformedOption, "Malformed option")]
    [TestCase(PcpResultCode.NetworkFailure, "Network failure")]
    [TestCase(PcpResultCode.NoResources, "No resources")]
    [TestCase(PcpResultCode.UnsupportedProtocol, "Unsupported protocol")]
    [TestCase(PcpResultCode.UserExceededQuota, "User exceeded quota")]
    [TestCase(PcpResultCode.CannotProvideExternal, "Cannot provide suggested external port/IP")]
    [TestCase(PcpResultCode.AddressMismatch, "Address mismatch")]
    [TestCase(PcpResultCode.ExcessiveRemotePeers, "Excessive remote peers")]
    [TestCase((PcpResultCode)99, "Unknown PCP result code: 99")]
    public void PcpPacket_GetResultCodeMessage_returns_expected_text_for_all_codes(PcpResultCode code, string expected)
    {
        var message = PcpPacket.GetResultCodeMessage(code);
        Assert.That(message, Is.EqualTo(expected));
    }
}
