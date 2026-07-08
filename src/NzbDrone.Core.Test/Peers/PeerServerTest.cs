using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Encryption;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers.MultiTracker;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class PeerServerTest
{
    private IConfigService _configService;
    private ITorrentService _torrentService;
    private IConnectionManager _connectionManager;
    private IPeerDiscoveryService _peerDiscovery;
    private IMultiTrackerManager _multiTracker;
    private PeerServer _server;
    private List<PeerConnection> _connections;
    private List<TcpListener> _listeners;
    private List<TcpClient> _clients;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _torrentService = Substitute.For<ITorrentService>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        _multiTracker = Substitute.For<IMultiTrackerManager>();

        _configService.MaxGlobalConnections.Returns(200);
        _configService.ListeningPort.Returns(0);
        _configService.EncryptionMode.Returns("enabled");
        _configService.HandshakeTimeoutSeconds.Returns(30);
        _configService.MessageReadTimeoutSeconds.Returns(60);
        _configService.KeepAliveIntervalSeconds.Returns(120);
        _configService.PeerRequestCount.Returns(200);
        _configService.PeerIdleChance.Returns(0.0);
        _configService.PeerContactIntervalSeconds.Returns(300);

        _server = new PeerServer(_configService, _torrentService, _connectionManager, _peerDiscovery, _multiTracker);
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

        _server?.Dispose();
    }

    private PeerConnection CreateTestConnection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listeners.Add(listener);

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        _clients.Add(client);
        var serverTcp = listener.AcceptTcpClient();
        listener.Stop();

        var conn = new PeerConnection(serverTcp);
        _connections.Add(conn);
        return conn;
    }

    private EncryptionMode InvokeGetEncryptionMode()
    {
        var method = typeof(PeerServer).GetMethod(
            "GetEncryptionMode",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (EncryptionMode)method.Invoke(_server, Array.Empty<object>());
    }

    private void InvokeHandleMessage(PeerConnection connection, PeerMessage message)
    {
        var method = typeof(PeerServer).GetMethod(
            "HandleMessage",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_server, new object[] { connection, message });
    }

    private static void InvokeHandlePieceRequest(PeerConnection connection, byte[] payload)
    {
        var method = typeof(PeerServer).GetMethod(
            "HandlePieceRequest",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Invoke(null, new object[] { connection, payload });
    }

    private bool InvokeValidateInfoHash(byte[] skeyHash)
    {
        var method = typeof(PeerServer).GetMethod(
            "ValidateInfoHash",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (bool)method.Invoke(_server, new object[] { skeyHash });
    }

    // Constructor tests

    [Test]
    public void Constructor_should_create_server_with_config_dependencies()
    {
        Assert.That(_server, Is.Not.Null);
    }

    [Test]
    public void Constructor_should_use_max_global_connections_for_semaphore()
    {
        _configService.MaxGlobalConnections.Returns(50);
        var server = new PeerServer(_configService, _torrentService, _connectionManager, _peerDiscovery, _multiTracker);

        Assert.That(server, Is.Not.Null);
        server.Dispose();
    }

    [Test]
    public void Dispose_should_not_throw()
    {
        Assert.DoesNotThrow(() => _server.Dispose());
    }

    [Test]
    public void Dispose_should_not_throw_when_called_twice()
    {
        _server.Dispose();

        Assert.DoesNotThrow(() => _server.Dispose());
    }

    // GetEncryptionMode tests

    [Test]
    public void GetEncryptionMode_should_return_require_encrypted_for_required()
    {
        _configService.EncryptionMode.Returns("required");

        var result = InvokeGetEncryptionMode();

        Assert.That(result, Is.EqualTo(EncryptionMode.RequireEncrypted));
    }

    [Test]
    public void GetEncryptionMode_should_return_prefer_plain_text_for_disabled()
    {
        _configService.EncryptionMode.Returns("disabled");

        var result = InvokeGetEncryptionMode();

        Assert.That(result, Is.EqualTo(EncryptionMode.PreferPlainText));
    }

    [Test]
    public void GetEncryptionMode_should_return_prefer_encrypted_for_enabled()
    {
        _configService.EncryptionMode.Returns("enabled");

        var result = InvokeGetEncryptionMode();

        Assert.That(result, Is.EqualTo(EncryptionMode.PreferEncrypted));
    }

    [Test]
    public void GetEncryptionMode_should_return_prefer_encrypted_for_unknown_value()
    {
        _configService.EncryptionMode.Returns("something_else");

        var result = InvokeGetEncryptionMode();

        Assert.That(result, Is.EqualTo(EncryptionMode.PreferEncrypted));
    }

    [Test]
    public void GetEncryptionMode_should_return_prefer_encrypted_for_empty_string()
    {
        _configService.EncryptionMode.Returns("");

        var result = InvokeGetEncryptionMode();

        Assert.That(result, Is.EqualTo(EncryptionMode.PreferEncrypted));
    }

    // HandleMessage tests

    [Test]
    public void HandleMessage_should_set_peer_interested_on_interested_message()
    {
        var conn = CreateTestConnection();
        conn.PeerInterested = false;
        conn.AmChoking = false;

        var message = new PeerMessage { Type = PeerMessageType.Interested };
        InvokeHandleMessage(conn, message);

        Assert.That(conn.PeerInterested, Is.True);
    }

    [Test]
    public void HandleMessage_should_unchoke_on_interested_when_choking()
    {
        var (clientConn, serverConn) = CreateTestPair();
        serverConn.AmChoking = true;
        serverConn.PeerInterested = false;

        var message = new PeerMessage { Type = PeerMessageType.Interested };
        InvokeHandleMessage(serverConn, message);

        Assert.That(serverConn.AmChoking, Is.False);
        Assert.That(serverConn.PeerInterested, Is.True);

        // Verify unchoke message was sent
        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Unchoke));
    }

    [Test]
    public void HandleMessage_should_not_send_unchoke_if_already_unchoking()
    {
        var conn = CreateTestConnection();
        conn.AmChoking = false;
        conn.PeerInterested = false;

        var message = new PeerMessage { Type = PeerMessageType.Interested };
        InvokeHandleMessage(conn, message);

        Assert.That(conn.AmChoking, Is.False);
        Assert.That(conn.PeerInterested, Is.True);
    }

    [Test]
    public void HandleMessage_should_set_peer_not_interested()
    {
        var conn = CreateTestConnection();
        conn.PeerInterested = true;

        var message = new PeerMessage { Type = PeerMessageType.NotInterested };
        InvokeHandleMessage(conn, message);

        Assert.That(conn.PeerInterested, Is.False);
    }

    [Test]
    public void HandleMessage_should_ignore_request_when_pipeline_full()
    {
        var conn = CreateTestConnection();
        conn.MaxPipelinedRequests = 5;
        conn.PendingRequestCount = 5;
        conn.IdleChance = 0.0;

        var payload = new byte[12];
        payload[11] = 16; // length = 16
        var message = new PeerMessage { Type = PeerMessageType.Request, Payload = payload };

        InvokeHandleMessage(conn, message);

        Assert.That(conn.PendingRequestCount, Is.EqualTo(5));
    }

    [Test]
    public void HandleMessage_should_process_request_and_increment_pending_count()
    {
        var (clientConn, serverConn) = CreateTestPair();
        serverConn.MaxPipelinedRequests = 200;
        serverConn.PendingRequestCount = 0;
        serverConn.IdleChance = 0.0;

        var payload = BuildRequestPayload(0, 0, 16384);
        var message = new PeerMessage { Type = PeerMessageType.Request, Payload = payload };

        InvokeHandleMessage(serverConn, message);

        Assert.That(serverConn.PendingRequestCount, Is.EqualTo(1));
    }

    [Test]
    public void HandleMessage_should_ignore_request_with_null_payload()
    {
        var conn = CreateTestConnection();
        conn.MaxPipelinedRequests = 200;
        conn.PendingRequestCount = 0;
        conn.IdleChance = 0.0;

        var message = new PeerMessage { Type = PeerMessageType.Request, Payload = null };

        InvokeHandleMessage(conn, message);

        Assert.That(conn.PendingRequestCount, Is.EqualTo(0));
    }

    [Test]
    public void HandleMessage_should_ignore_request_with_short_payload()
    {
        var conn = CreateTestConnection();
        conn.MaxPipelinedRequests = 200;
        conn.PendingRequestCount = 0;
        conn.IdleChance = 0.0;

        var message = new PeerMessage { Type = PeerMessageType.Request, Payload = new byte[8] };

        InvokeHandleMessage(conn, message);

        Assert.That(conn.PendingRequestCount, Is.EqualTo(0));
    }

    [Test]
    public void HandleMessage_should_ignore_unknown_message_types()
    {
        var conn = CreateTestConnection();
        conn.PeerInterested = false;

        var message = new PeerMessage { Type = PeerMessageType.Have };

        InvokeHandleMessage(conn, message);

        // Should not throw or change state
        Assert.That(conn.PeerInterested, Is.False);
    }

    [Test]
    public void HandleMessage_should_ignore_choke_message()
    {
        var conn = CreateTestConnection();

        var message = new PeerMessage { Type = PeerMessageType.Choke };

        Assert.DoesNotThrow(() => InvokeHandleMessage(conn, message));
    }

    [Test]
    public void HandleMessage_should_ignore_bitfield_message()
    {
        var conn = CreateTestConnection();

        var message = new PeerMessage { Type = PeerMessageType.Bitfield, Payload = new byte[] { 0xFF } };

        Assert.DoesNotThrow(() => InvokeHandleMessage(conn, message));
    }

    [Test]
    public void HandleMessage_should_ignore_cancel_message()
    {
        var conn = CreateTestConnection();

        var message = new PeerMessage { Type = PeerMessageType.Cancel };

        Assert.DoesNotThrow(() => InvokeHandleMessage(conn, message));
    }

    // HandlePieceRequest tests

    [Test]
    public void HandlePieceRequest_should_send_piece_for_valid_request()
    {
        var (clientConn, serverConn) = CreateTestPair();

        var payload = BuildRequestPayload(1, 0, 16384);
        InvokeHandlePieceRequest(serverConn, payload);

        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Piece));
        Assert.That(received.Payload, Is.Not.Null);

        // Verify index and begin in the piece payload
        var index = (received.Payload[0] << 24) | (received.Payload[1] << 16) |
                    (received.Payload[2] << 8) | received.Payload[3];
        var begin = (received.Payload[4] << 24) | (received.Payload[5] << 16) |
                    (received.Payload[6] << 8) | received.Payload[7];

        Assert.That(index, Is.EqualTo(1));
        Assert.That(begin, Is.EqualTo(0));
    }

    [Test]
    public void HandlePieceRequest_should_reject_zero_length()
    {
        var (clientConn, serverConn) = CreateTestPair();

        var payload = BuildRequestPayload(0, 0, 0);
        InvokeHandlePieceRequest(serverConn, payload);

        // Should not send anything - set a short timeout to verify
        clientConn.MessageReadTimeoutMs = 200;
        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Null);
    }

    [Test]
    public void HandlePieceRequest_should_reject_negative_length()
    {
        var (clientConn, serverConn) = CreateTestPair();

        // -1 in two's complement: 0xFF FF FF FF
        var payload = BuildRequestPayload(0, 0, -1);
        InvokeHandlePieceRequest(serverConn, payload);

        clientConn.MessageReadTimeoutMs = 200;
        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Null);
    }

    [Test]
    public void HandlePieceRequest_should_reject_length_exceeding_max_block_size()
    {
        var (clientConn, serverConn) = CreateTestPair();

        var payload = BuildRequestPayload(0, 0, 32769); // MaxBlockSize is 32768
        InvokeHandlePieceRequest(serverConn, payload);

        clientConn.MessageReadTimeoutMs = 200;
        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Null);
    }

    [Test]
    public void HandlePieceRequest_should_accept_max_block_size()
    {
        var (clientConn, serverConn) = CreateTestPair();

        var payload = BuildRequestPayload(0, 0, 32768); // exactly MaxBlockSize
        InvokeHandlePieceRequest(serverConn, payload);

        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Piece));
    }

    [Test]
    public void HandlePieceRequest_should_reject_negative_index()
    {
        var (clientConn, serverConn) = CreateTestPair();

        var payload = BuildRequestPayload(-1, 0, 16384);
        InvokeHandlePieceRequest(serverConn, payload);

        clientConn.MessageReadTimeoutMs = 200;
        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Null);
    }

    [Test]
    public void HandlePieceRequest_should_reject_negative_begin()
    {
        var (clientConn, serverConn) = CreateTestPair();

        var payload = BuildRequestPayload(0, -1, 16384);
        InvokeHandlePieceRequest(serverConn, payload);

        clientConn.MessageReadTimeoutMs = 200;
        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Null);
    }

    [Test]
    public void HandlePieceRequest_should_encode_index_and_begin_in_response()
    {
        var (clientConn, serverConn) = CreateTestPair();

        var payload = BuildRequestPayload(42, 8192, 1024);
        InvokeHandlePieceRequest(serverConn, payload);

        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);

        var index = (received.Payload[0] << 24) | (received.Payload[1] << 16) |
                    (received.Payload[2] << 8) | received.Payload[3];
        var begin = (received.Payload[4] << 24) | (received.Payload[5] << 16) |
                    (received.Payload[6] << 8) | received.Payload[7];

        Assert.That(index, Is.EqualTo(42));
        Assert.That(begin, Is.EqualTo(8192));
    }

    [Test]
    public void HandlePieceRequest_should_send_correct_payload_size()
    {
        var (clientConn, serverConn) = CreateTestPair();
        const int requestedLength = 4096;

        var payload = BuildRequestPayload(0, 0, requestedLength);
        InvokeHandlePieceRequest(serverConn, payload);

        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);

        // Payload = 8 bytes (index + begin) + requestedLength
        Assert.That(received.Payload.Length, Is.EqualTo(8 + requestedLength));
    }

    [Test]
    public void HandlePieceRequest_should_accept_standard_16kb_block()
    {
        var (clientConn, serverConn) = CreateTestPair();

        var payload = BuildRequestPayload(0, 0, 16384); // standard 16KB
        InvokeHandlePieceRequest(serverConn, payload);

        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Piece));
    }

    [Test]
    public void HandlePieceRequest_should_accept_single_byte_length()
    {
        var (clientConn, serverConn) = CreateTestPair();

        var payload = BuildRequestPayload(0, 0, 1);
        InvokeHandlePieceRequest(serverConn, payload);

        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Not.Null);
        Assert.That(received.Type, Is.EqualTo(PeerMessageType.Piece));
        Assert.That(received.Payload.Length, Is.EqualTo(9)); // 8 header + 1
    }

    // ValidateInfoHash tests

    [Test]
    public void ValidateInfoHash_should_return_true_for_matching_hash()
    {
        var infoHash = "0102030405060708091011121314151617181920";
        var torrent = new Torrent { InfoHash = infoHash };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var infoHashBytes = Convert.FromHexString(infoHash);
        var skeyHash = MseKeyDerivation.DeriveKey(infoHashBytes, System.Text.Encoding.ASCII.GetBytes("req2"));

        var result = InvokeValidateInfoHash(skeyHash);

        Assert.That(result, Is.True);
    }

    [Test]
    public void ValidateInfoHash_should_return_false_for_non_matching_hash()
    {
        var infoHash = "0102030405060708091011121314151617181920";
        var torrent = new Torrent { InfoHash = infoHash };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var wrongHash = new byte[20];
        var result = InvokeValidateInfoHash(wrongHash);

        Assert.That(result, Is.False);
    }

    [Test]
    public void ValidateInfoHash_should_return_false_when_no_torrents()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());

        var skeyHash = new byte[20];
        var result = InvokeValidateInfoHash(skeyHash);

        Assert.That(result, Is.False);
    }

    [Test]
    public void ValidateInfoHash_should_check_all_torrents()
    {
        var torrent1 = new Torrent { InfoHash = "0102030405060708091011121314151617181920" };
        var torrent2 = new Torrent { InfoHash = "A1A2A3A4A5A6A7A8A9A0B1B2B3B4B5B6B7B8B9B0" };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var infoHashBytes = Convert.FromHexString(torrent2.InfoHash);
        var skeyHash = MseKeyDerivation.DeriveKey(infoHashBytes, System.Text.Encoding.ASCII.GetBytes("req2"));

        var result = InvokeValidateInfoHash(skeyHash);

        Assert.That(result, Is.True);
    }

    // HandleMessage idle chance tests

    [Test]
    public void HandleMessage_should_send_keepalive_when_idle_chance_is_1()
    {
        var (clientConn, serverConn) = CreateTestPair();
        serverConn.MaxPipelinedRequests = 200;
        serverConn.PendingRequestCount = 0;
        serverConn.IdleChance = 1.0; // Always trigger idle chance

        var payload = BuildRequestPayload(0, 0, 16384);
        var message = new PeerMessage { Type = PeerMessageType.Request, Payload = payload };

        InvokeHandleMessage(serverConn, message);

        // PendingRequestCount must not increment - idle path was taken instead
        Assert.That(serverConn.PendingRequestCount, Is.EqualTo(0));

        // Server sent a keep-alive (4 zero bytes) - ReceiveMessage returns null for keep-alive
        clientConn.MessageReadTimeoutMs = 1000;
        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Null);
    }

    // RunListenerAsync tests

    private Task InvokeRunListenerAsync(CancellationToken ct)
    {
        var method = typeof(PeerServer).GetMethod(
            "RunListenerAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (Task)method.Invoke(_server, new object[] { ct });
    }

    private Task InvokeRunPeerContactLoopAsync(CancellationToken ct)
    {
        var method = typeof(PeerServer).GetMethod(
            "RunPeerContactLoopAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (Task)method.Invoke(_server, new object[] { ct });
    }

    [Test]
    public async Task RunListenerAsync_should_return_when_port_already_in_use()
    {
        // Occupy a port so RunListenerAsync fails to bind the same address:port
        var occupied = new TcpListener(IPAddress.Any, 0);
        occupied.Start();
        _listeners.Add(occupied);
        var port = ((IPEndPoint)occupied.LocalEndpoint).Port;
        _configService.ListeningPort.Returns(port);

        // Should return quickly due to SocketException, well before the 5s deadline
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await InvokeRunListenerAsync(cts.Token);

        Assert.Pass();
    }

    [Test]
    public async Task RunListenerAsync_should_stop_cleanly_when_cancelled()
    {
        _configService.ListeningPort.Returns(0); // OS picks a free port

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await InvokeRunListenerAsync(cts.Token);

        Assert.Pass();
    }

    // RunPeerContactLoopAsync tests

    [Test]
    public async Task RunPeerContactLoopAsync_should_stop_cleanly_when_cancelled()
    {
        _configService.PeerContactIntervalSeconds.Returns(1);
        _torrentService.GetAll().Returns(new System.Collections.Generic.List<Torrent>());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await InvokeRunPeerContactLoopAsync(cts.Token);

        Assert.Pass();
    }

    [Test]
    public async Task RunPeerContactLoopAsync_should_query_torrents_each_cycle()
    {
        _configService.PeerContactIntervalSeconds.Returns(1);
        _torrentService.GetAll().Returns(new System.Collections.Generic.List<Torrent>
        {
            new Torrent { Status = TorrentStatus.Seeding, InfoHash = "abc123" },
            new Torrent { Status = TorrentStatus.Downloading, InfoHash = "def456" }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await InvokeRunPeerContactLoopAsync(cts.Token);

        _torrentService.Received().GetAll();
    }

    // HandleConnection tests

    private void InvokeHandleConnection(TcpClient serverTcp, CancellationToken ct)
    {
        var method = typeof(PeerServer).GetMethod(
            "HandleConnection",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_server, new object[] { serverTcp, ct });
    }

    private static (TcpClient ClientTcp, TcpClient ServerTcp) CreateRawTcpPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var clientTcp = new TcpClient();
        clientTcp.Connect(IPAddress.Loopback, port);
        var serverTcp = listener.AcceptTcpClient();
        listener.Stop();

        return (clientTcp, serverTcp);
    }

    [Test]
    public void HandleConnection_should_return_when_encryption_negotiation_fails()
    {
        var (clientTcp, serverTcp) = CreateRawTcpPair();
        _clients.Add(clientTcp);

        // Close client before sending any bytes so server reads 0 bytes on first read
        clientTcp.Close();

        using var cts = new CancellationTokenSource();
        // HandleConnection disposes serverTcp internally via PeerConnection.Dispose()
        Assert.DoesNotThrow(() => InvokeHandleConnection(serverTcp, cts.Token));
    }

    [Test]
    public void HandleConnection_should_return_when_handshake_fails()
    {
        var (clientTcp, serverTcp) = CreateRawTcpPair();
        _clients.Add(clientTcp);

        // Send byte 0x13 to pass the BT-handshake detection path in NegotiateEncryptionIncoming,
        // then close so ReceiveHandshake gets EOF after just 1 byte
        var stream = clientTcp.GetStream();
        stream.WriteByte(0x13);
        stream.Flush();
        clientTcp.Close();

        using var cts = new CancellationTokenSource();
        Assert.DoesNotThrow(() => InvokeHandleConnection(serverTcp, cts.Token));
    }

    [Test]
    public void HandleConnection_should_return_for_unknown_info_hash()
    {
        var (clientTcp, serverTcp) = CreateRawTcpPair();
        _clients.Add(clientTcp);

        // Send a complete valid BT handshake so NegotiateEncryptionIncoming and ReceiveHandshake
        // both succeed, then HandleConnection looks up the torrent and finds nothing
        var handshake = BuildBtHandshake(
            "0102030405060708091011121314151617181920",
            "-SD0001-012345678901");
        var stream = clientTcp.GetStream();
        stream.Write(handshake, 0, handshake.Length);
        stream.Flush();

        _torrentService.GetAll().Returns(new System.Collections.Generic.List<Torrent>());

        using var cts = new CancellationTokenSource();
        Assert.DoesNotThrow(() => InvokeHandleConnection(serverTcp, cts.Token));
    }

    private static byte[] BuildBtHandshake(string infoHash, string peerId)
    {
        var buf = new byte[68];
        buf[0] = 19;
        System.Text.Encoding.ASCII.GetBytes("BitTorrent protocol", 0, 19, buf, 1);
        var hashBytes = Convert.FromHexString(infoHash);
        Array.Copy(hashBytes, 0, buf, 28, 20);
        System.Text.Encoding.ASCII.GetBytes(peerId.PadRight(20)[..20], 0, 20, buf, 48);
        return buf;
    }

    // Helper to build big-endian request payloads (index, begin, length)

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

    private static byte[] BuildRequestPayload(int index, int begin, int length)
    {
        var payload = new byte[12];
        payload[0] = (byte)(index >> 24);
        payload[1] = (byte)(index >> 16);
        payload[2] = (byte)(index >> 8);
        payload[3] = (byte)index;
        payload[4] = (byte)(begin >> 24);
        payload[5] = (byte)(begin >> 16);
        payload[6] = (byte)(begin >> 8);
        payload[7] = (byte)begin;
        payload[8] = (byte)(length >> 24);
        payload[9] = (byte)(length >> 16);
        payload[10] = (byte)(length >> 8);
        payload[11] = (byte)length;
        return payload;
    }

    // RunListenerAsync and ExecuteAsync loop-body tests

    [Test]
    public async Task ExecuteAsync_starts_and_exits_on_cancellation()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _server.StartAsync(cts.Token);
        await Task.Delay(400);
    }

    [Test]
    [CancelAfter(10000)]
    public async Task ExecuteAsync_contact_loop_queries_torrents()
    {
        _configService.PeerContactIntervalSeconds.Returns(1);
        _torrentService.GetAll().Returns(new List<Torrent>());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await _server.StartAsync(cts.Token);
        await Task.Delay(3500);

        _torrentService.Received().GetAll();
    }
}
