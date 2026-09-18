using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Transport;

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
        _configService.UtpEnabled.Returns(true);
        _configService.ListeningPort.Returns(1);
        _configService.TcpFallback.Returns(true);

        var method = typeof(UtpManager).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task)method.Invoke(_subject, new object[] { CancellationToken.None });
        await task;

        Assert.That(task.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_should_handle_port_bind_failure_without_tcp_fallback()
    {
        _configService.UtpEnabled.Returns(true);
        _configService.ListeningPort.Returns(1);
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
        var receiveKey = conn.ReceiveId.ToString();

        var activeConnectionsField = typeof(UtpManager).GetField(
            "_activeConnections",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dict = (ConcurrentDictionary<string, IUtpConnection>)activeConnectionsField.GetValue(_subject)!;

        Assert.That(dict.ContainsKey(receiveKey), Is.True);
        Assert.That(dict[receiveKey], Is.SameAs(conn));

        conn.Dispose();

        Assert.That(dict.ContainsKey(receiveKey), Is.False);
    }

    [Test]
    public void CreateConnection_indexes_both_send_and_receive_ids_and_cleans_up_all_keys_on_close()
    {
        var conn = (UtpConnection)_subject.CreateConnection();
        var activeConnectionsField = typeof(UtpManager).GetField(
            "_activeConnections",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dict = (ConcurrentDictionary<string, IUtpConnection>)activeConnectionsField.GetValue(_subject)!;

        var recvKey = conn.ReceiveId.ToString();
        var sendKey = conn.SendId.ToString();
        var recvPlusOneKey = ((ushort)(conn.ReceiveId + 1)).ToString();

        Assert.That(dict.ContainsKey(recvKey), Is.True);
        Assert.That(dict.ContainsKey(sendKey), Is.True);
        Assert.That(dict.ContainsKey(recvPlusOneKey), Is.True);

        var endpoint = new IPEndPoint(IPAddress.Loopback, 54321);
        conn.OnConnecting?.Invoke(conn, endpoint);

        var endpointRecvKey = $"{endpoint}_{conn.ReceiveId}";
        var endpointSendKey = $"{endpoint}_{conn.SendId}";
        var endpointRecvPlusOneKey = $"{endpoint}_{(ushort)(conn.ReceiveId + 1)}";

        Assert.That(dict.ContainsKey(endpointRecvKey), Is.True);
        Assert.That(dict.ContainsKey(endpointSendKey), Is.True);
        Assert.That(dict.ContainsKey(endpointRecvPlusOneKey), Is.True);

        conn.Dispose();

        Assert.That(dict.ContainsKey(recvKey), Is.False);
        Assert.That(dict.ContainsKey(sendKey), Is.False);
        Assert.That(dict.ContainsKey(recvPlusOneKey), Is.False);
        Assert.That(dict.ContainsKey(endpointRecvKey), Is.False);
        Assert.That(dict.ContainsKey(endpointSendKey), Is.False);
        Assert.That(dict.ContainsKey(endpointRecvPlusOneKey), Is.False);
        Assert.That(_subject.ActiveConnections, Is.EqualTo(0));
    }

    [Test]
    public void HandleIncoming_inbound_connection_matches_both_connectionId_and_offset_connectionId()
    {
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
}
