using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;
using NzbDrone.Core.Peers;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class PeerConnectionTest
{
    private List<PeerConnection> _connections;
    private List<TcpListener> _listeners;
    private List<TcpClient> _clients;

    [SetUp]
    public void Setup()
    {
        _connections = new List<PeerConnection>();
        _listeners = new List<TcpListener>();
        _clients = new List<TcpClient>();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var conn in _connections)
        {
            try
            {
                conn.Dispose();
            }
            catch
            {
            }
        }

        foreach (var client in _clients)
        {
            try
            {
                client.Dispose();
            }
            catch
            {
            }
        }

        foreach (var listener in _listeners)
        {
            try
            {
                listener.Stop();
            }
            catch
            {
            }
        }
    }

    private (PeerConnection Client, PeerConnection Server) CreateTestPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listeners.Add(listener);

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var clientTcp = new TcpClient();
        clientTcp.Connect(IPAddress.Loopback, port);
        var serverTcp = listener.AcceptTcpClient();
        listener.Stop();

        var clientConn = new PeerConnection(clientTcp);
        var serverConn = new PeerConnection(serverTcp);
        _connections.Add(clientConn);
        _connections.Add(serverConn);
        return (clientConn, serverConn);
    }

    private PeerConnection CreateSingleConnection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listeners.Add(listener);

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        var serverClient = listener.AcceptTcpClient();
        _clients.Add(client);
        listener.Stop();

        var conn = new PeerConnection(serverClient);
        _connections.Add(conn);
        return conn;
    }

    private (PeerConnection Connection, TcpClient RawClient) CreateConnectionWithRawClient()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listeners.Add(listener);

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var rawClient = new TcpClient();
        rawClient.Connect(IPAddress.Loopback, port);
        _clients.Add(rawClient);
        var serverTcp = listener.AcceptTcpClient();
        listener.Stop();

        var conn = new PeerConnection(serverTcp);
        _connections.Add(conn);
        return (conn, rawClient);
    }

    [Test]
    public void Constructor_should_set_remote_ip()
    {
        var conn = CreateSingleConnection();

        Assert.That(conn.RemoteIp, Is.EqualTo("127.0.0.1"));
    }

    [Test]
    public void Constructor_should_set_remote_port()
    {
        var conn = CreateSingleConnection();

        Assert.That(conn.RemotePort, Is.GreaterThan(0));
    }

    [Test]
    public void Constructor_should_set_connected_at()
    {
        var before = DateTime.UtcNow;

        var conn = CreateSingleConnection();

        var after = DateTime.UtcNow;
        Assert.That(conn.ConnectedAt, Is.GreaterThanOrEqualTo(before));
        Assert.That(conn.ConnectedAt, Is.LessThanOrEqualTo(after));
    }

    [Test]
    public void Constructor_should_set_last_activity()
    {
        var before = DateTime.UtcNow;

        var conn = CreateSingleConnection();

        var after = DateTime.UtcNow;
        Assert.That(conn.LastActivity, Is.GreaterThanOrEqualTo(before));
        Assert.That(conn.LastActivity, Is.LessThanOrEqualTo(after));
    }

    [Test]
    public void IsConnected_should_return_true_for_active_connection()
    {
        var (client, _) = CreateTestPair();

        Assert.That(client.IsConnected, Is.True);
    }

    [Test]
    public void AmChoking_should_default_to_true()
    {
        var conn = CreateSingleConnection();

        Assert.That(conn.AmChoking, Is.True);
    }

    [Test]
    public void AmInterested_should_default_to_false()
    {
        var conn = CreateSingleConnection();

        Assert.That(conn.AmInterested, Is.False);
    }

    [Test]
    public void PeerChoking_should_default_to_true()
    {
        var conn = CreateSingleConnection();

        Assert.That(conn.PeerChoking, Is.True);
    }

    [Test]
    public void PeerInterested_should_default_to_false()
    {
        var conn = CreateSingleConnection();

        Assert.That(conn.PeerInterested, Is.False);
    }

    [Test]
    public void KeepAliveIntervalSeconds_should_default_to_120()
    {
        var conn = CreateSingleConnection();

        Assert.That(conn.KeepAliveIntervalSeconds, Is.EqualTo(120));
    }

    [Test]
    public void MaxPipelinedRequests_should_default_to_200()
    {
        var conn = CreateSingleConnection();

        Assert.That(conn.MaxPipelinedRequests, Is.EqualTo(200));
    }

    [Test]
    public void SendHandshake_and_ReceiveHandshake_should_roundtrip()
    {
        var (client, server) = CreateTestPair();
        var infoHash = "0102030405060708091011121314151617181920";
        var peerId = "-SD0001-012345678901";

        var sent = client.SendHandshake(infoHash, peerId);
        var received = server.ReceiveHandshake();

        Assert.That(sent, Is.True);
        Assert.That(received, Is.True);
        Assert.That(server.InfoHash, Is.EqualTo(infoHash));
        Assert.That(server.PeerId, Is.EqualTo(peerId));
    }

    [Test]
    public void ReceiveHandshake_should_return_false_when_connection_closed()
    {
        var (client, server) = CreateTestPair();
        client.Dispose();

        var result = server.ReceiveHandshake();

        Assert.That(result, Is.False);
    }

    [Test]
    public void SendMessage_and_ReceiveMessage_should_roundtrip()
    {
        var (client, server) = CreateTestPair();
        var message = new PeerMessage { Type = PeerMessageType.Choke };

        client.SendMessage(message);
        var received = server.ReceiveMessage();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Choke));
    }

    [Test]
    public void SendMessage_and_ReceiveMessage_should_roundtrip_with_payload()
    {
        var (client, server) = CreateTestPair();
        var payload = new byte[] { 0x00, 0x00, 0x00, 0x07 };
        var message = new PeerMessage { Type = PeerMessageType.Have, Payload = payload };

        client.SendMessage(message);
        var received = server.ReceiveMessage();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Have));
        Assert.That(received.Payload, Is.EqualTo(payload));
    }

    [Test]
    public void ReceiveMessage_should_return_null_for_keepalive()
    {
        var (client, server) = CreateTestPair();

        client.SendKeepAlive();
        var received = server.ReceiveMessage();

        Assert.That(received, Is.Null);
    }

    [Test]
    public void ReceiveMessage_should_return_null_and_dispose_for_oversized_message()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listeners.Add(listener);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var rawClient = new TcpClient();
        rawClient.Connect(IPAddress.Loopback, port);
        _clients.Add(rawClient);
        var serverTcp = listener.AcceptTcpClient();
        listener.Stop();
        var server = new PeerConnection(serverTcp);
        _connections.Add(server);

        var oversizedLength = (16 * 1024 * 1024) + 1;
        var lengthBytes = new byte[]
        {
            (byte)(oversizedLength >> 24),
            (byte)(oversizedLength >> 16),
            (byte)(oversizedLength >> 8),
            (byte)oversizedLength
        };
        var stream = rawClient.GetStream();
        stream.Write(lengthBytes, 0, 4);
        stream.Flush();

        var received = server.ReceiveMessage();

        Assert.That(received, Is.Null);
    }

    [Test]
    public void ReceiveMessage_should_return_null_and_dispose_for_negative_length()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();

        // High bit set in length bytes: 0x80000000 reconstructed via (uint) casts then (int) cast
        // gives int.MinValue (-2147483648), triggering the length < 0 guard.
        var lengthBytes = new byte[] { 0x80, 0x00, 0x00, 0x00 };
        var stream = rawClient.GetStream();
        stream.Write(lengthBytes, 0, 4);
        stream.Flush();

        var received = conn.ReceiveMessage();

        Assert.That(received, Is.Null);
    }

    [Test]
    public void SendKeepAlive_should_send_four_zero_bytes()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listeners.Add(listener);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var rawClient = new TcpClient();
        rawClient.Connect(IPAddress.Loopback, port);
        _clients.Add(rawClient);
        var serverTcp = listener.AcceptTcpClient();
        listener.Stop();
        var server = new PeerConnection(serverTcp);
        _connections.Add(server);

        server.SendKeepAlive();

        var stream = rawClient.GetStream();
        var buffer = new byte[4];
        var read = stream.Read(buffer, 0, 4);
        Assert.That(read, Is.EqualTo(4));
        Assert.That(buffer, Is.EqualTo(new byte[] { 0, 0, 0, 0 }));
    }

    [Test]
    public void SendBitfield_should_send_correct_bitfield_for_8_pieces()
    {
        var (client, server) = CreateTestPair();

        server.SendBitfield(8);
        var received = client.ReceiveMessage();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Bitfield));
        Assert.That(received.Payload.Length, Is.EqualTo(1));
        Assert.That(received.Payload[0], Is.EqualTo(0xFF));
    }

    [Test]
    public void SendBitfield_should_clear_trailing_bits()
    {
        var (client, server) = CreateTestPair();

        server.SendBitfield(10);
        var received = client.ReceiveMessage();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Bitfield));
        Assert.That(received.Payload.Length, Is.EqualTo(2));
        Assert.That(received.Payload[0], Is.EqualTo(0xFF));
        Assert.That(received.Payload[1], Is.EqualTo(0xC0));
    }

    [Test]
    public void SendBitfield_should_handle_1_piece()
    {
        var (client, server) = CreateTestPair();

        server.SendBitfield(1);
        var received = client.ReceiveMessage();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Bitfield));
        Assert.That(received.Payload.Length, Is.EqualTo(1));
        Assert.That(received.Payload[0], Is.EqualTo(0x80));
    }

    [Test]
    public void Dispose_should_not_throw_when_called_twice()
    {
        var conn = CreateSingleConnection();

        conn.Dispose();

        Assert.DoesNotThrow(() => conn.Dispose());
    }

    [Test]
    public void SendHandshake_should_set_info_hash_on_sender()
    {
        var (client, _) = CreateTestPair();
        var infoHash = "0102030405060708091011121314151617181920";

        client.SendHandshake(infoHash, "-SD0001-012345678901");

        Assert.That(client.InfoHash, Is.EqualTo(infoHash));
    }

    [Test]
    public void SendHandshake_should_set_peer_id_on_sender()
    {
        var (client, _) = CreateTestPair();
        var peerId = "-SD0001-012345678901";

        client.SendHandshake("0102030405060708091011121314151617181920", peerId);

        Assert.That(client.PeerId, Is.EqualTo(peerId));
    }

    [Test]
    public void InfoHash_should_be_null_before_handshake()
    {
        var conn = CreateSingleConnection();

        Assert.That(conn.InfoHash, Is.Null);
    }

    [Test]
    public void PeerId_should_be_null_before_handshake()
    {
        var conn = CreateSingleConnection();

        Assert.That(conn.PeerId, Is.Null);
    }

    [Test]
    public void IsEncrypted_should_default_to_false()
    {
        var conn = CreateSingleConnection();

        Assert.That(conn.IsEncrypted, Is.False);
    }

    [Test]
    public void IsConnected_should_return_false_after_dispose()
    {
        var conn = CreateSingleConnection();

        conn.Dispose();

        Assert.That(conn.IsConnected, Is.False);
    }

    [Test]
    public void SendBitfield_should_handle_16_pieces()
    {
        var (client, server) = CreateTestPair();

        server.SendBitfield(16);
        var received = client.ReceiveMessage();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Payload.Length, Is.EqualTo(2));
        Assert.That(received.Payload[0], Is.EqualTo(0xFF));
        Assert.That(received.Payload[1], Is.EqualTo(0xFF));
    }

    [Test]
    public void SendMessage_should_roundtrip_unchoke()
    {
        var (client, server) = CreateTestPair();
        var message = new PeerMessage { Type = PeerMessageType.Unchoke };

        client.SendMessage(message);
        var received = server.ReceiveMessage();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Unchoke));
        Assert.That(received.Payload, Is.Null);
    }

    [Test]
    public void ReceiveMessage_should_return_null_when_connection_closed()
    {
        var (client, server) = CreateTestPair();
        client.Dispose();

        var received = server.ReceiveMessage();

        Assert.That(received, Is.Null);
    }

    [Test]
    public void SendBitfield_should_handle_9_pieces()
    {
        var (client, server) = CreateTestPair();

        server.SendBitfield(9);
        var received = client.ReceiveMessage();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Payload.Length, Is.EqualTo(2));
        Assert.That(received.Payload[0], Is.EqualTo(0xFF));
        Assert.That(received.Payload[1], Is.EqualTo(0x80));
    }

    // Constructor (host, port) tests

    [Test]
    public void Constructor_with_host_port_should_connect()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listeners.Add(listener);

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var conn = new PeerConnection("127.0.0.1", port);
        _connections.Add(conn);

        var serverTcp = listener.AcceptTcpClient();
        _clients.Add(serverTcp);
        listener.Stop();

        Assert.That(conn.IsConnected, Is.True);
        Assert.That(conn.RemoteIp, Is.EqualTo("127.0.0.1"));
        Assert.That(conn.RemotePort, Is.EqualTo(port));
    }

    [Test]
    public void Constructor_with_host_port_should_set_timestamps()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listeners.Add(listener);

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var before = DateTime.UtcNow;
        var conn = new PeerConnection("127.0.0.1", port);
        _connections.Add(conn);
        var after = DateTime.UtcNow;

        var serverTcp = listener.AcceptTcpClient();
        _clients.Add(serverTcp);
        listener.Stop();

        Assert.That(conn.ConnectedAt, Is.GreaterThanOrEqualTo(before));
        Assert.That(conn.ConnectedAt, Is.LessThanOrEqualTo(after));
        Assert.That(conn.LastActivity, Is.GreaterThanOrEqualTo(before));
        Assert.That(conn.LastActivity, Is.LessThanOrEqualTo(after));
    }

    [Test]
    public void Constructor_with_host_port_should_throw_on_connection_failure()
    {
        // Use a port that nothing is listening on
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Assert.Throws<SocketException>(() =>
        {
            var conn = new PeerConnection("127.0.0.1", port);
            _connections.Add(conn);
        });
    }

    // ReceiveHandshake validation branch tests

    [Test]
    public void ReceiveHandshake_should_return_false_for_wrong_pstrlen()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();

        // Send a handshake with wrong pstrlen (20 instead of 19)
        var badHandshake = new byte[68];
        badHandshake[0] = 20; // wrong pstrlen
        rawClient.GetStream().Write(badHandshake, 0, 68);
        rawClient.GetStream().Flush();

        var result = conn.ReceiveHandshake();

        Assert.That(result, Is.False);
    }

    [Test]
    public void ReceiveHandshake_should_return_false_for_wrong_protocol_string()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();

        var badHandshake = new byte[68];
        badHandshake[0] = 19;
        Encoding.ASCII.GetBytes("Wrong protocol str!", 0, 19, badHandshake, 1);
        rawClient.GetStream().Write(badHandshake, 0, 68);
        rawClient.GetStream().Flush();

        var result = conn.ReceiveHandshake();

        Assert.That(result, Is.False);
    }

    [Test]
    public void ReceiveHandshake_should_parse_info_hash_as_lowercase_hex()
    {
        var (client, server) = CreateTestPair();
        var infoHash = "AABBCCDDEE0102030405060708091011DEADBEEF";

        client.SendHandshake(infoHash, "-SD0001-012345678901");
        var result = server.ReceiveHandshake();

        Assert.That(result, Is.True);
        Assert.That(server.InfoHash, Is.EqualTo(infoHash.ToLowerInvariant()));
    }

    [Test]
    public void ReceiveHandshake_should_set_handshake_timeout()
    {
        var (client, server) = CreateTestPair();
        server.HandshakeTimeoutMs = 5000;

        var infoHash = "0102030405060708091011121314151617181920";
        client.SendHandshake(infoHash, "-SD0001-012345678901");
        var result = server.ReceiveHandshake();

        Assert.That(result, Is.True);
    }

    [Test]
    public void ReceiveHandshake_should_return_false_on_partial_data()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();

        // Send only partial data (less than 68 bytes) then close
        rawClient.GetStream().Write(new byte[10], 0, 10);
        rawClient.GetStream().Flush();
        rawClient.Close();

        var result = conn.ReceiveHandshake();

        Assert.That(result, Is.False);
    }

    // SendHandshake failure tests

    [Test]
    public void SendHandshake_should_return_false_when_own_connection_disposed()
    {
        var (client, _) = CreateTestPair();
        client.Dispose();

        var result = client.SendHandshake(
            "0102030405060708091011121314151617181920",
            "-SD0001-012345678901");

        Assert.That(result, Is.False);
    }

    [Test]
    public void SendHandshake_should_update_last_activity()
    {
        var (client, _) = CreateTestPair();
        var before = DateTime.UtcNow;

        client.SendHandshake(
            "0102030405060708091011121314151617181920",
            "-SD0001-012345678901");

        Assert.That(client.LastActivity, Is.GreaterThanOrEqualTo(before));
    }

    // ReceiveMessage additional tests

    [Test]
    public void ReceiveMessage_should_set_receive_timeout_when_configured()
    {
        var (client, server) = CreateTestPair();
        server.MessageReadTimeoutMs = 2000;

        var message = new PeerMessage { Type = PeerMessageType.Interested };
        client.SendMessage(message);

        var received = server.ReceiveMessage();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Interested));
    }

    [Test]
    public void ReceiveMessage_should_return_null_on_timeout()
    {
        var (_, server) = CreateTestPair();
        server.MessageReadTimeoutMs = 200;

        // Don't send anything - should timeout
        var received = server.ReceiveMessage();

        Assert.That(received, Is.Null);
    }

    [Test]
    public void ReceiveMessage_should_handle_message_with_payload()
    {
        var (client, server) = CreateTestPair();

        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var message = new PeerMessage { Type = PeerMessageType.Request, Payload = payload };
        client.SendMessage(message);

        var received = server.ReceiveMessage();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Request));
        Assert.That(received.Payload, Is.EqualTo(payload));
    }

    [Test]
    public void ReceiveMessage_should_update_last_activity()
    {
        var (client, server) = CreateTestPair();

        client.SendMessage(new PeerMessage { Type = PeerMessageType.Choke });
        var before = DateTime.UtcNow;
        server.ReceiveMessage();

        Assert.That(server.LastActivity, Is.GreaterThanOrEqualTo(before));
    }

    // SendMessage with PayloadLength tests

    [Test]
    public void SendMessage_should_use_effective_payload_length()
    {
        var (client, server) = CreateTestPair();

        // Rent a large buffer but only use part of it
        var bigPayload = new byte[100];
        bigPayload[0] = 0xAA;
        bigPayload[1] = 0xBB;
        bigPayload[2] = 0xCC;

        var message = new PeerMessage
        {
            Type = PeerMessageType.Piece,
            Payload = bigPayload,
            PayloadLength = 3
        };

        client.SendMessage(message);
        var received = server.ReceiveMessage();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Piece));
        Assert.That(received.Payload.Length, Is.EqualTo(3));
        Assert.That(received.Payload[0], Is.EqualTo(0xAA));
        Assert.That(received.Payload[1], Is.EqualTo(0xBB));
        Assert.That(received.Payload[2], Is.EqualTo(0xCC));
    }

    [Test]
    public void SendMessage_should_handle_no_payload()
    {
        var (client, server) = CreateTestPair();

        var message = new PeerMessage { Type = PeerMessageType.Interested };
        client.SendMessage(message);

        var received = server.ReceiveMessage();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Interested));
        Assert.That(received.Payload, Is.Null);
    }

    [Test]
    public void SendMessage_should_update_last_activity()
    {
        var (client, _) = CreateTestPair();

        var before = DateTime.UtcNow;
        client.SendMessage(new PeerMessage { Type = PeerMessageType.Choke });

        Assert.That(client.LastActivity, Is.GreaterThanOrEqualTo(before));
    }

    // SendKeepAlive additional tests

    [Test]
    public void SendKeepAlive_should_update_last_activity()
    {
        var (client, _) = CreateTestPair();

        var before = DateTime.UtcNow;
        client.SendKeepAlive();

        Assert.That(client.LastActivity, Is.GreaterThanOrEqualTo(before));
    }

    // SendBitfield additional tests

    [Test]
    public void SendBitfield_should_handle_large_piece_count()
    {
        var (client, server) = CreateTestPair();

        server.SendBitfield(1000);
        var received = client.ReceiveMessage();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Bitfield));
        Assert.That(received.Payload.Length, Is.EqualTo(125)); // ceil(1000/8) = 125
        Assert.That(received.Payload[0], Is.EqualTo(0xFF));
    }

    [Test]
    public void SendBitfield_should_handle_7_pieces()
    {
        var (client, server) = CreateTestPair();

        server.SendBitfield(7);
        var received = client.ReceiveMessage();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Payload.Length, Is.EqualTo(1));
        Assert.That(received.Payload[0], Is.EqualTo(0xFE)); // 7 bits set, 1 trailing cleared
    }

    [Test]
    public void SendBitfield_should_handle_2_pieces()
    {
        var (client, server) = CreateTestPair();

        server.SendBitfield(2);
        var received = client.ReceiveMessage();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Payload.Length, Is.EqualTo(1));
        Assert.That(received.Payload[0], Is.EqualTo(0xC0)); // 2 bits set: 11000000
    }

    // Property setter tests

    [Test]
    public void IdleChance_should_default_to_zero()
    {
        var conn = CreateSingleConnection();

        Assert.That(conn.IdleChance, Is.EqualTo(0.0));
    }

    [Test]
    public void PendingRequestCount_should_default_to_zero()
    {
        var conn = CreateSingleConnection();

        Assert.That(conn.PendingRequestCount, Is.EqualTo(0));
    }

    [Test]
    public void HandshakeTimeoutMs_should_default_to_zero()
    {
        var conn = CreateSingleConnection();

        Assert.That(conn.HandshakeTimeoutMs, Is.EqualTo(0));
    }

    [Test]
    public void MessageReadTimeoutMs_should_default_to_zero()
    {
        var conn = CreateSingleConnection();

        Assert.That(conn.MessageReadTimeoutMs, Is.EqualTo(0));
    }

    [Test]
    public void AmChoking_can_be_set()
    {
        var conn = CreateSingleConnection();
        conn.AmChoking = false;

        Assert.That(conn.AmChoking, Is.False);
    }

    [Test]
    public void PeerInterested_can_be_set()
    {
        var conn = CreateSingleConnection();
        conn.PeerInterested = true;

        Assert.That(conn.PeerInterested, Is.True);
    }

    [Test]
    public void PeerChoking_can_be_set()
    {
        var conn = CreateSingleConnection();
        conn.PeerChoking = false;

        Assert.That(conn.PeerChoking, Is.False);
    }

    [Test]
    public void AmInterested_can_be_set()
    {
        var conn = CreateSingleConnection();
        conn.AmInterested = true;

        Assert.That(conn.AmInterested, Is.True);
    }

    // Multiple message roundtrip

    [Test]
    public void Should_roundtrip_multiple_messages_in_sequence()
    {
        var (client, server) = CreateTestPair();

        client.SendMessage(new PeerMessage { Type = PeerMessageType.Interested });
        client.SendMessage(new PeerMessage { Type = PeerMessageType.Request, Payload = new byte[] { 1, 2, 3 } });
        client.SendMessage(new PeerMessage { Type = PeerMessageType.Cancel, Payload = new byte[] { 4, 5, 6 } });

        var msg1 = server.ReceiveMessage();
        var msg2 = server.ReceiveMessage();
        var msg3 = server.ReceiveMessage();

        Assert.That(msg1.Type, Is.EqualTo(PeerMessageType.Interested));
        Assert.That(msg2.Type, Is.EqualTo(PeerMessageType.Request));
        Assert.That(msg2.Payload, Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(msg3.Type, Is.EqualTo(PeerMessageType.Cancel));
        Assert.That(msg3.Payload, Is.EqualTo(new byte[] { 4, 5, 6 }));
    }

    // Handshake with short peer ID should pad

    [Test]
    public void SendHandshake_should_handle_short_peer_id()
    {
        var (client, server) = CreateTestPair();
        var shortPeerId = "-SD-";

        var sent = client.SendHandshake("0102030405060708091011121314151617181920", shortPeerId);
        var received = server.ReceiveHandshake();

        Assert.That(sent, Is.True);
        Assert.That(received, Is.True);
        Assert.That(server.PeerId.Length, Is.EqualTo(20));
    }

    // Extended message type roundtrip

    [Test]
    public void SendMessage_and_ReceiveMessage_should_roundtrip_extended_message()
    {
        var (client, server) = CreateTestPair();
        var payload = new byte[] { 0x00, 0x64, 0x31, 0x3A };
        var message = new PeerMessage { Type = PeerMessageType.Extended, Payload = payload };

        client.SendMessage(message);
        var received = server.ReceiveMessage();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Extended));
        Assert.That(received.Payload, Is.EqualTo(payload));
    }

    // ReceiveMessage should return null when ReadExact returns false mid-message

    [Test]
    public void ReceiveMessage_should_return_null_when_data_truncated()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();

        // Send length prefix indicating 5 bytes, then only send 2 and close
        var stream = rawClient.GetStream();
        stream.Write(new byte[] { 0, 0, 0, 5 }, 0, 4);
        stream.Write(new byte[] { 0x02, 0x01 }, 0, 2);
        stream.Flush();
        rawClient.Close();

        var received = conn.ReceiveMessage();

        Assert.That(received, Is.Null);
    }

    // Verify message length encoding for large payloads

    [Test]
    public void SendMessage_should_encode_length_correctly_for_large_payload()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();

        var payload = new byte[1000];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }

        conn.SendMessage(new PeerMessage { Type = PeerMessageType.Piece, Payload = payload });

        var stream = rawClient.GetStream();
        var lengthBuf = new byte[4];
        ReadFull(stream, lengthBuf, 4);

        var length = (lengthBuf[0] << 24) | (lengthBuf[1] << 16) |
            (lengthBuf[2] << 8) | lengthBuf[3];

        Assert.That(length, Is.EqualTo(1001)); // 1 (type) + 1000 (payload)
    }

    private static void ReadFull(Stream stream, byte[] buffer, int count)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }
    }

    // NegotiateEncryptionIncoming tests

    [Test]
    public void NegotiateEncryptionIncoming_should_return_false_when_stream_has_no_data()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();

        // Close the client before sending anything so the server's first Read returns 0
        rawClient.Close();

        var result = conn.NegotiateEncryptionIncoming(_ => true, NzbDrone.Core.Peers.Encryption.EncryptionMode.PreferEncrypted);

        Assert.That(result, Is.False);
    }

    [Test]
    public void NegotiateEncryptionIncoming_should_return_true_for_plain_bt_in_prefer_mode()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();

        // Send byte 0x13 (decimal 19) - the standard BT handshake pstrlen
        rawClient.GetStream().WriteByte(0x13);
        rawClient.GetStream().Flush();
        // Keep raw client open so subsequent ReceiveHandshake reads don't fail immediately

        var result = conn.NegotiateEncryptionIncoming(
            _ => true,
            NzbDrone.Core.Peers.Encryption.EncryptionMode.PreferEncrypted);

        Assert.That(result, Is.True);
        Assert.That(conn.IsEncrypted, Is.False);
        Assert.That(conn.EncryptionMethod, Is.EqualTo(NzbDrone.Core.Peers.Encryption.CryptoMethod.PlainText));
    }

    [Test]
    public void NegotiateEncryptionIncoming_should_return_true_for_plain_bt_in_prefer_plaintext_mode()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();

        rawClient.GetStream().WriteByte(0x13);
        rawClient.GetStream().Flush();

        var result = conn.NegotiateEncryptionIncoming(
            _ => true,
            NzbDrone.Core.Peers.Encryption.EncryptionMode.PreferPlainText);

        Assert.That(result, Is.True);
        Assert.That(conn.IsEncrypted, Is.False);
        Assert.That(conn.EncryptionMethod, Is.EqualTo(NzbDrone.Core.Peers.Encryption.CryptoMethod.PlainText));
    }

    [Test]
    public void NegotiateEncryptionIncoming_should_return_false_for_plain_bt_in_require_encrypted_mode()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();

        // Send byte 0x13 - BT plain handshake start - then close so MSE negotiation fails fast
        rawClient.GetStream().WriteByte(0x13);
        rawClient.GetStream().Flush();
        rawClient.Close();

        // RequireEncrypted: peek[0]==19 is not the PlainText shortcut; goes into MSE path which fails
        var result = conn.NegotiateEncryptionIncoming(
            _ => true,
            NzbDrone.Core.Peers.Encryption.EncryptionMode.RequireEncrypted);

        Assert.That(result, Is.False);
    }

    [Test]
    public void NegotiateEncryptionIncoming_should_allow_handshake_read_after_plain_detection()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();

        // Build and send a complete 68-byte BT handshake
        var infoHash = "0102030405060708091011121314151617181920";
        var handshake = new byte[68];
        handshake[0] = 19;
        Encoding.ASCII.GetBytes("BitTorrent protocol", 0, 19, handshake, 1);
        var hashBytes = Convert.FromHexString(infoHash);
        Array.Copy(hashBytes, 0, handshake, 28, 20);
        Encoding.ASCII.GetBytes("-SD0001-012345678901", 0, 20, handshake, 48);

        rawClient.GetStream().Write(handshake, 0, 68);
        rawClient.GetStream().Flush();

        // NegotiateEncryptionIncoming consumes byte[0]=0x13 from networkStream,
        // sets _activeStream = PrefixedStream([0x13], networkStream)
        var encResult = conn.NegotiateEncryptionIncoming(
            _ => true,
            NzbDrone.Core.Peers.Encryption.EncryptionMode.PreferEncrypted);
        Assert.That(encResult, Is.True);

        // ReceiveHandshake reads 68 bytes from _activeStream:
        //   PrefixedStream yields 0x13 first, then 67 bytes from networkStream = full 68 bytes
        var hsResult = conn.ReceiveHandshake();
        Assert.That(hsResult, Is.True);
        Assert.That(conn.InfoHash, Is.EqualTo(infoHash));
    }

    [Test]
    public void NegotiateEncryptionIncoming_should_return_false_on_exception()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();

        // Send a non-0x13 byte to trigger the MSE path, then close so negotiation throws
        rawClient.GetStream().WriteByte(0x01);
        rawClient.GetStream().Flush();
        rawClient.Close();

        var result = conn.NegotiateEncryptionIncoming(
            _ => false,
            NzbDrone.Core.Peers.Encryption.EncryptionMode.PreferEncrypted);

        Assert.That(result, Is.False);
    }

    // NegotiateEncryptionOutgoing tests

    [Test]
    public void NegotiateEncryptionOutgoing_should_return_false_when_remote_closes_immediately()
    {
        var (clientConn, serverConn) = CreateTestPair();

        // Close the server side so outgoing negotiation fails quickly
        serverConn.Dispose();

        var result = clientConn.NegotiateEncryptionOutgoing(
            "0102030405060708091011121314151617181920",
            NzbDrone.Core.Peers.Encryption.EncryptionMode.PreferEncrypted);

        Assert.That(result, Is.False);
    }

    [Test]
    public void NegotiateEncryptionOutgoing_should_return_false_for_invalid_info_hash_format()
    {
        var (clientConn, serverConn) = CreateTestPair();
        _connections.Remove(serverConn);
        serverConn.Dispose();

        // Non-hex string will cause Convert.FromHexString to throw, caught and returns false
        var result = clientConn.NegotiateEncryptionOutgoing(
            "ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ",
            NzbDrone.Core.Peers.Encryption.EncryptionMode.PreferEncrypted);

        Assert.That(result, Is.False);
    }
}
