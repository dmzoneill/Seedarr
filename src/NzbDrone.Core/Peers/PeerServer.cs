using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Peers.Encryption;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers;

public class PeerServer : BackgroundService
{
    private const int OutgoingConnectTimeoutMs = 5000;
    private readonly IConfigService _configService;
    private readonly ITorrentService _torrentService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly IConnectionManager _connectionManager;
    private readonly IPeerDiscoveryService _peerDiscovery;
    private readonly Trackers.MultiTracker.IMultiTrackerManager _multiTracker;
    private readonly ITorrentEventLogService _eventLogService;
    private readonly Trackers.Metrics.ITrackerMetricService _trackerMetricService;
    private readonly Trackers.ITrackerAnnounceService _trackerAnnounceService;
    private readonly IClientBehaviorSimulator _clientBehaviorSimulator;
    private readonly IRandomNumberGenerator _random;
    private readonly Transport.IUtpManager _utpManager;
    private readonly IVpnKillSwitchService _vpnKillSwitchService;
    private readonly SemaphoreSlim _connectionSemaphore;
    private readonly SemaphoreSlim _halfOpenSemaphore;
    private readonly ConcurrentDictionary<string, int> _connectionsPerIp = new();
    private readonly Logger _logger;

    public PeerServer(
        IConfigService configService,
        ITorrentService torrentService,
        IConnectionManager connectionManager,
        IPeerDiscoveryService peerDiscovery,
        Trackers.MultiTracker.IMultiTrackerManager multiTracker,
        ITrackerEntryService trackerEntryService = null,
        ITorrentEventLogService eventLogService = null,
        Trackers.Metrics.ITrackerMetricService trackerMetricService = null,
        Trackers.ITrackerAnnounceService trackerAnnounceService = null,
        IRandomNumberGenerator random = null,
        Transport.IUtpManager utpManager = null,
        IClientBehaviorSimulator clientBehaviorSimulator = null,
        IVpnKillSwitchService vpnKillSwitchService = null)
    {
        _configService = configService;
        _torrentService = torrentService;
        _connectionManager = connectionManager;
        _peerDiscovery = peerDiscovery;
        _multiTracker = multiTracker;
        _trackerEntryService = trackerEntryService;
        _eventLogService = eventLogService;
        _trackerMetricService = trackerMetricService;
        _trackerAnnounceService = trackerAnnounceService ??
            (trackerEntryService != null && multiTracker != null && peerDiscovery != null && eventLogService != null && configService != null
                ? new Trackers.TrackerAnnounceService(trackerEntryService, multiTracker, peerDiscovery, eventLogService, configService, trackerMetricService)
                : null);
        _clientBehaviorSimulator = clientBehaviorSimulator;
        _random = random ?? new RandomNumberGenerator();
        _utpManager = utpManager;
        _vpnKillSwitchService = vpnKillSwitchService;

        var maxGlobal = configService.MaxGlobalConnections > 0 ? configService.MaxGlobalConnections : 200;
        var maxHalfOpen = configService.MaximumHalfOpenConnections > 0 ? configService.MaximumHalfOpenConnections : 50;

        _connectionSemaphore = new SemaphoreSlim(maxGlobal);
        _halfOpenSemaphore = new SemaphoreSlim(maxHalfOpen);
        _logger = LogManager.GetCurrentClassLogger();

        if (_vpnKillSwitchService != null)
        {
            _vpnKillSwitchService.VpnDropped += OnVpnDropped;
        }
    }

    private void OnVpnDropped(string iface)
    {
        _logger.Warn("VPN kill switch engaged (interface '{0}' dropped). Terminating all active peer connections.", iface);
        try
        {
            _connectionManager?.DisconnectAll();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error disconnecting peers after VPN kill switch triggered");
        }
    }

    public override void Dispose()
    {
        if (_vpnKillSwitchService != null)
        {
            _vpnKillSwitchService.VpnDropped -= OnVpnDropped;
        }

        _connectionSemaphore?.Dispose();
        _halfOpenSemaphore?.Dispose();
        base.Dispose();
    }

