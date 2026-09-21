using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers;

public interface IChokeManager
{
    event Action<PeerConnection> PeerUnchoked;
    string CurrentOptimisticPeerKey { get; }
    void ProcessChoking();
    void ProcessRegularUnchoke();
    void ProcessOptimisticUnchoke();
    void ProcessOptimisticUnchokeForSwarm(string infoHash);
    void PeerConnected(PeerConnection connection);
    void PeerDisconnected(PeerConnection connection);
    void PeerInterestedChanged(PeerConnection connection);
    void PeerBecameSeed(PeerConnection connection);
    void PeerBecamePartialSeed(PeerConnection connection);
    void UpdatePeerActivity(PeerConnection connection);
    bool CanUnchoke(PeerConnection connection);
    bool CanUnchoke(string infoHash);
    bool IsTorrentSeeding(string infoHash);
    void SetTorrentSeeding(string infoHash, bool isSeeding);
    void Choke(PeerConnection connection);
}

public class ChokeManager : BackgroundService, IChokeManager
{
    public event Action<PeerConnection> PeerUnchoked;
    public string CurrentOptimisticPeerKey
    {
        get
        {
            lock (_lock)
            {
                return _currentOptimisticPeerKey;
            }
        }
    }
    public const int MinUnchokeDurationSeconds = 20;
    public const int MaxUnchokeLeaseSeconds = 60;
    public const double ChokeHysteresisMargin = 0.15;
    public const int OptimisticUnchokeRounds = 3;
    public const int NewPeerThresholdSeconds = 30;
    public const int NewPeerSelectionWeight = 3;
    public const int ExistingPeerSelectionWeight = 1;

    private const int RegularUnchokeIntervalSeconds = 10;
    private const int OptimisticUnchokeIntervalSeconds = 10;
    private const int SnubbingThresholdSeconds = 60;

    private readonly IConnectionManager _connectionManager;
    private readonly IConfigService _configService;
    private readonly ITorrentService _torrentService;
    private readonly IRandomNumberGenerator _random;
    private readonly IFastExtensionHandler _fastExtensionHandler;
    private readonly Logger _logger;
    private readonly object _lock = new();
    private readonly HashSet<string> _explicitSeedingTorrents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OptimisticSlot> _optimisticSlots = new(StringComparer.OrdinalIgnoreCase);
    private string _currentOptimisticPeerKey;

    private DateTime _lastRegularUnchoke = DateTime.MinValue;
    private DateTime _lastOptimisticUnchoke = DateTime.MinValue;

