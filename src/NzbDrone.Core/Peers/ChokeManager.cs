using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers;

public interface IChokeManager
{
    void ProcessChoking();
    void ProcessRegularUnchoke();
    void ProcessOptimisticUnchoke();
    void PeerConnected(PeerConnection connection);
    void PeerDisconnected(PeerConnection connection);
    void PeerInterestedChanged(PeerConnection connection);
    void PeerBecameSeed(PeerConnection connection);
    void UpdatePeerActivity(PeerConnection connection);
    bool CanUnchoke(PeerConnection connection);
    bool CanUnchoke(string infoHash);
    bool IsTorrentSeeding(string infoHash);
    void SetTorrentSeeding(string infoHash, bool isSeeding);
}

public class ChokeManager : BackgroundService, IChokeManager
{
    public const int MinUnchokeDurationSeconds = 20;
    public const int MaxUnchokeLeaseSeconds = 60;
    public const double ChokeHysteresisMargin = 0.15;

    private const int RegularUnchokeIntervalSeconds = 10;
    private const int OptimisticUnchokeIntervalSeconds = 30;
    private const int SnubbingThresholdSeconds = 60;

    private readonly IConnectionManager _connectionManager;
    private readonly IConfigService _configService;
    private readonly ITorrentService _torrentService;
    private readonly IRandomNumberGenerator _random;
    private readonly Logger _logger;
    private readonly object _lock = new();
    private readonly HashSet<string> _explicitSeedingTorrents = new(StringComparer.OrdinalIgnoreCase);

    private DateTime _lastRegularUnchoke = DateTime.MinValue;
    private DateTime _lastOptimisticUnchoke = DateTime.MinValue;