    private EncryptionMode GetEncryptionMode()
    {
        return _configService.EncryptionMode switch
        {
            "required" => EncryptionMode.RequireEncrypted,
            "disabled" => EncryptionMode.PreferPlainText,
            _ => EncryptionMode.PreferEncrypted
        };
    }

    private IPAddress GetListenAddress()
    {
        if (_vpnKillSwitchService != null)
        {
            var ifaceIp = _vpnKillSwitchService.GetVpnInterfaceIpAddress(
                _configService.EnableIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork);

            if (ifaceIp != null)
            {
                return ifaceIp;
            }

            ifaceIp = _vpnKillSwitchService.GetVpnInterfaceIpAddress(AddressFamily.InterNetwork);
            if (ifaceIp != null)
            {
                return ifaceIp;
            }
        }

        var bindIface = _configService.BindInterface?.Trim();
        if (!string.IsNullOrWhiteSpace(bindIface) &&
            !bindIface.Equals("Any", StringComparison.OrdinalIgnoreCase) &&
            !bindIface.Equals("all", StringComparison.OrdinalIgnoreCase) &&
            !bindIface.Equals("*", StringComparison.OrdinalIgnoreCase))
        {
            if (IPAddress.TryParse(bindIface, out var parsed))
            {
                return parsed;
            }
        }

        return _configService.EnableIPv6 ? IPAddress.IPv6Any : IPAddress.Any;
    }