    public ChokeManager(
        IConnectionManager connectionManager,
        IConfigService configService,
        ITorrentService torrentService = null,
        IRandomNumberGenerator random = null,
        IFastExtensionHandler fastExtensionHandler = null)
    {
        _connectionManager = connectionManager;
        _configService = configService;
        _torrentService = torrentService;
        _random = random ?? new RandomNumberGenerator();
        _fastExtensionHandler = fastExtensionHandler;
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("ChokeManager service started with 10s regular unchoke and 30s optimistic unchoke cycle (3 rounds).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                var connections = _connectionManager.GetAllConnections();
                foreach (var conn in connections)
                {
                    conn.UpdateTransferRates(now);
                }

                if ((now - _lastRegularUnchoke).TotalSeconds >= RegularUnchokeIntervalSeconds)
                {
                    ProcessRegularUnchoke();
                    _lastRegularUnchoke = now;
                }

                if ((now - _lastOptimisticUnchoke).TotalSeconds >= OptimisticUnchokeIntervalSeconds)
                {
                    ProcessOptimisticUnchoke();
                    _lastOptimisticUnchoke = now;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in ChokeManager rotation cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    public void ProcessChoking()
    {
        ProcessRegularUnchoke();
        ProcessOptimisticUnchoke();
    }

    public void ProcessRegularUnchoke()
    {
        lock (_lock)
        {
            var connections = _connectionManager.GetAllConnections();
            if (connections.Count == 0)
            {
                return;
            }

            var maxUploadSlots = _connectionManager != null
                ? _connectionManager.GetDynamicUploadSlotCount(connections.FirstOrDefault(c => !string.IsNullOrEmpty(c.InfoHash))?.InfoHash)
                : _configService.MaxUploadSlots;
            if (maxUploadSlots <= 0)
            {
                maxUploadSlots = _configService.MaxUploadSlots;
            }

            if (maxUploadSlots <= 0)
            {
                // 0 means unlimited: unchoke all interested peers
                foreach (var conn in connections)
                {
                    if (conn.PeerInterested && conn.AmChoking)
                    {
                        Unchoke(conn);
                    }
                }

                return;
            }

            var now = DateTime.UtcNow;

            // Anti-snubbing detection: peers unchoked without requests for > 60s
            // If a peer is choked by Seedarr, do not penalize it with snubbing timeouts
            foreach (var conn in connections)
            {
                if (!conn.AmChoking)
                {
                    var lastActive = conn.LastUnchokedAt.HasValue && conn.LastUnchokedAt.Value > conn.LastRequestReceived
                        ? conn.LastUnchokedAt.Value
                        : conn.LastRequestReceived;
                    var idleTime = (now - lastActive).TotalSeconds;
                    if (idleTime > SnubbingThresholdSeconds)
                    {
                        if (!conn.IsSnubbed)
                        {
                            _logger.Debug("Peer {0}:{1} marked as snubbed (idle for {2:F0}s)", conn.RemoteIp, conn.RemotePort, idleTime);
                            conn.IsSnubbed = true;
                        }
                    }
                }
            }

            // Anti-seed / anti-partial-seed choking: choke any unchoked peer that has become a seed or partial seed without wanted pieces
            foreach (var conn in connections)
            {
                if (!conn.AmChoking && (conn.IsSeed || conn.IsPartialSeed || !conn.HasMissingWantedPieces))
                {
                    conn.IsOptimisticUnchoked = false;
                    Choke(conn);
                }
            }

            // Reserve 1 slot for optimistic unchoke if maxUploadSlots > 1
            var regularSlotCount = maxUploadSlots > 1 ? maxUploadSlots - 1 : maxUploadSlots;

            // Group connections by infoHash to balance slots across swarms
            var byTorrent = connections
                .Where(c => !string.IsNullOrEmpty(c.InfoHash))
                .GroupBy(c => c.InfoHash, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var selectedRegular = new HashSet<PeerConnection>();

            if (regularSlotCount > 0)
            {
                // Filter torrent groups that have at least one interested, non-snubbed, non-seed candidate
                var torrentGroups = byTorrent
                    .Select(g =>
                    {
                        var isSeeding = IsTorrentSeeding(g.Key);
                        var eligible = g.Where(c => c.PeerInterested && !c.IsSnubbed && !c.IsSeed && !c.IsPartialSeed && c.HasMissingWantedPieces);
                        var candidates = OrderCandidatesWithHysteresis(eligible, isSeeding, now);

                        return new
                        {
                            InfoHash = g.Key,
                            IsSeeding = isSeeding,
                            Candidates = candidates
                        };
                    })
                    .Where(g => g.Candidates.Count > 0)
                    .ToList();

                if (torrentGroups.Count > 0)
                {
                    var allocatedSlots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var tg in torrentGroups)
                    {
                        allocatedSlots[tg.InfoHash] = 0;
                    }

                    var remainingSlots = regularSlotCount;

                    // 1. Fair allocation: Allocate baseline minimum (1 slot) to each active torrent with interested peers (up to available regularSlotCount)
                    if (torrentGroups.Count <= remainingSlots)
                    {
                        foreach (var tg in torrentGroups)
                        {
                            allocatedSlots[tg.InfoHash] = 1;
                        }

                        remainingSlots -= torrentGroups.Count;
                    }
                    else
                    {
                        // More active torrents than regular slots: prioritize swarms by highest candidate peer rate
                        var prioritizedTorrents = torrentGroups
                            .OrderByDescending(g => g.IsSeeding ? g.Candidates[0].UploadRate : g.Candidates[0].DownloadRate)
                            .ThenByDescending(g => g.IsSeeding ? g.Candidates[0].BytesUploaded : g.Candidates[0].BytesDownloaded)
                            .Take(remainingSlots);

                        foreach (var tg in prioritizedTorrents)
                        {
                            allocatedSlots[tg.InfoHash] = 1;
                        }

                        remainingSlots = 0;
                    }

                    // 2. Distribute remaining discretionary slots among active torrents with demand by best candidate rates
                    while (remainingSlots > 0)
                    {
                        var bestTorrent = torrentGroups
                            .Where(g => g.Candidates.Count > allocatedSlots[g.InfoHash])
                            .OrderByDescending(g =>
                            {
                                var nextPeer = g.Candidates[allocatedSlots[g.InfoHash]];
                                return g.IsSeeding ? nextPeer.UploadRate : nextPeer.DownloadRate;
                            })
                            .ThenBy(g =>
                            {
                                var nextPeer = g.Candidates[allocatedSlots[g.InfoHash]];
                                return g.IsSeeding ? (nextPeer.LastUnchokedAt ?? DateTime.MinValue) : DateTime.MaxValue;
                            })
                            .FirstOrDefault();

                        if (bestTorrent == null)
                        {
                            break;
                        }

                        allocatedSlots[bestTorrent.InfoHash]++;
                        remainingSlots--;
                    }

                    // 3. Select top candidates per torrent up to its allocated slot count into selectedRegular
                    foreach (var tg in torrentGroups)
                    {
                        var slotsForTorrent = allocatedSlots[tg.InfoHash];
                        var selectedForTorrent = tg.Candidates.Take(slotsForTorrent);
                        foreach (var peer in selectedForTorrent)
                        {
                            selectedRegular.Add(peer);
                        }
                    }

                    // 4. Fill any leftover unfilled slots from remaining top candidates globally so no upload slots are wasted
                    if (selectedRegular.Count < regularSlotCount)
                    {
                        var leftoverCandidates = torrentGroups
                            .SelectMany(g => g.Candidates)
                            .Where(c => !selectedRegular.Contains(c))
                            .OrderByDescending(c => IsTorrentSeeding(c.InfoHash) ? c.UploadRate : c.DownloadRate)
                            .ThenBy(c => IsTorrentSeeding(c.InfoHash) ? (c.LastUnchokedAt ?? DateTime.MinValue) : DateTime.MaxValue)
                            .ThenBy(c => c.ConnectedAt)
                            .Take(regularSlotCount - selectedRegular.Count);

                        foreach (var peer in leftoverCandidates)
                        {
                            selectedRegular.Add(peer);
                        }
                    }
                }
            }

            var promotedSwarms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var conn in connections)
            {
                var isOptimistic = conn.IsOptimisticUnchoked;
                if (selectedRegular.Contains(conn))
                {
                    if (isOptimistic && !string.IsNullOrEmpty(conn.InfoHash))
                    {
                        promotedSwarms.Add(conn.InfoHash);
                        ClearOptimisticSlot(conn.InfoHash, conn);
                    }

                    conn.IsOptimisticUnchoked = false;
                    if (conn.AmChoking)
                    {
                        Unchoke(conn);
                    }
                }
                else if (!isOptimistic)
                {
                    if (!conn.AmChoking)
                    {
                        Choke(conn);
                    }
                }
            }

            if (maxUploadSlots > 1 && promotedSwarms.Count > 0)
            {
                foreach (var infoHash in promotedSwarms)
                {
                    ProcessOptimisticUnchokeForSwarmInternal(infoHash, connections, advanceRound: false);
                }
            }
        }
    }

    public void ProcessOptimisticUnchoke()
    {
        lock (_lock)
        {
            var connections = _connectionManager.GetAllConnections();
            if (connections.Count == 0)
            {
                return;
            }

            var byTorrent = connections
                .Where(c => !string.IsNullOrEmpty(c.InfoHash))
                .GroupBy(c => c.InfoHash, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var activeInfoHashes = new HashSet<string>(byTorrent.Select(g => g.Key), StringComparer.OrdinalIgnoreCase);
            var toRemove = _optimisticSlots.Keys.Where(k => !activeInfoHashes.Contains(k)).ToList();
            foreach (var k in toRemove)
            {
                _optimisticSlots.Remove(k);
            }

            foreach (var group in byTorrent)
            {
                ProcessOptimisticUnchokeForSwarmInternal(group.Key, connections, advanceRound: true);
            }
        }
    }

    public void ProcessOptimisticUnchokeForSwarm(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return;
        }

        lock (_lock)
        {
            ProcessOptimisticUnchokeForSwarmInternal(infoHash, null, advanceRound: false);
        }
    }

    public void PeerConnected(PeerConnection connection)
    {
        // Initial state is choking
        connection.AmChoking = true;
    }

    public void PeerDisconnected(PeerConnection connection)
    {
        if (connection == null)
        {
            return;
        }

        lock (_lock)
        {
            var infoHash = connection.InfoHash;
            if (connection.IsOptimisticUnchoked)
            {
                connection.IsOptimisticUnchoked = false;
                _currentOptimisticPeerKey = null;
                Choke(connection);
                if (!string.IsNullOrEmpty(infoHash))
                {
                    ClearOptimisticSlot(infoHash, connection);
                    ProcessOptimisticUnchokeForSwarmInternal(infoHash, null, advanceRound: false, excludedPeer: connection);
                }
            }
            else if (!connection.AmChoking)
            {
                if (!string.IsNullOrEmpty(infoHash))
                {
                    PromoteNextEligibleChokedPeer(infoHash, excludedPeer: connection);
                }
            }
        }
    }

    public void PeerInterestedChanged(PeerConnection connection)
    {
        if (connection == null)
        {
            return;
        }

        lock (_lock)
        {
            if (connection.IsSeed || connection.IsPartialSeed || !connection.HasMissingWantedPieces)
            {
                if (!connection.AmChoking)
                {
                    var wasOptimistic = connection.IsOptimisticUnchoked;
                    connection.IsOptimisticUnchoked = false;
                    Choke(connection);
                    if (wasOptimistic)
                    {
                        _currentOptimisticPeerKey = null;
                        if (!string.IsNullOrEmpty(connection.InfoHash))
                        {
                            ClearOptimisticSlot(connection.InfoHash, connection);
                            ProcessOptimisticUnchokeForSwarmInternal(connection.InfoHash, null, advanceRound: false, excludedPeer: connection);
                        }
                    }
                    else
                    {
                        PromoteNextEligibleChokedPeer(connection.InfoHash, excludedPeer: connection);
                    }
                }

                return;
            }

            if (connection.PeerInterested)
            {
                if (CanUnchoke(connection))
                {
                    Unchoke(connection);
                }
            }
            else
            {
                if (!connection.AmChoking)
                {
                    var wasOptimistic = connection.IsOptimisticUnchoked;
                    connection.IsOptimisticUnchoked = false;
                    Choke(connection);
                    if (wasOptimistic)
                    {
                        _currentOptimisticPeerKey = null;
                        if (!string.IsNullOrEmpty(connection.InfoHash))
                        {
                            ClearOptimisticSlot(connection.InfoHash, connection);
                            ProcessOptimisticUnchokeForSwarmInternal(connection.InfoHash, null, advanceRound: false, excludedPeer: connection);
                        }
                    }
                    else
                    {
                        PromoteNextEligibleChokedPeer(connection.InfoHash, excludedPeer: connection);
                    }
                }
                else if (connection.IsOptimisticUnchoked)
                {
                    connection.IsOptimisticUnchoked = false;
                    _currentOptimisticPeerKey = null;
                    if (!string.IsNullOrEmpty(connection.InfoHash))
                    {
                        ClearOptimisticSlot(connection.InfoHash, connection);
                        ProcessOptimisticUnchokeForSwarmInternal(connection.InfoHash, null, advanceRound: false, excludedPeer: connection);
                    }
                }
            }
        }
    }

    public void PeerBecameSeed(PeerConnection connection)
    {
        if (connection == null)
        {
            return;
        }

        connection.IsSeed = true;

        lock (_lock)
        {
            if (!connection.AmChoking)
            {
                var wasOptimistic = connection.IsOptimisticUnchoked;
                connection.IsOptimisticUnchoked = false;
                Choke(connection);
                if (wasOptimistic)
                {
                    if (!string.IsNullOrEmpty(connection.InfoHash))
                    {
                        ClearOptimisticSlot(connection.InfoHash, connection);
                        ProcessOptimisticUnchokeForSwarmInternal(connection.InfoHash, null, advanceRound: false, excludedPeer: connection);
                    }
                }
                else
                {
                    PromoteNextEligibleChokedPeer(connection.InfoHash, excludedPeer: connection);
                }
            }
        }
    }

    public void PeerBecamePartialSeed(PeerConnection connection)
    {
        if (connection == null)
        {
            return;
        }

        connection.IsPartialSeed = true;

        lock (_lock)
        {
            if (!connection.AmChoking)
            {
                var wasOptimistic = connection.IsOptimisticUnchoked;
                connection.IsOptimisticUnchoked = false;
                Choke(connection);
                if (wasOptimistic)
                {
                    if (!string.IsNullOrEmpty(connection.InfoHash))
                    {
                        ClearOptimisticSlot(connection.InfoHash, connection);
                        ProcessOptimisticUnchokeForSwarmInternal(connection.InfoHash, null, advanceRound: false, excludedPeer: connection);
                    }
                }
                else
                {
                    PromoteNextEligibleChokedPeer(connection.InfoHash, excludedPeer: connection);
                }
            }
        }
    }

    public void UpdatePeerActivity(PeerConnection connection)
    {
        connection.LastRequestReceived = DateTime.UtcNow;
        connection.UpdateTransferRates();
        if (connection.IsSnubbed)
        {
            _logger.Debug("Peer {0}:{1} unsnubbed due to active request", connection.RemoteIp, connection.RemotePort);
            connection.IsSnubbed = false;
        }
    }

    public bool CanUnchoke(PeerConnection connection)
    {
        if (connection == null || connection.IsSeed || connection.IsPartialSeed || !connection.HasMissingWantedPieces)
        {
            return false;
        }

        return CanUnchoke(connection.InfoHash);
    }

    public bool CanUnchoke(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return false;
        }

        lock (_lock)
        {
            var maxUploadSlots = _connectionManager != null
                ? _connectionManager.GetDynamicUploadSlotCount(infoHash)
                : _configService.MaxUploadSlots;
            if (maxUploadSlots <= 0)
            {
                maxUploadSlots = _configService.MaxUploadSlots;
            }

            if (maxUploadSlots <= 0)
            {
                return true;
            }

            var regularSlotCount = maxUploadSlots > 1 ? maxUploadSlots - 1 : maxUploadSlots;
            var unchokedCount = _connectionManager.GetAllConnections()
                .Count(c => !c.AmChoking && string.Equals(c.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
            return unchokedCount < regularSlotCount;
        }
    }

    public bool IsTorrentSeeding(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return false;
        }

        lock (_lock)
        {
            if (_explicitSeedingTorrents.Contains(infoHash))
            {
                return true;
            }
        }

        if (_torrentService != null)
        {
            try
            {
                var torrent = _torrentService.FindByInfoHash(infoHash) ?? _torrentService.GetByInfoHash(infoHash);
                if (torrent != null)
                {
                    return torrent.Status == TorrentStatus.Seeding || torrent.Progress >= 1.0;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to check seeding state for torrent {0}", infoHash);
            }
        }

        return false;
    }

    public void SetTorrentSeeding(string infoHash, bool isSeeding)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return;
        }

        lock (_lock)
        {
            if (isSeeding)
            {
                _explicitSeedingTorrents.Add(infoHash);
            }
            else
            {
                _explicitSeedingTorrents.Remove(infoHash);
            }
        }
    }

    private void PromoteNextEligibleChokedPeer(string infoHash, PeerConnection excludedPeer = null)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return;
        }

