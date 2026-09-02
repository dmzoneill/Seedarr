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
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers.Encryption;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers;

public class PeerServer : BackgroundService
{
    private const int MaxConnectionsPerIp = 5;
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
    private readonly SemaphoreSlim _connectionSemaphore;
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
        Trackers.ITrackerAnnounceService trackerAnnounceService = null)
    {
        _configService = configService;
        _torrentService = torrentService;
        _connectionManager = connectionManager;
        _peerDiscovery = peerDiscovery;
        _multiTracker = multiTracker;
        _trackerEntryService = trackerEntryService;
        _eventLogService = eventLogService;
        _trackerMetricService = trackerMetricService;
        _trackerAnnounceService = trackerAnnounceService;
        _connectionSemaphore = new SemaphoreSlim(configService.MaxGlobalConnections);
        _logger = LogManager.GetCurrentClassLogger();
    }

    public override void Dispose()
    {
        _connectionSemaphore?.Dispose();
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listenerTask = RunListenerAsync(stoppingToken);
        var contactTask = RunPeerContactLoopAsync(stoppingToken);
        await Task.WhenAll(listenerTask, contactTask);
    }

    private async Task RunListenerAsync(CancellationToken stoppingToken)
    {
        var listeningPort = _configService.ListeningPort;
        var listener = new TcpListener(IPAddress.Any, listeningPort);

        try
        {
            try
            {
                listener.Start();
            }
            catch (SocketException ex)
            {
                _logger.Warn(ex, "Peer server failed to bind port {0}, skipping", listeningPort);
                return;
            }

            _logger.Info("Peer server listening on port {0}", listeningPort);

            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = Task.Run(
                    async () =>
                    {
                        var clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                        var currentCount = _connectionsPerIp.AddOrUpdate(clientIp, 1, (_, count) => count + 1);
                        if (currentCount > MaxConnectionsPerIp)
                        {
                            _connectionsPerIp.AddOrUpdate(clientIp, 0, (_, count) => Math.Max(0, count - 1));
                            client.Dispose();
                            return;
                        }

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
                var intervalSeconds = _configService.PeerContactIntervalSeconds;

                var torrents = _torrentService.GetAll()
                    .Where(t => t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Downloading)
                    .Where(t => !string.IsNullOrEmpty(t.InfoHash))
                    .ToList();

                _logger.Debug("Peer contact cycle: {0} active torrents", torrents.Count);

                foreach (var torrent in torrents)
                {
                    if (stoppingToken.IsCancellationRequested)
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
        try
        {
            if (_trackerAnnounceService != null)
            {
                _trackerAnnounceService.AnnounceTorrent(torrent, force: false);
                return;
            }

            var trackerEntries = _trackerEntryService.GetByTorrentId(torrent.Id);
            if (trackerEntries.Count == 0 && !string.IsNullOrEmpty(torrent.TrackerUrl))
            {
                var entry = new TrackerEntry
                {
                    TorrentId = torrent.Id,
                    Url = torrent.TrackerUrl,
                    Tier = 1,
                    Status = TrackerStatus.Working,
                    Enabled = true
                };
                entry = _trackerEntryService.Add(entry);
                trackerEntries = new System.Collections.Generic.List<TrackerEntry> { entry };
            }

            var enabledTrackers = trackerEntries.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.Url)).ToList();
            if (enabledTrackers.Count == 0)
            {
                return;
            }

            foreach (var entry in enabledTrackers)
            {
                var isFirstAnnounce = entry.TotalAnnounces == 0 || !entry.LastAnnounce.HasValue;
                if (!isFirstAnnounce && entry.NextAnnounce.HasValue && entry.NextAnnounce.Value > DateTime.UtcNow)
                {
                    continue;
                }

                var eventName = isFirstAnnounce ? "started" : (torrent.Status == TorrentStatus.Stopped ? "stopped" : "regular");
                _eventLogService.Info(
                    torrent.Id,
                    "Tracker",
                    $"Announcing to tracker: {entry.Url} (event: {eventName}, uploaded: {torrent.Uploaded:N0} bytes, left: {Math.Max(0, torrent.TotalSize - torrent.Downloaded):N0} bytes)");

                var request = new Trackers.TrackerAnnounceRequest
                {
                    InfoHash = torrent.InfoHash,
                    PeerId = "-SD1000-000000000000",
                    Port = _configService.ListeningPort,
                    Uploaded = torrent.Uploaded,
                    Downloaded = torrent.Downloaded,
                    Left = Math.Max(0, torrent.TotalSize - torrent.Downloaded),
                    Event = isFirstAnnounce ? "started" : null,
                    TrackerUrl = entry.Url,
                    Compact = true,
                    NumWant = 50
                };

                var announceList = new System.Collections.Generic.List<System.Collections.Generic.List<string>>
                {
                    new() { entry.Url }
                };

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var response = _multiTracker.Announce(request, announceList);
                sw.Stop();

                _trackerMetricService?.RecordAnnounce(
                    entry.Url,
                    torrent.Id,
                    torrent.Uploaded,
                    torrent.Downloaded,
                    Math.Max(0, torrent.TotalSize - torrent.Downloaded),
                    sw.ElapsedMilliseconds,
                    response.Success,
                    response.Complete,
                    response.Incomplete,
                    response.Peers?.Count ?? 0,
                    response.FailureReason);

                entry.TotalAnnounces++;
                entry.LastResponseTime = sw.ElapsedMilliseconds;

                if (response.Success)
                {
                    entry.Status = TrackerStatus.Working;
                    entry.Seeders = response.Complete;
                    entry.Leechers = response.Incomplete;
                    entry.LastAnnounce = DateTime.UtcNow;
                    var interval = response.Interval > 0 ? response.Interval : (_configService.AnnounceIntervalSeconds > 0 ? _configService.AnnounceIntervalSeconds : 1800);
                    entry.AnnounceInterval = interval;
                    entry.MinAnnounceInterval = response.MinInterval > 0 ? response.MinInterval : 900;
                    entry.NextAnnounce = DateTime.UtcNow.AddSeconds(interval);
                    entry.SuccessfulAnnounces++;
                    entry.ConsecutiveFailures = 0;
                    entry.ErrorMessage = null;
                    _trackerEntryService.Update(entry);

                    _eventLogService.Info(
                        torrent.Id,
                        "Tracker",
                        $"Tracker announce succeeded: {entry.Url} -> Seeders: {response.Complete}, Leechers: {response.Incomplete}, Peers: {response.Peers.Count}, Interval: {interval}s ({sw.ElapsedMilliseconds}ms)");

                    if (response.Peers.Count > 0)
                    {
                        _peerDiscovery.AddPeers(torrent.InfoHash, response.Peers, "tracker");
                        var peerSample = string.Join(", ", response.Peers.Take(5).Select(p => $"{p.Ip}:{p.Port}"));
                        _eventLogService.Info(
                            torrent.Id,
                            "Peers",
                            $"Discovered {response.Peers.Count} peer candidate(s) from {entry.Url} ({peerSample}{(response.Peers.Count > 5 ? ", ..." : "")})");
                    }
                }
                else
                {
                    entry.Status = TrackerStatus.Failed;
                    entry.ConsecutiveFailures++;
                    entry.ErrorMessage = response.FailureReason;
                    entry.LastErrorTime = DateTime.UtcNow;
                    var backoffSeconds = Math.Min(1800, 60 * Math.Pow(2, Math.Min(5, entry.ConsecutiveFailures)));
                    entry.NextAnnounce = DateTime.UtcNow.AddSeconds(backoffSeconds);
                    _trackerEntryService.Update(entry);

                    _eventLogService.Warn(
                        torrent.Id,
                        "Tracker",
                        $"Tracker announce failed: {entry.Url} -> {response.FailureReason ?? "Unreachable"} (failure #{entry.ConsecutiveFailures}, next retry in {(int)backoffSeconds}s)");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Tracker announce failed for {0}", torrent.Name);
        }
    }

    private void ConnectToDiscoveredPeers(Torrent torrent, CancellationToken stoppingToken)
    {
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
            if (stoppingToken.IsCancellationRequested || !_connectionManager.CanAddConnectionForTorrent(torrent.InfoHash))
            {
                break;
            }

            _ = Task.Run(() => ConnectToPeer(torrent, candidate), stoppingToken);
        }
    }

    private void ConnectToPeer(Torrent torrent, DiscoveredPeer candidate)
    {
        PeerConnection connection = null;
        try
        {
            _logger.Debug("Connecting to peer {0}:{1} for {2}", candidate.Ip, candidate.Port, torrent.Name);
            _eventLogService.Debug(torrent.Id, "Peers", $"Attempting connection to peer {candidate.Ip}:{candidate.Port} (source: {candidate.Source})");

            connection = new PeerConnection(candidate.Ip, candidate.Port);
            connection.HandshakeTimeoutMs = Math.Min(_configService.HandshakeTimeoutSeconds * 1000, OutgoingConnectTimeoutMs);
            connection.MessageReadTimeoutMs = _configService.MessageReadTimeoutSeconds * 1000;
            connection.KeepAliveIntervalSeconds = _configService.KeepAliveIntervalSeconds;
            connection.MaxPipelinedRequests = _configService.PeerRequestCount;
            connection.IdleChance = _configService.PeerIdleChance;

            if (!connection.NegotiateEncryptionOutgoing(torrent.InfoHash, GetEncryptionMode()))
            {
                _logger.Debug("Outgoing encryption failed to {0}:{1}", candidate.Ip, candidate.Port);
                _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
                _eventLogService.Debug(torrent.Id, "Peers", $"Encryption negotiation rejected by peer {candidate.Ip}:{candidate.Port}");
                connection.Dispose();
                return;
            }

            var peerId = "-SD1000-000000000000";
            connection.SendHandshake(torrent.InfoHash, peerId);

            if (!connection.ReceiveHandshake())
            {
                _logger.Debug("Outgoing handshake failed from {0}:{1}", candidate.Ip, candidate.Port);
                _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
                _eventLogService.Debug(torrent.Id, "Peers", $"BitTorrent handshake rejected/timed out from {candidate.Ip}:{candidate.Port}");
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

            _eventLogService.Info(
                torrent.Id,
                "Peers",
                $"Peer connected & active: {candidate.Ip}:{candidate.Port} (encrypted: {connection.IsEncrypted})");

            HandlePeerSession(connection, torrent);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to connect to peer {0}:{1}", candidate.Ip, candidate.Port);
            _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
            _eventLogService.Debug(torrent.Id, "Peers", $"Failed to connect to peer {candidate.Ip}:{candidate.Port}: {ex.Message}");
            connection?.Dispose();
        }
    }

    private void HandlePeerSession(PeerConnection connection, Torrent torrent)
    {
        try
        {
            while (connection.IsConnected)
            {
                var message = connection.ReceiveMessage();
                if (message == null)
                {
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
        }
    }

    private void HandleConnection(TcpClient client, CancellationToken stoppingToken)
    {
        using var connection = new PeerConnection(client);
        connection.HandshakeTimeoutMs = _configService.HandshakeTimeoutSeconds * 1000;
        connection.MessageReadTimeoutMs = _configService.MessageReadTimeoutSeconds * 1000;
        connection.KeepAliveIntervalSeconds = _configService.KeepAliveIntervalSeconds;
        connection.MaxPipelinedRequests = _configService.PeerRequestCount;
        connection.IdleChance = _configService.PeerIdleChance;

        _logger.Debug("Incoming peer: {0}:{1}", connection.RemoteIp, connection.RemotePort);

        try
        {
            // Attempt MSE/PE negotiation - this will detect plain BT handshakes and fall through
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

            // Find matching torrent
            var torrents = _torrentService.GetAll();
            var torrent = torrents.Find(t => string.Equals(t.InfoHash, connection.InfoHash, StringComparison.OrdinalIgnoreCase));

            if (torrent == null)
            {
                _logger.Debug("Unknown info hash from {0}: {1}", connection.RemoteIp, connection.InfoHash);
                return;
            }

            // Send our handshake back
            var peerId = "-SD1000-000000000000";
            connection.SendHandshake(torrent.InfoHash, peerId);

            _logger.Debug(
                "Peer {0} connected (encrypted: {1}, method: {2})",
                connection.RemoteIp,
                connection.IsEncrypted,
                connection.EncryptionMethod);

            _connectionManager.Add(connection);

            // Send bitfield (all pieces)
            connection.SendBitfield(torrent.PieceCount);

            // Unchoke
            connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
            connection.AmChoking = false;

            // Handle messages with keep-alive support
            while (connection.IsConnected && !stoppingToken.IsCancellationRequested)
            {
                var message = connection.ReceiveMessage();
                if (message == null)
                {
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
                if (Random.Shared.NextDouble() < connection.IdleChance)
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

        const int MaxBlockSize = 32768; // 2x standard 16KB block size
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