    private void ApplySocketQos(Socket socket)
    {
        if (socket == null)
        {
            return;
        }

        try
        {
            if (_configService.PeerDscp > 0)
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, _configService.PeerDscp << 2);
            }
            else if (_configService.PeerTos > 0)
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, _configService.PeerTos);
            }
        }
        catch (Exception ex)
        {
            _logger.Trace(ex, "Failed to apply DSCP/TOS socket options");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listenerTask = RunListenerAsync(stoppingToken);
        var contactTask = RunPeerContactLoopAsync(stoppingToken);
        await Task.WhenAll(listenerTask, contactTask);
    }

    private async Task RunListenerAsync(CancellationToken stoppingToken)
    {
        var listeningPort = _configService.ListeningPort;
        var bindAddress = GetListenAddress();
        var listener = new TcpListener(bindAddress, listeningPort);

        try
        {
            try
            {
                if (bindAddress.Equals(IPAddress.IPv6Any))
                {
                    listener.Server.DualMode = true;
                }

                listener.Start();
            }
            catch (SocketException ex)
            {
                _logger.Warn(ex, "Peer server failed to bind {0}:{1}, attempting fallback to IPv4 Any", bindAddress, listeningPort);
                try
                {
                    listener = new TcpListener(IPAddress.Any, listeningPort);
                    listener.Start();
                }
                catch (Exception fallbackEx)
                {
                    _logger.Warn(fallbackEx, "Peer server failed fallback bind on port {0}, skipping", listeningPort);
                    return;
                }
            }

            _logger.Info("Peer server listening on {0}:{1}", bindAddress, listeningPort);

            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);

                if (_vpnKillSwitchService?.IsFailClosedActive == true)
                {
                    _logger.Debug("VPN fail-closed engaged; rejecting incoming peer connection");
                    client.Dispose();
                    continue;
                }

                _ = Task.Run(
                    async () =>
                    {
                        var maxPerIp = _configService.MaxConnectionsPerIp > 0 ? _configService.MaxConnectionsPerIp : 5;
                        var clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                        var currentCount = _connectionsPerIp.AddOrUpdate(clientIp, 1, (_, count) => count + 1);
                        if (currentCount > maxPerIp)
                        {
                            _connectionsPerIp.AddOrUpdate(clientIp, 0, (_, count) => Math.Max(0, count - 1));
                            client.Dispose();
                            return;
                        }

                        ApplySocketQos(client.Client);

                        try
                        {
                            if (!await _connectionSemaphore.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken))
                            {
                                _connectionsPerIp.AddOrUpdate(clientIp, 0, (_, count) => Math.Max(0, count - 1));
                                client.Dispose();
                                return;
                            }
                        }
                        catch
                        {
                            _connectionsPerIp.AddOrUpdate(clientIp, 0, (_, count) => Math.Max(0, count - 1));
                            client.Dispose();
                            return;
                        }

                        try
                        {
                            HandleConnection(client, stoppingToken);
                        }
                        finally
                        {
                            _connectionSemaphore.Release();
                            _connectionsPerIp.AddOrUpdate(clientIp, 0, (_, count) => Math.Max(0, count - 1));
                        }
                    },
                    stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task RunPeerContactLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            var startupDelay = Math.Max(1, _configService.PeerContactIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(startupDelay), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_vpnKillSwitchService?.IsFailClosedActive == true)
                {
                    _logger.Debug("VPN fail-closed active, suppressing peer contact loop");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                var intervalSeconds = _configService.PeerContactIntervalSeconds;

                var torrents = _torrentService.GetAll()
                    .Where(t => t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Downloading)
                    .Where(t => !string.IsNullOrEmpty(t.InfoHash))
                    .ToList();

                _logger.Debug("Peer contact cycle: {0} active torrents", torrents.Count);

                foreach (var torrent in torrents)
                {
                    if (stoppingToken.IsCancellationRequested || _vpnKillSwitchService?.IsFailClosedActive == true)
                    {
                        break;
                    }

                    DiscoverPeersFromTracker(torrent);
                    ConnectToDiscoveredPeers(torrent, stoppingToken);
                }

                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void DiscoverPeersFromTracker(Torrent torrent)
    {
        if (_vpnKillSwitchService?.IsFailClosedActive == true)
        {
            return;
        }

        try
        {
            _trackerAnnounceService?.AnnounceTorrent(torrent, force: false);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Tracker announce failed for {0}", torrent.Name);
        }
    }

    private void ConnectToDiscoveredPeers(Torrent torrent, CancellationToken stoppingToken)
    {
        if (_vpnKillSwitchService?.IsFailClosedActive == true)
        {
            return;
        }

        if (!_connectionManager.CanAddConnectionForTorrent(torrent.InfoHash))
        {
            return;
        }

        var candidates = _peerDiscovery.GetPeers(torrent.InfoHash, 5);
        if (candidates.Count == 0)
        {
            return;
        }

        foreach (var candidate in candidates)
        {
            if (stoppingToken.IsCancellationRequested ||
                _vpnKillSwitchService?.IsFailClosedActive == true ||
                !_connectionManager.CanAddConnectionForTorrent(torrent.InfoHash))
            {
                break;
            }

            _ = Task.Run(() => ConnectToPeer(torrent, candidate, stoppingToken), stoppingToken);
        }
    }

    private async Task ConnectToPeer(Torrent torrent, DiscoveredPeer candidate, CancellationToken stoppingToken)
    {
        if (_vpnKillSwitchService?.IsFailClosedActive == true)
        {
            return;
        }

        if (!_configService.EnableIPv6 &&
            IPAddress.TryParse(candidate.Ip, out var candIp) &&
            candIp.AddressFamily == AddressFamily.InterNetworkV6)
        {
            _logger.Debug("IPv6 disabled, skipping IPv6 peer {0}:{1}", candidate.Ip, candidate.Port);
            return;
        }

        var maxPerIp = _configService.MaxConnectionsPerIp > 0 ? _configService.MaxConnectionsPerIp : 5;
        var currentCount = _connectionsPerIp.AddOrUpdate(candidate.Ip, 1, (_, count) => count + 1);
        if (currentCount > maxPerIp)
        {
            _connectionsPerIp.AddOrUpdate(candidate.Ip, 0, (_, count) => Math.Max(0, count - 1));
            return;
        }

        try
        {
            if (!await _halfOpenSemaphore.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken))
            {
                _connectionsPerIp.AddOrUpdate(candidate.Ip, 0, (_, count) => Math.Max(0, count - 1));
                return;
            }
        }
        catch
        {
            _connectionsPerIp.AddOrUpdate(candidate.Ip, 0, (_, count) => Math.Max(0, count - 1));
            return;
        }

        PeerConnection connection = null;
        try
        {
            _logger.Debug("Connecting to peer {0}:{1} for {2}", candidate.Ip, candidate.Port, torrent.Name);
            _eventLogService?.Debug(torrent.Id, "Peers", $"Attempting connection to peer {candidate.Ip}:{candidate.Port} (source: {candidate.Source})");

            var localBind = _vpnKillSwitchService?.GetVpnInterfaceIpAddress();

            if (_utpManager != null && _utpManager.IsEnabled)
            {
                try
                {
                    var utp = _utpManager.CreateConnection();
                    var endpoint = new IPEndPoint(IPAddress.Parse(candidate.Ip), candidate.Port);
                    utp.Connect(endpoint);
                    if (utp.IsConnected)
                    {
                        connection = new PeerConnection(utp.GetStream(), candidate.Ip, candidate.Port);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "uTP connection attempt to {0}:{1} failed", candidate.Ip, candidate.Port);
                }

                if (connection == null && _utpManager.TcpFallbackEnabled)
                {
                    _logger.Debug("Falling back to TCP for peer {0}:{1}", candidate.Ip, candidate.Port);
                    connection = new PeerConnection(candidate.Ip, candidate.Port, localBind, _configService.PeerDscp, _configService.PeerTos);
                }
            }
            else
            {
                connection = new PeerConnection(candidate.Ip, candidate.Port, localBind, _configService.PeerDscp, _configService.PeerTos);
            }

            if (connection == null)
            {
                _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
                return;
            }

            connection.HandshakeTimeoutMs = Math.Min(_configService.HandshakeTimeoutSeconds * 1000, OutgoingConnectTimeoutMs);
            connection.MessageReadTimeoutMs = _configService.MessageReadTimeoutSeconds * 1000;
            connection.KeepAliveIntervalSeconds = _configService.KeepAliveIntervalSeconds;
            connection.MaxPipelinedRequests = _configService.PeerRequestCount;
            connection.IdleChance = _clientBehaviorSimulator != null
                ? _clientBehaviorSimulator.GetEffectiveIdleChance(_configService.PeerIdleChance)
                : _configService.PeerIdleChance;

            if (!connection.NegotiateEncryptionOutgoing(torrent.InfoHash, GetEncryptionMode()))
            {
                _logger.Debug("Outgoing encryption failed to {0}:{1}", candidate.Ip, candidate.Port);
                _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
                _eventLogService?.Debug(torrent.Id, "Peers", $"Encryption negotiation rejected by peer {candidate.Ip}:{candidate.Port}");
                connection.Dispose();
                return;
            }

            var peerId = (_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled && !_configService.AnonymousMode)
                ? (_clientBehaviorSimulator.GetActiveProfile()?.GeneratePeerId() ?? "-SD1000-000000000000")
                : "-SD1000-000000000000";
            connection.SendHandshake(torrent.InfoHash, peerId);

            if (!connection.ReceiveHandshake())
            {
                _logger.Debug("Outgoing handshake failed from {0}:{1}", candidate.Ip, candidate.Port);
                _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
                _eventLogService?.Debug(torrent.Id, "Peers", $"BitTorrent handshake rejected/timed out from {candidate.Ip}:{candidate.Port}");
                connection.Dispose();
                return;
            }

            connection.SendBitfield(torrent.PieceCount);
            connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
            connection.AmChoking = false;

            _connectionManager.Add(connection);
            _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, true);

            _logger.Info(
                "Outgoing peer connected: {0}:{1} for {2} (encrypted: {3})",
                candidate.Ip,
                candidate.Port,
                torrent.Name,
                connection.IsEncrypted);

            _eventLogService?.Info(
                torrent.Id,
                "Peers",
                $"Peer connected & active: {candidate.Ip}:{candidate.Port} (encrypted: {connection.IsEncrypted})");

            HandlePeerSession(connection, torrent);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to connect to peer {0}:{1}", candidate.Ip, candidate.Port);
            _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
            _eventLogService?.Debug(torrent.Id, "Peers", $"Failed to connect to peer {candidate.Ip}:{candidate.Port}: {ex.Message}");
            connection?.Dispose();
        }
        finally
        {
            _halfOpenSemaphore.Release();
            _connectionsPerIp.AddOrUpdate(candidate.Ip, 0, (_, count) => Math.Max(0, count - 1));
        }
    }

    private void HandlePeerSession(PeerConnection connection, Torrent torrent)
    {
        try
        {
            while (connection.IsConnected)
            {
                if (_vpnKillSwitchService?.IsFailClosedActive == true)
                {
                    _logger.Debug("VPN fail-closed engaged; terminating session with {0}:{1}", connection.RemoteIp, connection.RemotePort);
                    break;
                }

                var message = connection.ReceiveMessage();
                if (message == null)
                {
                    if (!connection.IsConnected)
                    {
                        break;
                    }

                    var elapsed = DateTime.UtcNow - connection.LastActivity;
                    if (elapsed.TotalSeconds >= connection.KeepAliveIntervalSeconds)
                    {
                        connection.SendKeepAlive();
                    }

                    continue;
                }

                HandleMessage(connection, message);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Peer session ended with {0}:{1}", connection.RemoteIp, connection.RemotePort);
        }
        finally
        {
            _connectionManager.Remove(connection);
            connection.Dispose();
        }
    }

    private void HandleConnection(TcpClient client, CancellationToken stoppingToken)
    {
        using var connection = new PeerConnection(client);
        connection.HandshakeTimeoutMs = _configService.HandshakeTimeoutSeconds * 1000;
        connection.MessageReadTimeoutMs = _configService.MessageReadTimeoutSeconds * 1000;
        connection.KeepAliveIntervalSeconds = _configService.KeepAliveIntervalSeconds;
        connection.MaxPipelinedRequests = _configService.PeerRequestCount;
        connection.IdleChance = _clientBehaviorSimulator != null
            ? _clientBehaviorSimulator.GetEffectiveIdleChance(_configService.PeerIdleChance)
            : _configService.PeerIdleChance;

        _logger.Debug("Incoming peer: {0}:{1}", connection.RemoteIp, connection.RemotePort);

        try
        {
            var negotiated = connection.NegotiateEncryptionIncoming(ValidateInfoHash, GetEncryptionMode());
            if (!negotiated)
            {
                _logger.Debug("Encryption negotiation failed from {0}", connection.RemoteIp);
                return;
            }

            if (!connection.ReceiveHandshake())
            {
                _logger.Debug("Invalid handshake from {0}", connection.RemoteIp);
                return;
            }

            var torrents = _torrentService.GetAll();
            var torrent = torrents.Find(t => string.Equals(t.InfoHash, connection.InfoHash, StringComparison.OrdinalIgnoreCase));

            if (torrent == null)
            {
                _logger.Debug("Unknown info hash from {0}: {1}", connection.RemoteIp, connection.InfoHash);
                return;
            }

            var peerId = "-SD1000-000000000000";
            connection.SendHandshake(torrent.InfoHash, peerId);

            _logger.Debug(
                "Peer {0} connected (encrypted: {1}, method: {2})",
                connection.RemoteIp,
                connection.IsEncrypted,
                connection.EncryptionMethod);

            _connectionManager.Add(connection);
            connection.SendBitfield(torrent.PieceCount);
            connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
            connection.AmChoking = false;

            while (connection.IsConnected && !stoppingToken.IsCancellationRequested)
            {
                if (_vpnKillSwitchService?.IsFailClosedActive == true)
                {
                    _logger.Debug("VPN fail-closed engaged; terminating incoming session with {0}", connection.RemoteIp);
                    break;
                }

                var message = connection.ReceiveMessage();
                if (message == null)
                {
                    if (!connection.IsConnected)
                    {
                        break;
                    }

                    var elapsed = DateTime.UtcNow - connection.LastActivity;
                    if (elapsed.TotalSeconds >= connection.KeepAliveIntervalSeconds)
                    {
                        connection.SendKeepAlive();
                    }

                    continue;
                }

                HandleMessage(connection, message);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Peer connection error: {0}", connection.RemoteIp);
        }
        finally
        {
            _connectionManager.Remove(connection);
        }
    }

    private bool ValidateInfoHash(byte[] skeyHash)
    {
        var torrents = _torrentService.GetAll();
        return torrents.Where(t => !string.IsNullOrEmpty(t.InfoHash)).Any(t =>
        {
            var infoHashBytes = Convert.FromHexString(t.InfoHash);
            var expected = MseKeyDerivation.DeriveKey(infoHashBytes, Encoding.ASCII.GetBytes("req2"));
            return expected.AsSpan().SequenceEqual(skeyHash);
        });
    }

    private void HandleMessage(PeerConnection connection, PeerMessage message)
    {
        switch (message.Type)
        {
            case PeerMessageType.Interested:
                connection.PeerInterested = true;
                if (connection.AmChoking)
                {
                    connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                    connection.AmChoking = false;
                }

                break;

            case PeerMessageType.NotInterested:
                connection.PeerInterested = false;
                break;

            case PeerMessageType.Request:
                if (_random.NextDouble() < connection.IdleChance)
                {
                    connection.SendKeepAlive();
                    break;
                }

                if (connection.PendingRequestCount >= connection.MaxPipelinedRequests)
                {
                    _logger.Trace(
                        "Request pipeline full ({0}) from {1}, ignoring",
                        connection.MaxPipelinedRequests,
                        connection.RemoteIp);
                    break;
                }

                if (message.Payload != null && message.Payload.Length >= 12)
                {
                    HandlePieceRequest(connection, message.Payload);
                    connection.PendingRequestCount++;
                }

                break;

            default:
                _logger.Trace("Ignoring message type {0} from {1}", message.Type, connection.RemoteIp);
                break;
        }
    }

    private static void HandlePieceRequest(PeerConnection connection, byte[] payload)
    {
        var index = (int)(((uint)payload[0] << 24) | ((uint)payload[1] << 16) | ((uint)payload[2] << 8) | payload[3]);
        var begin = (int)(((uint)payload[4] << 24) | ((uint)payload[5] << 16) | ((uint)payload[6] << 8) | payload[7]);
        var length = (int)(((uint)payload[8] << 24) | ((uint)payload[9] << 16) | ((uint)payload[10] << 8) | payload[11]);

        const int MaxBlockSize = 32768;
        if (length <= 0 || length > MaxBlockSize)
        {
            return;
        }

        if (index < 0 || begin < 0)
        {
            return;
        }

        var payloadSize = 8 + length;
        var piecePayload = ArrayPool<byte>.Shared.Rent(payloadSize);
        try
        {
            Array.Clear(piecePayload, 0, payloadSize);
            piecePayload[0] = (byte)(index >> 24);
            piecePayload[1] = (byte)(index >> 16);
            piecePayload[2] = (byte)(index >> 8);
            piecePayload[3] = (byte)index;
            piecePayload[4] = (byte)(begin >> 24);
            piecePayload[5] = (byte)(begin >> 16);
            piecePayload[6] = (byte)(begin >> 8);
            piecePayload[7] = (byte)begin;

            connection.SendMessage(new PeerMessage { Type = PeerMessageType.Piece, Payload = piecePayload, PayloadLength = payloadSize });
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(piecePayload);
        }
    }
}