    public ChokeManager(
        IConnectionManager connectionManager,
        IConfigService configService,
        ITorrentService torrentService = null,
        IRandomNumberGenerator random = null)
    {
        _connectionManager = connectionManager;
        _configService = configService;
        _torrentService = torrentService;
        _random = random ?? new RandomNumberGenerator();
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("ChokeManager service started with 10s regular and 30s optimistic unchoke timers.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

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
            foreach (var conn in connections)
            {
                if (!conn.AmChoking)
                {
                    var idleTime = (now - conn.LastRequestReceived).TotalSeconds;
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

            // Anti-seed choking: choke any unchoked peer that has become a seed
            foreach (var conn in connections)
            {
                if (!conn.AmChoking && conn.IsSeed)
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
                        var eligible = g.Where(c => c.PeerInterested && !c.IsSnubbed && !c.IsSeed);
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
                            .OrderByDescending(g => g.IsSeeding ? g.Candidates[0].UploadRate : (g.Candidates[0].UploadRate + g.Candidates[0].DownloadRate))
                            .ThenByDescending(g => g.Candidates[0].BytesUploaded)
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
                                return g.IsSeeding ? nextPeer.UploadRate : (nextPeer.UploadRate + nextPeer.DownloadRate);
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
                            .OrderByDescending(c => IsTorrentSeeding(c.InfoHash) ? c.UploadRate : (c.UploadRate + c.DownloadRate))
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
                foreach (var group in byTorrent)
                {
                    if (!promotedSwarms.Contains(group.Key))
                    {
                        continue;
                    }

                    var hasOptimisticPeer = group.Any(c => c.IsOptimisticUnchoked && !c.AmChoking);
                    if (!hasOptimisticPeer)
                    {
                        var eligibleCandidates = group
                            .Where(c => !selectedRegular.Contains(c) && c.PeerInterested && c.AmChoking)
                            .ToList();

                        if (eligibleCandidates.Count > 0)
                        {
                            var chosenIndex = _random.Next(0, eligibleCandidates.Count);
                            var chosen = eligibleCandidates[chosenIndex];

                            chosen.IsOptimisticUnchoked = true;
                            _logger.Debug("Immediately elected replacement optimistic peer {0}:{1} for torrent {2}", chosen.RemoteIp, chosen.RemotePort, group.Key);
                            Unchoke(chosen);
                        }
                    }
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

            var maxUploadSlots = _connectionManager != null
                ? _connectionManager.GetDynamicUploadSlotCount(connections.FirstOrDefault(c => !string.IsNullOrEmpty(c.InfoHash))?.InfoHash)
                : _configService.MaxUploadSlots;
            if (maxUploadSlots <= 0)
            {
                maxUploadSlots = _configService.MaxUploadSlots;
            }

            if (maxUploadSlots <= 1)
            {
                return;
            }

            var byTorrent = connections
                .Where(c => !string.IsNullOrEmpty(c.InfoHash))
                .GroupBy(c => c.InfoHash, StringComparer.OrdinalIgnoreCase);

            // Clear previous optimistic peer flag
            foreach (var conn in connections)
            {
                if (conn.IsOptimisticUnchoked)
                {
                    conn.IsOptimisticUnchoked = false;

                    // If not regular unchoked, choke it
                    Choke(conn);
                }
            }

            foreach (var group in byTorrent)
            {
                // Find all interested peers that are currently choked in this torrent swarm (excluding seeds)
                var chokedInterested = group
                    .Where(c => c.PeerInterested && c.AmChoking && !c.IsSeed)
                    .ToList();

                if (chokedInterested.Count == 0)
                {
                    continue;
                }

                // Pick a random choked interested peer for optimistic unchoke in this swarm
                var chosenIndex = _random.Next(0, chokedInterested.Count);
                var chosen = chokedInterested[chosenIndex];

                chosen.IsOptimisticUnchoked = true;
                _logger.Debug("Optimistically unchoking peer {0}:{1} for torrent {2}", chosen.RemoteIp, chosen.RemotePort, group.Key);
                Unchoke(chosen);
            }
        }
    }

    public void PeerConnected(PeerConnection connection)
    {
        // Initial state is choking
        connection.AmChoking = true;
    }

    public void PeerDisconnected(PeerConnection connection)
    {
    }

    public void PeerInterestedChanged(PeerConnection connection)
    {
        if (connection == null)
        {
            return;
        }

        if (connection.IsSeed)
        {
            if (!connection.AmChoking)
            {
                connection.IsOptimisticUnchoked = false;
                Choke(connection);
                PromoteNextEligibleChokedPeer(connection.InfoHash);
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
                connection.IsOptimisticUnchoked = false;
                Choke(connection);
                PromoteNextEligibleChokedPeer(connection.InfoHash);
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
                connection.IsOptimisticUnchoked = false;
                Choke(connection);
                PromoteNextEligibleChokedPeer(connection.InfoHash);
            }
        }
    }

    public void UpdatePeerActivity(PeerConnection connection)
    {
        connection.LastRequestReceived = DateTime.UtcNow;
        if (connection.IsSnubbed)
        {
            _logger.Debug("Peer {0}:{1} unsnubbed due to active request", connection.RemoteIp, connection.RemotePort);
            connection.IsSnubbed = false;
        }
    }

    public bool CanUnchoke(PeerConnection connection)
    {
        if (connection == null || connection.IsSeed)
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

        var unchokedCount = _connectionManager.GetAllConnections()
            .Count(c => !c.AmChoking && string.Equals(c.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
        return unchokedCount < maxUploadSlots;
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

    private void PromoteNextEligibleChokedPeer(string infoHash)
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
                .Where(c => c.PeerInterested && c.AmChoking && !c.IsSnubbed && !c.IsSeed);

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

    private void Unchoke(PeerConnection connection)
    {
        if (connection.AmChoking)
        {
            connection.AmChoking = false;
            connection.LastUnchokedAt = DateTime.UtcNow;
            try
            {
                connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                _logger.Trace("Sent UNCHOKE to peer {0}:{1}", connection.RemoteIp, connection.RemotePort);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to send unchoke to {0}:{1}", connection.RemoteIp, connection.RemotePort);
            }
        }
    }

    private void Choke(PeerConnection connection)
    {
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

        var challengerRate = isSeeding ? challenger.UploadRate : (challenger.UploadRate + challenger.DownloadRate);
        var incumbentRate = isSeeding ? incumbent.UploadRate : (incumbent.UploadRate + incumbent.DownloadRate);

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
            var aRate = a.UploadRate + a.DownloadRate;
            var bRate = b.UploadRate + b.DownloadRate;
            var rateCompare = bRate.CompareTo(aRate);
            if (rateCompare != 0)
            {
                return rateCompare;
            }

            var bytesCompare = b.BytesUploaded.CompareTo(a.BytesUploaded);
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
