using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Network.NatPmp;

namespace NzbDrone.Core.Test.Network;

[TestFixture]
public class NatPmpClientTests
{
    private class TestNatPmpTransport : INatPmpTransport
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

            throw new InvalidOperationException("No response queued in TestNatPmpTransport");
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

    private class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualTimeProvider(DateTimeOffset initial)
        {
            _now = initial;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan timeSpan)
        {
            _now = _now.Add(timeSpan);
        }
    }

    private static byte[] BuildExternalIpResponse(NatPmpResultCode resultCode, uint epoch, IPAddress ip)
    {
        var buffer = new byte[NatPmpPacket.ExternalIpResponseLength];
        buffer[0] = NatPmpPacket.Version;
        buffer[1] = NatPmpPacket.OpcodeExternalIpResponse;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2, 2), (ushort)resultCode);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(4, 4), epoch);
        if (ip != null)
        {
            var bytes = ip.GetAddressBytes();
            bytes.CopyTo(buffer, 8);
        }

        return buffer;
    }

    private static byte[] BuildPortMappingResponse(
        NatPmpProtocol protocol,
        NatPmpResultCode resultCode,
        uint epoch,
        ushort internalPort,
        ushort mappedExternalPort,
        uint lifetime)
    {
        var buffer = new byte[NatPmpPacket.PortMappingResponseLength];
        buffer[0] = NatPmpPacket.Version;
        buffer[1] = (byte)(128 + (byte)protocol);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2, 2), (ushort)resultCode);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(4, 4), epoch);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(8, 2), internalPort);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(10, 2), mappedExternalPort);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(12, 4), lifetime);
        return buffer;
    }

    [Test]
    public void CreateExternalIpRequest_should_create_valid_opcode0_datagram()
    {
        var packet = NatPmpPacket.CreateExternalIpRequest();

        Assert.That(packet.Length, Is.EqualTo(2));
        Assert.That(packet[0], Is.EqualTo(0));
        Assert.That(packet[1], Is.EqualTo(0));
    }

    [Test]
    public void CreatePortMappingRequest_UDP_should_create_valid_12_byte_datagram()
    {
        var packet = NatPmpPacket.CreatePortMappingRequest(NatPmpProtocol.Udp, 6881, 6882, 3600);

        Assert.That(packet.Length, Is.EqualTo(12));
        Assert.That(packet[0], Is.EqualTo(0));
        Assert.That(packet[1], Is.EqualTo(1));
        Assert.That(packet[2], Is.EqualTo(0));
        Assert.That(packet[3], Is.EqualTo(0));

        var internalPort = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4, 2));
        var suggestedPort = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6, 2));
        var lifetime = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(8, 4));

        Assert.That(internalPort, Is.EqualTo(6881));
        Assert.That(suggestedPort, Is.EqualTo(6882));
        Assert.That(lifetime, Is.EqualTo(3600));
    }

    [Test]
    public void CreatePortMappingRequest_TCP_should_create_valid_12_byte_datagram()
    {
        var packet = NatPmpPacket.CreatePortMappingRequest(NatPmpProtocol.Tcp, 8080, 8080, 7200);

        Assert.That(packet.Length, Is.EqualTo(12));
        Assert.That(packet[0], Is.EqualTo(0));
        Assert.That(packet[1], Is.EqualTo(2));
        Assert.That(packet[2], Is.EqualTo(0));
        Assert.That(packet[3], Is.EqualTo(0));

        var internalPort = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4, 2));
        var suggestedPort = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6, 2));
        var lifetime = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(8, 4));

        Assert.That(internalPort, Is.EqualTo(8080));
        Assert.That(suggestedPort, Is.EqualTo(8080));
        Assert.That(lifetime, Is.EqualTo(7200));
    }

    [Test]
    public void CreateDeletePortMappingRequest_should_have_zero_lifetime_and_zero_suggested_port()
    {
        var packet = NatPmpPacket.CreateDeletePortMappingRequest(NatPmpProtocol.Tcp, 5000);

        Assert.That(packet.Length, Is.EqualTo(12));
        Assert.That(packet[1], Is.EqualTo(2));

        var internalPort = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4, 2));
        var suggestedPort = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6, 2));
        var lifetime = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(8, 4));

        Assert.That(internalPort, Is.EqualTo(5000));
        Assert.That(suggestedPort, Is.EqualTo(0));
        Assert.That(lifetime, Is.EqualTo(0));
    }

    [Test]
    public void ParseExternalIpResponse_should_parse_valid_response()
    {
        var raw = BuildExternalIpResponse(NatPmpResultCode.Success, 12345, IPAddress.Parse("203.0.113.195"));

        var response = NatPmpPacket.ParseExternalIpResponse(raw);

        Assert.That(response.Version, Is.EqualTo(0));
        Assert.That(response.Opcode, Is.EqualTo(128));
        Assert.That(response.ResultCode, Is.EqualTo(NatPmpResultCode.Success));
        Assert.That(response.Epoch, Is.EqualTo(12345));
        Assert.That(response.ExternalAddress, Is.EqualTo(IPAddress.Parse("203.0.113.195")));
    }

    [Test]
    public void ParseExternalIpResponse_should_throw_on_short_packet()
    {
        var shortPacket = new byte[7];

        Assert.Throws<NatPmpException>(() => NatPmpPacket.ParseExternalIpResponse(shortPacket));
    }

    [Test]
    public void ParseExternalIpResponse_should_throw_on_invalid_opcode()
    {
        var raw = BuildExternalIpResponse(NatPmpResultCode.Success, 100, IPAddress.Loopback);
        raw[1] = 129; // Wrong opcode

        Assert.Throws<NatPmpException>(() => NatPmpPacket.ParseExternalIpResponse(raw));
    }

    [Test]
    public void ParsePortMappingResponse_should_parse_valid_UDP_response()
    {
        var raw = BuildPortMappingResponse(NatPmpProtocol.Udp, NatPmpResultCode.Success, 9999, 6881, 6881, 3600);

        var response = NatPmpPacket.ParsePortMappingResponse(raw);

        Assert.That(response.Version, Is.EqualTo(0));
        Assert.That(response.Opcode, Is.EqualTo(129));
        Assert.That(response.ResultCode, Is.EqualTo(NatPmpResultCode.Success));
        Assert.That(response.Epoch, Is.EqualTo(9999));
        Assert.That(response.InternalPort, Is.EqualTo(6881));
        Assert.That(response.MappedExternalPort, Is.EqualTo(6881));
        Assert.That(response.Lifetime, Is.EqualTo(3600));
    }

    [Test]
    public void ParsePortMappingResponse_should_parse_valid_TCP_response()
    {
        var raw = BuildPortMappingResponse(NatPmpProtocol.Tcp, NatPmpResultCode.Success, 4321, 8080, 18080, 3600);

        var response = NatPmpPacket.ParsePortMappingResponse(raw);

        Assert.That(response.Version, Is.EqualTo(0));
        Assert.That(response.Opcode, Is.EqualTo(130));
        Assert.That(response.ResultCode, Is.EqualTo(NatPmpResultCode.Success));
        Assert.That(response.Epoch, Is.EqualTo(4321));
        Assert.That(response.InternalPort, Is.EqualTo(8080));
        Assert.That(response.MappedExternalPort, Is.EqualTo(18080));
        Assert.That(response.Lifetime, Is.EqualTo(3600));
    }

    [Test]
    public void ParsePortMappingResponse_should_throw_on_short_packet()
    {
        var shortPacket = new byte[7];

        Assert.Throws<NatPmpException>(() => NatPmpPacket.ParsePortMappingResponse(shortPacket));
    }

    [TestCase(NatPmpResultCode.UnsupportedVersion, "Unsupported version")]
    [TestCase(NatPmpResultCode.NotAuthorized, "Not authorized / Refused")]
    [TestCase(NatPmpResultCode.NetworkFailure, "Network failure")]
    [TestCase(NatPmpResultCode.OutOfResources, "Out of resources")]
    [TestCase(NatPmpResultCode.UnsupportedOpcode, "Unsupported opcode")]
    public void ResultCode_should_have_descriptive_messages(NatPmpResultCode code, string expectedSubstring)
    {
        var message = NatPmpPacket.GetResultCodeMessage(code);
        Assert.That(message, Does.Contain(expectedSubstring));
    }

    [Test]
    public async Task GetExternalIpAsync_should_query_opcode0_and_return_gateway_external_ip()
    {
        var transport = new TestNatPmpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");
        var expectedExternalIp = IPAddress.Parse("203.0.113.88");

        transport.Responses.Enqueue(BuildExternalIpResponse(NatPmpResultCode.Success, 5000, expectedExternalIp));

        using var client = new NatPmpClient(gatewayIp, transport);
        var actualIp = await client.GetExternalIpAsync();

        Assert.That(actualIp, Is.EqualTo(expectedExternalIp));
        Assert.That(client.LastEpoch, Is.EqualTo(5000));
        Assert.That(transport.SentRequests.Count, Is.EqualTo(1));
        Assert.That(transport.SentRequests[0], Is.EqualTo(new byte[] { 0, 0 }));
    }

    [Test]
    public void GetExternalIpAsync_should_throw_NatPmpException_when_gateway_returns_error()
    {
        var transport = new TestNatPmpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");

        transport.Responses.Enqueue(BuildExternalIpResponse(NatPmpResultCode.NotAuthorized, 100, IPAddress.None));

        using var client = new NatPmpClient(gatewayIp, transport);
        var ex = Assert.ThrowsAsync<NatPmpException>(async () => await client.GetExternalIpAsync());

        Assert.That(ex.ResultCode, Is.EqualTo(NatPmpResultCode.NotAuthorized));
    }

    [Test]
    public async Task MapPortAsync_should_map_TCP_port_and_store_active_mapping()
    {
        var transport = new TestNatPmpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");

        transport.Responses.Enqueue(BuildPortMappingResponse(NatPmpProtocol.Tcp, NatPmpResultCode.Success, 200, 6881, 6881, 3600));

        using var client = new NatPmpClient(gatewayIp, transport);
        var result = await client.MapPortAsync(NatPmpProtocol.Tcp, 6881, 6881, 3600);

        Assert.That(result.Protocol, Is.EqualTo(NatPmpProtocol.Tcp));
        Assert.That(result.InternalPort, Is.EqualTo(6881));
        Assert.That(result.MappedExternalPort, Is.EqualTo(6881));
        Assert.That(result.LifetimeSeconds, Is.EqualTo(3600));

        var mappings = client.ActiveMappings;
        Assert.That(mappings.Count, Is.EqualTo(1));
        Assert.That(mappings[0].InternalPort, Is.EqualTo(6881));
        Assert.That(mappings[0].MappedExternalPort, Is.EqualTo(6881));
        Assert.That(mappings[0].LifetimeSeconds, Is.EqualTo(3600));
    }

    [Test]
    public async Task MapPortAsync_when_gateway_assigns_different_external_port_should_store_assigned_port()
    {
        var transport = new TestNatPmpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");

        // Requested 6881, router assigned 16881
        transport.Responses.Enqueue(BuildPortMappingResponse(NatPmpProtocol.Udp, NatPmpResultCode.Success, 300, 6881, 16881, 3600));

        using var client = new NatPmpClient(gatewayIp, transport);
        var result = await client.MapPortAsync(NatPmpProtocol.Udp, 6881, 6881, 3600);

        Assert.That(result.MappedExternalPort, Is.EqualTo(16881));
        Assert.That(client.ActiveMappings[0].MappedExternalPort, Is.EqualTo(16881));
    }

    [Test]
    public void MapPortAsync_should_throw_NatPmpException_when_gateway_out_of_resources()
    {
        var transport = new TestNatPmpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");

        transport.Responses.Enqueue(BuildPortMappingResponse(NatPmpProtocol.Tcp, NatPmpResultCode.OutOfResources, 100, 8080, 0, 0));

        using var client = new NatPmpClient(gatewayIp, transport);
        var ex = Assert.ThrowsAsync<NatPmpException>(async () => await client.MapPortAsync(NatPmpProtocol.Tcp, 8080, 8080, 3600));

        Assert.That(ex.ResultCode, Is.EqualTo(NatPmpResultCode.OutOfResources));
        Assert.That(client.ActiveMappings.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task UnmapPortAsync_should_send_zero_lifetime_datagram_and_remove_mapping()
    {
        var transport = new TestNatPmpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");

        transport.Responses.Enqueue(BuildPortMappingResponse(NatPmpProtocol.Tcp, NatPmpResultCode.Success, 100, 5000, 5000, 3600));

        using var client = new NatPmpClient(gatewayIp, transport);
        await client.MapPortAsync(NatPmpProtocol.Tcp, 5000, 5000, 3600);
        Assert.That(client.ActiveMappings.Count, Is.EqualTo(1));

        await client.UnmapPortAsync(NatPmpProtocol.Tcp, 5000);

        Assert.That(client.ActiveMappings.Count, Is.EqualTo(0));
        Assert.That(transport.SentAsyncPackets.Count, Is.EqualTo(1));

        var unmapPacket = transport.SentAsyncPackets[0];
        Assert.That(unmapPacket.Length, Is.EqualTo(12));
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(unmapPacket.AsSpan(4, 2)), Is.EqualTo(5000));
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(unmapPacket.AsSpan(6, 2)), Is.EqualTo(0));
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(unmapPacket.AsSpan(8, 4)), Is.EqualTo(0));
    }

    [Test]
    public async Task HalfLife_renewal_calculation_should_be_half_of_granted_lifetime()
    {
        var transport = new TestNatPmpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(baseTime);

        // Gateway grants 3600 seconds lease
        transport.Responses.Enqueue(BuildPortMappingResponse(NatPmpProtocol.Tcp, NatPmpResultCode.Success, 500, 6881, 6881, 3600));

        using var client = new NatPmpClient(gatewayIp, transport, timeProvider);
        await client.MapPortAsync(NatPmpProtocol.Tcp, 6881, 6881, 3600);

        var mapping = client.ActiveMappings[0];
        var expectedRenewalTime = baseTime.UtcDateTime.AddSeconds(1800); // 3600 / 2 = 1800s

        Assert.That(mapping.RenewalDueUtc, Is.EqualTo(expectedRenewalTime));
    }

    [Test]
    public async Task RenewAllMappingsAsync_should_re_request_active_mappings_with_current_mapped_ports()
    {
        var transport = new TestNatPmpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");

        transport.Responses.Enqueue(BuildPortMappingResponse(NatPmpProtocol.Tcp, NatPmpResultCode.Success, 100, 6881, 16881, 3600));
        transport.Responses.Enqueue(BuildPortMappingResponse(NatPmpProtocol.Tcp, NatPmpResultCode.Success, 110, 6881, 16881, 3600));

        using var client = new NatPmpClient(gatewayIp, transport);
        await client.MapPortAsync(NatPmpProtocol.Tcp, 6881, 6881, 3600);

        await client.RenewAllMappingsAsync();

        // 1 initial map request + 1 renewal request
        Assert.That(transport.SentRequests.Count, Is.EqualTo(2));

        var renewPacket = transport.SentRequests[1];
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(renewPacket.AsSpan(4, 2)), Is.EqualTo(6881));
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(renewPacket.AsSpan(6, 2)), Is.EqualTo(16881)); // suggested port was mapped port
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(renewPacket.AsSpan(8, 4)), Is.EqualTo(3600));
    }

    [Test]
    public async Task Epoch_decrease_should_trigger_reboot_detection_and_reapply_mappings()
    {
        var transport = new TestNatPmpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");

        // Initial mapping at epoch 5000
        transport.Responses.Enqueue(BuildPortMappingResponse(NatPmpProtocol.Tcp, NatPmpResultCode.Success, 5000, 6881, 6881, 3600));
        // External IP request at epoch 100 (router rebooted!)
        transport.Responses.Enqueue(BuildExternalIpResponse(NatPmpResultCode.Success, 100, IPAddress.Parse("203.0.113.1")));
        // Follow-up renewal request response
        transport.Responses.Enqueue(BuildPortMappingResponse(NatPmpProtocol.Tcp, NatPmpResultCode.Success, 105, 6881, 6881, 3600));

        using var client = new NatPmpClient(gatewayIp, transport);

        NatPmpEpochResetEventArgs resetEvent = null;
        client.EpochReset += (sender, args) => { resetEvent = args; };

        await client.MapPortAsync(NatPmpProtocol.Tcp, 6881, 6881, 3600);
        Assert.That(client.LastEpoch, Is.EqualTo(5000));

        // Router rebooted, next query receives epoch 100 < 5000
        await client.GetExternalIpAsync();

        Assert.That(client.LastEpoch, Is.EqualTo(100));
        Assert.That(resetEvent, Is.Not.Null);
        Assert.That(resetEvent.PreviousEpoch, Is.EqualTo(5000));
        Assert.That(resetEvent.NewEpoch, Is.EqualTo(100));

        // Allow background re-mapping task to run
        await Task.Delay(50);
        Assert.That(transport.SentRequests.Count, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public async Task Epoch_increase_should_not_trigger_reboot()
    {
        var transport = new TestNatPmpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");

        transport.Responses.Enqueue(BuildExternalIpResponse(NatPmpResultCode.Success, 1000, IPAddress.Parse("203.0.113.1")));
        transport.Responses.Enqueue(BuildExternalIpResponse(NatPmpResultCode.Success, 1050, IPAddress.Parse("203.0.113.1")));

        using var client = new NatPmpClient(gatewayIp, transport);

        var resetTriggered = false;
        client.EpochReset += (sender, args) => { resetTriggered = true; };

        await client.GetExternalIpAsync();
        await client.GetExternalIpAsync();

        Assert.That(client.LastEpoch, Is.EqualTo(1050));
        Assert.That(resetTriggered, Is.False);
    }

    [Test]
    public async Task ReleaseAllMappingsAsync_should_send_lifetime_zero_for_all_active_mappings()
    {
        var transport = new TestNatPmpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");

        transport.Responses.Enqueue(BuildPortMappingResponse(NatPmpProtocol.Tcp, NatPmpResultCode.Success, 100, 6881, 6881, 3600));
        transport.Responses.Enqueue(BuildPortMappingResponse(NatPmpProtocol.Udp, NatPmpResultCode.Success, 101, 6882, 6882, 3600));

        using var client = new NatPmpClient(gatewayIp, transport);
        await client.MapPortAsync(NatPmpProtocol.Tcp, 6881, 6881, 3600);
        await client.MapPortAsync(NatPmpProtocol.Udp, 6882, 6882, 3600);

        Assert.That(client.ActiveMappings.Count, Is.EqualTo(2));

        await client.ReleaseAllMappingsAsync();

        Assert.That(client.ActiveMappings.Count, Is.EqualTo(0));
        Assert.That(transport.SentAsyncPackets.Count, Is.EqualTo(2));

        foreach (var packet in transport.SentAsyncPackets)
        {
            Assert.That(packet.Length, Is.EqualTo(12));
            Assert.That(BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6, 2)), Is.EqualTo(0)); // suggested port = 0
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(8, 4)), Is.EqualTo(0)); // lifetime = 0
        }
    }

    [Test]
    public async Task ApplicationStopping_should_release_all_mappings()
    {
        var transport = new TestNatPmpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");
        using var stoppingCts = new CancellationTokenSource();

        var appLifetime = Substitute.For<IHostApplicationLifetime>();
        appLifetime.ApplicationStopping.Returns(stoppingCts.Token);

        transport.Responses.Enqueue(BuildPortMappingResponse(NatPmpProtocol.Tcp, NatPmpResultCode.Success, 100, 7777, 7777, 3600));

        using var client = new NatPmpClient(gatewayIp, transport, appLifetime: appLifetime);
        await client.MapPortAsync(NatPmpProtocol.Tcp, 7777, 7777, 3600);

        Assert.That(client.ActiveMappings.Count, Is.EqualTo(1));

        // Trigger ApplicationStopping token
        await stoppingCts.CancelAsync();

        Assert.That(client.ActiveMappings.Count, Is.EqualTo(0));
        Assert.That(transport.SentAsyncPackets.Count, Is.EqualTo(1));

        var packet = transport.SentAsyncPackets[0];
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4, 2)), Is.EqualTo(7777));
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(8, 4)), Is.EqualTo(0));
    }

    [Test]
    public void Dispose_should_release_all_active_mappings()
    {
        var transport = new TestNatPmpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");

        transport.Responses.Enqueue(BuildPortMappingResponse(NatPmpProtocol.Tcp, NatPmpResultCode.Success, 100, 9999, 9999, 3600));

        var client = new NatPmpClient(gatewayIp, transport);
        client.MapPortAsync(NatPmpProtocol.Tcp, 9999, 9999, 3600).GetAwaiter().GetResult();

        client.Dispose();

        Assert.That(client.ActiveMappings.Count, Is.EqualTo(0));
        Assert.That(transport.SentAsyncPackets.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task DisposeAsync_should_release_all_active_mappings()
    {
        var transport = new TestNatPmpTransport();
        var gatewayIp = IPAddress.Parse("192.168.1.1");

        transport.Responses.Enqueue(BuildPortMappingResponse(NatPmpProtocol.Tcp, NatPmpResultCode.Success, 100, 9999, 9999, 3600));

        var client = new NatPmpClient(gatewayIp, transport);
        await client.MapPortAsync(NatPmpProtocol.Tcp, 9999, 9999, 3600);

        await client.DisposeAsync();

        Assert.That(client.ActiveMappings.Count, Is.EqualTo(0));
        Assert.That(transport.SentAsyncPackets.Count, Is.EqualTo(1));
    }
}
