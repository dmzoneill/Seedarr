using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers;

public interface IConnectionManager
{
    void Add(PeerConnection connection);
    bool TryAdd(PeerConnection connection, IConnectionReservation reservation);
    bool TryReserveSlot(string infoHash, bool isInbound, out IConnectionReservation reservation);
    void Remove(PeerConnection connection);
    List<PeerConnection> GetConnections(string infoHash);
    int ActiveCount { get; }
    int Count { get; }
    bool CanAddConnectionForTorrent(string infoHash);
    int GetUploadSlotCount();
    int GetDynamicUploadSlotCount(string infoHash = null);
    List<PeerConnection> GetAllConnections();
    List<PeerConnection> GetAll();
    List<PeerConnection> GetForTorrent(string infoHash);
    void DisconnectAll();
    Task DisconnectAllAsync();
    void DisconnectByInfoHash(string infoHash);
    void ProcessDropouts();
    void RotateConnections();
    void BanPeer(string ip, TimeSpan? duration = null);
    bool IsPeerBanned(string ip);
    void UnbanPeer(string ip);
    IReadOnlyCollection<string> GetBannedPeers();
}

public class ConnectionManager : IConnectionManager,
    IHandle<SeedingStoppedEvent>,
    IHandle<TorrentStatusChangedEvent>,
    IHandle<TorrentPausedEvent>,
    IHandle<TorrentDeletedEvent>,
    IHandle<PeerBannedEvent>
{
    private readonly IConfigService _configService;
    private readonly IPeerConnectionLogService _connectionLogService;
    private readonly ITorrentService _torrentService;
    private readonly IFastExtensionHandler _fastExtensionHandler;
    private readonly ITorrentEventLogService _eventLogService;
    private readonly IRandomNumberGenerator _random;
    private readonly List<PeerConnection> _connections = new();
    private readonly List<ConnectionReservation> _reservations = new();
    private readonly ConcurrentDictionary<string, BannedPeerEntry> _bannedPeers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly Logger _logger;

    private readonly IClientBehaviorSimulator _clientBehaviorSimulator;

    public int ActiveCount
    {
        get
        {
            lock (_lock)
            {
                return _connections.Count;
            }
        }
    }

    public int Count => ActiveCount;

    public List<PeerConnection> GetAll() => GetAllConnections();

    public List<PeerConnection> GetForTorrent(string infoHash) => GetConnections(infoHash);

    public ConnectionManager(
        IConfigService configService,
        IPeerConnectionLogService connectionLogService,
        ITorrentService torrentService,
        IFastExtensionHandler fastExtensionHandler,
        ITorrentEventLogService eventLogService,
        IRandomNumberGenerator random = null,
        IClientBehaviorSimulator clientBehaviorSimulator = null)
    {
        _configService = configService;
        _connectionLogService = connectionLogService;
        _torrentService = torrentService;
        _fastExtensionHandler = fastExtensionHandler;
        _eventLogService = eventLogService;
        _random = random ?? new RandomNumberGenerator();
        _clientBehaviorSimulator = clientBehaviorSimulator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool TryReserveSlot(string infoHash, bool isInbound, out IConnectionReservation reservation)
    {
        return TryReserveSlot(infoHash, isInbound, TimeSpan.FromSeconds(15), out reservation);
    }

    internal bool TryReserveSlot(string infoHash, bool isInbound, TimeSpan ttl, out IConnectionReservation reservation)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            reservation = null;
            return false;
        }

        lock (_lock)
        {
            PruneExpiredReservations();

            var maxGlobal = _configService.MaxGlobalConnections;
            if (maxGlobal > 0 && (_connections.Count + _reservations.Count) >= maxGlobal)
            {
                reservation = null;
                return false;
            }

            var maxPerTorrent = _configService.MaxPerTorrentConnections;
            var activeConnectionsForTorrent = _connections.Count(c =>
                string.Equals(c.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
            var activeReservationsForTorrent = _reservations.Count(r =>
                string.Equals(r.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));

            if (maxPerTorrent > 0 && (activeConnectionsForTorrent + activeReservationsForTorrent) >= maxPerTorrent)
            {
                reservation = null;
                return false;
            }

            var res = new ConnectionReservation(infoHash, isInbound, ttl, r =>
            {
                lock (_lock)
                {
                    _reservations.Remove(r);
                }
            });

            _reservations.Add(res);
            reservation = res;
            return true;
        }
    }

    public bool TryAdd(PeerConnection connection, IConnectionReservation reservation)
    {
        if (connection == null)
        {
            reservation?.Dispose();
            return false;
        }

        if (IsPeerBanned(connection.RemoteIp))
        {
            _logger.Debug("Rejecting connection from banned peer {0}", connection.RemoteIp);
            reservation?.Dispose();
            return false;
        }

        PeerConnection evicted = null;
        lock (_lock)
        {
            PruneExpiredReservations();

            if (reservation is ConnectionReservation res && _reservations.Remove(res))
            {
                if (res.IsInbound)
                {
                    connection.IsInbound = true;
                }

                _connections.Add(connection);
            }
            else
            {
                var maxGlobal = _configService.MaxGlobalConnections;
                var maxPerTorrent = _configService.MaxPerTorrentConnections;

                var sameTorrentPeers = _connections
                    .Where(c => string.Equals(c.InfoHash, connection.InfoHash, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (maxPerTorrent > 0 && sameTorrentPeers.Count >= maxPerTorrent)
                {
                    var nonLanCandidates = sameTorrentPeers.Where(c => !c.IsLocalPeer).ToList();
                    var evictionPool = nonLanCandidates.Count > 0 ? nonLanCandidates : sameTorrentPeers;

                    evicted = evictionPool
                        .OrderByDescending(GetEvictionPriority)
                        .ThenBy(c => c.DownloadRate + c.UploadRate)
                        .ThenBy(c => c.LastActivity)
                        .ThenBy(c => c.ConnectedAt)
                        .FirstOrDefault();

                    if (evicted != null)
                    {
                        _logger.Debug("Evicting peer {0} from same torrent {1} (per-torrent limit {2})", evicted.RemoteIp, connection.InfoHash, maxPerTorrent);
                        _connections.Remove(evicted);
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (maxGlobal > 0 && _connections.Count >= maxGlobal)
                {
                    var candidates = sameTorrentPeers.Count > 0 ? sameTorrentPeers : _connections;
                    var nonLanCandidates = candidates.Where(c => !c.IsLocalPeer).ToList();
                    var evictionPool = nonLanCandidates.Count > 0 ? nonLanCandidates : candidates;

                    evicted = evictionPool
                        .OrderByDescending(GetEvictionPriority)
                        .ThenBy(c => c.DownloadRate + c.UploadRate)
                        .ThenBy(c => c.LastActivity)
                        .ThenBy(c => c.ConnectedAt)
                        .FirstOrDefault();

                    if (evicted != null)
                    {
                        _logger.Debug("Evicting peer {0} (global limit {1})", evicted.RemoteIp, maxGlobal);
                        _connections.Remove(evicted);
                    }
                    else
                    {
                        return false;
                    }
                }

                _connections.Add(connection);
            }
        }

        if (evicted != null)
        {
            LogDisconnect(evicted);
            _fastExtensionHandler?.UnregisterPeer(evicted);
            evicted.Dispose();
        }

        LogConnect(connection);
        return true;
    }

    public void Add(PeerConnection connection)
    {
        TryAdd(connection, null);
    }

    public void Remove(PeerConnection connection)
    {
        if (connection == null)
        {
            return;
        }

        bool removed;
        lock (_lock)
        {
            removed = _connections.Remove(connection);
        }

        if (!removed)
        {
            return;
        }

        _fastExtensionHandler?.UnregisterPeer(connection);
        connection.Dispose();
        LogDisconnect(connection);
    }

    public List<PeerConnection> GetConnections(string infoHash)
    {
        lock (_lock)
        {
            return _connections
                .Where(c => string.Equals(c.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public List<PeerConnection> GetAllConnections()
    {
        lock (_lock)
        {
            return _connections.ToList();
        }
    }

    public void DisconnectAll()
    {
        List<PeerConnection> toDisconnect;
        lock (_lock)
        {
            toDisconnect = _connections.ToList();
            _connections.Clear();
        }

        foreach (var conn in toDisconnect)
        {
            try
            {
                _fastExtensionHandler.UnregisterPeer(conn);
                conn.Dispose();
                LogDisconnect(conn);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error disconnecting peer {0}", conn.RemoteIp);
            }
        }
    }

    public Task DisconnectAllAsync()
    {
        DisconnectAll();
        return Task.CompletedTask;
    }

    public void DisconnectByInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return;
        }

        List<PeerConnection> toDisconnect;
        lock (_lock)
        {
            toDisconnect = _connections
                .Where(c => string.Equals(c.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var conn in toDisconnect)
            {
                _connections.Remove(conn);
            }
        }

        foreach (var conn in toDisconnect)
        {
            try
            {
                _fastExtensionHandler.UnregisterPeer(conn);
                conn.Dispose();
                LogDisconnect(conn);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error disconnecting peer {0} for infoHash {1}", conn.RemoteIp, infoHash);
            }
        }
    }

    public void Handle(SeedingStoppedEvent message)
    {
        if (message == null || message.TorrentId <= 0)
        {
            return;
        }

        try
        {
            var torrent = _torrentService.Get(message.TorrentId);
            if (torrent != null && !string.IsNullOrWhiteSpace(torrent.InfoHash))
            {
                DisconnectByInfoHash(torrent.InfoHash);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to disconnect peers on SeedingStoppedEvent for torrent {0}", message.TorrentId);
        }
    }

    public void Handle(TorrentStatusChangedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        if (message.NewStatus == TorrentStatus.Stopped || message.NewStatus == TorrentStatus.Paused)
        {
            if (!string.IsNullOrWhiteSpace(message.Torrent.InfoHash))
            {
                DisconnectByInfoHash(message.Torrent.InfoHash);
            }
        }
    }

    public void Handle(TorrentPausedEvent message)
    {
        if (message?.Torrent != null && !string.IsNullOrWhiteSpace(message.Torrent.InfoHash))
        {
            DisconnectByInfoHash(message.Torrent.InfoHash);
            _clientBehaviorSimulator?.ReleaseSession(message.Torrent.InfoHash);
        }
    }

    public void Handle(TorrentDeletedEvent message)
    {
        var infoHash = message?.Torrent?.InfoHash;
        if (string.IsNullOrWhiteSpace(infoHash) && message?.TorrentId > 0)
        {
            try
            {
                var torrent = _torrentService.Get(message.TorrentId);
                infoHash = torrent?.InfoHash;
            }
            catch
            {
                // Best effort
            }
        }

        if (!string.IsNullOrWhiteSpace(infoHash))
        {
            DisconnectByInfoHash(infoHash);
            _clientBehaviorSimulator?.ReleaseSession(infoHash);
        }
    }

    public bool CanAddConnectionForTorrent(string infoHash)
    {
        lock (_lock)
        {
            PruneExpiredReservations();

            var maxGlobal = _configService.MaxGlobalConnections;
            if (maxGlobal > 0 && (_connections.Count + _reservations.Count) >= maxGlobal)
            {
                return false;
            }

            var maxPerTorrent = _configService.MaxPerTorrentConnections;
            var torrentCount = _connections.Count(c =>
                string.Equals(c.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase)) +
                _reservations.Count(r => string.Equals(r.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));

            return maxPerTorrent <= 0 || torrentCount < maxPerTorrent;
        }
    }

    public int GetUploadSlotCount()
    {
        return GetDynamicUploadSlotCount();
    }

    public int GetDynamicUploadSlotCount(string infoHash = null)
    {
        var u = _configService.MaxUploadSpeedKbps;
        if (u > 0)
        {
            var calculated = (int)Math.Ceiling(Math.Sqrt(2.0 * u));
            var maxLimit = _configService.MaxUploadSlots > 0 ? _configService.MaxUploadSlots : int.MaxValue;
            return Math.Max(4, Math.Min(maxLimit, calculated));
        }

        if (!string.IsNullOrWhiteSpace(infoHash))
        {
            var torrentConnections = GetConnections(infoHash);
            var activeLeechers = torrentConnections.Count(c => !c.IsSeed);
            if (activeLeechers > 0)
            {
                var leecherSlots = (int)Math.Ceiling(activeLeechers * 0.2);
                var maxLimit = _configService.MaxUploadSlots > 0 ? _configService.MaxUploadSlots : int.MaxValue;
                return Math.Max(4, Math.Min(maxLimit, leecherSlots));
            }
        }

        return Math.Max(4, _configService.MaxUploadSlots);
    }

    public void ProcessDropouts()
    {
        List<PeerConnection> toRemove;

        if (_configService.SimulationModeEnabled)
        {
            double dropoutProbability;
            lock (_lock)
            {
                dropoutProbability = _clientBehaviorSimulator != null
                    ? _clientBehaviorSimulator.GetEffectiveDropoutProbability(_configService.PeerDropoutProbability)
                    : _configService.PeerDropoutProbability;

                if (dropoutProbability <= 0 || _connections.Count == 0)
                {
                    return;
                }

                toRemove = _connections
                    .Where(_ => _random.NextDouble() < dropoutProbability)
                    .ToList();

                foreach (var conn in toRemove)
                {
                    _connections.Remove(conn);
                }
            }

            foreach (var conn in toRemove)
            {
                _logger.Debug(
                    "Peer {0} dropped out (probability: {1:F2})",
                    conn.RemoteIp,
                    dropoutProbability);
                LogDisconnect(conn);
                _fastExtensionHandler?.UnregisterPeer(conn);
                conn.Dispose();
            }

            return;
        }

        var now = DateTime.UtcNow;
        lock (_lock)
        {
            if (_connections.Count == 0)
            {
                return;
            }

            toRemove = new List<PeerConnection>();

            foreach (var conn in _connections)
            {
                // LAN Peer Immunity: Never drop peers tagged with L (LAN peers)
                if (conn.IsLocalPeer)
                {
                    continue;
                }

                // Active optimistic unchoke candidates during their 30s evaluation window
                if (conn.IsOptimisticUnchoked)
                {
                    if (conn.LastUnchokedAt == null || (now - conn.LastUnchokedAt.Value).TotalSeconds <= 30)
                    {
                        continue;
                    }
                }

                // Deterministic Anti-Leech Pruning Rules:
                // 1. Mutual Disinterest & Inactivity: Both !AmInterested && !PeerInterested with zero transfer for > 120s
                var isZeroSpeed = (conn.DownloadRate + conn.UploadRate) == 0;
                var inactiveTime = (now - conn.LastActivity).TotalSeconds;
                if (!conn.AmInterested && !conn.PeerInterested && isZeroSpeed && inactiveTime > 120)
                {
                    toRemove.Add(conn);
                    continue;
                }

                // 2. Snubbed Stalled Leechers: AmInterested && !PeerChoking (unchoked by peer) where zero payload bytes have been received for > 60s while requests remain pending
                var idleBytesTime = (now - conn.LastBytesReceived).TotalSeconds;
                if (conn.AmInterested && !conn.PeerChoking && (conn.PendingRequestCount > 0 || conn.IsSnubbed) && idleBytesTime > 60)
                {
                    toRemove.Add(conn);
                    continue;
                }

                // 3. Unreciprocated Upload Leechers: We are unchoking the peer and uploading blocks, but the peer is choked/choking us and we are in leecher mode on that torrent without download reciprocity
                if (!conn.AmChoking && (conn.UploadRate > 0 || conn.BytesUploaded > 0) && conn.PeerChoking && conn.DownloadRate == 0)
                {
                    var isLeecherMode = conn.MatchedTorrent != null
                        ? conn.MatchedTorrent.Progress < 1.0
                        : ResolveTorrent(conn.InfoHash) is { } t && t.Progress < 1.0;

                    if (isLeecherMode)
                    {
                        toRemove.Add(conn);
                        continue;
                    }
                }
            }

            foreach (var conn in toRemove)
            {
                _connections.Remove(conn);
            }
        }

        foreach (var conn in toRemove)
        {
            _logger.Debug("Pruning stale/unhealthy peer {0}", conn.RemoteIp);
            LogDisconnect(conn);
            _fastExtensionHandler?.UnregisterPeer(conn);
            conn.Dispose();
        }
    }

    public void RotateConnections()
    {
        List<PeerConnection> toEvict;
        double rotationPct;
        lock (_lock)
        {
            rotationPct = _clientBehaviorSimulator != null
                ? _clientBehaviorSimulator.GetEffectiveRotationPercentage(_configService.ConnectionRotationPercentage)
                : _configService.ConnectionRotationPercentage;
            var rotateCount = (int)Math.Ceiling(_connections.Count * rotationPct);

            if (rotateCount <= 0 || _connections.Count == 0)
            {
                return;
            }

            rotateCount = Math.Min(rotateCount, _connections.Count);

            var now = DateTime.UtcNow;
            var gracePeriod = TimeSpan.FromSeconds(60);

            // 1. Grace Period Protection: Protect connections created within the last 60 seconds, LAN peers, and optimistic unchoke candidates
            var matureConnections = _connections
                .Where(c => (now - c.ConnectedAt) >= gracePeriod &&
                            !c.IsLocalPeer &&
                            (!c.IsOptimisticUnchoked || (c.LastUnchokedAt != null && (now - c.LastUnchokedAt.Value).TotalSeconds > 30)))
                .ToList();

            if (matureConnections.Count == 0)
            {
                return;
            }

            // 2. Protected Top Peers: Protect top N (up to 4) fastest active peers with transfer activity
            var protectedTopPeers = matureConnections
                .Where(c => (c.DownloadRate + c.UploadRate) > 0)
                .OrderByDescending(c => c.DownloadRate + c.UploadRate)
                .Take(4)
                .ToHashSet();

            var candidates = matureConnections
                .Where(c => !protectedTopPeers.Contains(c))
                .ToList();

            if (candidates.Count == 0)
            {
                return;
            }

            // 3. Efficiency / Eviction Scoring: Prioritize snubbed, idle, choked, low throughput
            toEvict = candidates
                .OrderByDescending(GetEvictionPriority)
                .ThenBy(c => c.DownloadRate + c.UploadRate)
                .ThenBy(c => c.LastActivity)
                .ThenBy(c => c.ConnectedAt)
                .Take(rotateCount)
                .ToList();

            foreach (var conn in toEvict)
            {
                _connections.Remove(conn);
            }
        }

        foreach (var conn in toEvict)
        {
            _logger.Debug(
                "Rotating out peer {0} (rotation: {1:P0})",
                conn.RemoteIp,
                rotationPct);
            LogDisconnect(conn);
            _fastExtensionHandler?.UnregisterPeer(conn);
            conn.Dispose();
        }
    }

    private static int GetEvictionPriority(PeerConnection c)
    {
        var now = DateTime.UtcNow;

        // 1. Unresponsive / snubbed peers (no bytes received in >60s while interested)
        if (c.IsSnubbed || (c.AmInterested && !c.PeerChoking && (now - c.LastBytesReceived).TotalSeconds > 60))
        {
            return 100;
        }

        var isZeroSpeed = (c.DownloadRate + c.UploadRate) == 0;
        var noMutualInterest = !c.AmInterested && !c.PeerInterested;

        // 2. Mutually choked, non-interested peers with zero transfer rate
        if (isZeroSpeed && noMutualInterest && c.AmChoking && c.PeerChoking)
        {
            return 85;
        }

        if (isZeroSpeed && noMutualInterest)
        {
            return 80;
        }

        // 3. Unreciprocated upload leechers (we unchoke and upload, but peer chokes us with zero download)
        if (!c.AmChoking && (c.UploadRate > 0 || c.BytesUploaded > 0) && c.PeerChoking && c.DownloadRate == 0)
        {
            return 70;
        }

        if (isZeroSpeed)
        {
            return 60;
        }

        if (c.PeerChoking)
        {
            return 40;
        }

        return 20;
    }

    private Torrent ResolveTorrent(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        try
        {
            return _torrentService.FindByInfoHash(infoHash) ?? _torrentService.GetByInfoHash(infoHash);
        }
        catch
        {
            return null;
        }
    }

    private void LogConnect(PeerConnection connection)
    {
        try
        {
            var torrent = ResolveTorrent(connection.InfoHash);
            _connectionLogService.LogConnected(connection, torrent?.Name);

            if (torrent != null)
            {
                _eventLogService.Debug(
                    torrent.Id,
                    "Peers",
                    $"Peer connected {connection.RemoteIp}:{connection.RemotePort}{(connection.IsEncrypted ? " (encrypted)" : string.Empty)}");
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to log peer connection event");
        }
    }

    private void LogDisconnect(PeerConnection connection)
    {
        try
        {
            var torrent = ResolveTorrent(connection.InfoHash);
            _connectionLogService.LogDisconnected(connection, torrent?.Name);

            if (torrent != null)
            {
                _eventLogService.Debug(
                    torrent.Id,
                    "Peers",
                    $"Peer disconnected {connection.RemoteIp}:{connection.RemotePort}");
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to log peer disconnection event");
        }
    }

    public void BanPeer(string ip, TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return;
        }

        var normalizedIp = ip.Trim();
        var ttl = duration ?? TimeSpan.FromHours(24);
        var expiresAt = DateTime.UtcNow.Add(ttl);
        var entry = new BannedPeerEntry(normalizedIp, expiresAt);
        _bannedPeers[normalizedIp] = entry;

        List<PeerConnection> toDisconnect;
        lock (_lock)
        {
            toDisconnect = _connections
                .Where(c => !string.IsNullOrWhiteSpace(c.RemoteIp) && entry.Matches(c.RemoteIp))
                .ToList();
        }

        foreach (var conn in toDisconnect)
        {
            Remove(conn);
        }
    }

    public bool IsPeerBanned(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }

        var normalizedIp = ip.Trim();
        var now = DateTime.UtcNow;

        if (_bannedPeers.TryGetValue(normalizedIp, out var directEntry))
        {
            if (now < directEntry.ExpiresAt)
            {
                return true;
            }

            _bannedPeers.TryRemove(normalizedIp, out _);
        }

        var hasParsed = IPAddress.TryParse(normalizedIp, out var parsedIp);

        foreach (var kvp in _bannedPeers)
        {
            var entry = kvp.Value;
            if (now >= entry.ExpiresAt)
            {
                _bannedPeers.TryRemove(kvp.Key, out _);
                continue;
            }

            if (entry.Matches(normalizedIp))
            {
                return true;
            }

            if (hasParsed && entry.Matches(parsedIp))
            {
                return true;
            }
        }

        return false;
    }

    public void UnbanPeer(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return;
        }

        _bannedPeers.TryRemove(ip.Trim(), out _);
    }

    public IReadOnlyCollection<string> GetBannedPeers()
    {
        var now = DateTime.UtcNow;
        return _bannedPeers
            .Where(kvp => kvp.Value.ExpiresAt > now)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    public void Handle(PeerBannedEvent message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.PeerIp))
        {
            return;
        }

        BanPeer(message.PeerIp);
    }

    private sealed class BannedPeerEntry
    {
        public string Rule { get; }
        public DateTime ExpiresAt { get; }
        public IPAddress SingleIp { get; }
        public IPNetwork? Network { get; }

        public BannedPeerEntry(string rule, DateTime expiresAt)
        {
            Rule = rule;
            ExpiresAt = expiresAt;

            if (IPNetwork.TryParse(rule, out var network))
            {
                Network = network;
            }
            else if (IPAddress.TryParse(rule, out var singleIp))
            {
                if (singleIp.IsIPv4MappedToIPv6)
                {
                    singleIp = singleIp.MapToIPv4();
                }

                SingleIp = singleIp;
            }
        }

        public bool Matches(IPAddress ipAddress)
        {
            if (ipAddress == null)
            {
                return false;
            }

            var address = ipAddress.IsIPv4MappedToIPv6 ? ipAddress.MapToIPv4() : ipAddress;

            if (SingleIp != null && SingleIp.Equals(address))
            {
                return true;
            }

            if (Network.HasValue && Network.Value.Contains(address))
            {
                return true;
            }

            return false;
        }

        public bool Matches(string ipStr)
        {
            if (string.IsNullOrWhiteSpace(ipStr))
            {
                return false;
            }

            var trimmed = ipStr.Trim();
            if (string.Equals(Rule, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IPAddress.TryParse(trimmed, out var ip))
            {
                return Matches(ip);
            }

            return false;
        }
    }

    private void PruneExpiredReservations()
    {
        var now = DateTime.UtcNow;
        _reservations.RemoveAll(r => r.IsDisposed || now >= r.ExpiresAt);
    }

    internal sealed class ConnectionReservation : IConnectionReservation
    {
        public string InfoHash { get; }
        public bool IsInbound { get; }
        public DateTime CreatedAt { get; }
        public DateTime ExpiresAt { get; }
        public bool IsDisposed { get; private set; }

        private readonly Action<ConnectionReservation> _onDispose;

        public ConnectionReservation(string infoHash, bool isInbound, TimeSpan ttl, Action<ConnectionReservation> onDispose)
        {
            InfoHash = infoHash;
            IsInbound = isInbound;
            CreatedAt = DateTime.UtcNow;
            ExpiresAt = CreatedAt.Add(ttl);
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            _onDispose?.Invoke(this);
        }
    }
}
