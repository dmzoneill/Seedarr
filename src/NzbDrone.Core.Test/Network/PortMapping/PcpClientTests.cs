using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Pcp;

namespace NzbDrone.Core.Test.Network;

[TestFixture]
public class PcpClientTests
{
    private class TestPcpTransport : IPcpTransport
    {
        public List<byte[]> SentRequests { get; } = new();
        public List<byte[]> SentAsyncPackets { get; } = new();
        public Queue<byte[]> Responses { get; } = new();
        public Func<byte[], byte[]> ResponseHandler { get; set; }

        public Task<byte[]> SendAndReceiveAsync(byte[] request, IPEndPoint endpoint, CancellationToken cancellationToken = default)
        {
            SentRequests.Add(request);
            if (ResponseHandler != null)
            {
                return Task.FromResult(ResponseHandler(request));
            }

            if (Responses.Count > 0)
            {
                return Task.FromResult(Responses.Dequeue());
            }

            throw new InvalidOperationException("No response queued in TestPcpTransport");
        }

        public Task SendAsync(byte[] request, IPEndPoint endpoint, CancellationToken cancellationToken = default)
        {
            SentAsyncPackets.Add(request);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private static byte[] BuildMapResponse(
        PcpResultCode resultCode,
        uint lifetimeSeconds,
        uint epoch,
        byte[] nonce,
        PortMappingTransport protocol,
        ushort internalPort,
        ushort assignedExternalPort,
        IPAddress assignedAddress,
        byte version = 2)
    {
        var buffer = new byte[PcpPacket.TotalPacketLength];
        buffer[0] = version;
        buffer[1] = PcpPacket.OpcodeMapResponse;
        buffer[2] = 0;
        buffer[3] = (byte)resultCode;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(4, 4), lifetimeSeconds);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(8, 4), epoch);

        if (nonce != null)
        {
            nonce.CopyTo(buffer, 24);
        }

