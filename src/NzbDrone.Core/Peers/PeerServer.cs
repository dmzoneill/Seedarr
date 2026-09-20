using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Peers.Encryption;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers;

public class PeerServer : BackgroundService, IPeerServer, IHandle<VpnInterfaceRestoredEvent>, IHandle<VpnRestoredEvent>, IHandle<TorrentAddedEvent>, IHandle<TorrentUpdatedEvent>, IHandle<TorrentDeletedEvent>, IHandle<PeerRequestRejectedEvent>, IHandle<TorrentDownloadCompletedEvent>, IHandle<TorrentFinishedEvent>, IHandle<TorrentStatusChangedEvent>
{
    private const int OutgoingConnectTimeoutMs = 5000;
    private const int UnauthenticatedHandshakeTimeoutMs = 5000;
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
    private readonly Extensions.IFastExtensionHandler _fastExtensionHandler;
    private readonly Extensions.IExtensionManager _extensionManager;
    private readonly IChokeManager _chokeManager;
    private readonly Network.IProxySettingsProvider _proxySettingsProvider;
    private readonly IDhKeyPool _dhKeyPool;
    private readonly IMseSkeyRegistry _mseSkeyRegistry;
    private readonly Extensions.IMetadataExchange _metadataExchange;
    private readonly Extensions.ISyntheticMetadataGenerator _syntheticMetadataGenerator;
    private readonly Extensions.IMagnetMetadataDownloader _magnetMetadataDownloader;
    private readonly Extensions.IPeerExchange _peerExchange;
    private readonly Extensions.IPexService _pexService;
    private readonly PiecePicker.PiecePicker _piecePicker;
    private readonly PiecePicker.IPiecePicker _sequentialPicker;
    private readonly PiecePicker.IPiecePicker _rarestFirstPicker;
    private readonly IPieceStorage _pieceStorage;
    private readonly SemaphoreSlim _connectionSemaphore;
    private readonly SemaphoreSlim _halfOpenSemaphore;
    private readonly ConcurrentDictionary<string, int> _connectionsPerIp = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _inFlightOutgoingEndpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Torrent> _torrentCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte[]> _metadataCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SwarmPieceHistogram> _swarmHistograms = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ISuperSeedingTracker> _superSeedingTrackers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISuperSeedingTracker _superSeedingTracker;
    private readonly Logger _logger;
    private readonly object _listenerLock = new();
    private readonly SemaphoreSlim _rebindSignal = new(0, 1);
    private readonly CancellationTokenSource _serverLifecycleCts = new();
    private TcpListener _listener;
    private CancellationTokenSource _listenerCts;

    public Socket ListenerSocket => _listener?.Server;
    public PiecePicker.PiecePicker PiecePicker => _piecePicker;
    public PiecePicker.IPiecePicker SequentialPicker => _piecePicker?.SequentialPicker ?? _sequentialPicker;
    public PiecePicker.IPiecePicker RarestFirstPicker => _piecePicker?.RarestFirstPicker ?? _rarestFirstPicker;
    public bool IsListening { get; private set; }

    public SwarmPieceHistogram GetSwarmPieceHistogram(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return null;
        }