        lock (_lock)
        {
            if (!CanUnchoke(infoHash))
            {
                return;
            }

            var torrentConnections = _connectionManager.GetConnections(infoHash);
            if (torrentConnections == null || torrentConnections.Count == 0)
            {
                torrentConnections = _connectionManager.GetAllConnections()
                    .Where(c => string.Equals(c.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var isSeeding = IsTorrentSeeding(infoHash);
            var eligibleCandidates = torrentConnections
                .Where(c => !ReferenceEquals(c, excludedPeer) && c.PeerInterested && c.AmChoking && !c.IsSnubbed && !c.IsSeed && !c.IsPartialSeed && c.HasMissingWantedPieces);

            var nextPeer = isSeeding
                ? eligibleCandidates
                    .OrderByDescending(c => c.UploadRate)
                    .ThenBy(c => c.LastUnchokedAt ?? DateTime.MinValue)
                    .ThenBy(c => c.ConnectedAt)
                    .FirstOrDefault()
                : eligibleCandidates
                    .OrderByDescending(c => c.DownloadRate)
                    .ThenByDescending(c => c.UploadRate)
                    .ThenByDescending(c => c.BytesUploaded)
                    .ThenBy(c => c.ConnectedAt)
                    .FirstOrDefault();

            if (nextPeer != null)
            {
                _logger.Debug("Immediately promoting next eligible choked peer {0}:{1} for torrent {2}", nextPeer.RemoteIp, nextPeer.RemotePort, infoHash);
                Unchoke(nextPeer);
            }
        }
    }

    private void ClearOptimisticSlot(string infoHash, PeerConnection connection)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return;
        }

        if (_optimisticSlots.TryGetValue(infoHash, out var slot) && (slot.Peer == null || ReferenceEquals(slot.Peer, connection)))
        {
            _optimisticSlots.Remove(infoHash);
            _currentOptimisticPeerKey = null;
        }
    }