        buffer[36] = (byte)protocol;
        buffer[37] = 0;
        buffer[38] = 0;
        buffer[39] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(40, 2), internalPort);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(42, 2), assignedExternalPort);

        if (assignedAddress != null)
        {
            var ipBytes = PcpPacket.ConvertIpTo16Bytes(assignedAddress);
            ipBytes.CopyTo(buffer, 44);
        }

        return buffer;
    }

    [Test]
    public void CreateMapRequest_should_correctly_encode_60_byte_packet_for_ipv4()
    {
        var clientIp = IPAddress.Parse("192.168.1.105");
        var nonce = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        var suggestedIp = IPAddress.Parse("203.0.113.10");

        var packet = PcpPacket.CreateMapRequest(
            clientIp: clientIp,
            internalPort: 6881,
            suggestedExternalPort: 6881,
            protocol: PortMappingTransport.Tcp,
            lifetimeSeconds: 3600,
            nonce: nonce,
            suggestedExternalIp: suggestedIp);

        Assert.That(packet.Length, Is.EqualTo(60));
        Assert.That(packet[0], Is.EqualTo(2)); // Version 2
        Assert.That(packet[1], Is.EqualTo(1)); // Opcode MAP
        Assert.That(packet[2], Is.EqualTo(0));
        Assert.That(packet[3], Is.EqualTo(0));

        var lifetime = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(4, 4));
        Assert.That(lifetime, Is.EqualTo(3600));

        // Client IP as IPv4-mapped IPv6: ::ffff:192.168.1.105
        var clientIpFromPacket = new IPAddress(packet.AsSpan(8, 16));
        Assert.That(clientIpFromPacket.IsIPv4MappedToIPv6, Is.True);
        Assert.That(clientIpFromPacket.MapToIPv4(), Is.EqualTo(clientIp));

        // Nonce
        var nonceFromPacket = packet.AsSpan(24, 12).ToArray();
        Assert.That(nonceFromPacket, Is.EqualTo(nonce));

        // Protocol (TCP = 6)
        Assert.That(packet[36], Is.EqualTo(6));

        // Ports
        var internalPort = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(40, 2));
        var externalPort = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(42, 2));
        Assert.That(internalPort, Is.EqualTo(6881));
        Assert.That(externalPort, Is.EqualTo(6881));

        // Suggested External IP
        var suggestedIpFromPacket = new IPAddress(packet.AsSpan(44, 16));
        Assert.That(suggestedIpFromPacket.IsIPv4MappedToIPv6, Is.True);
        Assert.That(suggestedIpFromPacket.MapToIPv4(), Is.EqualTo(suggestedIp));
    }

    [Test]
    public void CreateMapRequest_should_correctly_encode_60_byte_packet_for_ipv6()
    {
        var clientIp = IPAddress.Parse("2001:db8::cafe");
        var nonce = new byte[] { 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };
        var suggestedIp = IPAddress.Parse("2001:db8::1");

        var packet = PcpPacket.CreateMapRequest(
            clientIp: clientIp,
            internalPort: 51413,
            suggestedExternalPort: 51413,
            protocol: PortMappingTransport.Udp,
            lifetimeSeconds: 7200,
            nonce: nonce,
            suggestedExternalIp: suggestedIp);

        Assert.That(packet.Length, Is.EqualTo(60));
        Assert.That(packet[0], Is.EqualTo(2));
        Assert.That(packet[1], Is.EqualTo(1));

        var lifetime = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(4, 4));
        Assert.That(lifetime, Is.EqualTo(7200));

        var clientIpFromPacket = new IPAddress(packet.AsSpan(8, 16));
        Assert.That(clientIpFromPacket, Is.EqualTo(clientIp));

        Assert.That(packet[36], Is.EqualTo(17)); // UDP = 17

        var internalPort = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(40, 2));
        var externalPort = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(42, 2));
        Assert.That(internalPort, Is.EqualTo(51413));
        Assert.That(externalPort, Is.EqualTo(51413));

        var externalIpFromPacket = new IPAddress(packet.AsSpan(44, 16));
        Assert.That(externalIpFromPacket, Is.EqualTo(suggestedIp));
    }

    [Test]
    public void CreateDeleteMapRequest_should_have_zero_lifetime_and_external_port()
    {
        var clientIp = IPAddress.Parse("192.168.1.105");
        var nonce = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

        var packet = PcpPacket.CreateDeleteMapRequest(clientIp, 6881, PortMappingTransport.Tcp, nonce);

        Assert.That(packet.Length, Is.EqualTo(60));

        var lifetime = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(4, 4));
        Assert.That(lifetime, Is.EqualTo(0));

        var suggestedExternalPort = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(42, 2));
        Assert.That(suggestedExternalPort, Is.EqualTo(0));

        var suggestedIp = new IPAddress(packet.AsSpan(44, 16));
        Assert.That(suggestedIp, Is.EqualTo(IPAddress.IPv6Any));
    }

    [Test]
    public void ParseMapResponse_should_extract_assigned_external_port_and_ip_ipv4()
    {
        var nonce = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120 };
        var externalIp = IPAddress.Parse("198.51.100.1");

        var responseBuffer = BuildMapResponse(
            resultCode: PcpResultCode.Success,
            lifetimeSeconds: 3600,
            epoch: 123456,
            nonce: nonce,
            protocol: PortMappingTransport.Tcp,
            internalPort: 6881,
            assignedExternalPort: 16881,
            assignedAddress: externalIp);

        var response = PcpPacket.ParseMapResponse(responseBuffer, nonce);

        Assert.That(response.Version, Is.EqualTo(2));
        Assert.That(response.Opcode, Is.EqualTo(PcpPacket.OpcodeMapResponse));
        Assert.That(response.ResultCode, Is.EqualTo(PcpResultCode.Success));
        Assert.That(response.LifetimeSeconds, Is.EqualTo(3600));
        Assert.That(response.Epoch, Is.EqualTo(123456));
        Assert.That(response.InternalPort, Is.EqualTo(6881));
        Assert.That(response.AssignedExternalPort, Is.EqualTo(16881));
        Assert.That(response.AssignedExternalAddress, Is.EqualTo(externalIp));
    }

    [Test]
    public void ParseMapResponse_should_extract_assigned_external_port_and_ip_ipv6()
    {
        var nonce = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120 };
        var externalIpv6 = IPAddress.Parse("2001:db8::beef");

        var responseBuffer = BuildMapResponse(
            resultCode: PcpResultCode.Success,
            lifetimeSeconds: 7200,
            epoch: 654321,
            nonce: nonce,
            protocol: PortMappingTransport.Udp,
            internalPort: 51413,
            assignedExternalPort: 51413,
            assignedAddress: externalIpv6);

        var response = PcpPacket.ParseMapResponse(responseBuffer, nonce);

        Assert.That(response.ResultCode, Is.EqualTo(PcpResultCode.Success));
        Assert.That(response.AssignedExternalAddress, Is.EqualTo(externalIpv6));
        Assert.That(response.AssignedExternalPort, Is.EqualTo(51413));
    }

    [Test]
    public void ParseMapResponse_should_reject_mismatched_nonce()
    {
        var requestedNonce = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        var spoofedNonce = new byte[] { 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99 };

        var responseBuffer = BuildMapResponse(
            resultCode: PcpResultCode.Success,
            lifetimeSeconds: 3600,
            epoch: 100,
            nonce: spoofedNonce,
            protocol: PortMappingTransport.Tcp,
            internalPort: 6881,
            assignedExternalPort: 6881,
            assignedAddress: IPAddress.Parse("198.51.100.1"));

        var ex = Assert.Throws<PcpException>(() => PcpPacket.ParseMapResponse(responseBuffer, requestedNonce));
        Assert.That(ex.Message, Does.Contain("nonce mismatch"));
    }

    [TestCase(PcpResultCode.UnsupportedVersion, "Unsupported PCP version")]
    [TestCase(PcpResultCode.NotAuthorized, "Not authorized")]
    [TestCase(PcpResultCode.MalformedRequest, "Malformed request")]
    [TestCase(PcpResultCode.UnsupportedOpcode, "Unsupported opcode")]
    [TestCase(PcpResultCode.NetworkFailure, "Network failure")]
    [TestCase(PcpResultCode.NoResources, "No resources")]
    [TestCase(PcpResultCode.CannotProvideExternal, "Cannot provide suggested external port/IP")]
    [TestCase(PcpResultCode.AddressMismatch, "Address mismatch")]
    public void ParseMapResponse_should_correctly_decode_error_result_codes(PcpResultCode resultCode, string expectedSubstring)
    {
        var nonce = new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
        var responseBuffer = BuildMapResponse(
            resultCode: resultCode,
            lifetimeSeconds: 0,
            epoch: 50,
            nonce: nonce,
            protocol: PortMappingTransport.Tcp,
            internalPort: 8080,
            assignedExternalPort: 0,
            assignedAddress: IPAddress.Any);

        var response = PcpPacket.ParseMapResponse(responseBuffer, nonce);

        Assert.That(response.ResultCode, Is.EqualTo(resultCode));
        Assert.That(PcpPacket.GetResultCodeMessage(response.ResultCode), Does.Contain(expectedSubstring));
    }

    [Test]
    public void ParseMapResponse_should_demote_to_natpmp_when_version_0_returned()
    {
        // NAT-PMP router returns 4-byte or 8-byte response with version 0 and result code 1 (Unsupported Version)
        var natPmpResponse = new byte[] { 0, 0x81, 0, 1, 0, 0, 0, 100 };

        var response = PcpPacket.ParseMapResponse(natPmpResponse);

        Assert.That(response.Version, Is.EqualTo(0));
        Assert.That(response.ResultCode, Is.EqualTo(PcpResultCode.UnsupportedVersion));
    }

    [Test]
    public async Task PcpClient_CreateMappingAsync_should_succeed_and_store_mapping()
    {
        var transport = new TestPcpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");
        var clientIp = IPAddress.Parse("192.168.1.105");
        var externalIp = IPAddress.Parse("203.0.113.1");

        transport.ResponseHandler = request =>
        {
            var reqNonce = request.AsSpan(24, 12).ToArray();
            return BuildMapResponse(
                resultCode: PcpResultCode.Success,
                lifetimeSeconds: 3600,
                epoch: 500,
                nonce: reqNonce,
                protocol: PortMappingTransport.Tcp,
                internalPort: 6881,
                assignedExternalPort: 16881,
                assignedAddress: externalIp);
        };

        using var client = new PcpClient(transport);
        var result = await client.CreateMappingAsync(
            gateway: gatewayIp,
            clientIp: clientIp,
            internalPort: 6881,
            suggestedExternalPort: 6881,
            protocol: PortMappingTransport.Tcp,
            lifetime: TimeSpan.FromSeconds(3600));

        Assert.That(result.Success, Is.True);
        Assert.That(result.ResultCode, Is.EqualTo(PcpResultCode.Success));
        Assert.That(result.ExternalPort, Is.EqualTo(16881));
        Assert.That(result.ExternalAddress, Is.EqualTo(externalIp));
        Assert.That(client.ExternalAddress, Is.EqualTo(externalIp));

        Assert.That(client.ActiveMappings.Count, Is.EqualTo(1));
        var mapping = client.ActiveMappings[0];
        Assert.That(mapping.InternalPort, Is.EqualTo(6881));
        Assert.That(mapping.AssignedExternalPort, Is.EqualTo(16881));
        Assert.That(mapping.Protocol, Is.EqualTo(PortMappingTransport.Tcp));
    }

    [Test]
    public async Task PcpClient_CreateMappingAsync_should_return_error_result_on_failure()
    {
        var transport = new TestPcpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");
        var clientIp = IPAddress.Parse("192.168.1.105");

        transport.ResponseHandler = request =>
        {
            var reqNonce = request.AsSpan(24, 12).ToArray();
            return BuildMapResponse(
                resultCode: PcpResultCode.NotAuthorized,
                lifetimeSeconds: 0,
                epoch: 500,
                nonce: reqNonce,
                protocol: PortMappingTransport.Tcp,
                internalPort: 8080,
                assignedExternalPort: 0,
                assignedAddress: IPAddress.Any);
        };

        using var client = new PcpClient(transport);
        var result = await client.CreateMappingAsync(
            gateway: gatewayIp,
            clientIp: clientIp,
            internalPort: 8080,
            suggestedExternalPort: 8080,
            protocol: PortMappingTransport.Tcp,
            lifetime: TimeSpan.FromSeconds(3600));

        Assert.That(result.Success, Is.False);
        Assert.That(result.ResultCode, Is.EqualTo(PcpResultCode.NotAuthorized));
        Assert.That(result.ErrorMessage, Does.Contain("Not authorized"));
        Assert.That(client.ActiveMappings.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task PcpClient_DeleteMappingAsync_should_remove_mapping_and_send_delete()
    {
        var transport = new TestPcpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");
        var clientIp = IPAddress.Parse("192.168.1.105");
        var externalIp = IPAddress.Parse("203.0.113.1");

        transport.ResponseHandler = request =>
        {
            var reqNonce = request.AsSpan(24, 12).ToArray();
            var lifetime = BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(4, 4));
            return BuildMapResponse(
                resultCode: PcpResultCode.Success,
                lifetimeSeconds: lifetime,
                epoch: 500,
                nonce: reqNonce,
                protocol: PortMappingTransport.Tcp,
                internalPort: 6881,
                assignedExternalPort: (ushort)(lifetime == 0 ? 0 : 16881),
                assignedAddress: lifetime == 0 ? IPAddress.IPv6Any : externalIp);
        };

        using var client = new PcpClient(transport);
        await client.CreateMappingAsync(gatewayIp, clientIp, 6881, 6881, PortMappingTransport.Tcp, TimeSpan.FromSeconds(3600));
        Assert.That(client.ActiveMappings.Count, Is.EqualTo(1));

        var deleteResult = await client.DeleteMappingAsync(gatewayIp, clientIp, 6881, PortMappingTransport.Tcp);
        Assert.That(deleteResult, Is.True);
        Assert.That(client.ActiveMappings.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task PortMappingEngine_CreatePcpMappingAsync_should_demote_to_NatPmp_on_UnsupportedVersion()
    {
        var gatewayService = Substitute.For<IGatewayDiscoveryService>();
        gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(IPAddress.Parse("192.168.1.1")));

        var pcpClient = Substitute.For<IPcpClient>();
        pcpClient.CreateMappingAsync(
            Arg.Any<IPAddress>(),
            Arg.Any<IPAddress>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<PortMappingTransport>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PcpMappingResult
            {
                Success = false,
                Version = 0,
                ResultCode = PcpResultCode.UnsupportedVersion,
                ErrorMessage = "Unsupported PCP version"
            }));

        var engine = new PortMappingEngine(
            gatewayDiscoveryService: gatewayService,
            pcpClient: pcpClient,
            udpProber: (_, _) => Task.FromResult(PortMappingProtocol.Pcp));

        await engine.DetectProtocolAsync();
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.Pcp));

        var result = await engine.CreatePcpMappingAsync(
            gateway: IPAddress.Parse("192.168.1.1"),
            clientIp: IPAddress.Parse("192.168.1.105"),
            internalPort: 6881,
            suggestedExternalPort: 6881,
            protocol: PortMappingTransport.Tcp,
            lifetime: TimeSpan.FromSeconds(3600));

        Assert.That(result.ResultCode, Is.EqualTo(PcpResultCode.UnsupportedVersion));
        Assert.That(engine.CurrentProtocol, Is.EqualTo(PortMappingProtocol.NatPmp));
    }

    [Test]
    public async Task PortMappingEngine_GetStatusAsync_should_include_pcp_active_mappings()
    {
        var gatewayService = Substitute.For<IGatewayDiscoveryService>();
        gatewayService.GetDefaultGatewayAsync().Returns(Task.FromResult(IPAddress.Parse("192.168.1.1")));

        var pcpClient = Substitute.For<IPcpClient>();
        var externalIp = IPAddress.Parse("203.0.113.19");
        pcpClient.ExternalAddress.Returns(externalIp);

        var activeMapping = new PcpActiveMapping
        {
            InternalPort = 6881,
            AssignedExternalPort = 16881,
            Protocol = PortMappingTransport.Tcp,
            Lifetime = TimeSpan.FromSeconds(3600),
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };
        pcpClient.ActiveMappings.Returns(new List<PcpActiveMapping> { activeMapping });

        var engine = new PortMappingEngine(
            gatewayDiscoveryService: gatewayService,
            pcpClient: pcpClient,
            udpProber: (_, _) => Task.FromResult(PortMappingProtocol.Pcp));

        await engine.DetectProtocolAsync();
        var status = await engine.GetStatusAsync();

        Assert.That(status.Protocol, Is.EqualTo("PCP"));
        Assert.That(status.ExternalIp, Is.EqualTo("203.0.113.19"));
        Assert.That(status.RouterModel, Is.EqualTo("PCP Gateway"));
        Assert.That(status.Mappings.Count, Is.EqualTo(1));
        Assert.That(status.Mappings[0].InternalPort, Is.EqualTo(6881));
        Assert.That(status.Mappings[0].ExternalPort, Is.EqualTo(16881));
        Assert.That(status.Mappings[0].Protocol, Is.EqualTo("TCP"));
        Assert.That(status.Mappings[0].Status, Is.EqualTo("Active"));
    }
}
