using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Network;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Encryption;
using NzbDrone.Core.Simulation.ClientBehavior;

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
        Assert.That(server.IsConnected, Is.False);
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
        Assert.That(server.IsConnected, Is.True);
    }

    [Test]
    public void ReceiveMessage_should_dispose_and_disconnect_on_timeout_after_partial_length_prefix()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();
        conn.MessageReadTimeoutMs = 200;

        // Send only 2 bytes of the 4-byte length prefix
        var stream = rawClient.GetStream();
        stream.Write(new byte[] { 0x00, 0x00 }, 0, 2);
        stream.Flush();

        var received = conn.ReceiveMessage();

        Assert.That(received, Is.Null);
        Assert.That(conn.IsConnected, Is.False);
    }

    [Test]
    public void ReceiveMessage_should_dispose_and_disconnect_on_timeout_after_partial_payload()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();
        conn.MessageReadTimeoutMs = 200;

        // Send 4-byte length prefix indicating 16 bytes payload, but only send 2 bytes of payload
        var stream = rawClient.GetStream();
        var lengthBytes = new byte[] { 0x00, 0x00, 0x00, 0x10 };
        stream.Write(lengthBytes, 0, 4);
        stream.Write(new byte[] { 0x01, 0x02 }, 0, 2);
        stream.Flush();

        var received = conn.ReceiveMessage();

        Assert.That(received, Is.Null);
        Assert.That(conn.IsConnected, Is.False);
    }

    [Test]
    public void ReceiveMessage_should_dispose_and_disconnect_on_timeout_after_length_prefix_with_zero_payload_bytes()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();
        conn.MessageReadTimeoutMs = 200;

        // Send 4-byte length prefix indicating 10 bytes payload, but send no payload bytes
        var stream = rawClient.GetStream();
        var lengthBytes = new byte[] { 0x00, 0x00, 0x00, 0x0A };
        stream.Write(lengthBytes, 0, 4);
        stream.Flush();

        var received = conn.ReceiveMessage();

        Assert.That(received, Is.Null);
        Assert.That(conn.IsConnected, Is.False);
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
        Assert.That(conn.IsConnected, Is.False);
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

        // Send byte 0x13 - BT plain handshake start
        rawClient.GetStream().WriteByte(0x13);
        rawClient.GetStream().Flush();
        rawClient.Close();

        // RequireEncrypted: peek[0]==19 is immediately rejected without entering MSE path
        var result = conn.NegotiateEncryptionIncoming(
            _ => true,
            NzbDrone.Core.Peers.Encryption.EncryptionMode.RequireEncrypted);

        Assert.That(result, Is.False);
    }

    [Test]
    public void NegotiateEncryptionIncoming_should_immediately_reject_plain_bt_in_require_encrypted_mode_without_dh()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();

        // Send a full 68-byte BitTorrent handshake, keeping connection open
        var handshake = new byte[68];
        handshake[0] = 0x13;
        Encoding.ASCII.GetBytes("BitTorrent protocol", 0, 19, handshake, 1);
        rawClient.GetStream().Write(handshake, 0, handshake.Length);
        rawClient.GetStream().Flush();

        // In RequireEncrypted mode, peek[0] == 19 must immediately return false without DH or blocking
        var result = conn.NegotiateEncryptionIncoming(
            _ => true,
            NzbDrone.Core.Peers.Encryption.EncryptionMode.RequireEncrypted);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task NegotiateEncryptionIncomingAsync_should_immediately_reject_plain_bt_in_require_encrypted_mode_without_dh()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();

        var handshake = new byte[68];
        handshake[0] = 0x13;
        Encoding.ASCII.GetBytes("BitTorrent protocol", 0, 19, handshake, 1);
        await rawClient.GetStream().WriteAsync(handshake.AsMemory(0, handshake.Length));
        await rawClient.GetStream().FlushAsync();

        var result = await conn.NegotiateEncryptionIncomingAsync(
            _ => true,
            NzbDrone.Core.Peers.Encryption.EncryptionMode.RequireEncrypted,
            CancellationToken.None);

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

    [Test]
    public async Task NegotiateEncryptionIncomingAsync_should_return_false_when_stream_has_no_data()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();
        rawClient.Close();

        var result = await conn.NegotiateEncryptionIncomingAsync(
            _ => true,
            EncryptionMode.PreferEncrypted,
            CancellationToken.None);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task NegotiateEncryptionIncomingAsync_should_return_true_for_plain_bt_in_prefer_mode()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();
        rawClient.GetStream().WriteByte(0x13);
        await rawClient.GetStream().FlushAsync();

        var result = await conn.NegotiateEncryptionIncomingAsync(
            _ => true,
            EncryptionMode.PreferEncrypted,
            CancellationToken.None);

        Assert.That(result, Is.True);
        Assert.That(conn.IsEncrypted, Is.False);
        Assert.That(conn.EncryptionMethod, Is.EqualTo(CryptoMethod.PlainText));
    }

    [Test]
    public async Task NegotiateEncryptionIncomingAsync_with_torrent_selector_should_return_true_for_plain_bt()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();
        rawClient.GetStream().WriteByte(0x13);
        await rawClient.GetStream().FlushAsync();

        var dummyTorrent = new NzbDrone.Core.Torrents.Torrent { Id = 456 };
        var result = await conn.NegotiateEncryptionIncomingAsync(
            _ => dummyTorrent,
            EncryptionMode.PreferEncrypted,
            CancellationToken.None);

        Assert.That(result, Is.True);
        Assert.That(conn.IsEncrypted, Is.False);
        Assert.That(conn.EncryptionMethod, Is.EqualTo(CryptoMethod.PlainText));
    }

    [Test]
    public async Task NegotiateEncryptionIncomingAsync_should_return_false_when_cancelled()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await conn.NegotiateEncryptionIncomingAsync(
            _ => true,
            EncryptionMode.PreferEncrypted,
            cts.Token);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task NegotiateEncryptionOutgoingAsync_should_return_false_when_remote_closes_immediately()
    {
        var (clientConn, serverConn) = CreateTestPair();
        serverConn.Dispose();

        var result = await clientConn.NegotiateEncryptionOutgoingAsync(
            "0102030405060708091011121314151617181920",
            EncryptionMode.PreferEncrypted,
            CancellationToken.None);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task NegotiateEncryptionOutgoingAsync_should_return_false_when_cancelled()
    {
        var (clientConn, serverConn) = CreateTestPair();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await clientConn.NegotiateEncryptionOutgoingAsync(
            "0102030405060708091011121314151617181920",
            EncryptionMode.PreferEncrypted,
            cts.Token);

        Assert.That(result, Is.False);
    }

    [Test]
    public void BuildHandshake_should_preserve_dht_bit_for_public_torrents()
    {
        var handshake = PeerConnection.BuildHandshake("0102030405060708091011121314151617181920", "-SD0001-012345678901", isPrivate: false);

        Assert.That(handshake[27] & 0x01, Is.EqualTo(0x01));
    }

    [Test]
    public void BuildHandshake_should_mask_dht_bit_for_private_torrents()
    {
        var handshake = PeerConnection.BuildHandshake("0102030405060708091011121314151617181920", "-SD0001-012345678901", isPrivate: true);

        Assert.That(handshake[27] & 0x01, Is.EqualTo(0));
    }

    [Test]
    public void BuildHandshake_should_mask_dht_bit_when_client_profile_does_not_support_dht()
    {
        var profile = Substitute.For<IClientProfile>();
        profile.SupportsDht.Returns(false);

        var handshake = PeerConnection.BuildHandshake("0102030405060708091011121314151617181920", "-SD0001-012345678901", isPrivate: false, clientProfile: profile);

        Assert.That(handshake[27] & 0x01, Is.EqualTo(0));
    }

    [Test]
    public void BuildHandshake_should_preserve_dht_bit_when_client_profile_supports_dht_and_torrent_is_public()
    {
        var profile = Substitute.For<IClientProfile>();
        profile.SupportsDht.Returns(true);

        var handshake = PeerConnection.BuildHandshake("0102030405060708091011121314151617181920", "-SD0001-012345678901", isPrivate: false, clientProfile: profile);

        Assert.That(handshake[27] & 0x01, Is.EqualTo(0x01));
    }

    [Test]
    public void BuildHandshake_should_mask_extension_protocol_bit_when_client_profile_does_not_support_extensions()
    {
        var profile = Substitute.For<IClientProfile>();
        profile.SupportsExtensionProtocol.Returns(false);

        var handshake = PeerConnection.BuildHandshake("0102030405060708091011121314151617181920", "-SD0001-012345678901", isPrivate: false, clientProfile: profile);

        Assert.That(handshake[25] & 0x10, Is.EqualTo(0));
    }

    [Test]
    public void BuildHandshake_should_preserve_extension_protocol_bit_when_client_profile_supports_extensions()
    {
        var profile = Substitute.For<IClientProfile>();
        profile.SupportsExtensionProtocol.Returns(true);

        var handshake = PeerConnection.BuildHandshake("0102030405060708091011121314151617181920", "-SD0001-012345678901", isPrivate: false, clientProfile: profile);

        Assert.That(handshake[25] & 0x10, Is.EqualTo(0x10));
    }

    [Test]
    public void BuildHandshake_should_mask_fast_extension_bit_when_client_profile_does_not_support_fast()
    {
        var profile = Substitute.For<IClientProfile>();
        profile.SupportsFastExtension.Returns(false);

        var handshake = PeerConnection.BuildHandshake("0102030405060708091011121314151617181920", "-SD0001-012345678901", isPrivate: false, clientProfile: profile);

        Assert.That(handshake[27] & 0x04, Is.EqualTo(0));
    }

    [Test]
    public void BuildHandshake_should_preserve_fast_extension_bit_when_client_profile_supports_fast()
    {
        var profile = Substitute.For<IClientProfile>();
        profile.SupportsFastExtension.Returns(true);

        var handshake = PeerConnection.BuildHandshake("0102030405060708091011121314151617181920", "-SD0001-012345678901", isPrivate: false, clientProfile: profile);

        Assert.That(handshake[27] & 0x04, Is.EqualTo(0x04));
    }

    [Test]
    public void Constructor_should_accept_and_store_DhKeyPool()
    {
        var pool = Substitute.For<IDhKeyPool>();
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 1234, pool);

        Assert.That(conn.DhKeyPool, Is.SameAs(pool));
    }

    [Test]
    public void BuildHandshake_should_set_reserved_bits_matching_configured_feature_flags()
    {
        const string infoHash = "0102030405060708091011121314151617181920";
        const string peerId = "-SD0001-012345678901";

        var allEnabled = PeerConnection.BuildHandshake(
            infoHash,
            peerId,
            isPrivate: false,
            clientProfile: null,
            supportsExtensions: true,
            supportsFast: true,
            supportsDht: true);

        Assert.That(allEnabled[25] & 0x10, Is.EqualTo(0x10));
        Assert.That(allEnabled[27] & 0x04, Is.EqualTo(0x04));
        Assert.That(allEnabled[27] & 0x01, Is.EqualTo(0x01));

        var allDisabled = PeerConnection.BuildHandshake(
            infoHash,
            peerId,
            isPrivate: false,
            clientProfile: null,
            supportsExtensions: false,
            supportsFast: false,
            supportsDht: false);

        Assert.That(allDisabled[25] & 0x10, Is.EqualTo(0));
        Assert.That(allDisabled[27] & 0x04, Is.EqualTo(0));
        Assert.That(allDisabled[27] & 0x01, Is.EqualTo(0));
    }

    [Test]
    public void BuildHandshake_should_disable_extension_protocol_when_supportsExtensions_is_false()
    {
        var handshake = PeerConnection.BuildHandshake(
            "0102030405060708091011121314151617181920",
            "-SD0001-012345678901",
            supportsExtensions: false);

        Assert.That(handshake[25] & 0x10, Is.EqualTo(0));
        Assert.That(handshake[27] & 0x04, Is.EqualTo(0x04));
        Assert.That(handshake[27] & 0x01, Is.EqualTo(0x01));
    }

    [Test]
    public void BuildHandshake_should_disable_fast_extension_when_supportsFast_is_false()
    {
        var handshake = PeerConnection.BuildHandshake(
            "0102030405060708091011121314151617181920",
            "-SD0001-012345678901",
            supportsFast: false);

        Assert.That(handshake[25] & 0x10, Is.EqualTo(0x10));
        Assert.That(handshake[27] & 0x04, Is.EqualTo(0));
        Assert.That(handshake[27] & 0x01, Is.EqualTo(0x01));
    }

    [Test]
    public void BuildHandshake_should_disable_dht_when_supportsDht_is_false()
    {
        var handshake = PeerConnection.BuildHandshake(
            "0102030405060708091011121314151617181920",
            "-SD0001-012345678901",
            supportsDht: false);

        Assert.That(handshake[25] & 0x10, Is.EqualTo(0x10));
        Assert.That(handshake[27] & 0x04, Is.EqualTo(0x04));
        Assert.That(handshake[27] & 0x01, Is.EqualTo(0));
    }

    [Test]
    public void UpdateTransferRates_should_calculate_accurate_rates_over_rolling_20_second_window()
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 1001);
        _connections.Add(conn);

        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // Initial sample at t0: 0 bytes transferred
        conn.BytesDownloaded = 0;
        conn.BytesUploaded = 0;
        conn.UpdateTransferRates(t0);

        Assert.That(conn.DownloadRate, Is.EqualTo(0));
        Assert.That(conn.UploadRate, Is.EqualTo(0));

        // At t0 + 10s: 100,000 bytes down, 50,000 bytes up (10s elapsed)
        var t1 = t0.AddSeconds(10);
        conn.BytesDownloaded = 100_000;
        conn.BytesUploaded = 50_000;
        conn.UpdateTransferRates(t1);

        // Rate = 100,000 / 10 = 10,000 B/s down, 50,000 / 10 = 5,000 B/s up
        Assert.That(conn.DownloadRate, Is.EqualTo(10_000));
        Assert.That(conn.UploadRate, Is.EqualTo(5_000));

        // At t0 + 20s: 300,000 bytes down, 150,000 bytes up (20s elapsed over full window)
        var t2 = t0.AddSeconds(20);
        conn.BytesDownloaded = 300_000;
        conn.BytesUploaded = 150_000;
        conn.UpdateTransferRates(t2);

        // Rate = 300,000 / 20 = 15,000 B/s down, 150,000 / 20 = 7,500 B/s up
        Assert.That(conn.DownloadRate, Is.EqualTo(15_000));
        Assert.That(conn.UploadRate, Is.EqualTo(7_500));

        // At t0 + 30s: Transfers stopped, Bytes stay 300,000 / 150,000 (rolling window evicts t0 sample)
        var t3 = t0.AddSeconds(30);
        conn.UpdateTransferRates(t3);

        // Window spans [t1, t3] = 20s. Delta down = 300k - 100k = 200k. Delta up = 150k - 50k = 100k.
        // Rate = 200,000 / 20 = 10,000 B/s down, 100,000 / 20 = 5,000 B/s up
        Assert.That(conn.DownloadRate, Is.EqualTo(10_000));
        Assert.That(conn.UploadRate, Is.EqualTo(5_000));

        // At t0 + 40s: Idle for full 20s window (all samples within window have 300k / 150k)
        var t4 = t0.AddSeconds(40);
        conn.UpdateTransferRates(t4);

        // Delta = 0 => rates drop to 0
        Assert.That(conn.DownloadRate, Is.EqualTo(0));
        Assert.That(conn.UploadRate, Is.EqualTo(0));
    }

    [Test]
    public void RecordBytes_and_ResetTransferRates_should_update_byte_counters_and_reset_rates()
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 1001);
        _connections.Add(conn);

        conn.RecordBytesUploaded(5000);
        conn.RecordBytesDownloaded(10000);

        Assert.That(conn.BytesUploaded, Is.EqualTo(5000));
        Assert.That(conn.BytesDownloaded, Is.EqualTo(10000));

        conn.UploadRate = 500;
        conn.DownloadRate = 1000;
        conn.ResetTransferRates();

        Assert.That(conn.UploadRate, Is.EqualTo(0));
        Assert.That(conn.DownloadRate, Is.EqualTo(0));
    }

    [Test]
    public void CalculateBdpBufferSize_should_return_unthrottled_size_when_rate_zero_or_negative()
    {
        Assert.That(PeerConnection.CalculateBdpBufferSize(0), Is.EqualTo(PeerConnection.DefaultUnthrottledBufferSize));
        Assert.That(PeerConnection.CalculateBdpBufferSize(-1), Is.EqualTo(PeerConnection.DefaultUnthrottledBufferSize));
        Assert.That(PeerConnection.CalculateBdpBufferSize(-1000), Is.EqualTo(PeerConnection.DefaultUnthrottledBufferSize));
    }

    [Test]
    public void CalculateBdpBufferSize_should_clamp_to_min_throttled_size_when_bdp_is_small()
    {
        // 10 KB/s with 100ms RTT => 1,000 bytes BDP => clamped to MinThrottledBufferSize (8KB)
        var size1 = PeerConnection.CalculateBdpBufferSize(10_000, 0.1);
        Assert.That(size1, Is.EqualTo(PeerConnection.MinThrottledBufferSize));

        // 50 KB/s with 100ms RTT => 5,000 bytes BDP => clamped to MinThrottledBufferSize (8KB)
        var size2 = PeerConnection.CalculateBdpBufferSize(50_000, 0.1);
        Assert.That(size2, Is.EqualTo(PeerConnection.MinThrottledBufferSize));
    }

    [Test]
    public void CalculateBdpBufferSize_should_scale_proportionally_when_within_throttled_bounds()
    {
        // 200 KB/s with 100ms RTT => 20,000 bytes BDP
        var size = PeerConnection.CalculateBdpBufferSize(200_000, 0.1);
        Assert.That(size, Is.EqualTo(20_000));

        // 500 KB/s with 100ms RTT => 50,000 bytes BDP
        var size2 = PeerConnection.CalculateBdpBufferSize(500_000, 0.1);
        Assert.That(size2, Is.EqualTo(50_000));
    }

    [Test]
    public void CalculateBdpBufferSize_should_clamp_to_max_throttled_size_when_bdp_is_large()
    {
        // 1 MB/s with 100ms RTT => 100,000 bytes BDP => clamped to MaxThrottledBufferSize (64KB)
        var size = PeerConnection.CalculateBdpBufferSize(1_000_000, 0.1);
        Assert.That(size, Is.EqualTo(PeerConnection.MaxThrottledBufferSize));

        // 10 MB/s with 100ms RTT => 1,000,000 bytes BDP => clamped to MaxThrottledBufferSize (64KB)
        var size2 = PeerConnection.CalculateBdpBufferSize(10_000_000, 0.1);
        Assert.That(size2, Is.EqualTo(PeerConnection.MaxThrottledBufferSize));
    }

    [Test]
    public void ApplySocketOptions_should_apply_calculated_bdp_buffer_sizes_to_tcp_client()
    {
        using var client = new TcpClient();
        _clients.Add(client);

        PeerConnection.ApplySocketOptions(client, sendRateLimit: 200_000, receiveRateLimit: 500_000, rttSeconds: 0.1);

        // On Linux, socket buffer sizes are doubled by kernel, so SendBufferSize >= 20000
        Assert.That(client.Client.SendBufferSize, Is.GreaterThanOrEqualTo(20_000));
        Assert.That(client.Client.ReceiveBufferSize, Is.GreaterThanOrEqualTo(50_000));
    }

    [Test]
    public void ApplySocketOptions_should_handle_null_client_safely()
    {
        Assert.DoesNotThrow(() => PeerConnection.ApplySocketOptions(null));
    }

    [Test]
    public void UploadRateLimit_and_DownloadRateLimit_change_should_dynamically_update_socket_buffers()
    {
        var pair = CreateTestPair();
        var client = pair.Client;

        client.UploadRateLimit = 200_000;
        client.DownloadRateLimit = 500_000;

        Assert.That(client.SendBufferSize, Is.GreaterThanOrEqualTo(20_000));
        Assert.That(client.ReceiveBufferSize, Is.GreaterThanOrEqualTo(50_000));

        client.SetRateLimits(10_000, 10_000_000, 0.1);
        Assert.That(client.SendBufferSize, Is.GreaterThanOrEqualTo(PeerConnection.MinThrottledBufferSize));
        Assert.That(client.ReceiveBufferSize, Is.GreaterThanOrEqualTo(PeerConnection.MaxThrottledBufferSize));
    }

    [Test]
    public void SendMessage_should_slice_large_payload_and_pace_writes_when_rate_limited()
    {
        var stream = new MemoryStream();
        var conn = new PeerConnection(stream, "127.0.0.1", 6881)
        {
            UploadRateLimit = 100_000, // 100 KB/s
            PacingChunkSize = 2048
        };
        _connections.Add(conn);

        var pacedChunks = new List<(int Size, long Ticks)>();
        conn.OnWriteChunkPaced = (size, ticks) => pacedChunks.Add((size, ticks));

        // Replace wait handler with non-blocking recorder for fast testing
        PeerConnection.PaceWaitHandler = (start, ticks) => { };

        try
        {
            var payload = new byte[8192];
            for (var i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i % 256);
            }

            conn.SendMessage(new PeerMessage
            {
                Type = PeerMessageType.Piece,
                Payload = payload,
                PayloadLength = payload.Length
            });

            // 4 bytes len + 1 byte type + 8192 bytes payload = 8197 bytes total.
            // Slices: 2048, 2048, 2048, 2048, 5 (remainder).
            // Pacing delay is invoked for all slices except the last one (4 paced slices).
            Assert.That(pacedChunks.Count, Is.EqualTo(4));
            foreach (var chunk in pacedChunks)
            {
                Assert.That(chunk.Size, Is.EqualTo(2048));
                Assert.That(chunk.Ticks, Is.GreaterThan(0));
            }

            // Verify entire stream was written correctly
            Assert.That(stream.Length, Is.EqualTo(8197));
        }
        finally
        {
            PeerConnection.PaceWaitHandler = BandwidthPacer.PaceWait;
        }
    }

    [Test]
    public void SendMessage_should_not_slice_or_pace_when_unthrottled()
    {
        var stream = new MemoryStream();
        var conn = new PeerConnection(stream, "127.0.0.1", 6881)
        {
            UploadRateLimit = 0, // Unthrottled
            PacingChunkSize = 2048
        };
        _connections.Add(conn);

        var pacedChunks = new List<(int Size, long Ticks)>();
        conn.OnWriteChunkPaced = (size, ticks) => pacedChunks.Add((size, ticks));

        var payload = new byte[8192];
        conn.SendMessage(new PeerMessage
        {
            Type = PeerMessageType.Piece,
            Payload = payload,
            PayloadLength = payload.Length
        });

        Assert.That(pacedChunks, Is.Empty);
        Assert.That(stream.Length, Is.EqualTo(8197));
    }

    [Test]
    public void SendMessage_should_not_pace_for_small_messages_even_when_rate_limited()
    {
        var stream = new MemoryStream();
        var conn = new PeerConnection(stream, "127.0.0.1", 6881)
        {
            UploadRateLimit = 50_000,
            PacingChunkSize = 2048
        };
        _connections.Add(conn);

        var pacedChunks = new List<(int Size, long Ticks)>();
        conn.OnWriteChunkPaced = (size, ticks) => pacedChunks.Add((size, ticks));

        conn.SendMessage(new PeerMessage
        {
            Type = PeerMessageType.Choke
        });

        Assert.That(pacedChunks, Is.Empty);
        Assert.That(stream.Length, Is.EqualTo(5));
    }

    [Test]
    public void BuildHandshake_should_build_80_byte_handshake_for_32_byte_v2_infohash()
    {
        const string v2InfoHash = "0102030405060708091011121314151617181920212223242526272829303132";
        const string peerId = "-SD0001-012345678901";

        var handshake = PeerConnection.BuildHandshake(v2InfoHash, peerId);

        Assert.That(handshake.Length, Is.EqualTo(80));
        Assert.That(handshake[0], Is.EqualTo(19));
        Assert.That(Encoding.ASCII.GetString(handshake, 1, 19), Is.EqualTo("BitTorrent protocol"));
        Assert.That(handshake[27] & 0x10, Is.EqualTo(0x10)); // BEP 52 capability bit

        var expectedHashBytes = Convert.FromHexString(v2InfoHash);
        var actualHashBytes = new byte[32];
        Array.Copy(handshake, 28, actualHashBytes, 0, 32);
        Assert.That(actualHashBytes, Is.EqualTo(expectedHashBytes));

        var actualPeerId = Encoding.ASCII.GetString(handshake, 60, 20);
        Assert.That(actualPeerId, Is.EqualTo(peerId));
    }

    [Test]
    public void BuildHandshake_should_set_bep52_capability_bit_for_hybrid_handshake()
    {
        const string v1InfoHash = "0102030405060708091011121314151617181920";
        const string peerId = "-SD0001-012345678901";

        var handshake = PeerConnection.BuildHandshake(v1InfoHash, peerId, supportsV2: true);

        Assert.That(handshake.Length, Is.EqualTo(68));
        Assert.That(handshake[27] & 0x10, Is.EqualTo(0x10));
    }

    [Test]
    public void BuildHandshake_should_throw_when_infohash_has_invalid_length()
    {
        const string invalidHash = "01020304050607080910111213141516171819";
        const string peerId = "-SD0001-012345678901";

        Assert.Throws<ArgumentException>(() => PeerConnection.BuildHandshake(invalidHash, peerId));
    }

    [Test]
    public void SendHandshake_and_ReceiveHandshake_should_roundtrip_80_byte_v2_handshake_without_stream_desync()
    {
        var (client, server) = CreateTestPair();
        const string v2InfoHash = "0102030405060708091011121314151617181920212223242526272829303132";
        const string clientPeerId = "-SD0001-012345678901";

        var sent = client.SendHandshake(v2InfoHash, clientPeerId);
        Assert.That(sent, Is.True);
        Assert.That(client.InfoHash, Is.EqualTo(v2InfoHash));
        Assert.That(client.InfoHashV2, Is.EqualTo(v2InfoHash));

        var received = server.ReceiveHandshake();
        Assert.That(received, Is.True);
        Assert.That(server.InfoHash, Is.EqualTo(v2InfoHash));
        Assert.That(server.InfoHashV2, Is.EqualTo(v2InfoHash));
        Assert.That(server.PeerId, Is.EqualTo(clientPeerId));
        Assert.That(server.SupportsV2, Is.True);
        Assert.That(server.SupportsBep52, Is.True);

        // Verify message stream framing is intact after 80-byte handshake (no desync)
        client.SendMessage(new PeerMessage { Type = PeerMessageType.Choke });
        var message = server.ReceiveMessage();

        Assert.That(message, Is.Not.Null);
        Assert.That(message.Type, Is.EqualTo(PeerMessageType.Choke));
    }

    [Test]
    public void SendHandshake_and_ReceiveHandshake_should_roundtrip_hybrid_handshake_with_bep52_capability()
    {
        var (client, server) = CreateTestPair();
        const string v1InfoHash = "0102030405060708091011121314151617181920";
        const string clientPeerId = "-SD0001-012345678901";

        var sent = client.SendHandshake(v1InfoHash, clientPeerId, supportsV2: true);
        Assert.That(sent, Is.True);

        var received = server.ReceiveHandshake();
        Assert.That(received, Is.True);
        Assert.That(server.InfoHash, Is.EqualTo(v1InfoHash));
        Assert.That(server.InfoHashV2, Is.Null);
        Assert.That(server.PeerId, Is.EqualTo(clientPeerId));
        Assert.That(server.SupportsV2, Is.True);
        Assert.That(server.ReservedBytes[7] & 0x10, Is.EqualTo(0x10));

        // Verify message stream framing is intact after 68-byte hybrid handshake
        client.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
        var message = server.ReceiveMessage();

        Assert.That(message, Is.Not.Null);
        Assert.That(message.Type, Is.EqualTo(PeerMessageType.Unchoke));
    }

    [Test]
    public void ReceiveHandshake_should_support_explicit_expected_v2_hash_length()
    {
        var (client, server) = CreateTestPair();
        const string v2InfoHash = "0102030405060708091011121314151617181920212223242526272829303132";
        const string clientPeerId = "-SD0001-012345678901";

        client.SendHandshake(v2InfoHash, clientPeerId);
        var received = server.ReceiveHandshake(expectedHashLength: 32);

        Assert.That(received, Is.True);
        Assert.That(server.InfoHash, Is.EqualTo(v2InfoHash));
        Assert.That(server.InfoHashV2, Is.EqualTo(v2InfoHash));
    }

    private class TrackingArrayPool : ArrayPool<byte>
    {
        public int RentCount { get; private set; }
        public int ReturnCount { get; private set; }

        public override byte[] Rent(int minimumLength)
        {
            RentCount++;
            return ArrayPool<byte>.Shared.Rent(minimumLength);
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            ReturnCount++;
            ArrayPool<byte>.Shared.Return(array, clearArray);
        }
    }

    [Test]
    public async Task ReceiveMessageAsync_should_handle_message_with_payload()
    {
        var (client, server) = CreateTestPair();

        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var message = new PeerMessage { Type = PeerMessageType.Request, Payload = payload };
        client.SendMessage(message);

        var received = await server.ReceiveMessageAsync();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Request));
        Assert.That(received.Payload, Is.EqualTo(payload));
    }

    [Test]
    public async Task ReceiveMessageAsync_should_handle_message_without_payload()
    {
        var (client, server) = CreateTestPair();

        var message = new PeerMessage { Type = PeerMessageType.Interested };
        client.SendMessage(message);

        var received = await server.ReceiveMessageAsync();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Interested));
        Assert.That(received.Payload, Is.Null);
    }

    [Test]
    public async Task ReceiveMessageAsync_should_track_download_bytes_for_piece()
    {
        var (client, server) = CreateTestPair();

        var payload = new byte[16];
        payload[0] = 0xAA;
        var message = new PeerMessage { Type = PeerMessageType.Piece, Payload = payload };
        client.SendMessage(message);

        var beforeBytes = server.BytesDownloaded;
        var received = await server.ReceiveMessageAsync();

        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Piece));
        Assert.That(server.BytesDownloaded, Is.EqualTo(beforeBytes + 8));
    }

    [Test]
    public async Task ReceiveMessageAsync_should_handle_keep_alive()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();

        var stream = rawClient.GetStream();
        await stream.WriteAsync(new byte[] { 0x00, 0x00, 0x00, 0x00 });
        await stream.FlushAsync();

        var before = DateTime.UtcNow;
        var received = await conn.ReceiveMessageAsync();

        Assert.That(received, Is.Null);
        Assert.That(conn.IsConnected, Is.True);
        Assert.That(conn.LastActivity, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public async Task ReceiveMessageAsync_should_return_rented_buffers_to_pool_on_success()
    {
        var (client, server) = CreateTestPair();
        var pool = new TrackingArrayPool();
        server.BufferPool = pool;

        var message = new PeerMessage
        {
            Type = PeerMessageType.Have,
            Payload = new byte[] { 0x00, 0x00, 0x00, 0x05 }
        };
        client.SendMessage(message);

        var received = await server.ReceiveMessageAsync();

        Assert.That(received, Is.Not.Null);
        Assert.That(pool.RentCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(pool.ReturnCount, Is.EqualTo(pool.RentCount));
    }

    [Test]
    public async Task ReceiveMessageAsync_should_return_rented_buffers_to_pool_on_keep_alive()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();
        var pool = new TrackingArrayPool();
        conn.BufferPool = pool;

        var stream = rawClient.GetStream();
        await stream.WriteAsync(new byte[] { 0x00, 0x00, 0x00, 0x00 });
        await stream.FlushAsync();

        var received = await conn.ReceiveMessageAsync();

        Assert.That(received, Is.Null);
        Assert.That(pool.RentCount, Is.EqualTo(1));
        Assert.That(pool.ReturnCount, Is.EqualTo(1));
    }

    [Test]
    public void ReceiveMessageAsync_should_throw_on_cancellation()
    {
        var (_, server) = CreateTestPair();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () =>
        {
            await server.ReceiveMessageAsync(cts.Token);
        });
    }

    [Test]
    public async Task ReceiveMessageAsync_should_return_null_on_timeout_without_cancellation()
    {
        var (_, server) = CreateTestPair();
        server.MessageReadTimeoutMs = 100;

        var received = await server.ReceiveMessageAsync();

        Assert.That(received, Is.Null);
        Assert.That(server.IsConnected, Is.True);
    }

    [Test]
    public async Task ReceiveMessageAsync_should_dispose_on_timeout_after_partial_length()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();
        conn.MessageReadTimeoutMs = 150;

        var stream = rawClient.GetStream();
        await stream.WriteAsync(new byte[] { 0x00, 0x00 });
        await stream.FlushAsync();

        var received = await conn.ReceiveMessageAsync();

        Assert.That(received, Is.Null);
        Assert.That(conn.IsConnected, Is.False);
    }

    [Test]
    public async Task ReceiveMessageAsync_should_dispose_on_timeout_after_partial_payload()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();
        conn.MessageReadTimeoutMs = 150;

        var stream = rawClient.GetStream();
        await stream.WriteAsync(new byte[] { 0x00, 0x00, 0x00, 0x10, 0x01, 0x02 });
        await stream.FlushAsync();

        var received = await conn.ReceiveMessageAsync();

        Assert.That(received, Is.Null);
        Assert.That(conn.IsConnected, Is.False);
    }

    [Test]
    public async Task ReceiveMessageAsync_should_return_null_when_data_truncated()
    {
        var (conn, rawClient) = CreateConnectionWithRawClient();

        var stream = rawClient.GetStream();
        await stream.WriteAsync(new byte[] { 0, 0, 0, 5, 0x02, 0x01 });
        await stream.FlushAsync();
        rawClient.Close();

        var received = await conn.ReceiveMessageAsync();

        Assert.That(received, Is.Null);
        Assert.That(conn.IsConnected, Is.False);
    }
}