    private void ProcessOptimisticUnchokeForSwarmInternal(
        string infoHash,
        List<PeerConnection> allConnections,
        bool advanceRound,
        PeerConnection excludedPeer = null)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return;
        }

        var maxUploadSlots = _connectionManager != null
            ? _connectionManager.GetDynamicUploadSlotCount(infoHash)
            : _configService.MaxUploadSlots;
        if (maxUploadSlots <= 0)
        {
            maxUploadSlots = _configService.MaxUploadSlots;
        }

        if (maxUploadSlots <= 1)
        {
            if (_optimisticSlots.TryGetValue(infoHash, out var existingSlot))
            {
                if (existingSlot.Peer != null && existingSlot.Peer.IsOptimisticUnchoked)
                {
                    existingSlot.Peer.IsOptimisticUnchoked = false;
                    Choke(existingSlot.Peer);
                }

                _optimisticSlots.Remove(infoHash);
                _currentOptimisticPeerKey = null;
            }

            return;
        }

        var swarmConnections = (allConnections != null
            ? allConnections.Where(c => string.Equals(c.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase))
            : (_connectionManager.GetConnections(infoHash) ?? _connectionManager.GetAllConnections().Where(c => string.Equals(c.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        OptimisticSlot slot = null;
        if (_optimisticSlots.TryGetValue(infoHash, out var trackedSlot))
        {
            slot = trackedSlot;
        }
        else
        {
            var existingPeer = swarmConnections.FirstOrDefault(c =>
                !ReferenceEquals(c, excludedPeer) &&
                c.IsOptimisticUnchoked &&
                !c.AmChoking &&
                c.PeerInterested &&
                !c.IsSeed &&
                !c.IsPartialSeed &&
                c.HasMissingWantedPieces);

            if (existingPeer != null)
            {
                slot = new OptimisticSlot
                {
                    Peer = existingPeer,
                    RoundsRemaining = OptimisticUnchokeRounds
                };
                _optimisticSlots[infoHash] = slot;
                _currentOptimisticPeerKey = $"{existingPeer.RemoteIp}:{existingPeer.RemotePort}";
            }
        }

        var isPeerValid = slot != null &&
                          slot.Peer != null &&
                          !ReferenceEquals(slot.Peer, excludedPeer) &&
                          slot.Peer.IsOptimisticUnchoked &&
                          !slot.Peer.AmChoking &&
                          slot.Peer.PeerInterested &&
                          !slot.Peer.IsSeed &&
                          !slot.Peer.IsPartialSeed &&
                          slot.Peer.HasMissingWantedPieces &&
                          swarmConnections.Contains(slot.Peer);

        PeerConnection previousPeer = null;

        if (isPeerValid)
        {
            if (advanceRound)
            {
                slot.RoundsRemaining--;
                if (slot.RoundsRemaining > 0)
                {
                    _logger.Debug(
                        "Retaining optimistic unchoke peer {0}:{1} for swarm {2} ({3} rounds remaining)",
                        slot.Peer.RemoteIp,
                        slot.Peer.RemotePort,
                        infoHash,
                        slot.RoundsRemaining);
                    return;
                }

                _logger.Debug(
                    "Optimistic unchoke persistence expired (3 rounds) for peer {0}:{1} on swarm {2}. Rotating.",
                    slot.Peer.RemoteIp,
                    slot.Peer.RemotePort,
                    infoHash);
                previousPeer = slot.Peer;
                previousPeer.IsOptimisticUnchoked = false;
                Choke(previousPeer);
                _optimisticSlots.Remove(infoHash);
                _currentOptimisticPeerKey = null;
                slot = null;
            }
            else
            {
                return;
            }
        }
        else
        {
            if (slot != null)
            {
                _optimisticSlots.Remove(infoHash);
                _currentOptimisticPeerKey = null;
                slot = null;
            }
        }

        var candidates = swarmConnections
            .Where(c => !ReferenceEquals(c, excludedPeer) &&
                        c.PeerInterested &&
                        c.AmChoking &&
                        !c.IsSeed &&
                        !c.IsPartialSeed &&
                        c.HasMissingWantedPieces &&
                        !c.IsOptimisticUnchoked)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var candidatePool = (candidates.Count > 1 && previousPeer != null)
            ? candidates.Where(c => !ReferenceEquals(c, previousPeer)).ToList()
            : candidates;

        if (candidatePool.Count == 0)
        {
            candidatePool = candidates;
        }

        var chosen = SelectOptimisticCandidate(candidatePool);
        if (chosen != null)
        {
            chosen.IsOptimisticUnchoked = true;
            _currentOptimisticPeerKey = $"{chosen.RemoteIp}:{chosen.RemotePort}";
            _optimisticSlots[infoHash] = new OptimisticSlot
            {
                Peer = chosen,
                RoundsRemaining = OptimisticUnchokeRounds
            };

            _logger.Debug("Optimistically unchoking peer {0}:{1} for torrent {2}", chosen.RemoteIp, chosen.RemotePort, infoHash);
            Unchoke(chosen);
        }
    }

    private PeerConnection SelectOptimisticCandidate(List<PeerConnection> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(NewPeerThresholdSeconds);
        var totalWeight = 0;
        foreach (var c in candidates)
        {
            totalWeight += (c.ConnectedAt > cutoff) ? NewPeerSelectionWeight : ExistingPeerSelectionWeight;
        }

        var sample = _random.Next(0, totalWeight);
        var running = 0;
        foreach (var c in candidates)
        {
            var weight = (c.ConnectedAt > cutoff) ? NewPeerSelectionWeight : ExistingPeerSelectionWeight;
            running += weight;
            if (sample < running)
            {
                return c;
            }
        }

        return candidates.Last();
    }

    private sealed class OptimisticSlot
    {
        public PeerConnection Peer { get; set; }
        public int RoundsRemaining { get; set; }
    }

    private void Unchoke(PeerConnection connection)
    {
        if (connection.AmChoking)
        {
            connection.AmChoking = false;
            connection.LastUnchokedAt = DateTime.UtcNow;
            connection.LastRequestReceived = DateTime.UtcNow;
            try
            {
                connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                _logger.Trace("Sent UNCHOKE to peer {0}:{1}", connection.RemoteIp, connection.RemotePort);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to send unchoke to {0}:{1}", connection.RemoteIp, connection.RemotePort);
            }

            try
            {
                PeerUnchoked?.Invoke(connection);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to notify PeerUnchoked for {0}:{1}", connection.RemoteIp, connection.RemotePort);
            }
        }
    }

    public void Choke(PeerConnection connection)
    {
        if (connection == null)
        {
            return;
        }

        if (!connection.AmChoking)
        {
            connection.AmChoking = true;
            try
            {
                connection.SendMessage(new PeerMessage { Type = PeerMessageType.Choke });
                _logger.Trace("Sent CHOKE to peer {0}:{1}", connection.RemoteIp, connection.RemotePort);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to send choke to {0}:{1}", connection.RemoteIp, connection.RemotePort);
            }

            if (connection.SupportsFastExtension)
            {
                RejectPendingRequestsNotAllowedFast(connection);
            }
        }
    }

    private void RejectPendingRequestsNotAllowedFast(PeerConnection connection)
    {
        if (connection == null || !connection.SupportsFastExtension)
        {
            return;
        }

        var allowedFast = new HashSet<int>();
        if (_fastExtensionHandler != null)
        {
            allowedFast.UnionWith(_fastExtensionHandler.GetAllowedFastSet(connection));
        }

        if (connection.AllowedFastPieces != null)
        {
            allowedFast.UnionWith(connection.AllowedFastPieces);
        }

        if (connection.RemoteAllowedFastPieces != null)
        {
            allowedFast.UnionWith(connection.RemoteAllowedFastPieces);
        }

        lock (connection.PendingIncomingRequests)
        {
            if (connection.PendingIncomingRequests.Count == 0)
            {
                return;
            }

            var toReject = connection.PendingIncomingRequests.Where(r => !allowedFast.Contains(r.PieceIndex)).ToList();
            foreach (var req in toReject)
            {
                try
                {
                    var payload = new byte[12];
                    BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), req.PieceIndex);
                    BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), req.Begin);
                    BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(8, 4), req.Length);

                    connection.SendMessage(new PeerMessage
                    {
                        Type = PeerMessageType.RejectRequest,
                        Payload = payload
                    });

                    _logger.Trace("Sent REJECT_REQUEST to peer {0}:{1} for choked piece {2}", connection.RemoteIp, connection.RemotePort, req.PieceIndex);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to send reject request to {0}:{1}", connection.RemoteIp, connection.RemotePort);
                }

                connection.PendingIncomingRequests.Remove(req);
            }
        }
    }

    private List<PeerConnection> OrderCandidatesWithHysteresis(
        IEnumerable<PeerConnection> eligible,
        bool isSeeding,
        DateTime now)
    {
        var eligibleList = eligible.ToList();
        var currentUnchoked = eligibleList
            .Where(c => !c.AmChoking && !c.IsOptimisticUnchoked)
            .OrderBy(c => c, new PeerComparer(isSeeding))
            .ToList();

        var challengers = eligibleList
            .Where(c => c.AmChoking || c.IsOptimisticUnchoked)
            .OrderBy(c => c, new PeerComparer(isSeeding))
            .ToList();

        if (currentUnchoked.Count == 0)
        {
            return challengers;
        }

        if (challengers.Count == 0)
        {
            return currentUnchoked;
        }

        var result = new List<PeerConnection>(eligibleList.Count);
        var uIndex = 0;
        var cIndex = 0;

        while (uIndex < currentUnchoked.Count && cIndex < challengers.Count)
        {
            var u = currentUnchoked[uIndex];
            var c = challengers[cIndex];

            if (CanChallengerPrecedeIncumbent(c, u, isSeeding, now))
            {
                result.Add(c);
                cIndex++;
            }
            else
            {
                result.Add(u);
                uIndex++;
            }
        }

        while (uIndex < currentUnchoked.Count)
        {
            result.Add(currentUnchoked[uIndex++]);
        }

        while (cIndex < challengers.Count)
        {
            result.Add(challengers[cIndex++]);
        }

        return result;
    }

    private static bool CanChallengerPrecedeIncumbent(
        PeerConnection challenger,
        PeerConnection incumbent,
        bool isSeeding,
        DateTime now)
    {
        var duration = incumbent.LastUnchokedAt.HasValue
            ? (now - incumbent.LastUnchokedAt.Value).TotalSeconds
            : MinUnchokeDurationSeconds;

        // 1. If incumbent is within minimum unchoke duration, it is protected from being displaced
        if (incumbent.LastUnchokedAt.HasValue && duration >= 0 && duration < MinUnchokeDurationSeconds)
        {
            return false;
        }

        var challengerRate = isSeeding ? challenger.UploadRate : challenger.DownloadRate;
        var incumbentRate = isSeeding ? incumbent.UploadRate : incumbent.DownloadRate;

        // 2. If lease has expired, standard rate comparison applies
        if (duration >= MaxUnchokeLeaseSeconds)
        {
            return ComparePeersBase(challenger, incumbent, isSeeding) < 0;
        }

        // 3. Otherwise, hysteresis margin applies: challenger must exceed incumbent's combined rate by > 15%
        if (challengerRate > incumbentRate * (1.0 + ChokeHysteresisMargin))
        {
            return true;
        }

        return false;
    }

    private static int ComparePeersBase(PeerConnection a, PeerConnection b, bool isSeeding)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        if (a == null)
        {
            return 1;
        }

        if (b == null)
        {
            return -1;
        }

        if (isSeeding)
        {
            var rateCompare = b.UploadRate.CompareTo(a.UploadRate);
            if (rateCompare != 0)
            {
                return rateCompare;
            }

            var aLast = a.LastUnchokedAt ?? DateTime.MinValue;
            var bLast = b.LastUnchokedAt ?? DateTime.MinValue;
            var lastCompare = aLast.CompareTo(bLast);
            if (lastCompare != 0)
            {
                return lastCompare;
            }

            return a.ConnectedAt.CompareTo(b.ConnectedAt);
        }
        else
        {
            var rateCompare = b.DownloadRate.CompareTo(a.DownloadRate);
            if (rateCompare != 0)
            {
                return rateCompare;
            }

            var aLast = a.LastUnchokedAt ?? DateTime.MinValue;
            var bLast = b.LastUnchokedAt ?? DateTime.MinValue;
            var lastCompare = aLast.CompareTo(bLast);
            if (lastCompare != 0)
            {
                return lastCompare;
            }

            var bytesCompare = b.BytesDownloaded.CompareTo(a.BytesDownloaded);
            if (bytesCompare != 0)
            {
                return bytesCompare;
            }

            return a.ConnectedAt.CompareTo(b.ConnectedAt);
        }
    }

    private sealed class PeerComparer : IComparer<PeerConnection>
    {
        private readonly bool _isSeeding;

        public PeerComparer(bool isSeeding)
        {
            _isSeeding = isSeeding;
        }

        public int Compare(PeerConnection x, PeerConnection y)
        {
            return ComparePeersBase(x, y, _isSeeding);
        }
    }
}
