using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Composition;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Transport;
using NzbDrone.Host;

namespace NzbDrone.Core.Test.Transport;

[TestFixture]
public class UtpManagerTest
{
    private IConfigService _configService;
    private UtpManager _subject;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.UtpEnabled.Returns(true);
        _configService.TcpFallback.Returns(true);
        _configService.TransportConnectionTimeoutSeconds.Returns(30);
        _configService.ListeningPort.Returns(16881);
        _subject = new UtpManager(_configService);
    }

    [TearDown]
    public void TearDown()
    {
        _subject?.Dispose();
    }

    // --- Constructor tests ---

    [Test]
    public void Constructor_should_create_instance()
    {
        Assert.That(_subject, Is.Not.Null);
    }

    // --- Property tests ---

    [Test]
    public void ActiveConnections_should_always_return_zero()
    {
        Assert.That(_subject.ActiveConnections, Is.EqualTo(0));
    }

    [Test]
    public void IsEnabled_should_return_true_when_utp_enabled()
    {
        _configService.UtpEnabled.Returns(true);

        Assert.That(_subject.IsEnabled, Is.True);
    }

    [Test]
    public void IsEnabled_should_return_false_when_utp_disabled()
    {
        _configService.UtpEnabled.Returns(false);

        Assert.That(_subject.IsEnabled, Is.False);
    }

    [Test]
    public void TcpFallbackEnabled_should_return_true_when_configured()
    {
        _configService.TcpFallback.Returns(true);

        Assert.That(_subject.TcpFallbackEnabled, Is.True);
    }

    [Test]
    public void TcpFallbackEnabled_should_return_false_when_not_configured()
    {
        _configService.TcpFallback.Returns(false);

        Assert.That(_subject.TcpFallbackEnabled, Is.False);
    }

    [Test]
    public void IsEnabled_should_delegate_to_config_service()
    {
        _configService.UtpEnabled.Returns(false);
        Assert.That(_subject.IsEnabled, Is.False);

        _configService.UtpEnabled.Returns(true);
        Assert.That(_subject.IsEnabled, Is.True);
    }

    [Test]
    public void TcpFallbackEnabled_should_delegate_to_config_service()
    {
        _configService.TcpFallback.Returns(false);
        Assert.That(_subject.TcpFallbackEnabled, Is.False);

        _configService.TcpFallback.Returns(true);
        Assert.That(_subject.TcpFallbackEnabled, Is.True);
    }

    // --- CreateConnection tests ---

    [Test]
    public void CreateConnection_should_throw_when_utp_disabled()
    {
        _configService.UtpEnabled.Returns(false);

        Assert.That(() => _subject.CreateConnection(),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("uTP is disabled"));
    }

    [Test]
    public void CreateConnection_should_return_connection_when_enabled()
    {
        _configService.UtpEnabled.Returns(true);
        _configService.TransportConnectionTimeoutSeconds.Returns(15);

        using var connection = _subject.CreateConnection();

        Assert.That(connection, Is.Not.Null);
        Assert.That(connection, Is.InstanceOf<IUtpConnection>());
    }

    [Test]
    public void CreateConnection_should_return_utp_connection_instance()
    {
        _configService.UtpEnabled.Returns(true);

        using var connection = _subject.CreateConnection();

        Assert.That(connection, Is.InstanceOf<UtpConnection>());
    }

    [Test]
    public void CreateConnection_should_create_disconnected_connection()
    {
        _configService.UtpEnabled.Returns(true);

        using var connection = _subject.CreateConnection();

        Assert.That(connection.IsConnected, Is.False);
    }

    [Test]
    public void CreateConnection_should_read_timeout_from_config()
    {
        _configService.UtpEnabled.Returns(true);
        _configService.TransportConnectionTimeoutSeconds.Returns(42);

        using var connection = _subject.CreateConnection();

        // Verify the config was read
        _ = _configService.Received().TransportConnectionTimeoutSeconds;
    }

    [Test]
    public void CreateConnection_should_create_new_instance_each_call()
    {
        _configService.UtpEnabled.Returns(true);

        using var connection1 = _subject.CreateConnection();
        using var connection2 = _subject.CreateConnection();

        Assert.That(connection1, Is.Not.SameAs(connection2));
    }

    // --- ExecuteAsync tests ---

    [Test]
    public async Task ExecuteAsync_should_return_immediately_when_disabled()
    {
        _configService.UtpEnabled.Returns(false);

        var method = typeof(UtpManager).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task)method.Invoke(_subject, new object[] { CancellationToken.None });
        await task;

        Assert.That(task.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_should_stop_on_cancellation()
    {
        _configService.UtpEnabled.Returns(true);
        _configService.ListeningPort.Returns(19999);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var method = typeof(UtpManager).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);

        var task = (Task)method.Invoke(_subject, new object[] { cts.Token });
        await task;

        Assert.That(task.IsCompleted, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_should_handle_port_bind_failure_gracefully()
    {
        const int port = 37655;
        using var blocker = new UdpClient(AddressFamily.InterNetwork);
        blocker.Client.Bind(new IPEndPoint(IPAddress.Any, port));

        _configService.UtpEnabled.Returns(true);
        _configService.ListeningPort.Returns(port);
        _configService.TcpFallback.Returns(true);

        var method = typeof(UtpManager).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task)method.Invoke(_subject, new object[] { CancellationToken.None });
        await task;

        Assert.That(task.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_should_handle_port_bind_failure_without_tcp_fallback()
    {
        const int port = 37656;
        using var blocker = new UdpClient(AddressFamily.InterNetwork);
        blocker.Client.Bind(new IPEndPoint(IPAddress.Any, port));

        _configService.UtpEnabled.Returns(true);
        _configService.ListeningPort.Returns(port);
        _configService.TcpFallback.Returns(false);

        var method = typeof(UtpManager).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task)method.Invoke(_subject, new object[] { CancellationToken.None });
        await task;

        Assert.That(task.IsCompletedSuccessfully, Is.True);
    }

    // --- HandleIncoming tests (private method, via reflection) ---

    private void InvokeHandleIncoming(byte[] data, IPEndPoint sender)
    {
        var method = typeof(UtpManager).GetMethod("HandleIncoming", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, new object[] { data, sender });
    }

    private void RetainAcceptedConnections()
    {
        _subject.OnConnectionAccepted += _ => { };
    }

    private ConcurrentDictionary<string, IUtpConnection> ActiveConnectionMap()
    {
        var activeConnectionsField = typeof(UtpManager).GetField(
            "_activeConnections",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (ConcurrentDictionary<string, IUtpConnection>)activeConnectionsField.GetValue(_subject);
    }

    [Test]
    public void HandleIncoming_should_ignore_packets_shorter_than_20_bytes()
    {
        var data = new byte[19];
        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_ignore_empty_data()
    {
        var data = Array.Empty<byte>();
        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_process_syn_packet()
    {
        // Build a 20-byte SYN packet: type=4 (Syn), high nibble of first byte = 4
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1); // type=Syn, version=1
        data[2] = 0x00; // connection ID high byte
        data[3] = 0x01; // connection ID low byte

        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 54321);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_fire_OnConnectionAccepted_when_syn_received()
    {
        IUtpConnection acceptedConn = null;
        var eventFired = 0;
        _subject.OnConnectionAccepted += conn =>
        {
            eventFired++;
            acceptedConn = conn;
        };

        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
        data[2] = 0x01;
        data[3] = 0x23;

        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 54321);
        InvokeHandleIncoming(data, sender);

        Assert.That(eventFired, Is.EqualTo(1));
        Assert.That(acceptedConn, Is.Not.Null);
        Assert.That(acceptedConn.RemoteEndPoint, Is.EqualTo(sender));
        Assert.That(acceptedConn.IsConnected, Is.True);
        Assert.That(acceptedConn.SendId, Is.EqualTo(0x0123));
    }

    [Test]
    public void HandleIncoming_should_not_fire_OnConnectionAccepted_for_duplicate_syn()
    {
        var eventFired = 0;
        _subject.OnConnectionAccepted += conn => eventFired++;

        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
        data[2] = 0x02;
        data[3] = 0x34;

        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.101"), 54322);
        InvokeHandleIncoming(data, sender);
        InvokeHandleIncoming(data, sender);

        Assert.That(eventFired, Is.EqualTo(1));
    }

    [Test]
    public void HandleIncoming_should_not_fire_OnConnectionAccepted_for_non_syn_packets()
    {
        var eventFired = 0;
        _subject.OnConnectionAccepted += conn => eventFired++;

        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.102"), 54323);

        foreach (var type in new[] { UtpPacketType.Data, UtpPacketType.Fin, UtpPacketType.State, UtpPacketType.Reset })
        {
            var data = new byte[20];
            data[0] = (byte)(((byte)type) << 4 | 1);
            InvokeHandleIncoming(data, sender);
        }

        Assert.That(eventFired, Is.EqualTo(0));
    }

    [Test]
    public void HandleIncoming_should_process_data_packet()
    {
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Data) << 4 | 1); // type=Data

        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_process_fin_packet()
    {
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Fin) << 4 | 1); // type=Fin

        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_process_state_packet()
    {
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.State) << 4 | 1); // type=State

        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_process_reset_packet()
    {
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Reset) << 4 | 1); // type=Reset

        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_extract_connection_id_from_syn()
    {
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
        data[2] = 0xAB; // connection ID high byte
        data[3] = 0xCD; // connection ID low byte

        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_handle_exactly_20_byte_packet()
    {
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Data) << 4 | 1);

        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_handle_oversized_packet()
    {
        var data = new byte[1500]; // typical MTU-sized packet
        data[0] = (byte)(((byte)UtpPacketType.Data) << 4 | 1);

        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void ActiveConnections_should_track_connected_connections()
    {
        var conn = (UtpConnection)_subject.CreateConnection();
        var backingField = typeof(UtpConnection).GetField("<IsConnected>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!;
        backingField.SetValue(conn, true);

        Assert.That(_subject.ActiveConnections, Is.EqualTo(1));

        conn.Dispose();
        Assert.That(_subject.ActiveConnections, Is.EqualTo(0));
    }

    [Test]
    public void HandleIncoming_should_track_incoming_syn_connection()
    {
        RetainAcceptedConnections();
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
        data[2] = 0x12;
        data[3] = 0x34;

        var sender = new IPEndPoint(IPAddress.Loopback, 54321);
        InvokeHandleIncoming(data, sender);

        Assert.That(_subject.ActiveConnections, Is.EqualTo(1));
    }

    [Test]
    public void HandleIncoming_syn_with_configured_listener_shares_socket_and_disposing_connection_does_not_dispose_listener()
    {
        RetainAcceptedConnections();
        using var listener = new UdpClient();
        listener.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        _subject.Listener = listener;

        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
        data[2] = 0x12;
        data[3] = 0x34;

        var sender = new IPEndPoint(IPAddress.Loopback, 54322);
        InvokeHandleIncoming(data, sender);

        Assert.That(_subject.ActiveConnections, Is.EqualTo(1));

        var activeConnectionsField = typeof(UtpManager).GetField(
            "_activeConnections",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dict = (System.Collections.Concurrent.ConcurrentDictionary<string, IUtpConnection>)activeConnectionsField.GetValue(_subject)!;
        var conn = dict.Values.First() as UtpConnection;

        Assert.That(conn, Is.Not.Null);
        Assert.That(conn!.OwnsUdpClient, Is.False);

        conn.Dispose();
        Assert.That(_subject.ActiveConnections, Is.EqualTo(0));
        Assert.That(listener.Client.IsBound, Is.True);
    }

    [Test]
    public void HandleIncoming_syn_packet_creates_connection_and_maps_both_sendId_and_receiveId()
    {
        RetainAcceptedConnections();
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
        data[2] = 0x12;
        data[3] = 0x34; // 0x1234 = 4660

        var sender = new IPEndPoint(IPAddress.Loopback, 54321);
        InvokeHandleIncoming(data, sender);

        var activeConnectionsField = typeof(UtpManager).GetField(
            "_activeConnections",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dict = (ConcurrentDictionary<string, IUtpConnection>)activeConnectionsField.GetValue(_subject)!;

        var synKey = $"{sender}_4660";
        var dataKey = $"{sender}_4661";

        Assert.That(dict.ContainsKey(synKey), Is.True);
        Assert.That(dict.ContainsKey(dataKey), Is.True);
        Assert.That(dict[synKey], Is.SameAs(dict[dataKey]));

        var conn = (UtpConnection)dict[synKey];
        Assert.That(conn.SendId, Is.EqualTo(4660));
        Assert.That(conn.ReceiveId, Is.EqualTo(4661));
        Assert.That(_subject.ActiveConnections, Is.EqualTo(1));
    }

    [Test]
    public void HandleIncoming_subsequent_data_packet_with_receiveId_delivers_to_connection()
    {
        RetainAcceptedConnections();
        var synData = new byte[20];
        synData[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
        synData[2] = 0x12;
        synData[3] = 0x34;
        synData[16] = 0x00;
        synData[17] = 0x0A; // initial seq nr = 10

        var sender = new IPEndPoint(IPAddress.Loopback, 54321);
        InvokeHandleIncoming(synData, sender);

        var activeConnectionsField = typeof(UtpManager).GetField(
            "_activeConnections",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dict = (ConcurrentDictionary<string, IUtpConnection>)activeConnectionsField.GetValue(_subject)!;
        var conn = (UtpConnection)dict[$"{sender}_4661"];

        var payload = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var dataPacket = new byte[20 + payload.Length];
        dataPacket[0] = (byte)(((byte)UtpPacketType.Data) << 4 | 1);
        dataPacket[2] = 0x12;
        dataPacket[3] = 0x35; // X + 1 = 4661
        dataPacket[16] = 0x00;
        dataPacket[17] = 0x0B; // seq nr = 11 (next expected seq)
        Array.Copy(payload, 0, dataPacket, 20, payload.Length);

        InvokeHandleIncoming(dataPacket, sender);

        var receivedBuffer = new byte[4];
        var read = conn.Receive(receivedBuffer, 0, 4);

        Assert.That(read, Is.EqualTo(4));
        Assert.That(receivedBuffer, Is.EqualTo(payload));
    }

    [Test]
    public void Closing_incoming_connection_cleans_up_both_keys()
    {
        RetainAcceptedConnections();
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
        data[2] = 0x12;
        data[3] = 0x34;

        var sender = new IPEndPoint(IPAddress.Loopback, 54321);
        InvokeHandleIncoming(data, sender);

        var activeConnectionsField = typeof(UtpManager).GetField(
            "_activeConnections",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dict = (ConcurrentDictionary<string, IUtpConnection>)activeConnectionsField.GetValue(_subject)!;

        var synKey = $"{sender}_4660";
        var dataKey = $"{sender}_4661";

        Assert.That(dict.ContainsKey(synKey), Is.True);
        Assert.That(dict.ContainsKey(dataKey), Is.True);

        var conn = dict[synKey];
        conn.Dispose();

        Assert.That(dict.ContainsKey(synKey), Is.False);
        Assert.That(dict.ContainsKey(dataKey), Is.False);
        Assert.That(_subject.ActiveConnections, Is.EqualTo(0));
    }

    [Test]
    public void CreateConnection_registers_outgoing_connection_receive_id_and_cleans_up_on_close()
    {
        using var conn = (UtpConnection)_subject.CreateConnection();
        var dict = ActiveConnectionMap();

        Assert.That(dict.ContainsKey(conn.ReceiveId.ToString()), Is.False, "Connection ids must not be global demux keys");
        Assert.That(dict.Values, Does.Contain(conn));

        conn.Dispose();

        Assert.That(dict.Values, Does.Not.Contain(conn));
    }

    [Test]
    public void CreateConnection_indexes_both_send_and_receive_ids_and_cleans_up_all_keys_on_close()
    {
        var conn = (UtpConnection)_subject.CreateConnection();
        var dict = ActiveConnectionMap();

        Assert.That(dict.ContainsKey(conn.ReceiveId.ToString()), Is.False);
        Assert.That(dict.ContainsKey(conn.SendId.ToString()), Is.False);
        Assert.That(dict.ContainsKey(((ushort)(conn.ReceiveId + 1)).ToString()), Is.False);

        var endpoint = new IPEndPoint(IPAddress.Loopback, 54321);
        conn.OnConnecting?.Invoke(conn, endpoint);

        var endpointRecvKey = $"{endpoint}_{conn.ReceiveId}";
        var endpointSendKey = $"{endpoint}_{conn.SendId}";
        var endpointRecvPlusOneKey = $"{endpoint}_{(ushort)(conn.ReceiveId + 1)}";

        Assert.That(dict.ContainsKey(endpointRecvKey), Is.True);
        Assert.That(dict.ContainsKey(endpointSendKey), Is.True);
        Assert.That(dict.ContainsKey(endpointRecvPlusOneKey), Is.True);

        conn.Dispose();

        Assert.That(dict.ContainsKey(endpointRecvKey), Is.False);
        Assert.That(dict.ContainsKey(endpointSendKey), Is.False);
        Assert.That(dict.ContainsKey(endpointRecvPlusOneKey), Is.False);
        Assert.That(_subject.ActiveConnections, Is.EqualTo(0));
    }

    [Test]
    public void HandleIncoming_inbound_connection_matches_both_connectionId_and_offset_connectionId()
    {
        RetainAcceptedConnections();
        var synData = new byte[20];
        synData[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
        synData[2] = 0x20;
        synData[3] = 0x00; // connId = 8192
        synData[16] = 0x00;
        synData[17] = 0x01; // seq = 1

        var sender = new IPEndPoint(IPAddress.Loopback, 50001);
        InvokeHandleIncoming(synData, sender);

        var activeConnectionsField = typeof(UtpManager).GetField(
            "_activeConnections",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dict = (ConcurrentDictionary<string, IUtpConnection>)activeConnectionsField.GetValue(_subject)!;

        // Inbound connection should be matched whether packet uses 8192 (SendId) or 8193 (ReceiveId)
        Assert.That(dict.ContainsKey($"{sender}_8192"), Is.True);
        Assert.That(dict.ContainsKey($"{sender}_8193"), Is.True);

        var conn = (UtpConnection)dict[$"{sender}_8192"];

        // Send STATE packet with 8192
        var statePacket = new byte[20];
        statePacket[0] = (byte)(((byte)UtpPacketType.State) << 4 | 1);
        statePacket[2] = 0x20;
        statePacket[3] = 0x00; // 8192
        statePacket[18] = 0x00;
        statePacket[19] = 0x01; // ack = 1
        InvokeHandleIncoming(statePacket, sender);

        // Send DATA packet with 8193 (connId + 1)
        var payload = new byte[] { 0x11, 0x22 };
        var dataPacket = new byte[20 + payload.Length];
        dataPacket[0] = (byte)(((byte)UtpPacketType.Data) << 4 | 1);
        dataPacket[2] = 0x20;
        dataPacket[3] = 0x01; // 8193
        dataPacket[16] = 0x00;
        dataPacket[17] = 0x02; // seq = 2
        Array.Copy(payload, 0, dataPacket, 20, payload.Length);
        InvokeHandleIncoming(dataPacket, sender);

        var received = new byte[2];
        var read = conn.Receive(received, 0, 2);
        Assert.That(read, Is.EqualTo(2));
        Assert.That(received, Is.EqualTo(payload));

        conn.Dispose();
        Assert.That(_subject.ActiveConnections, Is.EqualTo(0));
    }

    [Test]
    public void HandleIncoming_outbound_connection_matches_by_endpoint_and_connection_id()
    {
        var conn = (UtpConnection)_subject.CreateConnection();
        var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 50002);
        conn.OnConnecting?.Invoke(conn, remoteEndpoint);

        // Simulate STATE packet from remoteEndpoint arriving at UtpManager with conn.ReceiveId
        var statePacket = new byte[20];
        statePacket[0] = (byte)(((byte)UtpPacketType.State) << 4 | 1);
        statePacket[2] = (byte)(conn.ReceiveId >> 8);
        statePacket[3] = (byte)(conn.ReceiveId & 0xFF);
        statePacket[18] = 0x00;
        statePacket[19] = 0x01; // ack = 1

        // UtpManager should find conn and invoke HandleIncomingPacket without throwing
        Assert.DoesNotThrow(() => InvokeHandleIncoming(statePacket, remoteEndpoint));

        conn.Dispose();
        Assert.That(_subject.ActiveConnections, Is.EqualTo(0));
    }

    [Test]
    public void UtpManager_WhenVpnKillSwitchTriggered_TearsDownActiveConnections_AndBlocksConnectionCreation()
    {
        var conn = _subject.CreateConnection();
        Assert.That(conn, Is.Not.Null);

        _subject.Handle(new VpnKillSwitchTriggeredEvent("wg0"));

        Assert.That(_subject.IsVpnFailClosed(), Is.True);
        Assert.That(_subject.ActiveConnections, Is.EqualTo(0));
        Assert.Throws<InvalidOperationException>(() => _subject.CreateConnection());
    }

    [Test]
    public void UtpManager_WhenVpnRestored_DisengagesFailClosed_AndAllowsConnectionCreation()
    {
        _subject.Handle(new VpnKillSwitchTriggeredEvent("wg0"));
        Assert.That(_subject.IsVpnFailClosed(), Is.True);

        _subject.Handle(new VpnRestoredEvent("wg0"));
        Assert.That(_subject.IsVpnFailClosed(), Is.False);

        Assert.DoesNotThrow(() =>
        {
            var conn = _subject.CreateConnection();
            conn.Dispose();
        });
    }

    [Test]
    public void UtpManager_WhenVpnDroppedViaServiceEvent_TearsDownActiveConnections()
    {
        var vpnKillSwitch = Substitute.For<IVpnKillSwitchService>();
        using var manager = new UtpManager(_configService, vpnKillSwitch);

        vpnKillSwitch.VpnDropped += Raise.Event<Action<string>>("wg0");

        Assert.That(manager.IsVpnFailClosed(), Is.True);
        Assert.Throws<InvalidOperationException>(() => manager.CreateConnection());
    }

    [Test]
    public void HandleIncoming_WhenVpnFailClosedActive_DropsPacketWithoutProcessing()
    {
        _subject.Handle(new VpnKillSwitchTriggeredEvent("wg0"));

        var synData = new byte[20];
        synData[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
        synData[2] = 0x20;
        synData[3] = 0x00;

        var sender = new IPEndPoint(IPAddress.Loopback, 50001);
        InvokeHandleIncoming(synData, sender);

        Assert.That(_subject.ActiveConnections, Is.EqualTo(0));
    }

    [Test]
    public void HandleIncoming_syn_without_consumer_does_not_retain_connection()
    {
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
        data[2] = 0x10;
        data[3] = 0x01;

        InvokeHandleIncoming(data, new IPEndPoint(IPAddress.Parse("10.9.9.9"), 4100));

        Assert.That(_subject.ActiveConnections, Is.EqualTo(0));
        Assert.That(ActiveConnectionMap(), Is.Empty);
    }

    [Test]
    public void HandleIncoming_same_connection_id_from_different_endpoints_does_not_collide()
    {
        RetainAcceptedConnections();

        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
        data[2] = 0x01;
        data[3] = 0x01;
        data[16] = 0x00;
        data[17] = 0x01;

        var endpointA = new IPEndPoint(IPAddress.Parse("10.1.0.1"), 1000);
        var endpointB = new IPEndPoint(IPAddress.Parse("10.2.0.2"), 2000);
        InvokeHandleIncoming(data, endpointA);
        InvokeHandleIncoming(data, endpointB);

        var dict = ActiveConnectionMap();
        var connA = (UtpConnection)dict[$"{endpointA}_257"];
        var connB = (UtpConnection)dict[$"{endpointB}_257"];
        Assert.That(connA, Is.Not.SameAs(connB));
        Assert.That(_subject.ActiveConnections, Is.EqualTo(2));

        var payload = new byte[] { 0xAB, 0xCD };
        var dataPacket = new byte[20 + payload.Length];
        dataPacket[0] = (byte)(((byte)UtpPacketType.Data) << 4 | 1);
        dataPacket[2] = 0x01;
        dataPacket[3] = 0x02;
        dataPacket[16] = 0x00;
        dataPacket[17] = 0x02;
        Array.Copy(payload, 0, dataPacket, 20, payload.Length);
        InvokeHandleIncoming(dataPacket, endpointA);

        var fromA = new byte[2];
        Assert.That(connA.Receive(fromA, 0, 2, 200), Is.EqualTo(2));
        Assert.That(fromA, Is.EqualTo(payload));
        Assert.That(connB.Receive(new byte[2], 0, 2, 50), Is.EqualTo(0));
    }

    [Test]
    public void HandleIncoming_handler_exception_does_not_retain_connection()
    {
        _subject.OnConnectionAccepted += _ => throw new InvalidOperationException("reject");

        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
        data[3] = 0x2A;

        InvokeHandleIncoming(data, new IPEndPoint(IPAddress.Parse("10.4.4.4"), 4400));

        Assert.That(ActiveConnectionMap(), Is.Empty);
        Assert.That(_subject.ActiveConnections, Is.EqualTo(0));
    }

    [Test]
    public async Task Inbound_utp_handshake_completes()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)probe.Client.LocalEndPoint).Port;
        probe.Close();

        _configService.UtpEnabled.Returns(true);
        _configService.ListeningPort.Returns(port);
        _configService.TransportConnectionTimeoutSeconds.Returns(5);

        var accepted = new TaskCompletionSource<IUtpConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        _subject.OnConnectionAccepted += conn => accepted.TrySetResult(conn);

        using var cts = new CancellationTokenSource();
        var execute = typeof(UtpManager).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var listenTask = (Task)execute.Invoke(_subject, new object[] { cts.Token });

        try
        {
            Assert.That(() => _subject.Listener != null, Is.True.After(2000, 20));

            using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var syn = new byte[20];
            syn[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
            syn[2] = 0x11;
            syn[3] = 0x22;
            syn[12] = 0x00;
            syn[13] = 0x00;
            syn[14] = 0xFF;
            syn[15] = 0xFF;
            syn[16] = 0x00;
            syn[17] = 0x01;
            await client.SendAsync(syn, new IPEndPoint(IPAddress.Loopback, port));

            using var receiveCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var result = await client.ReceiveAsync(receiveCts.Token);
            Assert.That(result.Buffer.Length, Is.GreaterThanOrEqualTo(20));
            Assert.That((UtpPacketType)(result.Buffer[0] >> 4), Is.EqualTo(UtpPacketType.State));
            Assert.That((result.Buffer[2] << 8) | result.Buffer[3], Is.EqualTo(0x1122));

            var completed = await Task.WhenAny(accepted.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.That(completed, Is.SameAs(accepted.Task));
            var conn = await accepted.Task;
            Assert.That(conn.IsConnected, Is.True);
            Assert.That(conn.SendId, Is.EqualTo(0x1122));
            Assert.That(conn.RemoteEndPoint.Address, Is.EqualTo(IPAddress.Loopback));
        }
        finally
        {
            cts.Cancel();
            try
            {
                listenTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }

            if (accepted.Task.IsCompletedSuccessfully)
            {
                accepted.Task.Result.Dispose();
            }
        }
    }

    [Test]
    public void Hosted_UtpManager_is_the_IUtpManager_singleton()
    {
        var config = Substitute.For<IConfigService>();
        config.UtpEnabled.Returns(true);
        config.TransportConnectionTimeoutSeconds.Returns(5);

        var container = new Container(rules => rules.WithNzbDroneRules());
        container.RegisterInstance<IConfigService>(config);
        container.RegisterMany(
            new[] { typeof(UtpManager) },
            Reuse.Singleton,
            serviceTypeCondition: t => !ContainerExtensions.IsIgnoredInterface(t),
            ifAlreadyRegistered: IfAlreadyRegistered.Keep);

        var services = new ServiceCollection();
        Startup.RegisterBackgroundHostedServices(services, new[] { typeof(UtpManager) });
        using var provider = new DryIocServiceProviderFactory(container).CreateBuilder(services);

        var injected = provider.GetRequiredService<IUtpManager>();
        var hosted = provider.GetServices<IHostedService>().OfType<UtpManager>().ToList();

        Assert.That(hosted, Has.Count.EqualTo(1));
        Assert.That(hosted[0], Is.SameAs(injected));

        IUtpConnection accepted = null;
        injected.OnConnectionAccepted += conn => accepted = conn;

        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
        data[3] = 9;
        var sender = new IPEndPoint(IPAddress.Loopback, 4242);
        typeof(UtpManager).GetMethod("HandleIncoming", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(hosted[0], new object[] { data, sender });

        Assert.That(accepted, Is.Not.Null);
        Assert.That(accepted.IsConnected, Is.True);
        Assert.That(injected.ActiveConnections, Is.EqualTo(1));
        Assert.That(hosted[0].ActiveConnections, Is.EqualTo(1));
        accepted.Dispose();
    }

    [Test]
    public void OnConnected_with_no_remote_endpoint_does_not_add_demux_keys()
    {
        using var conn = (UtpConnection)_subject.CreateConnection();
        var before = ActiveConnectionMap().Count;

        conn.OnConnected?.Invoke(conn);

        Assert.That(ActiveConnectionMap().Count, Is.EqualTo(before));
    }

    [Test]
    public void RegisterEndpointKeys_ignores_missing_endpoint_callback_or_connection()
    {
        var method = typeof(UtpManager).GetMethod("RegisterEndpointKeys", BindingFlags.NonPublic | BindingFlags.Static);
        using var conn = new UtpConnection(5);
        var endpoint = new IPEndPoint(IPAddress.Loopback, 1);
        var keys = new List<string>();

        Assert.DoesNotThrow(() => method.Invoke(null, new object[] { null, endpoint, conn }));
        Assert.DoesNotThrow(() => method.Invoke(null, new object[] { (Action<string>)keys.Add, null, conn }));
        Assert.DoesNotThrow(() => method.Invoke(null, new object[] { (Action<string>)keys.Add, endpoint, null }));
        Assert.That(keys, Is.Empty);

        method.Invoke(null, new object[] { (Action<string>)keys.Add, endpoint, conn });
        Assert.That(keys, Has.Count.EqualTo(3));
    }

    [Test]
    public void FindConnection_uses_neighbor_ids_and_rejects_a_null_sender()
    {
        RetainAcceptedConnections();
        var syn = new byte[20];
        syn[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
        syn[2] = 0x01;
        syn[3] = 0x01;
        var sender = new IPEndPoint(IPAddress.Parse("10.8.8.8"), 4800);
        InvokeHandleIncoming(syn, sender);

        var find = typeof(UtpManager).GetMethod("FindConnection", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(find.Invoke(_subject, new object[] { null, (ushort)1 }), Is.Null);

        var higher = new byte[20];
        higher[0] = (byte)(((byte)UtpPacketType.State) << 4 | 1);
        higher[2] = 0x01;
        higher[3] = 0x03;
        Assert.DoesNotThrow(() => InvokeHandleIncoming(higher, sender));

        var lower = new byte[20];
        lower[0] = (byte)(((byte)UtpPacketType.State) << 4 | 1);
        lower[2] = 0x01;
        lower[3] = 0x00;
        Assert.DoesNotThrow(() => InvokeHandleIncoming(lower, sender));

        var demux = typeof(UtpManager).GetMethod("DemuxKey", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(demux.Invoke(null, new object[] { null, (ushort)7 }), Is.EqualTo("_7"));
    }
}
