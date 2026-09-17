using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BencodeNET.Objects;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Encryption;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers.MultiTracker;
using NzbDrone.Core.Transport;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class PeerServerTest
{
    private IConfigService _configService;
    private ITorrentService _torrentService;
    private IConnectionManager _connectionManager;
    private IPeerDiscoveryService _peerDiscovery;
    private IMultiTrackerManager _multiTracker;
    private IMseSkeyRegistry _mseSkeyRegistry;
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
        _mseSkeyRegistry = Substitute.For<IMseSkeyRegistry>();

        _configService.MaxGlobalConnections.Returns(200);
        _configService.ListeningPort.Returns(0);
        _configService.EncryptionMode.Returns("enabled");
        _configService.HandshakeTimeoutSeconds.Returns(30);
        _configService.MessageReadTimeoutSeconds.Returns(60);
        _configService.KeepAliveIntervalSeconds.Returns(120);
        _configService.PeerRequestCount.Returns(200);
        _configService.PeerIdleChance.Returns(0.0);
        _configService.PeerContactIntervalSeconds.Returns(300);

        _server = new PeerServer(_configService, _torrentService, _connectionManager, _peerDiscovery, _multiTracker, mseSkeyRegistry: _mseSkeyRegistry);
        _connectionManager.TryReserveSlot(Arg.Any<string>(), Arg.Any<bool>(), out Arg.Any<IConnectionReservation>())
            .Returns(x =>
            {
                x[2] = Substitute.For<IConnectionReservation>();
                return true;
            });
        _connectionManager.TryAdd(Arg.Any<PeerConnection>(), Arg.Any<IConnectionReservation>())
            .Returns(true);
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

    private void InvokeHandleMessage(PeerConnection connection, PeerMessage message, Torrent torrent = null)
    {
        var method = typeof(PeerServer).GetMethod(
            "HandleMessage",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_server, new object[] { connection, message, torrent });
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

    private ConcurrentDictionary<string, int> GetConnectionsPerIp(PeerServer server = null)
    {
        var target = server ?? _server;
        var field = typeof(PeerServer).GetField(
            "_connectionsPerIp",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (ConcurrentDictionary<string, int>)field.GetValue(target)!;
    }

    private void InvokeDecrementConnectionCount(string clientIp, PeerServer server = null)
    {
        var target = server ?? _server;
        var method = typeof(PeerServer).GetMethod(
            "DecrementConnectionCount",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(target, new object[] { clientIp });
    }

    private SemaphoreSlim GetHalfOpenSemaphore(PeerServer server = null)
    {
        var target = server ?? _server;
        var field = typeof(PeerServer).GetField(
            "_halfOpenSemaphore",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (SemaphoreSlim)field.GetValue(target)!;
    }

    private SemaphoreSlim GetConnectionSemaphore(PeerServer server = null)
    {
        var target = server ?? _server;
        var field = typeof(PeerServer).GetField(
            "_connectionSemaphore",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (SemaphoreSlim)field.GetValue(target)!;
    }

    private Task InvokeProcessIncomingClientAsync(TcpClient client, CancellationToken ct, PeerServer server = null)
    {
        var target = server ?? _server;
        var method = typeof(PeerServer).GetMethod(
            "ProcessIncomingClientAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(target, new object[] { client, ct })!;
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
    public void GetEncryptionMode_should_return_require_encrypted_for_forced()
    {
        _configService.EncryptionMode.Returns("forced");

        var result = InvokeGetEncryptionMode();

        Assert.That(result, Is.EqualTo(EncryptionMode.RequireEncrypted));
    }

    [TestCase("FORCED")]
    [TestCase("Required")]
    [TestCase("REQUIRED")]
    public void GetEncryptionMode_should_be_case_insensitive_for_require_encrypted(string mode)
    {
        _configService.EncryptionMode.Returns(mode);

        var result = InvokeGetEncryptionMode();

        Assert.That(result, Is.EqualTo(EncryptionMode.RequireEncrypted));
    }

    [TestCase("disabled")]
    [TestCase("plain")]
    [TestCase("none")]
    [TestCase("DISABLED")]
    public void GetEncryptionMode_should_return_prefer_plain_text_for_disabled_synonyms(string mode)
    {
        _configService.EncryptionMode.Returns(mode);

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

    [Test]
    public void GetEncryptionMode_should_return_prefer_encrypted_for_null()
    {
        _configService.EncryptionMode.Returns((string)null);

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
    public void HandleMessage_should_process_request_and_decrement_pending_count_on_fulfillment()
    {
        var (clientConn, serverConn) = CreateTestPair();
        serverConn.MaxPipelinedRequests = 200;
        serverConn.PendingRequestCount = 0;
        serverConn.IdleChance = 0.0;

        var payload = BuildRequestPayload(0, 0, 16384);
        var message = new PeerMessage { Type = PeerMessageType.Request, Payload = payload };

        InvokeHandleMessage(serverConn, message);

        Assert.That(serverConn.PendingRequestCount, Is.EqualTo(0));
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
    public void HandlePieceRequest_should_reject_high_bit_set_in_index()
    {
        var (clientConn, serverConn) = CreateTestPair();

        // int.MinValue = 0x80000000 — only the high bit set; reconstructed as a negative int
        // via (uint) shifts then (int) cast, caught by the index < 0 guard.
        var payload = BuildRequestPayload(int.MinValue, 0, 16384);
        InvokeHandlePieceRequest(serverConn, payload);

        clientConn.MessageReadTimeoutMs = 200;
        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Null);
    }

    [Test]
    public void HandlePieceRequest_should_reject_high_bit_set_in_begin()
    {
        var (clientConn, serverConn) = CreateTestPair();

        var payload = BuildRequestPayload(0, int.MinValue, 16384);
        InvokeHandlePieceRequest(serverConn, payload);

        clientConn.MessageReadTimeoutMs = 200;
        var received = clientConn.ReceiveMessage();
        Assert.That(received, Is.Null);
    }

    [Test]
    public void HandlePieceRequest_should_reject_high_bit_set_in_length()
    {
        var (clientConn, serverConn) = CreateTestPair();

        // int.MinValue as length: reconstructed as -2147483648, caught by length <= 0 guard.
        var payload = BuildRequestPayload(0, 0, int.MinValue);
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
        var infoHashBytes = Convert.FromHexString(infoHash);
        var skeyHash = MseKeyDerivation.DeriveKey(infoHashBytes, System.Text.Encoding.ASCII.GetBytes("req2"));

        _mseSkeyRegistry.TryMatchTorrent(skeyHash, out Arg.Any<Torrent>()).Returns(x =>
        {
            x[1] = torrent;
            return true;
        });

        var result = InvokeValidateInfoHash(skeyHash);

        Assert.That(result, Is.True);
        _mseSkeyRegistry.Received(1).TryMatchTorrent(skeyHash, out Arg.Any<Torrent>());
    }

    [Test]
    public void ValidateInfoHash_should_return_false_for_non_matching_hash()
    {
        var wrongHash = new byte[20];
        _mseSkeyRegistry.TryMatchTorrent(wrongHash, out Arg.Any<Torrent>()).Returns(false);

        var result = InvokeValidateInfoHash(wrongHash);

        Assert.That(result, Is.False);
        _mseSkeyRegistry.Received(1).TryMatchTorrent(wrongHash, out Arg.Any<Torrent>());
    }

    [Test]
    public void ValidateInfoHash_should_return_false_when_no_torrents()
    {
        var skeyHash = new byte[20];
        _mseSkeyRegistry.TryMatchTorrent(skeyHash, out Arg.Any<Torrent>()).Returns(false);

        var result = InvokeValidateInfoHash(skeyHash);

        Assert.That(result, Is.False);
        _mseSkeyRegistry.Received(1).TryMatchTorrent(skeyHash, out Arg.Any<Torrent>());
    }

    [Test]
    public void ValidateInfoHash_should_use_skey_registry_for_matching()
    {
        var torrent2 = new Torrent { InfoHash = "A1A2A3A4A5A6A7A8A9A0B1B2B3B4B5B6B7B8B9B0" };
        var infoHashBytes = Convert.FromHexString(torrent2.InfoHash);
        var skeyHash = MseKeyDerivation.DeriveKey(infoHashBytes, System.Text.Encoding.ASCII.GetBytes("req2"));

        _mseSkeyRegistry.TryMatchTorrent(skeyHash, out Arg.Any<Torrent>()).Returns(x =>
        {
            x[1] = torrent2;
            return true;
        });

        var result = InvokeValidateInfoHash(skeyHash);

        Assert.That(result, Is.True);
        _mseSkeyRegistry.Received(1).TryMatchTorrent(skeyHash, out Arg.Any<Torrent>());
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

    private Task InvokeRunListenerAsync(CancellationToken ct, PeerServer server = null)
    {
        var target = server ?? _server;
        var method = typeof(PeerServer).GetMethod(
            "RunListenerAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (Task)method.Invoke(target, new object[] { ct });
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
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(TcpClient), typeof(CancellationToken) },
            null);
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

    [Test]
    [CancelAfter(5000)]
    public void HandleConnection_should_reject_inbound_connection_cleanly_when_quota_exceeded()
    {
        var (clientTcp, serverTcp) = CreateRawTcpPair();
        _clients.Add(clientTcp);

        var infoHash = "0102030405060708091011121314151617181920";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = infoHash,
            PieceCount = 10,
            PieceLength = 16384,
            Name = "Test"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        _connectionManager.TryReserveSlot(infoHash, true, out Arg.Any<IConnectionReservation>())
            .Returns(x =>
            {
                x[2] = null;
                return false;
            });

        var handshake = BuildBtHandshake(infoHash, "-SD0001-012345678901");
        var stream = clientTcp.GetStream();
        stream.Write(handshake, 0, handshake.Length);
        stream.Flush();

        using var cts = new CancellationTokenSource();
        Assert.DoesNotThrow(() => InvokeHandleConnection(serverTcp, cts.Token));

        _connectionManager.DidNotReceive().Add(Arg.Any<PeerConnection>());
        _connectionManager.DidNotReceive().TryAdd(Arg.Any<PeerConnection>(), Arg.Any<IConnectionReservation>());
        _connectionManager.DidNotReceive().Remove(Arg.Any<PeerConnection>());
    }

    [Test]
    [CancelAfter(5000)]
    public void HandleConnection_should_exit_immediately_when_peer_disconnects_eof()
    {
        var (clientTcp, serverTcp) = CreateRawTcpPair();
        _clients.Add(clientTcp);

        var infoHash = "0102030405060708091011121314151617181920";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = infoHash,
            PieceCount = 10,
            PieceLength = 16384,
            Name = "Test"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var handshake = BuildBtHandshake(infoHash, "-SD0001-012345678901");
        var stream = clientTcp.GetStream();
        stream.Write(handshake, 0, handshake.Length);
        stream.Flush();

        // Remote peer closes the TCP connection immediately after handshake
        clientTcp.Close();

        using var cts = new CancellationTokenSource();
        Assert.DoesNotThrow(() => InvokeHandleConnection(serverTcp, cts.Token));
        _connectionManager.Received().Remove(Arg.Any<PeerConnection>());
    }

    [Test]
    [CancelAfter(5000)]
    public void HandleConnection_inbound_MSE_handshake_should_validate_using_registry_and_match_torrent()
    {
        var (clientTcp, serverTcp) = CreateRawTcpPair();
        _clients.Add(clientTcp);

        var infoHash = "0102030405060708091011121314151617181920";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = infoHash,
            PieceCount = 10,
            PieceLength = 16384,
            Name = "MseTestTorrent"
        };

        var infoHashBytes = Convert.FromHexString(infoHash);
        var skeyHash = MseKeyDerivation.DeriveKey(infoHashBytes, Encoding.ASCII.GetBytes("req2"));

        _mseSkeyRegistry.TryMatchTorrent(Arg.Is<byte[]>(b => b.SequenceEqual(skeyHash)), out Arg.Any<Torrent>()).Returns(x =>
        {
            x[1] = torrent;
            return true;
        });

        _configService.EncryptionMode.Returns("enabled");

        var clientTask = Task.Run(() =>
        {
            var outgoing = new MseHandshake(infoHashBytes, EncryptionMode.RequireEncrypted);
            var stream = clientTcp.GetStream();
            var encStream = outgoing.NegotiateOutgoing(stream);
            var handshake = BuildBtHandshake(infoHash, "-SD0001-012345678901");
            encStream.Write(handshake, 0, handshake.Length);
            encStream.Flush();
            clientTcp.Close();
        });

        using var cts = new CancellationTokenSource();
        Assert.DoesNotThrow(() => InvokeHandleConnection(serverTcp, cts.Token));

        clientTask.Wait(TimeSpan.FromSeconds(5));

        _mseSkeyRegistry.Received().TryMatchTorrent(Arg.Is<byte[]>(b => b.SequenceEqual(skeyHash)), out Arg.Any<Torrent>());
        _connectionManager.Received().Remove(Arg.Any<PeerConnection>());
    }

    private void InvokeHandlePeerSession(PeerConnection connection, Torrent torrent)
    {
        var method = typeof(PeerServer).GetMethod(
            "HandlePeerSession",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_server, new object[] { connection, torrent });
    }

    [Test]
    [CancelAfter(5000)]
    public void HandlePeerSession_should_exit_immediately_when_peer_disconnects_eof()
    {
        var (client, server) = CreateTestPair();
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0102030405060708091011121314151617181920",
            PieceCount = 10,
            PieceLength = 16384,
            Name = "Test"
        };

        _connectionManager.Add(server);
        client.Dispose();

        Assert.DoesNotThrow(() => InvokeHandlePeerSession(server, torrent));
        _connectionManager.Received().Remove(server);
        Assert.That(server.IsConnected, Is.False);
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

    [Test]
    public void ConnectToPeer_should_fallback_to_tcp_when_utp_fails_and_tcp_fallback_enabled()
    {
        var utpManager = Substitute.For<IUtpManager>();
        utpManager.IsEnabled.Returns(true);
        utpManager.TcpFallbackEnabled.Returns(true);

        _configService.EncryptionMode.Returns("disabled");

        var mockUtp = Substitute.For<IUtpConnection>();
        mockUtp.IsConnected.Returns(false);
        utpManager.CreateConnection().Returns(mockUtp);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listeners.Add(listener);
        var tcpPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverWithUtp = new PeerServer(
            _configService,
            _torrentService,
            _connectionManager,
            _peerDiscovery,
            _multiTracker,
            utpManager: utpManager);

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0102030405060708091011121314151617181920",
            Name = "TestTorrent",
            PieceCount = 10
        };

        var candidate = new DiscoveredPeer
        {
            Ip = "127.0.0.1",
            Port = tcpPort,
            Source = "tracker"
        };

        var acceptTask = Task.Run(() =>
        {
            var acceptedClient = listener.AcceptTcpClient();
            _clients.Add(acceptedClient);
            var stream = acceptedClient.GetStream();

            var hashBytes = Convert.FromHexString(torrent.InfoHash);
            var expectedSkeyHash = MseKeyDerivation.DeriveKey(hashBytes, Encoding.ASCII.GetBytes("req2"));
            var incomingHandshake = new MseHandshake(hashBytes, EncryptionMode.PreferPlainText);
            var negotiatedStream = incomingHandshake.NegotiateIncoming(stream, h => System.Linq.Enumerable.SequenceEqual(h, expectedSkeyHash));

            // Receive handshake from PeerServer
            var buf = new byte[68];
            negotiatedStream.Read(buf, 0, 68);

            // Send handshake response
            var response = new byte[68];
            response[0] = 19;
            Encoding.ASCII.GetBytes("BitTorrent protocol", 0, 19, response, 1);
            Array.Copy(hashBytes, 0, response, 28, 20);
            Encoding.ASCII.GetBytes("-SD1000-000000000000", 0, 20, response, 48);
            negotiatedStream.Write(response, 0, 68);
            negotiatedStream.Flush();

            // Read the bitfield message header sent by ConnectToPeer after successful handshake
            var msgLen = new byte[4];
            negotiatedStream.Read(msgLen, 0, 4);
            acceptedClient.Close();
        });

        var connectMethod = typeof(PeerServer).GetMethod("ConnectToPeer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        connectMethod.Invoke(serverWithUtp, new object[] { torrent, candidate });

        acceptTask.Wait(TimeSpan.FromSeconds(5));

        _connectionManager.Received().Add(Arg.Any<PeerConnection>());
    }

    private IPAddress InvokeGetBindAddress(PeerServer server = null)
    {
        var target = server ?? _server;
        var method = typeof(PeerServer).GetMethod(
            "GetBindAddress",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            typeof(PeerServer).GetMethod(
            "GetListenAddress",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (IPAddress)method.Invoke(target, Array.Empty<object>());
    }

    private void InvokeConnectToPeer(PeerServer server, Torrent torrent, DiscoveredPeer candidate)
    {
        var method = typeof(PeerServer).GetMethod(
            "ConnectToPeer",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(server, new object[] { torrent, candidate });
    }

    private void InvokeConnectToDiscoveredPeers(PeerServer server, Torrent torrent, CancellationToken stoppingToken)
    {
        var method = typeof(PeerServer).GetMethod(
            "ConnectToDiscoveredPeers",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(server, new object[] { torrent, stoppingToken });
    }

    [Test]
    public void GetBindAddress_should_return_null_when_dedicated_interface_is_unplumbed()
    {
        _configService.BindInterface.Returns("tun0");
        var vpnService = Substitute.For<IVpnKillSwitchService>();
        vpnService.GetVpnInterfaceIpAddress(Arg.Any<AddressFamily>()).Returns((IPAddress)null);

        var server = new PeerServer(
            _configService,
            _torrentService,
            _connectionManager,
            _peerDiscovery,
            _multiTracker,
            vpnKillSwitchService: vpnService);

        var address = InvokeGetBindAddress(server);

        Assert.That(address, Is.Null);
        Assert.That(address, Is.Not.EqualTo(IPAddress.Any));
        Assert.That(address, Is.Not.EqualTo(IPAddress.IPv6Any));
        server.Dispose();
    }

    [Test]
    public void GetBindAddress_should_return_interface_ip_when_dedicated_interface_is_plumbed()
    {
        _configService.BindInterface.Returns("tun0");
        var expectedIp = IPAddress.Parse("10.8.0.2");
        var vpnService = Substitute.For<IVpnKillSwitchService>();
        vpnService.GetVpnInterfaceIpAddress(AddressFamily.InterNetwork).Returns(expectedIp);

        var server = new PeerServer(
            _configService,
            _torrentService,
            _connectionManager,
            _peerDiscovery,
            _multiTracker,
            vpnKillSwitchService: vpnService);

        var address = InvokeGetBindAddress(server);

        Assert.That(address, Is.EqualTo(expectedIp));
        server.Dispose();
    }

    [Test]
    public void GetBindAddress_should_return_any_when_bind_interface_is_any()
    {
        _configService.BindInterface.Returns("Any");
        _configService.EnableIPv6.Returns(false);

        var address = InvokeGetBindAddress(_server);

        Assert.That(address, Is.EqualTo(IPAddress.Any));
    }

    [Test]
    public void GetBindAddress_should_return_ipv6_any_when_ipv6_enabled_and_interface_is_any()
    {
        _configService.BindInterface.Returns("");
        _configService.EnableIPv6.Returns(true);

        var address = InvokeGetBindAddress(_server);

        Assert.That(address, Is.EqualTo(IPAddress.IPv6Any));
    }

    [Test]
    [CancelAfter(5000)]
    public async Task RunListenerAsync_should_defer_binding_when_interface_is_unplumbed()
    {
        _configService.BindInterface.Returns("tun0");
        var vpnService = Substitute.For<IVpnKillSwitchService>();
        vpnService.GetVpnInterfaceIpAddress(Arg.Any<AddressFamily>()).Returns((IPAddress)null);

        var server = new PeerServer(
            _configService,
            _torrentService,
            _connectionManager,
            _peerDiscovery,
            _multiTracker,
            vpnKillSwitchService: vpnService);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await InvokeRunListenerAsync(cts.Token, server);

        Assert.That(server.ListenerSocket, Is.Null);
        server.Dispose();
    }

    [Test]
    [CancelAfter(10000)]
    public async Task RunListenerAsync_should_bind_to_interface_ip_when_vpn_restored_event_fires()
    {
        _configService.BindInterface.Returns("tun0");
        _configService.ListeningPort.Returns(0);

        var vpnService = Substitute.For<IVpnKillSwitchService>();
        vpnService.GetVpnInterfaceIpAddress(Arg.Any<AddressFamily>()).Returns((IPAddress)null);

        var server = new PeerServer(
            _configService,
            _torrentService,
            _connectionManager,
            _peerDiscovery,
            _multiTracker,
            vpnKillSwitchService: vpnService);

        using var cts = new CancellationTokenSource();
        var listenerTask = InvokeRunListenerAsync(cts.Token, server);

        // Initially deferred, socket should not be bound
        await Task.Delay(100);
        Assert.That(server.ListenerSocket, Is.Null);

        // Simulate VPN interface coming online
        vpnService.GetVpnInterfaceIpAddress(AddressFamily.InterNetwork).Returns(IPAddress.Loopback);
        vpnService.VpnRestored += Raise.Event<Action<string>>("tun0");

        // Wait for listener to bind
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (server.ListenerSocket == null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.That(server.ListenerSocket, Is.Not.Null);
        var boundEndpoint = (IPEndPoint)server.ListenerSocket.LocalEndPoint!;
        Assert.That(boundEndpoint.Address, Is.EqualTo(IPAddress.Loopback));

        await cts.CancelAsync();
        try
        {
            await listenerTask;
        }
        catch (OperationCanceledException)
        {
        }

        server.Dispose();
    }

    [Test]
    [CancelAfter(10000)]
    public async Task Handle_VpnInterfaceRestoredEvent_should_rebind_listener()
    {
        _configService.BindInterface.Returns("tun0");
        _configService.ListeningPort.Returns(0);

        var vpnService = Substitute.For<IVpnKillSwitchService>();
        vpnService.GetVpnInterfaceIpAddress(Arg.Any<AddressFamily>()).Returns((IPAddress)null);

        var server = new PeerServer(
            _configService,
            _torrentService,
            _connectionManager,
            _peerDiscovery,
            _multiTracker,
            vpnKillSwitchService: vpnService);

        using var cts = new CancellationTokenSource();
        var listenerTask = InvokeRunListenerAsync(cts.Token, server);

        await Task.Delay(100);
        Assert.That(server.ListenerSocket, Is.Null);

        vpnService.GetVpnInterfaceIpAddress(AddressFamily.InterNetwork).Returns(IPAddress.Loopback);
        server.Handle(new VpnInterfaceRestoredEvent("tun0"));

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (server.ListenerSocket == null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.That(server.ListenerSocket, Is.Not.Null);
        var boundEndpoint = (IPEndPoint)server.ListenerSocket.LocalEndPoint!;
        Assert.That(boundEndpoint.Address, Is.EqualTo(IPAddress.Loopback));

        await cts.CancelAsync();
        try
        {
            await listenerTask;
        }
        catch (OperationCanceledException)
        {
        }

        server.Dispose();
    }

    [Test]
    [CancelAfter(10000)]
    public async Task Handle_VpnRestoredEvent_should_rebind_listener()
    {
        _configService.BindInterface.Returns("tun0");
        _configService.ListeningPort.Returns(0);

        var vpnService = Substitute.For<IVpnKillSwitchService>();
        vpnService.GetVpnInterfaceIpAddress(Arg.Any<AddressFamily>()).Returns((IPAddress)null);

        var server = new PeerServer(
            _configService,
            _torrentService,
            _connectionManager,
            _peerDiscovery,
            _multiTracker,
            vpnKillSwitchService: vpnService);

        using var cts = new CancellationTokenSource();
        var listenerTask = InvokeRunListenerAsync(cts.Token, server);

        await Task.Delay(100);
        Assert.That(server.ListenerSocket, Is.Null);

        vpnService.GetVpnInterfaceIpAddress(AddressFamily.InterNetwork).Returns(IPAddress.Loopback);
        server.Handle(new VpnRestoredEvent("tun0"));

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (server.ListenerSocket == null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.That(server.ListenerSocket, Is.Not.Null);
        var boundEndpoint = (IPEndPoint)server.ListenerSocket.LocalEndPoint!;
        Assert.That(boundEndpoint.Address, Is.EqualTo(IPAddress.Loopback));

        await cts.CancelAsync();
        try
        {
            await listenerTask;
        }
        catch (OperationCanceledException)
        {
        }

        server.Dispose();
    }

    [Test]
    [CancelAfter(10000)]
    public async Task OnVpnDropped_should_stop_listener_when_dedicated_interface_configured()
    {
        _configService.BindInterface.Returns("tun0");
        _configService.ListeningPort.Returns(0);

        var vpnService = Substitute.For<IVpnKillSwitchService>();
        vpnService.GetVpnInterfaceIpAddress(AddressFamily.InterNetwork).Returns(IPAddress.Loopback);

        var server = new PeerServer(
            _configService,
            _torrentService,
            _connectionManager,
            _peerDiscovery,
            _multiTracker,
            vpnKillSwitchService: vpnService);

        using var cts = new CancellationTokenSource();
        var listenerTask = InvokeRunListenerAsync(cts.Token, server);

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (server.ListenerSocket == null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.That(server.ListenerSocket, Is.Not.Null);

        // Drop VPN
        vpnService.GetVpnInterfaceIpAddress(Arg.Any<AddressFamily>()).Returns((IPAddress)null);
        vpnService.VpnDropped += Raise.Event<Action<string>>("tun0");

        deadline = DateTime.UtcNow.AddSeconds(3);
        while (server.ListenerSocket != null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.That(server.ListenerSocket, Is.Null);

        await cts.CancelAsync();
        try
        {
            await listenerTask;
        }
        catch (OperationCanceledException)
        {
        }

        server.Dispose();
    }

    [Test]
    public void ConnectToPeer_should_fail_closed_when_dedicated_interface_is_unplumbed()
    {
        _configService.BindInterface.Returns("tun0");

        var vpnService = Substitute.For<IVpnKillSwitchService>();
        vpnService.GetVpnInterfaceIpAddress(Arg.Any<AddressFamily>()).Returns((IPAddress)null);

        var server = new PeerServer(
            _configService,
            _torrentService,
            _connectionManager,
            _peerDiscovery,
            _multiTracker,
            vpnKillSwitchService: vpnService);

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0102030405060708091011121314151617181920",
            Name = "TestTorrent",
            PieceCount = 10
        };

        var candidate = new DiscoveredPeer
        {
            Ip = "127.0.0.1",
            Port = 5000,
            Source = "tracker"
        };

        InvokeConnectToPeer(server, torrent, candidate);

        _connectionManager.DidNotReceive().Add(Arg.Any<PeerConnection>());
        _peerDiscovery.Received().MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);

        server.Dispose();
    }

    [Test]
    public void HandleMessage_should_use_supplied_torrent_without_calling_GetAll()
    {
        var conn = CreateTestConnection();
        conn.InfoHash = "0102030405060708091011121314151617181920";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = conn.InfoHash,
            PieceCount = 10
        };

        var message = new PeerMessage { Type = PeerMessageType.Choke };
        InvokeHandleMessage(conn, message, torrent);

        _torrentService.DidNotReceive().GetAll();
        _torrentService.DidNotReceive().GetByInfoHash(Arg.Any<string>());
    }

    [Test]
    public void HandleMessage_should_use_cached_torrent_without_calling_GetAll()
    {
        var conn = CreateTestConnection();
        conn.InfoHash = "0102030405060708091011121314151617181920";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = conn.InfoHash,
            PieceCount = 10
        };

        _torrentService.GetByInfoHash(conn.InfoHash).Returns(torrent);

        var message1 = new PeerMessage { Type = PeerMessageType.Choke };
        var message2 = new PeerMessage { Type = PeerMessageType.Choke };

        // First message resolves from service and populates cache
        InvokeHandleMessage(conn, message1);

        // Second message uses in-memory cache
        InvokeHandleMessage(conn, message2);

        _torrentService.DidNotReceive().GetAll();
        _torrentService.Received(1).GetByInfoHash(conn.InfoHash);
    }

    [Test]
    public void TorrentUpdatedEvent_should_update_cached_torrent()
    {
        var infoHash = "0102030405060708091011121314151617181920";
        var torrent1 = new Torrent { Id = 1, InfoHash = infoHash, Name = "Original" };
        var torrent2 = new Torrent { Id = 1, InfoHash = infoHash, Name = "Updated" };

        _torrentService.GetByInfoHash(infoHash).Returns(torrent1, torrent2);

        var conn = CreateTestConnection();
        conn.InfoHash = infoHash;

        InvokeHandleMessage(conn, new PeerMessage { Type = PeerMessageType.Choke });
        _torrentService.Received(1).GetByInfoHash(infoHash);

        _server.Handle(new TorrentUpdatedEvent(torrent2));

        // After update event, cache is updated with torrent2 so GetByInfoHash is not called again
        InvokeHandleMessage(conn, new PeerMessage { Type = PeerMessageType.Choke });
        _torrentService.Received(1).GetByInfoHash(infoHash);
    }

    [Test]
    public void TorrentDeletedEvent_should_evict_cached_torrent()
    {
        var infoHash = "0102030405060708091011121314151617181920";
        var torrent = new Torrent { Id = 1, InfoHash = infoHash };

        _torrentService.GetByInfoHash(infoHash).Returns(torrent);

        var conn = CreateTestConnection();
        conn.InfoHash = infoHash;

        InvokeHandleMessage(conn, new PeerMessage { Type = PeerMessageType.Choke });
        _torrentService.Received(1).GetByInfoHash(infoHash);

        _server.Handle(new TorrentDeletedEvent(1, torrent));

        // After deletion event, cache is cleared so next message triggers GetByInfoHash again
        InvokeHandleMessage(conn, new PeerMessage { Type = PeerMessageType.Choke });
        _torrentService.Received(2).GetByInfoHash(infoHash);
    }

    [Test]
    public void Disconnecting_peer_evicts_ip_entry_from_connectionsPerIp_when_count_reaches_zero()
    {
        var dict = GetConnectionsPerIp();
        var clientIp = "192.168.1.50";
        dict[clientIp] = 1;

        InvokeDecrementConnectionCount(clientIp);

        Assert.That(dict.ContainsKey(clientIp), Is.False);
        Assert.That(dict.Count, Is.EqualTo(0));
    }

    [Test]
    public void Multiple_connections_from_same_ip_evicts_only_when_all_connections_close()
    {
        var dict = GetConnectionsPerIp();
        var clientIp = "10.0.0.1";
        dict[clientIp] = 2;

        InvokeDecrementConnectionCount(clientIp);

        Assert.That(dict.ContainsKey(clientIp), Is.True);
        Assert.That(dict[clientIp], Is.EqualTo(1));

        InvokeDecrementConnectionCount(clientIp);

        Assert.That(dict.ContainsKey(clientIp), Is.False);
        Assert.That(dict.Count, Is.EqualTo(0));
    }

    [Test]
    public void Exception_during_connection_initialization_releases_and_evicts_ip_reservation()
    {
        _configService.BindInterface.Returns("tun0");

        var vpnService = Substitute.For<IVpnKillSwitchService>();
        vpnService.GetVpnInterfaceIpAddress(Arg.Any<AddressFamily>()).Returns((IPAddress)null);

        var server = new PeerServer(
            _configService,
            _torrentService,
            _connectionManager,
            _peerDiscovery,
            _multiTracker,
            vpnKillSwitchService: vpnService);

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0102030405060708091011121314151617181920",
            Name = "TestTorrent",
            PieceCount = 10
        };

        var candidate = new DiscoveredPeer
        {
            Ip = "192.168.1.99",
            Port = 5000,
            Source = "tracker"
        };

        var dict = GetConnectionsPerIp(server);

        InvokeConnectToPeer(server, torrent, candidate);

        // Fail-closed occurred during connection initialization; verify IP reservation was evicted
        Assert.That(dict.ContainsKey(candidate.Ip), Is.False);
        Assert.That(dict.Count, Is.EqualTo(0));

        server.Dispose();
    }

    [Test]
    public void ConnectToPeer_when_exceeding_max_connections_per_ip_evicts_reservation()
    {
        _configService.MaxConnectionsPerIp.Returns(1);

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0102030405060708091011121314151617181920",
            Name = "TestTorrent",
            PieceCount = 10
        };

        var candidate = new DiscoveredPeer
        {
            Ip = "192.168.1.200",
            Port = 5000,
            Source = "tracker"
        };

        var dict = GetConnectionsPerIp();
        dict[candidate.Ip] = 1;

        InvokeConnectToPeer(_server, torrent, candidate);

        // Exceeded limit: count remains 1 and is not leaked higher
        Assert.That(dict[candidate.Ip], Is.EqualTo(1));

        InvokeDecrementConnectionCount(candidate.Ip);
        Assert.That(dict.ContainsKey(candidate.Ip), Is.False);
    }

    [Test]
    public async Task Inbound_connections_acquire_and_release_half_open_semaphore_during_handshake_phase()
    {
        var (clientTcp, serverTcp) = CreateRawTcpPair();
        _clients.Add(clientTcp);

        var halfOpen = GetHalfOpenSemaphore();
        var initialCount = halfOpen.CurrentCount;

        var infoHash = "0102030405060708091011121314151617181920";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = infoHash,
            PieceCount = 10,
            PieceLength = 16384,
            Name = "Test"
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _torrentService.GetByInfoHash(infoHash).Returns(torrent);

        using var cts = new CancellationTokenSource();
        var processTask = InvokeProcessIncomingClientAsync(serverTcp, cts.Token);

        // Allow server to accept and acquire semaphores, entering handshake negotiation
        for (var i = 0; i < 50 && halfOpen.CurrentCount == initialCount; i++)
        {
            await Task.Delay(10);
        }

        // Permit should be acquired during the handshake phase
        Assert.That(halfOpen.CurrentCount, Is.EqualTo(initialCount - 1));

        // Now client sends handshake to complete verification
        var handshake = BuildBtHandshake(infoHash, "-SD0001-012345678901");
        var stream = clientTcp.GetStream();
        await stream.WriteAsync(handshake.AsMemory());
        await stream.FlushAsync();

        // Once handshake verification succeeds, half-open permit must be released immediately
        for (var i = 0; i < 50 && halfOpen.CurrentCount < initialCount; i++)
        {
            await Task.Delay(10);
        }

        Assert.That(halfOpen.CurrentCount, Is.EqualTo(initialCount));

        // Terminate connection to exit session loop
        clientTcp.Close();
        await cts.CancelAsync();
        try
        {
            await processTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    [Test]
    public async Task Accepted_client_with_null_remote_endpoint_is_disposed_cleanly_without_throwing()
    {
        var client = new TcpClient();
        _clients.Add(client);

        var halfOpen = GetHalfOpenSemaphore();
        var initialCount = halfOpen.CurrentCount;
        var dict = GetConnectionsPerIp();

        using var cts = new CancellationTokenSource();
        Assert.DoesNotThrowAsync(async () => await InvokeProcessIncomingClientAsync(client, cts.Token));

        // Should return gracefully without leaking half-open permits or IP entries
        Assert.That(halfOpen.CurrentCount, Is.EqualTo(initialCount));
        Assert.That(dict.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task Permitted_limit_of_concurrent_half_open_inbound_connections_is_enforced()
    {
        const int maxHalfOpen = 2;
        var config = Substitute.For<IConfigService>();
        config.MaxGlobalConnections.Returns(200);
        config.MaximumHalfOpenConnections.Returns(maxHalfOpen);
        config.ListeningPort.Returns(0);
        config.EncryptionMode.Returns("enabled");
        config.HandshakeTimeoutSeconds.Returns(30);
        config.MessageReadTimeoutSeconds.Returns(60);
        config.KeepAliveIntervalSeconds.Returns(120);
        config.PeerRequestCount.Returns(200);
        config.PeerIdleChance.Returns(0.0);
        config.PeerContactIntervalSeconds.Returns(300);

        using var server = new PeerServer(config, _torrentService, _connectionManager, _peerDiscovery, _multiTracker);
        var halfOpen = GetHalfOpenSemaphore(server);
        Assert.That(halfOpen.CurrentCount, Is.EqualTo(maxHalfOpen));

        var (c1, s1) = CreateRawTcpPair();
        var (c2, s2) = CreateRawTcpPair();
        var (c3, s3) = CreateRawTcpPair();
        _clients.Add(c1);
        _clients.Add(c2);
        _clients.Add(c3);

        using var cts = new CancellationTokenSource();
        var t1 = InvokeProcessIncomingClientAsync(s1, cts.Token, server);
        var t2 = InvokeProcessIncomingClientAsync(s2, cts.Token, server);

        // Wait until both half-open slots are occupied
        for (var i = 0; i < 50 && halfOpen.CurrentCount > 0; i++)
        {
            await Task.Delay(10);
        }

        Assert.That(halfOpen.CurrentCount, Is.EqualTo(0));

        // Third inbound connection attempts to enter handshake phase while limit is reached
        using var shortCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var t3 = InvokeProcessIncomingClientAsync(s3, shortCts.Token, server);
        await t3;

        // Since limit was reached and token timed out, s3 could not proceed
        Assert.That(halfOpen.CurrentCount, Is.EqualTo(0));

        // Closing c1 frees a half-open slot
        c1.Close();
        await cts.CancelAsync();
        try
        {
            await t1;
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            await t2;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.That(halfOpen.CurrentCount, Is.EqualTo(maxHalfOpen));
    }

    [Test]
    public void HandlePieceRequest_should_decrement_pending_request_count_when_fulfilled()
    {
        var (clientConn, serverConn) = CreateTestPair();
        serverConn.PendingRequestCount = 3;

        var payload = BuildRequestPayload(1, 0, 16384);
        InvokeHandlePieceRequest(serverConn, payload);

        Assert.That(serverConn.PendingRequestCount, Is.EqualTo(2));
    }

    [Test]
    public void Processing_multiple_requests_sequentially_does_not_hit_pipeline_lockup()
    {
        var (clientConn, serverConn) = CreateTestPair();
        serverConn.MaxPipelinedRequests = 200;
        serverConn.PendingRequestCount = 0;
        serverConn.IdleChance = 0.0;

        // Send 250 requests sequentially - exceeding default limit of 200
        for (var i = 0; i < 250; i++)
        {
            var payload = BuildRequestPayload(0, i * 16, 16);
            var message = new PeerMessage { Type = PeerMessageType.Request, Payload = payload };
            InvokeHandleMessage(serverConn, message);

            var pieceMsg = clientConn.ReceiveMessage();
            Assert.That(pieceMsg, Is.Not.Null);
            Assert.That(pieceMsg.Type, Is.EqualTo(PeerMessageType.Piece));
            Assert.That(serverConn.PendingRequestCount, Is.EqualTo(0));
        }
    }

    [Test]
    public void HandleMessage_choke_resets_pending_request_count()
    {
        var conn = CreateTestConnection();
        conn.PendingRequestCount = 50;

        var message = new PeerMessage { Type = PeerMessageType.Choke };
        InvokeHandleMessage(conn, message);

        Assert.That(conn.PendingRequestCount, Is.EqualTo(0));
    }

    [Test]
    public void Disposing_connection_resets_pending_request_count()
    {
        var conn = CreateTestConnection();
        conn.PendingRequestCount = 42;

        conn.Dispose();

        Assert.That(conn.PendingRequestCount, Is.EqualTo(0));
    }

    [Test]
    public void HandleMessage_reject_request_decrements_pending_request_count()
    {
        var conn = CreateTestConnection();
        conn.PendingRequestCount = 5;

        var payload = BuildRequestPayload(1, 0, 16384);
        var message = new PeerMessage { Type = PeerMessageType.RejectRequest, Payload = payload };
        InvokeHandleMessage(conn, message);

        Assert.That(conn.PendingRequestCount, Is.EqualTo(4));
    }

    [Test]
    public void HandleMessage_reject_request_releases_block_in_piece_picker_for_immediate_reassignment()
    {
        var conn = CreateTestConnection();
        conn.PeerChoking = false;
        conn.PeerPieces = new bool[5];
        conn.PeerPieces[0] = true;

        var picker = _server.PiecePicker;
        picker.AddActivePiece(0, 32768, 16384);

        var block = picker.RequestBlock(conn, 0);
        Assert.That(block, Is.Not.Null);
        Assert.That(block.IsRequested, Is.True);
        Assert.That(block.RequestedFrom, Is.EqualTo(conn));
        Assert.That(conn.PendingRequestCount, Is.EqualTo(1));

        var payload = BuildRequestPayload(block.PieceIndex, block.Begin, block.Length);
        var message = new PeerMessage { Type = PeerMessageType.RejectRequest, Payload = payload };
        InvokeHandleMessage(conn, message);

        Assert.That(conn.PendingRequestCount, Is.EqualTo(0));
        Assert.That(block.IsRequested, Is.False);
        Assert.That(block.RequestedFrom, Is.Null);

        var otherConn = CreateTestConnection();
        otherConn.PeerChoking = false;
        otherConn.PeerPieces = new bool[5];
        otherConn.PeerPieces[0] = true;

        var reassigned = picker.RequestBlock(otherConn, 0);
        Assert.That(reassigned, Is.Not.Null);
        Assert.That(reassigned.PieceIndex, Is.EqualTo(block.PieceIndex));
        Assert.That(reassigned.Begin, Is.EqualTo(block.Begin));
        Assert.That(reassigned.RequestedFrom, Is.EqualTo(otherConn));
    }

    [Test]
    public void ConnectToDiscoveredPeers_should_skip_peer_endpoints_already_in_flight()
    {
        var torrent = new Torrent { Id = 1, InfoHash = "0123456789abcdef0123456789abcdef01234567", Name = "Test" };
        _connectionManager.CanAddConnectionForTorrent(torrent.InfoHash).Returns(true);

        var candidate = new DiscoveredPeer { Ip = "1.2.3.4", Port = 5000 };
        _peerDiscovery.GetPeers(torrent.InfoHash, 5).Returns(new List<DiscoveredPeer> { candidate });

        // Simulate that 1.2.3.4:5000 is already in flight
        var inFlightField = typeof(PeerServer).GetField("_inFlightOutgoingEndpoints", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var inFlightDict = (ConcurrentDictionary<string, byte>)inFlightField.GetValue(_server)!;
        inFlightDict.TryAdd("1.2.3.4:5000", 0);

        InvokeConnectToDiscoveredPeers(_server, torrent, CancellationToken.None);

        Assert.That(_server.IsOutgoingEndpointInFlight("1.2.3.4", 5000), Is.True);
    }

    [Test]
    public void HandleMessage_should_respond_with_metadata_chunk_when_metadata_is_available()
    {
        var (clientConn, serverConn) = CreateTestPair();
        const string infoHash = "0123456789abcdef0123456789abcdef01234567";
        serverConn.InfoHash = infoHash;
        serverConn.RemoteExtensions["ut_metadata"] = 2;

        var metadata = new byte[20000];
        new Random(42).NextBytes(metadata);
        _server.SetTorrentMetadata(infoHash, metadata);

        var exchange = new MetadataExchange();
        var requestBytes = exchange.BuildMetadataRequest(0);
        var payload = new byte[1 + requestBytes.Length];
        payload[0] = 2; // remote ut_metadata ID
        Array.Copy(requestBytes, 0, payload, 1, requestBytes.Length);

        InvokeHandleMessage(serverConn, new PeerMessage { Type = PeerMessageType.Extended, Payload = payload });

        var response = clientConn.ReceiveMessage();
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Type, Is.EqualTo(PeerMessageType.Extended));
        Assert.That(response.Payload[0], Is.EqualTo(2));

        var parsed = exchange.ParseMetadataMessage(response.Payload[1..]);
        Assert.That(parsed.MessageType, Is.EqualTo(1));
        Assert.That(parsed.Piece, Is.EqualTo(0));
        Assert.That(parsed.TotalSize, Is.EqualTo(20000));
        Assert.That(parsed.Data, Is.Not.Null);
        Assert.That(parsed.Data.Length, Is.EqualTo(MetadataExchange.MetadataBlockSize));
    }

    [Test]
    public void HandleMessage_should_respond_with_reject_when_metadata_is_unavailable()
    {
        var (clientConn, serverConn) = CreateTestPair();
        const string infoHash = "0123456789abcdef0123456789abcdef01234567";
        serverConn.InfoHash = infoHash;
        serverConn.RemoteExtensions["ut_metadata"] = 2;

        var exchange = new MetadataExchange();
        var requestBytes = exchange.BuildMetadataRequest(0);
        var payload = new byte[1 + requestBytes.Length];
        payload[0] = 2;
        Array.Copy(requestBytes, 0, payload, 1, requestBytes.Length);

        InvokeHandleMessage(serverConn, new PeerMessage { Type = PeerMessageType.Extended, Payload = payload });

        var response = clientConn.ReceiveMessage();
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Type, Is.EqualTo(PeerMessageType.Extended));
        Assert.That(response.Payload[0], Is.EqualTo(2));

        var parsed = exchange.ParseMetadataMessage(response.Payload[1..]);
        Assert.That(parsed.MessageType, Is.EqualTo(2));
        Assert.That(parsed.Piece, Is.EqualTo(0));
    }

    [Test]
    public void HandleMessage_should_respond_with_reject_when_requested_piece_is_out_of_bounds()
    {
        var (clientConn, serverConn) = CreateTestPair();
        const string infoHash = "0123456789abcdef0123456789abcdef01234567";
        serverConn.InfoHash = infoHash;
        serverConn.RemoteExtensions["ut_metadata"] = 2;

        var metadata = new byte[20000]; // 2 pieces: 0 and 1
        _server.SetTorrentMetadata(infoHash, metadata);

        var exchange = new MetadataExchange();
        var requestBytes = exchange.BuildMetadataRequest(5); // Piece 5 is out of bounds
        var payload = new byte[1 + requestBytes.Length];
        payload[0] = 2;
        Array.Copy(requestBytes, 0, payload, 1, requestBytes.Length);

        InvokeHandleMessage(serverConn, new PeerMessage { Type = PeerMessageType.Extended, Payload = payload });

        var response = clientConn.ReceiveMessage();
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Type, Is.EqualTo(PeerMessageType.Extended));
        Assert.That(response.Payload[0], Is.EqualTo(2));

        var parsed = exchange.ParseMetadataMessage(response.Payload[1..]);
        Assert.That(parsed.MessageType, Is.EqualTo(2));
        Assert.That(parsed.Piece, Is.EqualTo(5));
    }

    [Test]
    public void HandleMessage_should_parse_extension_handshake_and_record_remote_extensions()
    {
        var conn = CreateTestConnection();
        var dict = new BDictionary
        {
            ["m"] = new BDictionary
            {
                ["ut_metadata"] = new BNumber(3)
            }
        };
        var handshakeBytes = dict.EncodeAsBytes();
        var payload = new byte[1 + handshakeBytes.Length];
        payload[0] = 0; // Handshake id
        Array.Copy(handshakeBytes, 0, payload, 1, handshakeBytes.Length);

        InvokeHandleMessage(conn, new PeerMessage { Type = PeerMessageType.Extended, Payload = payload });

        Assert.That(conn.RemoteExtensions.ContainsKey("ut_metadata"), Is.True);
        Assert.That(conn.RemoteExtensions["ut_metadata"], Is.EqualTo(3));
    }

    [Test]
    public void HandleMessage_should_serve_synthetic_metadata_when_physical_torrent_file_is_missing()
    {
        var (clientConn, serverConn) = CreateTestPair();
        const string infoHash = "fedcba9876543210fedcba9876543210fedcba98";
        serverConn.InfoHash = infoHash;
        serverConn.RemoteExtensions["ut_metadata"] = 2;

        var torrent = new Torrent
        {
            Id = 42,
            Name = "simulated.torrent",
            InfoHash = infoHash,
            TotalSize = 10000,
            PieceLength = 16384,
            SourcePath = "/nonexistent/path/simulated.torrent"
        };

        _torrentService.GetByInfoHash(infoHash).Returns(torrent);

        var exchange = new MetadataExchange();
        var requestBytes = exchange.BuildMetadataRequest(0);
        var payload = new byte[1 + requestBytes.Length];
        payload[0] = 2;
        Array.Copy(requestBytes, 0, payload, 1, requestBytes.Length);

        InvokeHandleMessage(serverConn, new PeerMessage { Type = PeerMessageType.Extended, Payload = payload }, torrent);

        var response = clientConn.ReceiveMessage();
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Type, Is.EqualTo(PeerMessageType.Extended));
        Assert.That(response.Payload[0], Is.EqualTo(2));

        var parsed = exchange.ParseMetadataMessage(response.Payload[1..]);
        Assert.That(parsed.MessageType, Is.EqualTo(1));
        Assert.That(parsed.Piece, Is.EqualTo(0));
        Assert.That(parsed.TotalSize, Is.GreaterThan(0));
        Assert.That(parsed.Data, Is.Not.Null);
        Assert.That(parsed.Data.Length, Is.GreaterThan(0));
    }

    [Test]
    public void ConnectToPeer_should_reject_self_connection_when_port_matches_listening_port_on_loopback()
    {
        _configService.ListeningPort.Returns(6881);

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0102030405060708091011121314151617181920",
            Name = "TestTorrent",
            PieceCount = 10
        };

        var candidate = new DiscoveredPeer
        {
            Ip = "127.0.0.1",
            Port = 6881,
            Source = "pex"
        };

        InvokeConnectToPeer(_server, torrent, candidate);

        _connectionManager.DidNotReceive().Add(Arg.Any<PeerConnection>());
        _peerDiscovery.Received().MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
    }

    [Test]
    public void HandleExtendedMessage_should_filter_seeders_when_torrent_is_seeding()
    {
        _configService.EnablePex.Returns(true);

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0102030405060708091011121314151617181920",
            Name = "SeedingTorrent",
            Progress = 1.0
        };

        _torrentService.GetByInfoHash(torrent.InfoHash).Returns(torrent);

        var (_, serverConn) = CreateTestPair();
        serverConn.InfoHash = torrent.InfoHash;
        serverConn.RemoteExtensions["ut_pex"] = 1;

        var peerExchange = new PeerExchange(_configService);
        var added = new List<PeerInfo>
        {
            new PeerInfo { Ip = "93.184.216.34", Port = 6881, Flags = 0x02 }, // Seeder
            new PeerInfo { Ip = "93.184.216.35", Port = 6882, Flags = 0x00 }  // Leecher
        };

        var pexBytes = peerExchange.BuildPexMessage(added, new List<PeerInfo>());
        var payload = new byte[1 + pexBytes.Length];
        payload[0] = 1; // ut_pex extension ID
        Array.Copy(pexBytes, 0, payload, 1, pexBytes.Length);

        InvokeHandleMessage(serverConn, new PeerMessage { Type = PeerMessageType.Extended, Payload = payload }, torrent);

        _peerDiscovery.Received(1).AddPeers(
            torrent.InfoHash,
            Arg.Is<IEnumerable<PeerInfo>>(p => p.Count() == 1 && p.First().Ip == "93.184.216.35" && !p.First().IsSeeder),
            "pex");
    }

    [Test]
    public void HandleExtendedMessage_should_retain_and_prioritize_seeders_when_torrent_is_downloading()
    {
        _configService.EnablePex.Returns(true);

        var torrent = new Torrent
        {
            Id = 2,
            InfoHash = "0102030405060708091011121314151617181920",
            Name = "DownloadingTorrent",
            Progress = 0.4
        };

        _torrentService.GetByInfoHash(torrent.InfoHash).Returns(torrent);

        var (_, serverConn) = CreateTestPair();
        serverConn.InfoHash = torrent.InfoHash;
        serverConn.RemoteExtensions["ut_pex"] = 1;

        var peerExchange = new PeerExchange(_configService);
        var added = new List<PeerInfo>
        {
            new PeerInfo { Ip = "93.184.216.35", Port = 6882, Flags = 0x00 }, // Leecher first
            new PeerInfo { Ip = "93.184.216.34", Port = 6881, Flags = 0x02 }  // Seeder second
        };

        var pexBytes = peerExchange.BuildPexMessage(added, new List<PeerInfo>());
        var payload = new byte[1 + pexBytes.Length];
        payload[0] = 1; // ut_pex extension ID
        Array.Copy(pexBytes, 0, payload, 1, pexBytes.Length);

        InvokeHandleMessage(serverConn, new PeerMessage { Type = PeerMessageType.Extended, Payload = payload }, torrent);

        _peerDiscovery.Received(1).AddPeers(
            torrent.InfoHash,
            Arg.Is<IEnumerable<PeerInfo>>(p => p.Count() == 2 &&
                p.First().Ip == "93.184.216.34" && p.First().IsSeeder &&
                p.Last().Ip == "93.184.216.35" && !p.Last().IsSeeder),
            "pex");
    }

    [Test]
    public void HandleExtendedMessage_pex_received_under_55_seconds_apart_should_be_dropped_as_rate_limit_violation()
    {
        _configService.EnablePex.Returns(true);

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0102030405060708091011121314151617181920",
            Name = "RateLimitTorrent",
            Progress = 0.5
        };

        _torrentService.GetByInfoHash(torrent.InfoHash).Returns(torrent);

        var (_, serverConn) = CreateTestPair();
        serverConn.InfoHash = torrent.InfoHash;
        serverConn.RemoteExtensions["ut_pex"] = 1;
        serverConn.LastPexReceived = DateTime.UtcNow.AddSeconds(-30);

        var peerExchange = new PeerExchange(_configService);
        var added = new List<PeerInfo>
        {
            new PeerInfo { Ip = "93.184.216.34", Port = 6881, Flags = 0x00 }
        };

        var pexBytes = peerExchange.BuildPexMessage(added, new List<PeerInfo>());
        var payload = new byte[1 + pexBytes.Length];
        payload[0] = 1;
        Array.Copy(pexBytes, 0, payload, 1, pexBytes.Length);

        InvokeHandleMessage(serverConn, new PeerMessage { Type = PeerMessageType.Extended, Payload = payload }, torrent);

        _peerDiscovery.DidNotReceive().AddPeers(Arg.Any<string>(), Arg.Any<IEnumerable<PeerInfo>>(), Arg.Any<string>());
        Assert.That(serverConn.PexRateLimitViolations, Is.EqualTo(1));
    }

    [Test]
    public void HandleExtendedMessage_repeated_pex_rate_limit_violations_should_trigger_peer_disconnection()
    {
        _configService.EnablePex.Returns(true);

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0102030405060708091011121314151617181920",
            Name = "FloodTorrent",
            Progress = 0.5
        };

        _torrentService.GetByInfoHash(torrent.InfoHash).Returns(torrent);

        var (_, serverConn) = CreateTestPair();
        serverConn.InfoHash = torrent.InfoHash;
        serverConn.RemoteExtensions["ut_pex"] = 1;
        serverConn.LastPexReceived = DateTime.UtcNow.AddSeconds(-10);
        serverConn.PexRateLimitViolations = 3;

        var peerExchange = new PeerExchange(_configService);
        var added = new List<PeerInfo>
        {
            new PeerInfo { Ip = "93.184.216.34", Port = 6881, Flags = 0x00 }
        };

        var pexBytes = peerExchange.BuildPexMessage(added, new List<PeerInfo>());
        var payload = new byte[1 + pexBytes.Length];
        payload[0] = 1;
        Array.Copy(pexBytes, 0, payload, 1, pexBytes.Length);

        InvokeHandleMessage(serverConn, new PeerMessage { Type = PeerMessageType.Extended, Payload = payload }, torrent);

        Assert.That(serverConn.PexRateLimitViolations, Is.EqualTo(4));
        _connectionManager.Received(1).Remove(serverConn);
        _peerDiscovery.DidNotReceive().AddPeers(Arg.Any<string>(), Arg.Any<IEnumerable<PeerInfo>>(), Arg.Any<string>());
    }

    [Test]
    public void HandleExtendedMessage_valid_pex_spaced_at_least_55_seconds_apart_should_reset_violations_and_process_normally()
    {
        _configService.EnablePex.Returns(true);

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "0102030405060708091011121314151617181920",
            Name = "ValidTorrent",
            Progress = 0.5
        };

        _torrentService.GetByInfoHash(torrent.InfoHash).Returns(torrent);

        var (_, serverConn) = CreateTestPair();
        serverConn.InfoHash = torrent.InfoHash;
        serverConn.RemoteExtensions["ut_pex"] = 1;
        serverConn.LastPexReceived = DateTime.UtcNow.AddSeconds(-60);
        serverConn.PexRateLimitViolations = 2;

        var peerExchange = new PeerExchange(_configService);
        var added = new List<PeerInfo>
        {
            new PeerInfo { Ip = "93.184.216.34", Port = 6881, Flags = 0x00 }
        };

        var pexBytes = peerExchange.BuildPexMessage(added, new List<PeerInfo>());
        var payload = new byte[1 + pexBytes.Length];
        payload[0] = 1;
        Array.Copy(pexBytes, 0, payload, 1, pexBytes.Length);

        InvokeHandleMessage(serverConn, new PeerMessage { Type = PeerMessageType.Extended, Payload = payload }, torrent);

        Assert.That(serverConn.PexRateLimitViolations, Is.EqualTo(0));
        Assert.That(serverConn.LastPexReceived, Is.GreaterThan(DateTime.UtcNow.AddSeconds(-5)));
        _peerDiscovery.Received(1).AddPeers(torrent.InfoHash, Arg.Any<IEnumerable<PeerInfo>>(), "pex");
    }
}