        _swarmHistograms.TryGetValue(infoHash, out var histogram);
        return histogram;
    }

    private SwarmPieceHistogram GetOrCreateHistogram(string infoHash, int pieceCount = 0)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return null;
        }

        if (pieceCount <= 0)
        {
            var cached = GetCachedTorrent(infoHash);
            pieceCount = cached?.PieceCount ?? 0;
        }

        if (pieceCount <= 0)
        {
            return null;
        }

        return _swarmHistograms.GetOrAdd(infoHash, _ => new SwarmPieceHistogram(pieceCount));
    }

    private static string GetPeerKey(PeerConnection connection)
    {
        if (connection == null)
        {
            return string.Empty;
        }

        return !string.IsNullOrEmpty(connection.PeerId)
            ? connection.PeerId
            : $"{connection.RemoteIp}:{connection.RemotePort}";
    }

    public ISuperSeedingTracker GetSuperSeedingTracker(string infoHash, int pieceCount = 0)
    {
        if (_superSeedingTracker != null)
        {
            return _superSeedingTracker;
        }

        if (string.IsNullOrEmpty(infoHash))
        {
            return null;
        }

        return _superSeedingTrackers.GetOrAdd(infoHash, _ =>
        {
            var count = pieceCount > 0 ? pieceCount : (GetCachedTorrent(infoHash)?.PieceCount ?? 0);
            return new SuperSeedingTracker(count);
        });
    }

    private ISuperSeedingTracker GetOrCreateTracker(Torrent torrent)
    {
        if (_superSeedingTracker != null)
        {
            return _superSeedingTracker;
        }

        if (torrent == null || string.IsNullOrEmpty(torrent.InfoHash))
        {
            return null;
        }

        return _superSeedingTrackers.GetOrAdd(torrent.InfoHash, _ => new SuperSeedingTracker(torrent.PieceCount));
    }

    public bool BindFailed { get; private set; }
    public int ListeningPort { get; private set; }
    public string BindErrorMessage { get; private set; }

    public PiecePicker.IPiecePicker GetPiecePicker(Torrent torrent)
    {
        if (torrent != null && _piecePicker?.StreamingPicker != null && _piecePicker.StreamingPicker.HasActiveStream(torrent.Id))
        {
            return _piecePicker.StreamingPicker;
        }

        return torrent?.SequentialDownload == true ? SequentialPicker : RarestFirstPicker;
    }

    public PiecePicker.IPiecePicker GetPiecePicker(bool sequential)
    {
        return sequential ? SequentialPicker : RarestFirstPicker;
    }

    internal bool IsOutgoingEndpointInFlight(string ip, int port) => _inFlightOutgoingEndpoints.ContainsKey($"{ip}:{port}");

    internal byte[] GetLocalTorrentBitfield(Torrent torrent)
    {
        if (torrent == null || torrent.PieceCount <= 0)
        {
            return Array.Empty<byte>();
        }

        var byteCount = (torrent.PieceCount + 7) / 8;
        var bitfield = new byte[byteCount];

        if (torrent.Status == TorrentStatus.Checking || torrent.Status == TorrentStatus.QueuedForChecking)
        {
            return bitfield;
        }

        bool[] verified = null;
        if (_pieceStorage != null && !string.IsNullOrEmpty(torrent.InfoHash))
        {
            verified = _pieceStorage.GetVerifiedPieces(torrent.InfoHash);
        }

        if (verified != null)
        {
            var max = Math.Min(torrent.PieceCount, verified.Length);
            for (var i = 0; i < max; i++)
            {
                if (verified[i])
                {
                    bitfield[i / 8] |= (byte)(0x80 >> (i % 8));
                }
            }
        }
        else if (torrent.Progress >= 1.0 || torrent.Status == TorrentStatus.Seeding)
        {
            for (var i = 0; i < byteCount; i++)
            {
                bitfield[i] = 0xFF;
            }

            var spare = (byteCount * 8) - torrent.PieceCount;
            if (spare > 0)
            {
                bitfield[byteCount - 1] = (byte)(0xFF << spare);
            }
        }
        else if (torrent.Progress > 0.0)
        {
            var verifiedCount = (int)Math.Round(torrent.Progress * torrent.PieceCount);
            for (var i = 0; i < verifiedCount && i < torrent.PieceCount; i++)
            {
                bitfield[i / 8] |= (byte)(0x80 >> (i % 8));
            }
        }

        return bitfield;
    }

    internal void SendInitialAvailability(PeerConnection connection, Torrent torrent)
    {
        if (torrent == null || torrent.PieceCount <= 0)
        {
            return;
        }

        if (torrent.Status == TorrentStatus.Checking || torrent.Status == TorrentStatus.QueuedForChecking)
        {
            if (_fastExtensionHandler != null && (connection.SupportsFastExtension || _fastExtensionHandler.IsFastPeer(connection)))
            {
                connection.SendMessage(_fastExtensionHandler.SerializeHaveNone());
            }
            else
            {
                var byteCount = (torrent.PieceCount + 7) / 8;
                connection.SendBitfield(new byte[byteCount]);
            }

            return;
        }

        if (torrent.SuperSeeding)
        {
            if (_fastExtensionHandler != null && (connection.SupportsFastExtension || _fastExtensionHandler.IsFastPeer(connection)))
            {
                connection.SendMessage(_fastExtensionHandler.SerializeHaveNone());
            }
            else
            {
                var byteCount = (torrent.PieceCount + 7) / 8;
                connection.SendBitfield(new byte[byteCount]);
            }

            return;
        }

        bool[] verified = null;
        if (_pieceStorage != null && !string.IsNullOrEmpty(torrent.InfoHash))
        {
            verified = _pieceStorage.GetVerifiedPieces(torrent.InfoHash);
        }

        var isComplete = (torrent.Progress >= 1.0 || torrent.Status == TorrentStatus.Seeding) ||
            (verified != null && verified.Length >= torrent.PieceCount && verified.Take(torrent.PieceCount).All(x => x));

        var hasNoPieces = !isComplete && (
            (verified != null && !verified.Take(torrent.PieceCount).Any(x => x)) ||
            (verified == null && (torrent.Progress <= 0.0 || torrent.Downloaded == 0)));

        var bitfield = GetLocalTorrentBitfield(torrent);

        if (_fastExtensionHandler != null && (connection.SupportsFastExtension || _fastExtensionHandler.IsFastPeer(connection)))
        {
            _fastExtensionHandler.SendHaveAllOrBitfield(connection, torrent.PieceCount, isComplete, hasNoPieces, bitfield);
        }
        else
        {
            connection.SendBitfield(bitfield);
        }
    }

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
        IVpnKillSwitchService vpnKillSwitchService = null,
        Extensions.IFastExtensionHandler fastExtensionHandler = null,
        Extensions.IExtensionManager extensionManager = null,
        IChokeManager chokeManager = null,
        Network.IProxySettingsProvider proxySettingsProvider = null,
        IDhKeyPool dhKeyPool = null,
        IMseSkeyRegistry mseSkeyRegistry = null,
        Extensions.IMetadataExchange metadataExchange = null,
        Extensions.ISyntheticMetadataGenerator syntheticMetadataGenerator = null,
        Extensions.IMagnetMetadataDownloader magnetMetadataDownloader = null,
        Extensions.IPeerExchange peerExchange = null,
        PiecePicker.PiecePicker piecePicker = null,
        IPieceStorage pieceStorage = null,
        Extensions.IPexService pexService = null,
        ISuperSeedingTracker superSeedingTracker = null)
    {
        _configService = configService;
        _torrentService = torrentService;
        _connectionManager = connectionManager;
        _peerDiscovery = peerDiscovery;
        _multiTracker = multiTracker;
        _trackerEntryService = trackerEntryService;
        _eventLogService = eventLogService;
        _trackerMetricService = trackerMetricService;
        _fastExtensionHandler = fastExtensionHandler ?? new Extensions.FastExtensionHandler();
        _extensionManager = extensionManager;
        _chokeManager = chokeManager;
        _proxySettingsProvider = proxySettingsProvider;
        _dhKeyPool = dhKeyPool;
        _mseSkeyRegistry = mseSkeyRegistry ?? new MseSkeyRegistry(_torrentService);
        _metadataExchange = metadataExchange ?? new Extensions.MetadataExchange();
        _syntheticMetadataGenerator = syntheticMetadataGenerator ?? new Extensions.SyntheticMetadataGenerator();
        _magnetMetadataDownloader = magnetMetadataDownloader;
        _peerExchange = peerExchange ?? new Extensions.PeerExchange(_configService);
        _pexService = pexService ?? new Extensions.PexService(_connectionManager, _peerExchange, _torrentService, _configService);
        _random = random ?? new RandomNumberGenerator();
        _rarestFirstPicker = new PiecePicker.RarestFirstPiecePicker(_random);
        _sequentialPicker = new PiecePicker.SequentialPiecePicker(_rarestFirstPicker, _random);
        _piecePicker = piecePicker ?? new PiecePicker.PiecePicker(_sequentialPicker, _rarestFirstPicker);
        _pieceStorage = pieceStorage;
        _superSeedingTracker = superSeedingTracker;
        _trackerAnnounceService = trackerAnnounceService ??
            (trackerEntryService != null && multiTracker != null && peerDiscovery != null && eventLogService != null && configService != null
                ? new Trackers.TrackerAnnounceService(trackerEntryService, multiTracker, peerDiscovery, eventLogService, configService, trackerMetricService)
                : null);
        _clientBehaviorSimulator = clientBehaviorSimulator;
        _utpManager = utpManager;
        _vpnKillSwitchService = vpnKillSwitchService;

        if (_fastExtensionHandler != null)
        {
            _fastExtensionHandler.OnRequestRejected += OnFastExtensionRequestRejected;
        }

        if (_chokeManager != null)
        {
            _chokeManager.PeerUnchoked += OnPeerUnchoked;
        }

        if (_utpManager != null)
        {
            _utpManager.OnConnectionAccepted += OnUtpConnectionAccepted;
        }

        var maxGlobal = configService.MaxGlobalConnections > 0 ? configService.MaxGlobalConnections : 200;
        var maxHalfOpen = configService.MaximumHalfOpenConnections > 0 ? configService.MaximumHalfOpenConnections : 50;

        _connectionSemaphore = new SemaphoreSlim(maxGlobal);
        _halfOpenSemaphore = new SemaphoreSlim(maxHalfOpen);
        _logger = LogManager.GetCurrentClassLogger();

        if (_vpnKillSwitchService != null)
        {
            _vpnKillSwitchService.VpnDropped += OnVpnDropped;
            _vpnKillSwitchService.VpnRestored += OnVpnRestored;
        }

        ListeningPort = configService.ListeningPort;
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

        if (HasDedicatedBindInterface())
        {
            StopListener();
        }
    }

    private void OnVpnRestored(string iface)
    {
        _logger.Info("VPN interface '{0}' restored. Rebinding peer listener socket.", iface);
        TriggerRebind();
    }

    public void Handle(VpnInterfaceRestoredEvent message)
    {
        OnVpnRestored(message?.InterfaceName);
    }

    public void Handle(VpnRestoredEvent message)
    {
        OnVpnRestored(message?.InterfaceName);
    }

    public void Handle(TorrentAddedEvent message)
    {
        if (!string.IsNullOrEmpty(message?.Torrent?.InfoHash))
        {
            _torrentCache[message.Torrent.InfoHash] = message.Torrent;
            _mseSkeyRegistry?.RegisterTorrent(message.Torrent);
        }
    }

    public void Handle(TorrentUpdatedEvent message)
    {
        if (!string.IsNullOrEmpty(message?.Torrent?.InfoHash))
        {
            _torrentCache[message.Torrent.InfoHash] = message.Torrent;
            _mseSkeyRegistry?.RegisterTorrent(message.Torrent);

            if (message.Torrent.Progress >= 1.0 || message.Torrent.Status == TorrentStatus.Seeding)
            {
                OnTorrentCompleted(message.Torrent);
            }
        }
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        OnTorrentCompleted(message?.Torrent);
    }

    public void Handle(TorrentFinishedEvent message)
    {
        OnTorrentCompleted(message?.Torrent);
    }

    public void Handle(TorrentStatusChangedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        if (message.NewStatus == TorrentStatus.Checking || message.NewStatus == TorrentStatus.QueuedForChecking)
        {
            _torrentCache[message.Torrent.InfoHash] = message.Torrent;
            ChokePeers(message.Torrent.InfoHash);
            return;
        }

        if (message.OldStatus == TorrentStatus.Checking || message.OldStatus == TorrentStatus.QueuedForChecking)
        {
            _torrentCache[message.Torrent.InfoHash] = message.Torrent;
            var connections = _connectionManager?.GetConnections(message.Torrent.InfoHash);
            if (connections != null)
            {
                foreach (var conn in connections)
                {
                    if (conn != null && conn.IsConnected)
                    {
                        SendInitialAvailability(conn, message.Torrent);
                    }
                }
            }
        }

        if (message.NewStatus == TorrentStatus.Seeding)
        {
            OnTorrentCompleted(message.Torrent);
        }
    }

    public void OnTorrentCompleted(Torrent torrent)
    {
        torrent ??= GetCachedTorrent(torrent?.InfoHash);
        if (torrent == null || string.IsNullOrEmpty(torrent.InfoHash))
        {
            return;
        }

        var connections = _connectionManager?.GetConnections(torrent.InfoHash);
        if (connections == null)
        {
            return;
        }

        foreach (var connection in connections)
        {
            if (connection != null && connection.AmInterested)
            {
                connection.AmInterested = false;
                connection.SendMessage(new PeerMessage { Type = PeerMessageType.NotInterested });
                _logger.Trace("Torrent {0} completed: sent NOT_INTERESTED to {1}:{2}", torrent.Name, connection.RemoteIp, connection.RemotePort);
            }
        }
    }

    public void ChokePeers(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return;
        }

        var connections = _connectionManager?.GetConnections(infoHash);
        if (connections == null)
        {
            return;
        }

        foreach (var connection in connections)
        {
            if (connection != null && !connection.AmChoking)
            {
                connection.AmChoking = true;
                connection.SendMessage(new PeerMessage { Type = PeerMessageType.Choke });
                _logger.Debug("Choked peer {0}:{1} on torrent {2}", connection.RemoteIp, connection.RemotePort, infoHash);
            }
        }
    }

    public void UnchokePeers(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return;
        }

        var connections = _connectionManager?.GetConnections(infoHash);
        if (connections == null)
        {
            return;
        }

        foreach (var connection in connections)
        {
            if (connection != null && connection.AmChoking)
            {
                connection.AmChoking = false;
                connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                _logger.Debug("Unchoked peer {0}:{1} on torrent {2}", connection.RemoteIp, connection.RemotePort, infoHash);

                var torrent = connection.MatchedTorrent ?? GetCachedTorrent(infoHash);
                if (torrent != null && torrent.SuperSeeding && connection.AssignedSuperSeedingPiece == null)
                {
                    AllocateAndRevealSuperSeedingPiece(connection, torrent);
                }
            }
        }
    }

    public void BroadcastPex(string infoHash = null)
    {
        if (_configService != null && (!_configService.EnablePex || !_configService.ExtensionUtPex))
        {
            return;
        }

        _pexService?.BroadcastPex(infoHash);
    }

    public void BroadcastLtDontHave(string infoHash, int pieceIndex)
    {
        if (string.IsNullOrEmpty(infoHash) || pieceIndex < 0)
        {
            return;
        }

        if (_configService != null && !_configService.ExtensionLtDontHave)
        {
            return;
        }

        var connections = _connectionManager?.GetConnections(infoHash);
        if (connections == null || connections.Count == 0)
        {
            return;
        }

        foreach (var conn in connections)
        {
            if (conn.SupportsLtDontHave)
            {
                try
                {
                    conn.SendLtDontHave(pieceIndex);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to broadcast lt_donthave for piece {0} to peer {1}:{2}", pieceIndex, conn.RemoteIp, conn.RemotePort);
                }
            }
        }
    }

    public void BroadcastLtDontHave(string infoHash, IEnumerable<int> pieceIndices)
    {
        if (string.IsNullOrEmpty(infoHash) || pieceIndices == null)
        {
            return;
        }

        foreach (var pieceIndex in pieceIndices)
        {
            BroadcastLtDontHave(infoHash, pieceIndex);
        }
    }

    public byte[] BuildPexMessage(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash) || (_configService != null && !_configService.EnablePex))
        {
            return Array.Empty<byte>();
        }

        var torrent = _torrentService?.GetByInfoHash(infoHash);
        var isPrivate = torrent?.IsPrivate == true;
        if (isPrivate)
        {
            return Array.Empty<byte>();
        }

        var connections = _connectionManager?.GetConnections(infoHash) ?? new List<PeerConnection>();
        var activeConnections = connections
            .Where(c => c.IsConnected && !string.IsNullOrWhiteSpace(c.RemoteIp) && c.RemotePort > 0)
            .ToList();

        var maxBatch = _configService?.PexMaxPeersPerMessage ?? 50;
        if (maxBatch <= 0)
        {
            maxBatch = 50;
        }

        var addedInfo = activeConnections.Take(maxBatch).Select(c =>
        {
            byte flags = 0;
            if (c.IsEncrypted)
            {
                flags |= 0x01;
            }

            if (c.IsSeed)
            {
                flags |= 0x02;
            }

            return new Extensions.PeerInfo
            {
                Ip = c.RemoteIp,
                Port = c.RemotePort,
                Flags = flags
            };
        }).ToList();

        return _peerExchange.BuildPexMessage(addedInfo, new List<Extensions.PeerInfo>(), isPrivate);
    }

    public void UpdateLocalInterest(PeerConnection connection, Torrent torrent)
    {
        torrent ??= connection?.MatchedTorrent ?? GetCachedTorrent(connection?.InfoHash);
        if (connection == null || torrent == null)
        {
            return;
        }

        bool[] verified = null;
        if (_pieceStorage != null && !string.IsNullOrEmpty(torrent.InfoHash))
        {
            verified = _pieceStorage.GetVerifiedPieces(torrent.InfoHash);
        }

        var isComplete = torrent.Progress >= 1.0 ||
            torrent.Status == TorrentStatus.Seeding ||
            (verified != null && torrent.PieceCount > 0 && verified.Length >= torrent.PieceCount && verified.Take(torrent.PieceCount).All(x => x));

        if (isComplete)
        {
            if (connection.AmInterested)
            {
                connection.AmInterested = false;
                connection.SendMessage(new PeerMessage { Type = PeerMessageType.NotInterested });
                _logger.Trace("Torrent {0} is complete: sent NOT_INTERESTED to {1}:{2}", torrent.Name, connection.RemoteIp, connection.RemotePort);
            }

            return;
        }

        if (connection.PeerPieces == null || torrent.PieceCount <= 0)
        {
            if (connection.AmInterested)
            {
                connection.AmInterested = false;
                connection.SendMessage(new PeerMessage { Type = PeerMessageType.NotInterested });
                _logger.Trace("Remote peer {0}:{1} has no pieces: sent NOT_INTERESTED", connection.RemoteIp, connection.RemotePort);
            }

            return;
        }

        var peerHasMissingPiece = false;
        var pieceCount = Math.Min(torrent.PieceCount, connection.PeerPieces.Length);

        for (var i = 0; i < pieceCount; i++)
        {
            if (!connection.PeerPieces[i])
            {
                continue;
            }

            var localHasPiece = LocalHasPiece(torrent, verified, i);
            if (!localHasPiece)
            {
                peerHasMissingPiece = true;
                break;
            }
        }

        if (peerHasMissingPiece)
        {
            if (!connection.AmInterested)
            {
                connection.AmInterested = true;
                connection.SendMessage(new PeerMessage { Type = PeerMessageType.Interested });
                _logger.Trace("Sent INTERESTED to {0}:{1}", connection.RemoteIp, connection.RemotePort);
            }
        }
        else
        {
            if (connection.AmInterested)
            {
                connection.AmInterested = false;
                connection.SendMessage(new PeerMessage { Type = PeerMessageType.NotInterested });
                _logger.Trace("Sent NOT_INTERESTED to {0}:{1}", connection.RemoteIp, connection.RemotePort);
            }
        }
    }

    private static bool LocalHasPiece(Torrent torrent, bool[] verified, int pieceIndex)
    {
        if (verified != null && pieceIndex < verified.Length)
        {
            return verified[pieceIndex];
        }

        if (torrent.Progress >= 1.0 || torrent.Status == TorrentStatus.Seeding)
        {
            return true;
        }

        if (torrent.Progress > 0.0 && torrent.PieceCount > 0)
        {
            var verifiedCount = (int)Math.Round(torrent.Progress * torrent.PieceCount);
            return pieceIndex < verifiedCount;
        }

        return false;
    }

    public void Handle(TorrentDeletedEvent message)
    {
        var infoHash = message?.Torrent?.InfoHash;
        if (!string.IsNullOrEmpty(infoHash))
        {
            _torrentCache.TryRemove(infoHash, out _);
            _swarmHistograms.TryRemove(infoHash, out _);
            _metadataCache.TryRemove(infoHash, out _);
            _peerDiscovery?.RemoveTorrent(infoHash);
            _mseSkeyRegistry?.UnregisterTorrent(infoHash);
        }
        else if (message?.TorrentId > 0)
        {
            foreach (var kvp in _torrentCache)
            {
                if (kvp.Value?.Id == message.TorrentId)
                {
                    _torrentCache.TryRemove(kvp.Key, out _);
                    _swarmHistograms.TryRemove(kvp.Key, out _);
                    _metadataCache.TryRemove(kvp.Key, out _);
                    _peerDiscovery?.RemoveTorrent(kvp.Key);
                    _mseSkeyRegistry?.UnregisterTorrent(kvp.Key);
                    break;
                }
            }
        }
    }

    public void Handle(PeerRequestRejectedEvent message)
    {
        if (message?.Connection != null)
        {
            _piecePicker?.OnBlockRejected(message.Connection, message.PieceIndex, message.Begin, message.Length);
        }
    }

    private void OnFastExtensionRequestRejected(PeerConnection connection, int pieceIndex, int begin, int length)
    {
        _piecePicker?.OnBlockRejected(connection, pieceIndex, begin, length);
    }

    public bool CanRequestBlock(PeerConnection connection, int pieceIndex)
    {
        if (connection == null)
        {
            return false;
        }

        if (connection.PendingRequestCount >= connection.MaxPipelinedRequests)
        {
            return false;
        }

        return !connection.PeerChoking || (connection.SupportsFastExtension && connection.RemoteAllowedFastPieces.Contains(pieceIndex));
    }

    public PiecePicker.PieceBlock RequestBlock(PeerConnection connection, int pieceIndex = -1)
    {
        var torrent = connection?.MatchedTorrent ?? GetCachedTorrent(connection?.InfoHash);
        var sequential = torrent?.SequentialDownload ?? false;
        var firstLast = torrent?.FirstLastPiecePrio ?? false;
        var pieceCount = torrent?.PieceCount ?? 0;
        return _piecePicker?.RequestBlock(connection, pieceIndex, sequential, firstLast, pieceCount);
    }

    public PiecePicker.PieceBlock RequestBlock(PeerConnection connection, int pieceIndex, bool sequential)
    {
        var torrent = connection?.MatchedTorrent ?? GetCachedTorrent(connection?.InfoHash);
        var firstLast = torrent?.FirstLastPiecePrio ?? false;
        var pieceCount = torrent?.PieceCount ?? 0;
        return _piecePicker?.RequestBlock(connection, pieceIndex, sequential, firstLast, pieceCount);
    }

    public void SetTorrentMetadata(string infoHash, byte[] metadata)
    {
        if (!string.IsNullOrWhiteSpace(infoHash) && metadata != null)
        {
            _metadataCache[infoHash] = metadata;
        }
    }

    public byte[] GetTorrentMetadata(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        if (_metadataCache.TryGetValue(infoHash, out var cached))
        {
            return cached;
        }

        var torrent = GetCachedTorrent(infoHash);
        if (torrent != null && !string.IsNullOrWhiteSpace(torrent.SourcePath) && File.Exists(torrent.SourcePath))
        {
            try
            {
                var fileBytes = File.ReadAllBytes(torrent.SourcePath);
                if (TorrentFileParser.TryExtractRawInfoBytes(fileBytes, out var rawInfoBytes))
                {
                    var metadata = rawInfoBytes.ToArray();
                    _metadataCache[infoHash] = metadata;
                    return metadata;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to extract metadata from {0}", torrent.SourcePath);
            }
        }

        if (torrent != null && _syntheticMetadataGenerator != null)
        {
            try
            {
                var synthetic = _syntheticMetadataGenerator.GenerateMetadataBytes(torrent);
                if (synthetic != null && synthetic.Length > 0)
                {
                    _metadataCache[infoHash] = synthetic;
                    return synthetic;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to generate synthetic metadata for {0}", infoHash);
            }
        }

        return null;
    }

    public override void Dispose()
    {
        if (_vpnKillSwitchService != null)
        {
            _vpnKillSwitchService.VpnDropped -= OnVpnDropped;
            _vpnKillSwitchService.VpnRestored -= OnVpnRestored;
        }

        if (_fastExtensionHandler != null)
        {
            _fastExtensionHandler.OnRequestRejected -= OnFastExtensionRequestRejected;
        }

        if (_utpManager != null)
        {
            _utpManager.OnConnectionAccepted -= OnUtpConnectionAccepted;
        }

        try
        {
            _serverLifecycleCts.Cancel();
            _serverLifecycleCts.Dispose();
        }
        catch
        {
        }

        StopListener();
        _connectionsPerIp.Clear();
        _rebindSignal?.Dispose();
        _connectionSemaphore?.Dispose();
        _halfOpenSemaphore?.Dispose();
        base.Dispose();
    }

    public void StopListening()
    {
        StopListener();
    }

    private void StopListener()
    {
        lock (_listenerLock)
        {
            IsListening = false;

            if (_listenerCts != null && !_listenerCts.IsCancellationRequested)
            {
                try
                {
                    _listenerCts.Cancel();
                }
                catch
                {
                }
            }

            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                }
                catch
                {
                }

                _listener = null;
            }
        }
    }

    private void TriggerRebind()
    {
        StopListener();

        try
        {
            if (_rebindSignal.CurrentCount == 0)
            {
                _rebindSignal.Release();
            }
        }
        catch
        {
        }
    }

    private async Task WaitUntilRestoredOrCancelledAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _rebindSignal.WaitAsync(TimeSpan.FromSeconds(2), stoppingToken);
        }
        catch (ObjectDisposedException)
        {
            stoppingToken.ThrowIfCancellationRequested();
        }
    }

    private bool HasDedicatedBindInterface()
    {
        var bindIface = _configService.BindInterface?.Trim();
        return !string.IsNullOrWhiteSpace(bindIface) &&
            !bindIface.Equals("Any", StringComparison.OrdinalIgnoreCase) &&
            !bindIface.Equals("all", StringComparison.OrdinalIgnoreCase) &&
            !bindIface.Equals("*", StringComparison.OrdinalIgnoreCase) &&
            !bindIface.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) &&
            !bindIface.Equals("::", StringComparison.OrdinalIgnoreCase);
    }

    private IPAddress ResolveDedicatedBindIp()
    {
        if (_vpnKillSwitchService != null)
        {
            if (_vpnKillSwitchService.IsFailClosedActive)
            {
                return null;
            }

            if (_configService.EnableIPv6)
            {
                var ip6 = _vpnKillSwitchService.GetVpnInterfaceIpAddress(AddressFamily.InterNetworkV6);
                if (ip6 != null)
                {
                    return ip6;
                }
            }

            var ip4 = _vpnKillSwitchService.GetVpnInterfaceIpAddress(AddressFamily.InterNetwork);
            if (ip4 != null)
            {
                return ip4;
            }
        }

        var bindIface = _configService.BindInterface?.Trim();
        if (string.IsNullOrWhiteSpace(bindIface))
        {
            return null;
        }

        if (IPAddress.TryParse(bindIface, out var parsed))
        {
            if (!parsed.Equals(IPAddress.Any) && !parsed.Equals(IPAddress.IPv6Any))
            {
                return parsed;
            }

            return null;
        }

        return ResolveInterfaceIpFromName(bindIface);
    }

    private IPAddress ResolveInterfaceIpFromName(string interfaceName)
    {
        try
        {
            var family = _configService.EnableIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            var nic = nics.FirstOrDefault(n =>
                string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.Id, interfaceName, StringComparison.OrdinalIgnoreCase));

            if (nic == null || nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
            {
                return null;
            }

            var unicast = nic.GetIPProperties()?.UnicastAddresses;
            if (unicast == null)
            {
                return null;
            }

            if (_configService.EnableIPv6)
            {
                var ip6 = unicast.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                    !IPAddress.IsLoopback(a.Address) &&
                    !a.Address.Equals(IPAddress.IPv6Any) &&
                    !a.Address.Equals(IPAddress.IPv6None) &&
                    !a.Address.IsIPv6LinkLocal &&
                    !a.Address.IsIPv6SiteLocal &&
                    !a.Address.IsIPv6Multicast)?.Address;

                if (ip6 != null)
                {
                    return ip6;
                }
            }

            var ip4 = unicast.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(a.Address) &&
                !a.Address.Equals(IPAddress.Any) &&
                !a.Address.Equals(IPAddress.None))?.Address;

            return ip4;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to resolve IP for interface '{0}'", interfaceName);
            return null;
        }
    }

    private IPAddress GetBindAddress()
    {
        if (HasDedicatedBindInterface())
        {
            var ip = ResolveDedicatedBindIp();
            if (ip == null)
            {
                _logger.Warn("Configured bind interface '{0}' is not available or not yet plumbed.", _configService.BindInterface?.Trim());
            }

            return ip;
        }

        return _configService.EnableIPv6 ? IPAddress.IPv6Any : IPAddress.Any;
    }

    private IPAddress GetListenAddress() => GetBindAddress();

    private EncryptionMode GetEncryptionMode()
    {
        return _configService.EncryptionMode?.ToLowerInvariant() switch
        {
            "required" or "forced" => EncryptionMode.RequireEncrypted,
            "disabled" or "plain" or "none" => EncryptionMode.PreferPlainText,
            _ => EncryptionMode.PreferEncrypted
        };
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
        using var registration = stoppingToken.Register(() =>
        {
            try
            {
                _serverLifecycleCts.Cancel();
            }
            catch
            {
            }
        });

        var listenerTask = RunListenerAsync(stoppingToken);
        var contactTask = RunPeerContactLoopAsync(stoppingToken);
        var pexTask = RunPexLoopAsync(stoppingToken);
        await Task.WhenAll(listenerTask, contactTask, pexTask);
    }

    private async Task RunListenerAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var bindAddress = GetBindAddress();
            if (bindAddress == null)
            {
                var configuredIface = _configService.BindInterface?.Trim();
                _logger.Warn(
                    "Configured bind interface '{0}' is not available or unplumbed. Deferring peer listener binding.",
                    configuredIface);

                try
                {
                    await WaitUntilRestoredOrCancelledAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            var listeningPort = _configService.ListeningPort;
            ListeningPort = listeningPort;
            TcpListener listener;

            try
            {
                listener = new TcpListener(bindAddress, listeningPort);
                if (bindAddress.Equals(IPAddress.IPv6Any))
                {
                    listener.Server.DualMode = true;
                }

                listener.Server.BindToNetworkInterface(_configService.BindInterface);
                listener.Start();
            }
            catch (SocketException ex)
            {
                if (HasDedicatedBindInterface())
                {
                    if (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    {
                        _logger.Warn(
                            ex,
                            "Peer server failed to bind configured interface {0}:{1}: Port is already in use.",
                            bindAddress,
                            listeningPort);
                        BindFailed = true;
                        IsListening = false;
                        BindErrorMessage = ex.Message;
                        return;
                    }

                    _logger.Warn(
                        ex,
                        "Peer server failed to bind configured interface {0}:{1}. Deferring listener binding.",
                        bindAddress,
                        listeningPort);

                    try
                    {
                        await WaitUntilRestoredOrCancelledAsync(stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    continue;
                }

                _logger.Warn(
                    ex,
                    "Peer server failed to bind {0}:{1}, attempting fallback to IPv4 Any",
                    bindAddress,
                    listeningPort);

                try
                {
                    listener = new TcpListener(IPAddress.Any, listeningPort);
                    listener.Server.BindToNetworkInterface(_configService.BindInterface);
                    listener.Start();
                }
                catch (Exception fallbackEx)
                {
                    _logger.Warn(
                        fallbackEx,
                        "Peer server failed fallback bind on port {0}, skipping",
                        listeningPort);
                    BindFailed = true;
                    IsListening = false;
                    BindErrorMessage = fallbackEx.Message;
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(
                    ex,
                    "Peer server failed to bind on port {0}, skipping",
                    listeningPort);
                BindFailed = true;
                IsListening = false;
                BindErrorMessage = ex.Message;
                return;
            }

            BindFailed = false;
            IsListening = true;
            BindErrorMessage = null;
            _logger.Info("Peer server listening on {0}:{1}", bindAddress, listeningPort);

            CancellationToken linkedToken;
            lock (_listenerLock)
            {
                _listener = listener;
                _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                linkedToken = _listenerCts.Token;
            }

            try
            {
                while (!linkedToken.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(linkedToken);

                    if (_vpnKillSwitchService?.IsFailClosedActive == true)
                    {
                        _logger.Debug("VPN fail-closed engaged; rejecting incoming peer connection");
                        client.Dispose();
                        continue;
                    }

                    _ = Task.Run(
                        async () =>
                        {
                            await ProcessIncomingClientAsync(client, stoppingToken);
                        },
                        stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stoppingToken is cancelled or _listenerCts cancelled for rebind
            }
            catch (SocketException)
            {
                // Expected when listener is stopped/closed
            }
            catch (ObjectDisposedException)
            {
                // Expected when listener is stopped/disposed
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.Debug(ex, "Exception in peer server listener loop");
            }
            finally
            {
                lock (_listenerLock)
                {
                    try
                    {
                        listener.Stop();
                    }
                    catch
                    {
                    }

                    if (_listener == listener)
                    {
                        _listener = null;
                    }

                    _listenerCts?.Dispose();
                    _listenerCts = null;
                    IsListening = false;
                }
            }
        }
    }

    private async Task ProcessIncomingClientAsync(TcpClient client, CancellationToken stoppingToken)
    {
        string clientIp = null;
        var ipReserved = false;
        var acquiredConnectionSemaphore = false;
        var acquiredHalfOpenSemaphore = false;

        try
        {
            if (client?.Client?.RemoteEndPoint is not IPEndPoint endpoint)
            {
                client?.Dispose();
                return;
            }

            clientIp = endpoint.Address.ToString();

            if (_connectionManager?.IsPeerBanned(clientIp) == true)
            {
                _logger.Debug("PeerServer: rejected incoming connection from banned peer {0}", clientIp);
                client.Dispose();
                return;
            }

            var maxPerIp = _configService.MaxConnectionsPerIp > 0 ? _configService.MaxConnectionsPerIp : 5;
            var currentCount = _connectionsPerIp.AddOrUpdate(clientIp, 1, (_, count) => count + 1);
            ipReserved = true;

            if (currentCount > maxPerIp)
            {
                client.Dispose();
                return;
            }

            ApplySocketQos(client.Client);

            try
            {
                if (client.Client != null)
                {
                    client.Client.ReceiveTimeout = UnauthenticatedHandshakeTimeoutMs;
                }
            }
            catch
            {
            }

            try
            {
                if (!await _halfOpenSemaphore.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken))
                {
                    client.Dispose();
                    return;
                }

                acquiredHalfOpenSemaphore = true;
            }
            catch
            {
                client.Dispose();
                return;
            }

            try
            {
                if (!await _connectionSemaphore.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken))
                {
                    client.Dispose();
                    return;
                }

                acquiredConnectionSemaphore = true;
            }
            catch
            {
                client.Dispose();
                return;
            }

            var halfOpenReleased = 0;
            void ReleaseHalfOpen()
            {
                if (Interlocked.Exchange(ref halfOpenReleased, 1) == 0)
                {
                    if (acquiredHalfOpenSemaphore)
                    {
                        _halfOpenSemaphore.Release();
                        acquiredHalfOpenSemaphore = false;
                    }
                }
            }

            try
            {
                await HandleConnectionAsync(client, stoppingToken, ReleaseHalfOpen);
            }
            finally
            {
                ReleaseHalfOpen();
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to handle incoming peer connection from {0}", clientIp ?? "unknown");
            client?.Dispose();
        }
        finally
        {
            if (acquiredHalfOpenSemaphore)
            {
                _halfOpenSemaphore.Release();
            }

            if (acquiredConnectionSemaphore)
            {
                _connectionSemaphore.Release();
            }

            if (ipReserved && clientIp != null)
            {
                DecrementConnectionCount(clientIp);
            }
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

                _peerDiscovery?.PruneStalePeers();

                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunPexLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            var interval = _configService?.PexInterval ?? 60;
            if (interval <= 0)
            {
                interval = 60;
            }

            await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_vpnKillSwitchService?.IsFailClosedActive == true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                if (_configService == null || (_configService.EnablePex && _configService.ExtensionUtPex))
                {
                    BroadcastPex();
                }

                var currentInterval = _configService?.PexInterval ?? 60;
                if (currentInterval <= 0)
                {
                    currentInterval = 60;
                }

                await Task.Delay(TimeSpan.FromSeconds(currentInterval), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in PeerServer PEX broadcast loop");
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

            if (!_connectionManager.TryReserveSlot(torrent.InfoHash, isInbound: false, out var reservation))
            {
                break;
            }

            var endpointKey = $"{candidate.Ip}:{candidate.Port}";
            if (!_inFlightOutgoingEndpoints.TryAdd(endpointKey, 0))
            {
                reservation.Dispose();
                continue;
            }

            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await ConnectToPeerAsync(torrent, candidate, stoppingToken, reservation);
                    }
                    finally
                    {
                        _inFlightOutgoingEndpoints.TryRemove(endpointKey, out _);
                    }
                },
                stoppingToken);
        }
    }

    private PeerConnection CreateOutgoingPeerConnection(DiscoveredPeer candidate, IPAddress localBind)
    {
        PeerConnection connection = null;

        if (_utpManager != null && _utpManager.IsEnabled)
        {
            try
            {
                var utp = _utpManager.CreateConnection();
                var endpoint = new IPEndPoint(IPAddress.Parse(candidate.Ip), candidate.Port);
                utp.Connect(endpoint);
                if (utp.IsConnected)
                {
                    connection = new PeerConnection(utp.GetStream(), candidate.Ip, candidate.Port, _dhKeyPool);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "uTP connection attempt to {0}:{1} failed", candidate.Ip, candidate.Port);
            }

            if (connection == null && _utpManager.TcpFallbackEnabled)
            {
                _logger.Debug("Falling back to TCP for peer {0}:{1}", candidate.Ip, candidate.Port);
                connection = new PeerConnection(candidate.Ip, candidate.Port, localBind, _configService.PeerDscp, _configService.PeerTos, _proxySettingsProvider, _dhKeyPool, _configService.BindInterface);
            }
        }
        else
        {
            connection = new PeerConnection(candidate.Ip, candidate.Port, localBind, _configService.PeerDscp, _configService.PeerTos, _proxySettingsProvider, _dhKeyPool, _configService.BindInterface);
        }

        if (connection != null)
        {
            connection.DiscoverySource = candidate.Source;
            connection.IsInbound = false;
            connection.HandshakeTimeoutMs = Math.Min(_configService.HandshakeTimeoutSeconds * 1000, OutgoingConnectTimeoutMs);
            connection.MessageReadTimeoutMs = _configService.MessageReadTimeoutSeconds * 1000;
            connection.KeepAliveIntervalSeconds = _configService.KeepAliveIntervalSeconds;
            connection.MaxPipelinedRequests = _configService.PeerRequestCount;
            connection.IdleChance = _clientBehaviorSimulator != null
                ? _clientBehaviorSimulator.GetEffectiveIdleChance(_configService.PeerIdleChance)
                : _configService.PeerIdleChance;
        }

        return connection;
    }

    private Task ConnectToPeer(Torrent torrent, DiscoveredPeer candidate)
    {
        return ConnectToPeerAsync(torrent, candidate, CancellationToken.None);
    }

    private async Task ConnectToPeerAsync(
        Torrent torrent,
        DiscoveredPeer candidate,
        CancellationToken stoppingToken,
        IConnectionReservation reservation = null)
    {
        await Task.Yield();
        using var reservationScope = reservation;

        if (_vpnKillSwitchService?.IsFailClosedActive == true)
        {
            return;
        }

        if (HasDedicatedBindInterface() && GetBindAddress() == null)
        {
            _logger.Debug(
                "Dedicated bind interface '{0}' is unplumbed or unavailable; suppressing outgoing connection to {1}:{2}",
                _configService.BindInterface,
                candidate.Ip,
                candidate.Port);
            _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
            return;
        }

        if (!IPAddress.TryParse(candidate.Ip, out var candIp))
        {
            _logger.Debug("Skipping invalid IP candidate '{0}'", candidate.Ip);
            _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
            return;
        }

        if (_connectionManager?.IsPeerBanned(candidate.Ip) == true)
        {
            _logger.Debug("PeerServer: rejected outgoing connection to banned peer {0}:{1}", candidate.Ip, candidate.Port);
            _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
            return;
        }

        var listeningPort = _configService?.ListeningPort ?? 0;
        if (listeningPort > 0 && candidate.Port == listeningPort &&
            (IPAddress.IsLoopback(candIp) || Lpd.LocalPeerDiscovery.IsLocalAddress(candIp)))
        {
            _logger.Debug("PeerServer: rejected self-connection to {0}:{1}", candidate.Ip, candidate.Port);
            _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
            return;
        }

        if (!_configService.EnableIPv6 && candIp.AddressFamily == AddressFamily.InterNetworkV6)
        {
            _logger.Debug("IPv6 disabled, skipping IPv6 peer {0}:{1}", candidate.Ip, candidate.Port);
            return;
        }

        var maxPerIp = _configService.MaxConnectionsPerIp > 0 ? _configService.MaxConnectionsPerIp : 5;
        var currentCount = _connectionsPerIp.AddOrUpdate(candidate.Ip, 1, (_, count) => count + 1);
        if (currentCount > maxPerIp)
        {
            DecrementConnectionCount(candidate.Ip);
            return;
        }

        var acquiredHalfOpen = false;
        PeerConnection connection = null;
        try
        {
            try
            {
                if (!await _halfOpenSemaphore.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken))
                {
                    return;
                }

                acquiredHalfOpen = true;
            }
            catch
            {
                return;
            }

            var addedToConnectionManager = false;
            var connectionEstablished = false;
            try
            {
                _logger.Debug("Connecting to peer {0}:{1} for {2}", candidate.Ip, candidate.Port, torrent.Name);
                _eventLogService?.Debug(torrent.Id, "Peers", $"Attempting connection to peer {candidate.Ip}:{candidate.Port} (source: {candidate.Source})");

                var localBind = _vpnKillSwitchService?.GetVpnInterfaceIpAddress();
                if (localBind == null && HasDedicatedBindInterface())
                {
                    localBind = ResolveDedicatedBindIp();
                }

                if (HasDedicatedBindInterface() && localBind == null)
                {
                    _logger.Warn(
                        "Dedicated bind interface '{0}' is configured but unplumbed/unavailable. Failing closed on outgoing connection to {1}:{2}",
                        _configService.BindInterface,
                        candidate.Ip,
                        candidate.Port);
                    _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
                    return;
                }

                connection = CreateOutgoingPeerConnection(candidate, localBind);

                if (connection == null)
                {
                    _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
                    return;
                }

                if (!string.Equals(_configService.EncryptionMode, "disabled", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(_configService.EncryptionMode, "plain", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(_configService.EncryptionMode, "none", StringComparison.OrdinalIgnoreCase))
                {
                    if (!await connection.NegotiateEncryptionOutgoingAsync(torrent.InfoHash, GetEncryptionMode(), stoppingToken))
                    {
                        if (GetEncryptionMode() == EncryptionMode.RequireEncrypted)
                        {
                            _logger.Debug("Outgoing encryption failed to {0}:{1} in RequireEncrypted mode", candidate.Ip, candidate.Port);
                            _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
                            _eventLogService?.Debug(torrent.Id, "Peers", $"Encryption negotiation rejected by peer {candidate.Ip}:{candidate.Port}");
                            connection.Dispose();
                            return;
                        }

                        _logger.Debug("Outgoing encryption failed to {0}:{1}; falling back to plaintext", candidate.Ip, candidate.Port);
                        connection.Dispose();
                        connection = null;

                        try
                        {
                            connection = CreateOutgoingPeerConnection(candidate, localBind);
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug(ex, "Plaintext fallback connection to {0}:{1} failed", candidate.Ip, candidate.Port);
                        }

                        if (connection == null)
                        {
                            _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
                            return;
                        }

                        if (stoppingToken.IsCancellationRequested)
                        {
                            connection.Dispose();
                            return;
                        }

                        connection.EncryptionMethod = CryptoMethod.PlainText;
                    }
                }
                else
                {
                    connection.EncryptionMethod = CryptoMethod.PlainText;
                }

                var session = (_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled && !_configService.AnonymousMode)
                    ? _clientBehaviorSimulator.GetOrCreateSession(torrent.InfoHash, torrent.IsPrivate)
                    : null;
                var profile = session?.Profile ?? ((_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled && !_configService.AnonymousMode)
                    ? _clientBehaviorSimulator.GetProfileForTorrent(torrent.InfoHash, torrent.IsPrivate)
                    : null);
                var peerId = session?.PeerId ?? profile?.GeneratePeerId() ?? "-SD1000-000000000000";
                var supportsFast = _configService?.ExtensionFastExtension ?? true;
                var supportsDht = _configService?.EnableDht ?? true;
                connection.SendHandshake(torrent.InfoHash, peerId, torrent.IsPrivate, profile, supportsExtensions: true, supportsFast: supportsFast, supportsDht: supportsDht);

                if (!connection.ReceiveHandshake())
                {
                    _logger.Debug("Outgoing handshake failed from {0}:{1}", candidate.Ip, candidate.Port);
                    _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
                    _eventLogService?.Debug(torrent.Id, "Peers", $"BitTorrent handshake rejected/timed out from {candidate.Ip}:{candidate.Port}");
                    connection.Dispose();
                    return;
                }

                if (!string.Equals(connection.InfoHash, torrent.InfoHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Warn(
                        "Peer {0}:{1} returned info-hash {2}, expected {3}; closing connection",
                        candidate.Ip,
                        candidate.Port,
                        connection.InfoHash,
                        torrent.InfoHash);
                    _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
                    connection.Dispose();
                    return;
                }

                if (connection.SupportsFastExtension && _fastExtensionHandler != null && !string.IsNullOrEmpty(torrent.InfoHash))
                {
                    _fastExtensionHandler.RegisterFastPeer(connection, Convert.FromHexString(torrent.InfoHash), torrent.PieceCount, 10);
                }

                SendExtensionHandshake(connection, torrent, profile);

                SendInitialAvailability(connection, torrent);

                _chokeManager?.PeerConnected(connection);
                if (_chokeManager != null)
                {
                    if (_chokeManager.CanUnchoke(connection))
                    {
                        connection.AmChoking = false;
                        connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                    }
                }
                else
                {
                    connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                    connection.AmChoking = false;
                }

                if (!connection.AmChoking && torrent.SuperSeeding && connection.AssignedSuperSeedingPiece == null)
                {
                    AllocateAndRevealSuperSeedingPiece(connection, torrent);
                }

                if (reservation != null)
                {
                    if (!_connectionManager.TryAdd(connection, reservation))
                    {
                        _logger.Debug("Failed to add outgoing connection for {0}", connection.InfoHash);
                        connection.Dispose();
                        return;
                    }
                }
                else
                {
                    _connectionManager.Add(connection);
                }

                addedToConnectionManager = true;

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

                connectionEstablished = true;
            }
            finally
            {
                if (acquiredHalfOpen)
                {
                    _halfOpenSemaphore.Release();
                    acquiredHalfOpen = false;
                }
            }

            if (connectionEstablished)
            {
                HandlePeerSession(connection, torrent);
            }
            else if (addedToConnectionManager)
            {
                _connectionManager.Remove(connection);
            }
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
            if (acquiredHalfOpen)
            {
                _halfOpenSemaphore.Release();
            }

            DecrementConnectionCount(candidate.Ip);
        }
    }

    private void HandlePeerSession(PeerConnection connection, Torrent torrent)
    {
        try
        {
            if (torrent != null && !string.IsNullOrEmpty(torrent.InfoHash))
            {
                _torrentCache[torrent.InfoHash] = torrent;
            }

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

                HandleMessage(connection, message, torrent);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Peer session ended with {0}:{1}", connection.RemoteIp, connection.RemotePort);
        }
        finally
        {
            _fastExtensionHandler?.UnregisterPeer(connection);
            _chokeManager?.PeerDisconnected(connection);
            _connectionManager.Remove(connection);
            var sessionInfoHash = connection.MatchedTorrent?.InfoHash ?? connection.InfoHash;
            if (!string.IsNullOrEmpty(sessionInfoHash))
            {
                var tracker = GetSuperSeedingTracker(sessionInfoHash);
                tracker?.OnPeerDisconnected(GetPeerKey(connection));
            }

            if (connection.PeerPieces != null)
            {
                var infoHash = connection.MatchedTorrent?.InfoHash ?? connection.InfoHash;
                if (!string.IsNullOrEmpty(infoHash) && _swarmHistograms.TryGetValue(infoHash, out var histogram))
                {
                    histogram.UnregisterPeer(connection.PeerPieces);
                }

                connection.PeerPieces = null;
            }

            connection.PendingRequestCount = 0;
            connection.Dispose();
        }
    }

    private void OnUtpConnectionAccepted(Transport.IUtpConnection conn)
    {
        if (conn == null)
        {
            return;
        }

        try
        {
            var token = _serverLifecycleCts.Token;
            if (token.IsCancellationRequested)
            {
                conn.Dispose();
                return;
            }

            _ = Task.Run(
                async () =>
                {
                    await ProcessIncomingUtpConnectionAsync(conn, token);
                },
                token);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to dispatch inbound uTP connection");
            conn.Dispose();
        }
    }

    internal async Task ProcessIncomingUtpConnectionAsync(Transport.IUtpConnection conn, CancellationToken stoppingToken = default)
    {
        string clientIp = null;
        var ipReserved = false;
        var acquiredConnectionSemaphore = false;
        var acquiredHalfOpenSemaphore = false;

        try
        {
            if (conn?.RemoteEndPoint == null || !conn.IsConnected)
            {
                conn?.Dispose();
                return;
            }

            if (_vpnKillSwitchService?.IsFailClosedActive == true)
            {
                _logger.Debug("VPN fail-closed engaged; rejecting incoming uTP peer connection");
                conn.Dispose();
                return;
            }

            var endpoint = conn.RemoteEndPoint;
            clientIp = endpoint.Address.ToString();
            var maxPerIp = _configService.MaxConnectionsPerIp > 0 ? _configService.MaxConnectionsPerIp : 5;
            var currentCount = _connectionsPerIp.AddOrUpdate(clientIp, 1, (_, count) => count + 1);
            ipReserved = true;

            if (currentCount > maxPerIp)
            {
                _logger.Debug("Maximum connections per IP exceeded for {0}; rejecting incoming uTP peer", clientIp);
                conn.Dispose();
                return;
            }

            try
            {
                if (!await _halfOpenSemaphore.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken))
                {
                    _logger.Debug("Half-open connection quota exceeded; rejecting incoming uTP peer {0}", clientIp);
                    conn.Dispose();
                    return;
                }

                acquiredHalfOpenSemaphore = true;
            }
            catch
            {
                conn.Dispose();
                return;
            }

            try
            {
                if (!await _connectionSemaphore.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken))
                {
                    _logger.Debug("Global connection quota exceeded; rejecting incoming uTP peer {0}", clientIp);
                    conn.Dispose();
                    return;
                }

                acquiredConnectionSemaphore = true;
            }
            catch
            {
                conn.Dispose();
                return;
            }

            var halfOpenReleased = 0;
            void ReleaseHalfOpen()
            {
                if (Interlocked.Exchange(ref halfOpenReleased, 1) == 0)
                {
                    if (acquiredHalfOpenSemaphore)
                    {
                        _halfOpenSemaphore.Release();
                        acquiredHalfOpenSemaphore = false;
                    }
                }
            }

            try
            {
                var stream = conn.GetStream();
                await HandleInboundPeerStreamAsync(stream, endpoint, stoppingToken, ReleaseHalfOpen);
            }
            finally
            {
                ReleaseHalfOpen();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to handle incoming uTP peer connection from {0}", clientIp ?? "unknown");
            conn?.Dispose();
        }
        finally
        {
            if (acquiredHalfOpenSemaphore)
            {
                _halfOpenSemaphore.Release();
            }

            if (acquiredConnectionSemaphore)
            {
                _connectionSemaphore.Release();
            }

            if (ipReserved && clientIp != null)
            {
                DecrementConnectionCount(clientIp);
            }
        }
    }

    internal async Task HandleInboundPeerStreamAsync(
        Stream stream,
        IPEndPoint remoteEndPoint,
        CancellationToken stoppingToken = default,
        Action onHandshakeSuccess = null)
    {
        if (stream == null || remoteEndPoint == null)
        {
            if (stream != null)
            {
                await stream.DisposeAsync();
            }

            return;
        }

        using var connection = new PeerConnection(stream, remoteEndPoint.Address.ToString(), remoteEndPoint.Port, _dhKeyPool)
        {
            IsInbound = true,
            DiscoverySource = "inbound"
        };
        await HandleInboundPeerConnectionAsync(connection, stoppingToken, onHandshakeSuccess);
    }

    private void HandleConnection(TcpClient client, CancellationToken stoppingToken)
    {
        HandleConnection(client, stoppingToken, null);
    }

    private void HandleConnection(TcpClient client, CancellationToken stoppingToken, Action onHandshakeSuccess)
    {
        HandleConnectionAsync(client, stoppingToken, onHandshakeSuccess).GetAwaiter().GetResult();
    }

    private void HandleConnection(Transport.IUtpConnection conn, CancellationToken stoppingToken)
    {
        HandleConnection(conn, stoppingToken, null);
    }

    private void HandleConnection(Transport.IUtpConnection conn, CancellationToken stoppingToken, Action onHandshakeSuccess)
    {
        if (conn?.RemoteEndPoint == null)
        {
            conn?.Dispose();
            return;
        }

        var stream = conn.GetStream();
        HandleInboundPeerStreamAsync(stream, conn.RemoteEndPoint, stoppingToken, onHandshakeSuccess).GetAwaiter().GetResult();
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken stoppingToken, Action onHandshakeSuccess = null)
    {
        using var connection = new PeerConnection(client, _dhKeyPool)
        {
            IsInbound = true,
            DiscoverySource = "inbound"
        };

        if (client.Client != null)
        {
            try
            {
                client.Client.ReceiveTimeout = UnauthenticatedHandshakeTimeoutMs;
            }
            catch
            {
            }
        }

        await HandleInboundPeerConnectionAsync(
            connection,
            stoppingToken,
            onHandshakeSuccess,
            () =>
            {
                if (client.Client != null)
                {
                    try
                    {
                        client.Client.ReceiveTimeout = connection.MessageReadTimeoutMs > 0 ? connection.MessageReadTimeoutMs : 0;
                    }
                    catch
                    {
                    }
                }
            });
    }

    private async Task HandleInboundPeerConnectionAsync(
        PeerConnection connection,
        CancellationToken stoppingToken,
        Action onHandshakeSuccess = null,
        Action onPostHandshakeTimeout = null)
    {
        connection.HandshakeTimeoutMs = UnauthenticatedHandshakeTimeoutMs;
        connection.MessageReadTimeoutMs = _configService.MessageReadTimeoutSeconds * 1000;
        connection.KeepAliveIntervalSeconds = _configService.KeepAliveIntervalSeconds;
        connection.MaxPipelinedRequests = _configService.PeerRequestCount;
        connection.IdleChance = _clientBehaviorSimulator != null
            ? _clientBehaviorSimulator.GetEffectiveIdleChance(_configService.PeerIdleChance)
            : _configService.PeerIdleChance;

        _logger.Debug("Incoming peer: {0}:{1}", connection.RemoteIp, connection.RemotePort);

        var addedToConnectionManager = false;
        try
        {
            var negotiated = _mseSkeyRegistry != null
                ? await connection.NegotiateEncryptionIncomingAsync(_mseSkeyRegistry, GetEncryptionMode(), stoppingToken)
                : await connection.NegotiateEncryptionIncomingAsync(hash => ValidateInfoHash(hash, connection), GetEncryptionMode(), stoppingToken);
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

            if (connection.MatchedTorrent != null &&
                !string.IsNullOrEmpty(connection.InfoHash) &&
                !string.Equals(connection.MatchedTorrent.InfoHash, connection.InfoHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug("MSE SKEY info hash mismatch with BitTorrent handshake from {0}", connection.RemoteIp);
                return;
            }

            var torrent = connection.MatchedTorrent ?? GetCachedTorrent(connection.InfoHash);

            if (torrent == null)
            {
                _logger.Debug("Unknown info hash from {0}: {1}", connection.RemoteIp, connection.InfoHash);
                connection.Dispose();
                return;
            }

            if (!_connectionManager.TryReserveSlot(connection.InfoHash, isInbound: true, out var reservation))
            {
                _logger.Debug("Inbound connection rejected: quota exceeded for {0}", connection.InfoHash);
                return;
            }

            using (reservation)
            {
                if (!_connectionManager.TryAdd(connection, reservation))
                {
                    _logger.Debug("Failed to add inbound connection for {0}", connection.InfoHash);
                    return;
                }

                addedToConnectionManager = true;

                if (!string.IsNullOrEmpty(torrent.InfoHash))
                {
                    _torrentCache[torrent.InfoHash] = torrent;
                }

                onHandshakeSuccess?.Invoke();

                onPostHandshakeTimeout?.Invoke();

                var session = (_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled && !_configService.AnonymousMode)
                    ? _clientBehaviorSimulator.GetOrCreateSession(torrent.InfoHash, torrent.IsPrivate)
                    : null;
                var profile = session?.Profile ?? ((_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled && !_configService.AnonymousMode)
                    ? _clientBehaviorSimulator.GetProfileForTorrent(torrent.InfoHash, torrent.IsPrivate)
                    : null);
                var peerId = session?.PeerId ?? profile?.GeneratePeerId() ?? "-SD1000-000000000000";
                var supportsFast = _configService?.ExtensionFastExtension ?? true;
                var supportsDht = _configService?.EnableDht ?? true;
                connection.SendHandshake(torrent.InfoHash, peerId, torrent.IsPrivate, profile, supportsExtensions: true, supportsFast: supportsFast, supportsDht: supportsDht);

                _logger.Debug(
                    "Peer {0} connected (encrypted: {1}, method: {2})",
                    connection.RemoteIp,
                    connection.IsEncrypted,
                    connection.EncryptionMethod);

                if (connection.SupportsFastExtension && _fastExtensionHandler != null && !string.IsNullOrEmpty(torrent.InfoHash))
                {
                    _fastExtensionHandler.RegisterFastPeer(connection, Convert.FromHexString(torrent.InfoHash), torrent.PieceCount, 10);
                }

                SendExtensionHandshake(connection, torrent, profile);

                SendInitialAvailability(connection, torrent);

                _chokeManager?.PeerConnected(connection);
                if (_chokeManager != null)
                {
                    if (_chokeManager.CanUnchoke(connection))
                    {
                        connection.AmChoking = false;
                        connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                    }
                }
                else
                {
                    connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                    connection.AmChoking = false;
                }

                if (!connection.AmChoking && torrent.SuperSeeding && connection.AssignedSuperSeedingPiece == null)
                {
                    AllocateAndRevealSuperSeedingPiece(connection, torrent);
                }

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

                    HandleMessage(connection, message, torrent);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Peer connection error: {0}", connection.RemoteIp);
        }
        finally
        {
            _fastExtensionHandler?.UnregisterPeer(connection);
            _chokeManager?.PeerDisconnected(connection);
            if (addedToConnectionManager)
            {
                _connectionManager.Remove(connection);
            }

            if (connection.PeerPieces != null)
            {
                var infoHash = connection.MatchedTorrent?.InfoHash ?? connection.InfoHash;
                if (!string.IsNullOrEmpty(infoHash) && _swarmHistograms.TryGetValue(infoHash, out var histogram))
                {
                    histogram.UnregisterPeer(connection.PeerPieces);
                }

                connection.PeerPieces = null;
            }

            connection.PendingRequestCount = 0;
        }
    }

    private bool ValidateInfoHash(byte[] skeyHash)
    {
        return ValidateInfoHash(skeyHash, null);
    }

    private bool ValidateInfoHash(byte[] skeyHash, PeerConnection connection)
    {
        if (_mseSkeyRegistry != null && _mseSkeyRegistry.TryMatchTorrent(skeyHash, out var matchedTorrent))
        {
            if (connection != null && matchedTorrent != null)
            {
                connection.InfoHash = matchedTorrent.InfoHash;
                connection.MatchedTorrent = matchedTorrent;
            }

            return true;
        }

        if (_torrentService != null)
        {
            try
            {
                var torrents = _torrentService.GetAll();
                if (torrents != null)
                {
                    matchedTorrent = torrents.FirstOrDefault(t =>
                        !string.IsNullOrEmpty(t.InfoHash) &&
                        MseKeyDerivation.DeriveKey(Convert.FromHexString(t.InfoHash.Trim()), System.Text.Encoding.ASCII.GetBytes("req2"))
                            .AsSpan().SequenceEqual(skeyHash));

                    if (matchedTorrent != null)
                    {
                        _mseSkeyRegistry?.RegisterTorrent(matchedTorrent);
                        if (connection != null)
                        {
                            connection.InfoHash = matchedTorrent.InfoHash;
                            connection.MatchedTorrent = matchedTorrent;
                        }

                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to validate info hash against torrent service");
            }
        }

        return false;
    }

    private void DecrementConnectionCount(string clientIp)
    {
        if (string.IsNullOrEmpty(clientIp))
        {
            return;
        }

        _connectionsPerIp.AddOrUpdate(clientIp, 0, (_, count) => Math.Max(0, count - 1));
        if (_connectionsPerIp.TryGetValue(clientIp, out var remaining) && remaining <= 0)
        {
            _connectionsPerIp.TryRemove(new KeyValuePair<string, int>(clientIp, 0));
        }
    }

    private Torrent GetCachedTorrent(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash) || _torrentService == null)
        {
            return null;
        }

        if (_torrentCache.TryGetValue(infoHash, out var cachedTorrent))
        {
            return cachedTorrent;
        }

        var torrent = _torrentService.GetByInfoHash(infoHash);
        if (torrent == null)
        {
            try
            {
                torrent = _torrentService.GetAll()?.FirstOrDefault(t => string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
            }
        }

        if (torrent != null)
        {
            _torrentCache[infoHash] = torrent;
        }

        return torrent;
    }

    internal void HandleMessage(PeerConnection connection, PeerMessage message, Torrent torrent = null)
    {
        torrent ??= connection?.MatchedTorrent ?? GetCachedTorrent(connection?.InfoHash);

        switch (message.Type)
        {
            case PeerMessageType.Choke:
                connection.PeerChoking = true;
                connection.PendingRequestCount = 0;
                break;

            case PeerMessageType.Unchoke:
                connection.PeerChoking = false;
                break;

            case PeerMessageType.Interested:
                connection.PeerInterested = true;
                if (_chokeManager != null)
                {
                    _chokeManager.PeerInterestedChanged(connection);
                }
                else if (connection.AmChoking)
                {
                    connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                    connection.AmChoking = false;
                }

                if (torrent != null && torrent.SuperSeeding && !connection.AmChoking && connection.AssignedSuperSeedingPiece == null)
                {
                    AllocateAndRevealSuperSeedingPiece(connection, torrent);
                }

                break;

            case PeerMessageType.NotInterested:
                connection.PeerInterested = false;
                _chokeManager?.PeerInterestedChanged(connection);
                break;

            case PeerMessageType.Have:
                if (message.Payload != null && message.Payload.Length >= 4)
                {
                    var pieceIndex = (int)(((uint)message.Payload[0] << 24) | ((uint)message.Payload[1] << 16) | ((uint)message.Payload[2] << 8) | message.Payload[3]);
                    if (torrent != null && torrent.PieceCount > 0)
                    {
                        if (connection.PeerPieces == null)
                        {
                            connection.PeerPieces = new bool[torrent.PieceCount];
                        }

                        if (pieceIndex >= 0 && pieceIndex < connection.PeerPieces.Length)
                        {
                            if (!connection.PeerPieces[pieceIndex])
                            {
                                connection.PeerPieces[pieceIndex] = true;
                                connection.HaveCount++;
                                var histogram = GetOrCreateHistogram(torrent.InfoHash, torrent.PieceCount);
                                histogram?.IncrementPiece(pieceIndex);
                            }

                            connection.Progress = (double)connection.HaveCount / torrent.PieceCount;
                            if (connection.IsSeed)
                            {
                                _chokeManager?.PeerBecameSeed(connection);
                            }
                        }

                        UpdateLocalInterest(connection, torrent);

                        if (torrent.SuperSeeding)
                        {
                            var tracker = GetOrCreateTracker(torrent);
                            var peerKey = GetPeerKey(connection);
                            if (tracker != null)
                            {
                                var result = tracker.RecordPieceHave(peerKey, pieceIndex);
                                if (result.NewlyPropagated && !string.IsNullOrEmpty(result.FreedPeerId))
                                {
                                    var peers = _connectionManager?.GetConnections(torrent.InfoHash);
                                    var freedPeer = peers?.FirstOrDefault(p => p != null && GetPeerKey(p) == result.FreedPeerId);
                                    if (freedPeer != null && !freedPeer.AmChoking)
                                    {
                                        AllocateAndRevealSuperSeedingPiece(freedPeer, torrent);
                                    }
                                }
                            }

                            if (!connection.AmChoking && connection.AssignedSuperSeedingPiece == null)
                            {
                                AllocateAndRevealSuperSeedingPiece(connection, torrent);
                            }
                        }
                    }
                }

                break;

            case PeerMessageType.Bitfield:
                if (message.Payload != null && torrent != null && torrent.PieceCount > 0)
                {
                    var histogram = GetOrCreateHistogram(torrent.InfoHash, torrent.PieceCount);
                    if (connection.PeerPieces != null)
                    {
                        histogram?.UnregisterPeer(connection.PeerPieces);
                    }

                    var peerPieces = new bool[torrent.PieceCount];
                    var haveCount = 0;
                    for (var i = 0; i < torrent.PieceCount; i++)
                    {
                        var byteIndex = i / 8;
                        var bitIndex = 7 - (i % 8);
                        if (byteIndex < message.Payload.Length && ((message.Payload[byteIndex] >> bitIndex) & 1) != 0)
                        {
                            peerPieces[i] = true;
                            haveCount++;
                        }
                    }

                    connection.PeerPieces = peerPieces;
                    connection.HaveCount = haveCount;
                    connection.Progress = (double)haveCount / torrent.PieceCount;
                    histogram?.RegisterPeer(peerPieces);

                    if (connection.IsSeed)
                    {
                        _chokeManager?.PeerBecameSeed(connection);
                    }

                    UpdateLocalInterest(connection, torrent);

                    if (torrent.SuperSeeding)
                    {
                        var tracker = GetOrCreateTracker(torrent);
                        var peerKey = GetPeerKey(connection);
                        if (tracker != null)
                        {
                            var propagations = tracker.RecordPeerBitfield(peerKey, connection.PeerPieces);
                            if (propagations != null && propagations.Count > 0)
                            {
                                var peers = _connectionManager?.GetConnections(torrent.InfoHash);
                                foreach (var prop in propagations)
                                {
                                    if (prop.NewlyPropagated && !string.IsNullOrEmpty(prop.FreedPeerId))
                                    {
                                        var freedPeer = peers?.FirstOrDefault(p => p != null && GetPeerKey(p) == prop.FreedPeerId);
                                        if (freedPeer != null && !freedPeer.AmChoking)
                                        {
                                            AllocateAndRevealSuperSeedingPiece(freedPeer, torrent);
                                        }
                                    }
                                }
                            }
                        }

                        if (!connection.AmChoking && connection.AssignedSuperSeedingPiece == null)
                        {
                            AllocateAndRevealSuperSeedingPiece(connection, torrent);
                        }
                    }
                }

                break;

            case PeerMessageType.Request:
                _chokeManager?.UpdatePeerActivity(connection);

                if (torrent != null && (torrent.Status == TorrentStatus.Checking || torrent.Status == TorrentStatus.QueuedForChecking))
                {
                    _logger.Debug(
                        "Rejecting piece request for {0} during {1} from {2}:{3}",
                        torrent.Name,
                        torrent.Status,
                        connection.RemoteIp,
                        connection.RemotePort);

                    if (connection.SupportsFastExtension && _fastExtensionHandler != null && message.Payload?.Length >= 12)
                    {
                        var rejectMsg = _fastExtensionHandler.BuildRejectForRequest(message.Payload);
                        if (rejectMsg != null)
                        {
                            connection.SendMessage(rejectMsg);
                        }
                    }

                    break;
                }

                if (_random.NextDouble() < connection.IdleChance)
                {
                    connection.SendKeepAlive();
                    break;
                }

                if (connection.PendingRequestCount >= connection.MaxPipelinedRequests)
                {
                    _logger.Trace(
                        "Request pipeline full ({0}) from {1}, rejecting",
                        connection.MaxPipelinedRequests,
                        connection.RemoteIp);

                    if (connection.SupportsFastExtension && _fastExtensionHandler != null && message.Payload?.Length >= 12)
                    {
                        var rejectMsg = _fastExtensionHandler.BuildRejectForRequest(message.Payload);
                        if (rejectMsg != null)
                        {
                            connection.SendMessage(rejectMsg);
                        }
                    }

                    break;
                }

                if (message.Payload != null && message.Payload.Length >= 12)
                {
                    var pieceIndex = (int)(((uint)message.Payload[0] << 24) | ((uint)message.Payload[1] << 16) | ((uint)message.Payload[2] << 8) | message.Payload[3]);

                    if (torrent != null && torrent.SuperSeeding)
                    {
                        if (connection.AssignedSuperSeedingPiece == null || connection.AssignedSuperSeedingPiece.Value != pieceIndex)
                        {
                            _logger.Debug(
                                "Super-seeding active: rejecting request for piece {0} (assigned: {1}) from peer {2}:{3}",
                                pieceIndex,
                                connection.AssignedSuperSeedingPiece,
                                connection.RemoteIp,
                                connection.RemotePort);

                            if (_fastExtensionHandler != null && (connection.SupportsFastExtension || _fastExtensionHandler.IsFastPeer(connection)))
                            {
                                var rejectMsg = _fastExtensionHandler.BuildRejectForRequest(message.Payload);
                                if (rejectMsg != null)
                                {
                                    connection.SendMessage(rejectMsg);
                                }
                            }

                            break;
                        }
                    }

                    var isAllowedFast = connection.SupportsFastExtension && _fastExtensionHandler != null && _fastExtensionHandler.GetAllowedFastSet(connection).Contains(pieceIndex);

                    if (!connection.AmChoking || isAllowedFast || _chokeManager == null)
                    {
                        connection.PendingRequestCount++;
                        HandlePieceRequest(connection, message.Payload);

                        if (torrent != null && torrent.SuperSeeding && message.Payload?.Length >= 12)
                        {
                            var length = (int)(((uint)message.Payload[8] << 24) | ((uint)message.Payload[9] << 16) | ((uint)message.Payload[10] << 8) | message.Payload[11]);
                            connection.AssignedPieceBytesUploaded += length;
                            var pieceSize = (torrent.PieceLength > 0 && torrent.TotalSize > 0 && pieceIndex == torrent.PieceCount - 1)
                                ? (int)(torrent.TotalSize - ((long)pieceIndex * torrent.PieceLength))
                                : (torrent.PieceLength > 0 ? torrent.PieceLength : 16384);

                            if (connection.AssignedPieceBytesUploaded >= pieceSize)
                            {
                                var tracker = GetOrCreateTracker(torrent);
                                tracker?.RecordPieceUploaded(GetPeerKey(connection), pieceIndex);
                            }
                        }
                    }
                    else
                    {
                        if (connection.SupportsFastExtension && _fastExtensionHandler != null)
                        {
                            var rejectMsg = _fastExtensionHandler.BuildRejectForRequest(message.Payload);
                            if (rejectMsg != null)
                            {
                                connection.SendMessage(rejectMsg);
                            }
                        }
                    }
                }

                break;

            case PeerMessageType.Cancel:
                if (connection.PendingRequestCount > 0)
                {
                    connection.PendingRequestCount--;
                }

                break;

            case PeerMessageType.SuggestPiece:
            case PeerMessageType.HaveAll:
            case PeerMessageType.HaveNone:
            case PeerMessageType.AllowedFast:
                if (_fastExtensionHandler != null)
                {
                    connection.SupportsFastExtension = true;
                    _fastExtensionHandler.HandleMessage(connection, message, torrent?.PieceCount ?? 0);
                }

                if (message.Type == PeerMessageType.HaveAll && torrent != null && torrent.PieceCount > 0)
                {
                    var histogram = GetOrCreateHistogram(torrent.InfoHash, torrent.PieceCount);
                    if (connection.PeerPieces != null)
                    {
                        histogram?.UnregisterPeer(connection.PeerPieces);
                    }

                    connection.PeerPieces = new bool[torrent.PieceCount];
                    Array.Fill(connection.PeerPieces, true);
                    connection.HaveCount = torrent.PieceCount;
                    connection.Progress = 1.0;
                    histogram?.RegisterPeer(connection.PeerPieces);
                    _chokeManager?.PeerBecameSeed(connection);
                    UpdateLocalInterest(connection, torrent);
                }
                else if (message.Type == PeerMessageType.HaveNone && torrent != null && torrent.PieceCount > 0)
                {
                    var histogram = GetOrCreateHistogram(torrent.InfoHash, torrent.PieceCount);
                    if (connection.PeerPieces != null)
                    {
                        histogram?.UnregisterPeer(connection.PeerPieces);
                    }

                    connection.PeerPieces = new bool[torrent.PieceCount];
                    connection.HaveCount = 0;
                    connection.Progress = 0.0;
                    UpdateLocalInterest(connection, torrent);
                }

                break;

            case PeerMessageType.RejectRequest:
                if (connection.SupportsFastExtension && _fastExtensionHandler != null)
                {
                    _fastExtensionHandler.HandleMessage(connection, message, torrent?.PieceCount ?? 0);
                }
                else
                {
                    connection.DecrementPendingRequests();

                    if (message.Payload != null && message.Payload.Length >= 12)
                    {
                        var pieceIndex = (int)(((uint)message.Payload[0] << 24) | ((uint)message.Payload[1] << 16) | ((uint)message.Payload[2] << 8) | message.Payload[3]);
                        var begin = (int)(((uint)message.Payload[4] << 24) | ((uint)message.Payload[5] << 16) | ((uint)message.Payload[6] << 8) | message.Payload[7]);
                        var length = (int)(((uint)message.Payload[8] << 24) | ((uint)message.Payload[9] << 16) | ((uint)message.Payload[10] << 8) | message.Payload[11]);

                        _piecePicker?.OnBlockRejected(connection, pieceIndex, begin, length);
                    }
                }

                break;

            case PeerMessageType.Piece:
                if (torrent != null && (torrent.Status == TorrentStatus.Checking || torrent.Status == TorrentStatus.QueuedForChecking))
                {
                    _logger.Debug(
                        "Dropping incoming piece block for {0} during {1} from {2}:{3}",
                        torrent.Name,
                        torrent.Status,
                        connection.RemoteIp,
                        connection.RemotePort);
                    break;
                }

                break;

            case PeerMessageType.Extended:
                HandleExtendedMessage(connection, message, torrent);
                break;

            case PeerMessageType.HashRequest:
            case PeerMessageType.Hashes:
            case PeerMessageType.HashReject:
                connection?.HandleMessage(message);
                break;

            default:
                _logger.Trace("Ignoring message type {0} from {1}", message.Type, connection.RemoteIp);
                break;
        }
    }

    private bool IsUtMetadataExtension(PeerConnection connection, byte extId, Torrent torrent)
    {
        if (_extensionManager != null)
        {
            var localExts = _extensionManager.GetSupportedExtensions(torrent?.IsPrivate ?? false);
            if (localExts.TryGetValue("ut_metadata", out var localId) && extId == localId)
            {
                return true;
            }
        }

        if (connection.RemoteExtensions.TryGetValue("ut_metadata", out var remoteId) && extId == remoteId)
        {
            return true;
        }

        if (_extensionManager == null && connection.RemoteExtensions.IsEmpty && (extId == 1 || extId == 2))
        {
            return true;
        }

        return false;
    }

    private bool IsUtPexExtension(PeerConnection connection, byte extId, Torrent torrent)
    {
        if (torrent?.IsPrivate == true)
        {
            return false;
        }

        if (_configService != null && !_configService.EnablePex)
        {
            return false;
        }

        if (_extensionManager != null)
        {
            var localExts = _extensionManager.GetSupportedExtensions(torrent?.IsPrivate ?? false);
            if (localExts.TryGetValue("ut_pex", out var localId) && extId == localId)
            {
                return true;
            }
        }

        if (connection.RemoteUtPexId.HasValue && extId == connection.RemoteUtPexId.Value)
        {
            return true;
        }

        if (connection.RemoteExtensions.TryGetValue("ut_pex", out var remoteId) && extId == remoteId)
        {
            return true;
        }

        return false;
    }

    private bool IsLtDontHaveExtension(PeerConnection connection, byte extId, Torrent torrent)
    {
        if (_configService != null && !_configService.ExtensionLtDontHave)
        {
            return false;
        }

        if (_extensionManager != null)
        {
            var localExts = _extensionManager.GetSupportedExtensions(torrent?.IsPrivate ?? false);
            if (localExts.TryGetValue("lt_donthave", out var localId) && extId == localId)
            {
                return true;
            }
        }

        if (connection.RemoteLtDontHaveId.HasValue && extId == connection.RemoteLtDontHaveId.Value)
        {
            return true;
        }

        if (connection.RemoteExtensions.TryGetValue("lt_donthave", out var remoteId) && extId == remoteId)
        {
            return true;
        }

        return false;
    }

    private void SendExtensionHandshake(PeerConnection connection, Torrent torrent, IClientProfile profile)
    {
        if (connection == null || !connection.SupportsExtensionProtocol || _extensionManager == null)
        {
            return;
        }

        IPAddress.TryParse(connection.RemoteIp, out var remoteIp);
        var listeningPort = _configService?.ListeningPort ?? 0;
        var metadata = GetTorrentMetadata(torrent?.InfoHash);
        var metadataSize = metadata?.Length ?? 0;

        var extHandshake = _extensionManager.BuildExtensionHandshake(
            remoteIp: remoteIp,
            listeningPort: listeningPort,
            metadataSize: metadataSize,
            isPrivate: torrent?.IsPrivate ?? false,
            clientProfile: profile);

        var payload = new byte[extHandshake.Length + 1];
        payload[0] = 0;
        Array.Copy(extHandshake, 0, payload, 1, extHandshake.Length);
        connection.SendMessage(new PeerMessage { Type = PeerMessageType.Extended, Payload = payload });
    }

    private void HandleExtendedMessage(PeerConnection connection, PeerMessage message, Torrent torrent)
    {
        if (message.Payload == null || message.Payload.Length < 2)
        {
            _logger.Trace("Ignoring invalid extended message payload from {0}", connection?.RemoteIp);
            return;
        }

        var extId = message.Payload[0];
        var extendedPayload = message.Payload[1..];

        if (extId == 0)
        {
            try
            {
                if (_extensionManager != null)
                {
                    var handshake = _extensionManager.ParseExtensionHandshake(extendedPayload);
                    if (handshake != null)
                    {
                        foreach (var kvp in handshake.Extensions)
                        {
                            connection.RemoteExtensions[kvp.Key] = kvp.Value;
                        }

                        if (handshake.Extensions.TryGetValue("ut_pex", out var utPexId))
                        {
                            connection.RemoteUtPexId = utPexId;
                        }

                        if (handshake.MetadataSize.HasValue)
                        {
                            connection.MetadataSize = (int)handshake.MetadataSize.Value;
                        }
                    }
                }
                else
                {
                    var parser = new BencodeParser();
                    using var stream = new MemoryStream(extendedPayload);
                    var dict = parser.Parse<BDictionary>(stream);
                    if (dict.TryGetValue("m", out var mObj) && mObj is BDictionary mDict)
                    {
                        foreach (var kvp in mDict)
                        {
                            if (kvp.Value is BNumber num)
                            {
                                connection.RemoteExtensions[kvp.Key.ToString()] = (int)num.Value;
                            }
                        }

                        if (mDict.TryGetValue("ut_pex", out var utPexNum) && utPexNum is BNumber pexNum)
                        {
                            connection.RemoteUtPexId = (int)pexNum.Value;
                        }
                    }

                    if (dict.TryGetValue("metadata_size", out var metaSizeObj) && metaSizeObj is BNumber metaSizeNum)
                    {
                        connection.MetadataSize = (int)metaSizeNum.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to parse extension handshake from {0}", connection.RemoteIp);
            }

            if (_magnetMetadataDownloader != null && torrent != null && torrent.PieceCount == 0)
            {
                _magnetMetadataDownloader.OnPeerHandshake(connection, torrent);
            }

            return;
        }

        if (IsUtMetadataExtension(connection, extId, torrent))
        {
            HandleUtMetadataMessage(connection, extId, extendedPayload, torrent);
            return;
        }

        if (IsUtPexExtension(connection, extId, torrent))
        {
            HandleUtPexMessage(connection, extendedPayload, torrent);
            return;
        }

        if (IsLtDontHaveExtension(connection, extId, torrent))
        {
            HandleLtDontHaveMessage(connection, extendedPayload, torrent);
            return;
        }

        _logger.Trace("Ignoring unknown extension message ID {0} from {1}", extId, connection.RemoteIp);
    }

    private void HandleLtDontHaveMessage(PeerConnection connection, byte[] extendedPayload, Torrent torrent)
    {
        if (connection == null || extendedPayload == null || extendedPayload.Length != 4)
        {
            connection?.HandleLtDontHave(extendedPayload);
            return;
        }

        var pieceIndex = (int)(((uint)extendedPayload[0] << 24) | ((uint)extendedPayload[1] << 16) | ((uint)extendedPayload[2] << 8) | extendedPayload[3]);

        if (torrent != null && torrent.PieceCount > 0 && pieceIndex >= 0 && pieceIndex < torrent.PieceCount)
        {
            if (connection.PeerPieces != null && pieceIndex < connection.PeerPieces.Length && connection.PeerPieces[pieceIndex])
            {
                var histogram = GetOrCreateHistogram(torrent.InfoHash, torrent.PieceCount);
                histogram?.DecrementPiece(pieceIndex);
            }
        }

        connection.HandleLtDontHave(extendedPayload);

        UpdateLocalInterest(connection, torrent);
    }

    private void HandleUtPexMessage(PeerConnection connection, byte[] extendedPayload, Torrent torrent)
    {
        if (_configService != null && !_configService.EnablePex)
        {
            return;
        }

        var currentTorrent = torrent ?? (!string.IsNullOrEmpty(connection.InfoHash) ? _torrentService.GetByInfoHash(connection.InfoHash) : null);
        if (currentTorrent?.IsPrivate == true)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var elapsed = (now - connection.LastPexReceived).TotalSeconds;

        if (elapsed < 55.0)
        {
            connection.PexRateLimitViolations++;
            _logger.Warn(
                "Peer {0}:{1} violated BEP 11 PEX rate limit ({2:F1}s since last PEX, violations: {3})",
                connection.RemoteIp,
                connection.RemotePort,
                elapsed,
                connection.PexRateLimitViolations);

            if (connection.PexRateLimitViolations > 3)
            {
                _logger.Error(
                    "Disconnecting abusive peer {0}:{1} for repeated PEX flood attacks",
                    connection.RemoteIp,
                    connection.RemotePort);
                _connectionManager?.Remove(connection);
                connection.Dispose();
                return;
            }

            return;
        }

        connection.LastPexReceived = now;
        connection.PexRateLimitViolations = 0;

        var pexData = _peerExchange.ParsePexMessage(extendedPayload, currentTorrent?.IsPrivate ?? false);
        if (pexData == null || pexData.Added == null || pexData.Added.Count == 0)
        {
            return;
        }

        var isLocalSeeder = currentTorrent != null && currentTorrent.Progress >= 1.0;
        var candidatePeers = pexData.Added;

        if (isLocalSeeder)
        {
            // Filter out seeders to avoid useless seed-to-seed connections
            candidatePeers = candidatePeers.Where(p => !p.IsSeeder).ToList();
            _logger.Debug(
                "Filtered {0} seeders from PEX payload for completed torrent {1}",
                pexData.Added.Count - candidatePeers.Count,
                currentTorrent?.Name ?? currentTorrent?.InfoHash ?? connection.InfoHash);
        }
        else
        {
            // Prioritize seeders (IsSeeder == true)
            candidatePeers = candidatePeers.OrderByDescending(p => p.IsSeeder ? 1 : 0).ToList();
        }

        if (candidatePeers.Count > 0)
        {
            var infoHash = !string.IsNullOrEmpty(connection.InfoHash) ? connection.InfoHash : currentTorrent?.InfoHash;
            if (!string.IsNullOrEmpty(infoHash))
            {
                _peerDiscovery?.AddPeers(infoHash, candidatePeers, "pex");
            }
        }
    }

    private void HandleUtMetadataMessage(PeerConnection connection, byte extId, byte[] extendedPayload, Torrent torrent)
    {
        var metaMsg = _metadataExchange.ParseMetadataMessage(extendedPayload);
        if (metaMsg.MessageType != 0)
        {
            if (_magnetMetadataDownloader != null && (metaMsg.MessageType == 1 || metaMsg.MessageType == 2))
            {
                _magnetMetadataDownloader.HandleMetadataMessage(connection, metaMsg, torrent);
            }

            return;
        }

        var remoteExtId = connection.RemoteExtensions.TryGetValue("ut_metadata", out var rId) && rId > 0
            ? (byte)rId
            : extId;

        var infoHash = !string.IsNullOrEmpty(connection.InfoHash) ? connection.InfoHash : torrent?.InfoHash;
        var metadata = GetTorrentMetadata(infoHash);

        if (metadata != null && metadata.Length > 0)
        {
            var totalSize = metadata.Length;
            var totalPieces = (int)Math.Ceiling((double)totalSize / Extensions.MetadataExchange.MetadataBlockSize);

            if (metaMsg.Piece >= 0 && metaMsg.Piece < totalPieces)
            {
                var offset = metaMsg.Piece * Extensions.MetadataExchange.MetadataBlockSize;
                var chunkSize = Math.Min(Extensions.MetadataExchange.MetadataBlockSize, totalSize - offset);
                var chunk = new byte[chunkSize];
                Array.Copy(metadata, offset, chunk, 0, chunkSize);

                var responseBytes = _metadataExchange.BuildMetadataResponse(metaMsg.Piece, totalSize, chunk);
                var payload = new byte[1 + responseBytes.Length];
                payload[0] = remoteExtId;
                Array.Copy(responseBytes, 0, payload, 1, responseBytes.Length);

                connection.SendMessage(new PeerMessage
                {
                    Type = PeerMessageType.Extended,
                    Payload = payload
                });
                return;
            }
        }

        var rejectBytes = _metadataExchange.BuildMetadataReject(metaMsg.Piece);
        var rejectPayload = new byte[1 + rejectBytes.Length];
        rejectPayload[0] = remoteExtId;
        Array.Copy(rejectBytes, 0, rejectPayload, 1, rejectBytes.Length);

        connection.SendMessage(new PeerMessage
        {
            Type = PeerMessageType.Extended,
            Payload = rejectPayload
        });
    }

    private static void HandlePieceRequest(PeerConnection connection, byte[] payload)
    {
        try
        {
            if (connection.MatchedTorrent != null && (connection.MatchedTorrent.Status == TorrentStatus.Checking || connection.MatchedTorrent.Status == TorrentStatus.QueuedForChecking))
            {
                return;
            }

            var index = (int)(((uint)payload[0] << 24) | ((uint)payload[1] << 16) | ((uint)payload[2] << 8) | payload[3]);

            if (connection.MatchedTorrent != null && connection.MatchedTorrent.SuperSeeding)
            {
                if (connection.AssignedSuperSeedingPiece == null || connection.AssignedSuperSeedingPiece.Value != index)
                {
                    return;
                }
            }

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
                connection.BytesUploaded += length;
                if (connection.MatchedTorrent != null)
                {
                    connection.MatchedTorrent.RealUploaded += length;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(piecePayload);
            }
        }
        finally
        {
            if (connection.PendingRequestCount > 0)
            {
                connection.PendingRequestCount--;
            }
        }
    }

    private void OnPeerUnchoked(PeerConnection connection)
    {
        if (connection == null)
        {
            return;
        }

        var torrent = connection.MatchedTorrent ?? GetCachedTorrent(connection.InfoHash);
        if (torrent != null && torrent.SuperSeeding && connection.AssignedSuperSeedingPiece == null)
        {
            AllocateAndRevealSuperSeedingPiece(connection, torrent);
        }
    }

    internal bool AllocateAndRevealSuperSeedingPiece(PeerConnection connection, Torrent torrent = null)
    {
        torrent ??= connection?.MatchedTorrent ?? GetCachedTorrent(connection?.InfoHash);
        if (torrent == null || !torrent.SuperSeeding || torrent.PieceCount <= 0 || connection == null)
        {
            return false;
        }

        if (connection.AmChoking)
        {
            return false;
        }

        var tracker = GetOrCreateTracker(torrent);
        var peerKey = GetPeerKey(connection);

        if (tracker != null)
        {
            if (!tracker.IsPeerEligible(peerKey))
            {
                return false;
            }

            if (!tracker.TryAllocatePiece(peerKey, connection.PeerPieces, out var chosenPiece))
            {
                return false;
            }

            connection.AssignedSuperSeedingPiece = chosenPiece;
            connection.AssignedPieceBytesUploaded = 0;
            connection.SendHave(chosenPiece);
            _logger.Debug(
                "Super-seeding: revealed piece {0} to peer {1}:{2} on torrent {3}",
                chosenPiece,
                connection.RemoteIp,
                connection.RemotePort,
                torrent.Name ?? torrent.InfoHash);
            return true;
        }

        return false;
    }

    public List<SelfishLeecherResult> CheckSuperSeedingTimeouts(Torrent torrent = null, DateTime? now = null)
    {
        var allTimeouts = new List<SelfishLeecherResult>();

        if (torrent != null)
        {
            if (!torrent.SuperSeeding || string.IsNullOrEmpty(torrent.InfoHash))
            {
                return allTimeouts;
            }

            var tracker = GetOrCreateTracker(torrent);
            if (tracker != null)
            {
                var timeouts = tracker.CheckTimeouts(now);
                if (timeouts != null && timeouts.Count > 0)
                {
                    ProcessSelfishLeechers(timeouts, torrent);
                    allTimeouts.AddRange(timeouts);
                }
            }
        }
        else
        {
            foreach (var kvp in _superSeedingTrackers)
            {
                var infoHash = kvp.Key;
                var tracker = kvp.Value;
                var cachedTorrent = GetCachedTorrent(infoHash);
                if (cachedTorrent != null && cachedTorrent.SuperSeeding)
                {
                    var timeouts = tracker.CheckTimeouts(now);
                    if (timeouts != null && timeouts.Count > 0)
                    {
                        ProcessSelfishLeechers(timeouts, cachedTorrent);
                        allTimeouts.AddRange(timeouts);
                    }
                }
            }
        }

        return allTimeouts;
    }

    private void ProcessSelfishLeechers(List<SelfishLeecherResult> timeouts, Torrent torrent)
    {
        var peers = _connectionManager?.GetConnections(torrent.InfoHash);
        foreach (var timeout in timeouts)
        {
            var peer = peers?.FirstOrDefault(p => p != null && GetPeerKey(p) == timeout.PeerId);
            if (peer != null)
            {
                peer.AmChoking = true;
                peer.AssignedSuperSeedingPiece = null;
                peer.AssignedPieceBytesUploaded = 0;
                try
                {
                    peer.SendMessage(new PeerMessage { Type = PeerMessageType.Choke });
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Error sending choke message to selfish peer {0}", timeout.PeerId);
                }

                _logger.Warn(
                    "Super-seeding: choked selfish peer {0} on torrent {1} after holding piece {2} for {3:F1}s without propagation",
                    timeout.PeerId,
                    torrent.Name ?? torrent.InfoHash,
                    timeout.PieceIndex,
                    timeout.HeldDuration.TotalSeconds);
            }

            if (peers != null)
            {
                foreach (var otherPeer in peers)
                {
                    if (otherPeer != null && otherPeer != peer && !otherPeer.AmChoking && otherPeer.AssignedSuperSeedingPiece == null)
                    {
                        if (AllocateAndRevealSuperSeedingPiece(otherPeer, torrent))
                        {
                            break;
                        }
                    }
                }
            }
        }
    }
}
