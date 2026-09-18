using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Seeding.Distribution;
using NzbDrone.Core.Seeding.Scheduling;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Simulation.Swarm;
using NzbDrone.Core.Simulation.Traffic;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerServer;

namespace NzbDrone.Core.Seeding;

public class SeedingEngine : BackgroundService
{
    private const int LocalPeerPort = 6881;

    private TimeSpan TickInterval => TimeSpan.FromSeconds(Math.Max(1, _configService.UiRefreshRateSec));

    private readonly ITorrentService _torrentService;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ISpeedDistributionManager _distributionManager;
    private readonly ISpeedScheduler _speedScheduler;
    private readonly IConfigService _configService;
    private readonly IEventAggregator _eventAggregator;
    private readonly IPeerDatabase _peerDatabase;
    private readonly IConnectionManager _connectionManager;
    private readonly ITorrentEventLogService _eventLogService;
    private readonly ITorrentStateMachine _stateMachine;
    private readonly ISpeedPolicy _speedPolicy;
    private readonly IStopPolicy _stopPolicy;
    private readonly ITrafficPatternSimulator _trafficPatternSimulator;
    private readonly IClientBehaviorSimulator _clientBehaviorSimulator;
    private readonly ISwarmAnalyzer _swarmAnalyzer;
    private readonly ICategoryService _categoryService;
    private readonly ITagService _tagService;
    private readonly ISystemClock _clock;
    private readonly IRandomNumberGenerator _random;
    private readonly Logger _logger;
    private readonly Dictionary<int, long> _prevUploaded = new();
    private readonly Dictionary<int, long> _prevDownloaded = new();
    private readonly Dictionary<int, long> _sessionStartUploaded = new();
    private readonly Dictionary<int, long> _sessionStartDownloaded = new();
    private readonly Dictionary<int, Queue<(DateTime Timestamp, long Speed)>> _uploadSpeedHistory = new();
    private readonly Dictionary<int, Queue<(DateTime Timestamp, long Speed)>> _downloadSpeedHistory = new();
    private readonly HashSet<int> _stalledTorrentIds = new();
    private readonly HashSet<int> _seedingTimeReachedTorrentIds = new();
    private bool _speedThresholdExceededState;
    private long _lastTickTimestamp;

    private string _localPeerId;

    public SeedingEngine(
        ITorrentService torrentService,
        ISpeedDistributionManager distributionManager,
        ISpeedScheduler speedScheduler,
        IConfigService configService,
        IEventAggregator eventAggregator,
        IPeerDatabase peerDatabase,
        IConnectionManager connectionManager,
        ITorrentEventLogService eventLogService,
        ITorrentStateMachine stateMachine = null,
        ISpeedPolicy speedPolicy = null,
        IStopPolicy stopPolicy = null,
        ISystemClock clock = null,
        IRandomNumberGenerator random = null,
        ITrafficPatternSimulator trafficPatternSimulator = null,
        IClientBehaviorSimulator clientBehaviorSimulator = null,
        ISwarmAnalyzer swarmAnalyzer = null,
        ICategoryService categoryService = null,
        ITagService tagService = null,
        ITorrentRepository torrentRepository = null)
    {
        _torrentService = torrentService;
        _torrentRepository = torrentRepository;
        _distributionManager = distributionManager;
        _speedScheduler = speedScheduler;
        _configService = configService;
        _eventAggregator = eventAggregator;
        _peerDatabase = peerDatabase;
        _connectionManager = connectionManager;
        _eventLogService = eventLogService;
        _clock = clock ?? new SystemClock();
        _random = random ?? new NzbDrone.Common.EnvironmentInfo.RandomNumberGenerator();
        _stateMachine = stateMachine ?? new TorrentStateMachine(eventLogService, eventAggregator, torrentService);
        _stopPolicy = stopPolicy ?? new StopPolicy(configService, _random, tagService);
        _swarmAnalyzer = swarmAnalyzer ?? new SwarmAnalyzer(configService);
        _trafficPatternSimulator = trafficPatternSimulator ?? new TrafficPatternSimulator(configService, _random, _clock);
        _clientBehaviorSimulator = clientBehaviorSimulator;
        _categoryService = categoryService;
        _tagService = tagService;
        _speedPolicy = speedPolicy ?? new SpeedPolicy(distributionManager, speedScheduler, configService, eventLogService, _stateMachine, _stopPolicy, _random, _swarmAnalyzer, eventAggregator, categoryService, tagService);
        _logger = LogManager.GetCurrentClassLogger();
    }

    private HashSet<int> SelectStoppedTorrents(List<Torrent> torrents) => _stopPolicy.SelectStoppedTorrents(torrents);

    private HashSet<int> SelectDownloadStoppedTorrents(List<Torrent> torrents) => _stopPolicy.SelectDownloadStoppedTorrents(torrents);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configService.AutoStart)
        {
            _logger.Info("AutoStart is disabled, waiting for configuration change or ForceStart torrent before starting seeding engine");

            while (!stoppingToken.IsCancellationRequested && !_configService.AutoStart && !HasForceStartTorrents())
            {
                await Task.Delay(TickInterval, stoppingToken);
            }

            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            _logger.Info("AutoStart enabled or ForceStart torrent detected, resuming seeding engine startup");
        }

        if (_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled)
        {
            var activeProfile = _clientBehaviorSimulator.GetActiveProfile();
            _localPeerId = activeProfile?.GeneratePeerId();
        }

        if (string.IsNullOrEmpty(_localPeerId))
        {
            var prefix = _configService.PeerIdPrefix;
            var suffix = new char[20 - prefix.Length];
            var suffixBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(suffix.Length);
            for (var i = 0; i < suffix.Length; i++)
            {
                suffix[i] = (char)('A' + (suffixBytes[i] % 26));
            }

            _localPeerId = prefix + new string(suffix);
        }

        _logger.Info("Seeding engine started, local peer ID: {0}", _localPeerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            var tickStart = Stopwatch.GetTimestamp();

            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Seeding engine tick error");
            }

            var elapsedMs = (long)((Stopwatch.GetTimestamp() - tickStart) * 1000.0 / Stopwatch.Frequency);
            var remainingDelay = Math.Max(0, (int)TickInterval.TotalMilliseconds - (int)elapsedMs);

            if (remainingDelay > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(remainingDelay), stoppingToken);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Seeding engine stopping");
        try
        {
            if (_connectionManager != null)
            {
                await _connectionManager.DisconnectAllAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error disconnecting connections during SeedingEngine stop");
        }

        await base.StopAsync(cancellationToken);
    }

    private void Tick()
    {
        var now = Stopwatch.GetTimestamp();
        TimeSpan actualDelta;

        if (_lastTickTimestamp == 0)
        {
            actualDelta = TickInterval;
        }
        else
        {
            var elapsedSeconds = (double)(now - _lastTickTimestamp) / Stopwatch.Frequency;
            elapsedSeconds = Math.Clamp(elapsedSeconds, 0.1, 10.0);
            actualDelta = TimeSpan.FromSeconds(elapsedSeconds);
        }

        _lastTickTimestamp = now;

        var allTorrents = _torrentService.GetAll();
        var initialStatuses = allTorrents
            .GroupBy(t => t.Id)
            .ToDictionary(g => g.Key, g => g.First().Status);
        var autoStart = _configService.AutoStart;

        foreach (var torrent in allTorrents)
        {
            if (!string.IsNullOrEmpty(torrent.InfoHash))
            {
                var stats = _peerDatabase.GetStats(torrent.InfoHash);
                if (stats != null)
                {
                    torrent.Seeders = stats.Complete;
                    torrent.Leechers = stats.Incomplete;
                }
            }
        }

        var downloadingTorrents = allTorrents
            .Where(t => t.Status == TorrentStatus.Downloading && (autoStart || t.ForceStart))
            .ToList();

        var seedingTorrents = allTorrents
            .Where(t => t.Status == TorrentStatus.Seeding && (autoStart || t.ForceStart))
            .ToList();

        EvaluateSuperSeeding(allTorrents.Where(t => (t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Downloading) && t.SuperSeeding));

        if (_categoryService != null && autoStart)
        {
            var queuedTorrents = allTorrents
                .Where(t => t.Status == TorrentStatus.Queued)
                .OrderBy(t => t.SortOrder)
                .ThenBy(t => t.Id)
                .ToList();

            if (queuedTorrents.Count > 0)
            {
                var globalMaxDl = _configService.MaxActiveDownloads > 0 ? (int?)_configService.MaxActiveDownloads : null;
                var toPromote = _categoryService.EvaluateDownloadQueue(queuedTorrents, downloadingTorrents, globalMaxDl);
                if (toPromote.Count > 0)
                {
                    foreach (var torrent in toPromote)
                    {
                        torrent.Status = TorrentStatus.Downloading;
                        torrent.LastActive = _clock.UtcNow;
                        _torrentService.Update(torrent);
                        downloadingTorrents.Add(torrent);
                        _eventAggregator.PublishEvent(new TorrentStatusChangedEvent(torrent, TorrentStatus.Queued, TorrentStatus.Downloading) { IsQueueManagerInternal = true });
                    }
                }
            }
        }

        var isAnyPrivate = allTorrents.Any(t => t.IsPrivate && (t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Downloading));

        if (string.IsNullOrEmpty(_localPeerId))
        {
            if (_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled)
            {
                var activeProfile = _clientBehaviorSimulator.GetActiveProfile(isAnyPrivate);
                _localPeerId = activeProfile?.GeneratePeerId();
            }

            if (string.IsNullOrEmpty(_localPeerId))
            {
                var prefix = _configService.PeerIdPrefix;
                var suffix = new char[20 - prefix.Length];
                var suffixBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(suffix.Length);
                for (var i = 0; i < suffix.Length; i++)
                {
                    suffix[i] = (char)('A' + (suffixBytes[i] % 26));
                }

                _localPeerId = prefix + new string(suffix);
            }
        }
        else if (_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled && downloadingTorrents.Count == 0 && seedingTorrents.Count == 0)
        {
            var activeProfile = _clientBehaviorSimulator.GetActiveProfile(isAnyPrivate);
            if (activeProfile != null)
            {
                _localPeerId = activeProfile.GeneratePeerId();
            }
        }

        if (downloadingTorrents.Count == 0 && seedingTorrents.Count == 0)
        {
            var idleToUpdate = new List<Torrent>();
            foreach (var t in allTorrents)
            {
                if (t.UploadSpeed != 0 || t.DownloadSpeed != 0 || t.Active)
                {
                    t.UploadSpeed = 0;
                    t.DownloadSpeed = 0;
                    t.Active = false;
                    _uploadSpeedHistory.Remove(t.Id);
                    _downloadSpeedHistory.Remove(t.Id);
                    idleToUpdate.Add(t);
                }
            }

            if (idleToUpdate.Count > 0)
            {
                _torrentService.UpdateMany(idleToUpdate);
            }

            return;
        }

        var limits = _speedPolicy.GetEffectiveLimits();

        if (!string.Equals(_configService.TrafficPatternProfile, "off", StringComparison.OrdinalIgnoreCase))
        {
            var totalPeers = _connectionManager.ActiveCount;
            var speedMultiplier = _trafficPatternSimulator.GetSpeedMultiplier(SeedingProfile.Balanced, totalPeers);
            if (limits.MaxUploadSpeed != SpeedLimits.Unlimited)
            {
                limits.MaxUploadSpeed = Math.Max(0, (long)(limits.MaxUploadSpeed * speedMultiplier));
            }

            if (limits.MaxDownloadSpeed != SpeedLimits.Unlimited)
            {
                limits.MaxDownloadSpeed = Math.Max(0, (long)(limits.MaxDownloadSpeed * speedMultiplier));
            }
        }

        if (downloadingTorrents.Count > 0)
        {
            _speedPolicy.ProcessDownloading(downloadingTorrents, limits, actualDelta);
        }

        if (seedingTorrents.Count > 0)
        {
            _speedPolicy.ProcessSeeding(seedingTorrents, limits, actualDelta);
        }

        var globalRatioLimit = _configService.GlobalSeedRatioLimit;
        List<Torrent> stoppedByRatio = null;
        if (globalRatioLimit > 0)
        {
            stoppedByRatio = _stateMachine.ApplyRatioLimit(seedingTorrents, globalRatioLimit, _configService.SeedGoalReachedAction);
        }

        var thresholdPercent = _configService.DownloadThresholdPercent;
        var activeTorrents = allTorrents
            .Where(t => t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Downloading)
            .ToList();

        UpdateComputedFields(activeTorrents, thresholdPercent);

        var dirtyTorrents = new List<Torrent>();
        dirtyTorrents.AddRange(activeTorrents);

        if (stoppedByRatio != null && stoppedByRatio.Count > 0)
        {
            foreach (var t in stoppedByRatio)
            {
                _uploadSpeedHistory.Remove(t.Id);
                _downloadSpeedHistory.Remove(t.Id);
                if (!dirtyTorrents.Any(d => d.Id == t.Id))
                {
                    dirtyTorrents.Add(t);
                }
            }
        }

        foreach (var t in allTorrents.Where(t => t.Status != TorrentStatus.Seeding && t.Status != TorrentStatus.Downloading))
        {
            var statusChanged = initialStatuses.TryGetValue(t.Id, out var initialStatus) && initialStatus != t.Status;
            if (t.UploadSpeed != 0 || t.DownloadSpeed != 0 || t.Active || statusChanged)
            {
                t.UploadSpeed = 0;
                t.DownloadSpeed = 0;
                t.Active = false;
                _uploadSpeedHistory.Remove(t.Id);
                _downloadSpeedHistory.Remove(t.Id);
                if (!dirtyTorrents.Any(d => d.Id == t.Id))
                {
                    dirtyTorrents.Add(t);
                }
            }
        }

        if (dirtyTorrents.Count > 0)
        {
            _torrentService.UpdateMany(dirtyTorrents);
        }

        var activeIds = new HashSet<int>(activeTorrents.Select(t => t.Id));
        var staleIds = _prevUploaded.Keys.Where(id => !activeIds.Contains(id)).ToList();
        foreach (var id in staleIds)
        {
            _prevUploaded.Remove(id);
            _prevDownloaded.Remove(id);
            _sessionStartUploaded.Remove(id);
            _sessionStartDownloaded.Remove(id);
            _uploadSpeedHistory.Remove(id);
            _downloadSpeedHistory.Remove(id);
            _stalledTorrentIds.Remove(id);
            _seedingTimeReachedTorrentIds.Remove(id);
        }

        var totalActive = downloadingTorrents.Count + seedingTorrents.Count;
        _eventAggregator.PublishEvent(new SeedingTickEvent(totalActive));

        // Resolve previously stalled torrents that are no longer stalled or downloading
        var stalledIdsToRemove = new List<int>();
        foreach (var stalledId in _stalledTorrentIds)
        {
            var torrent = activeTorrents.FirstOrDefault(t => t.Id == stalledId);
            if (torrent == null || torrent.Status != TorrentStatus.Downloading || torrent.DownloadSpeed > 0 || torrent.Progress >= 1.0 || torrent.IsVpnPaused)
            {
                stalledIdsToRemove.Add(stalledId);
            }
        }

        foreach (var stalledId in stalledIdsToRemove)
        {
            _stalledTorrentIds.Remove(stalledId);
            var torrent = activeTorrents.FirstOrDefault(t => t.Id == stalledId);
            if (torrent != null)
            {
                _eventAggregator.PublishEvent(new TorrentStallResolvedEvent(torrent));
            }
        }

        // Clean up reached state for torrents no longer seeding or whose seeding goal was reset / increased
        var seedingTimeReachedIdsToRemove = new List<int>();
        foreach (var reachedId in _seedingTimeReachedTorrentIds)
        {
            var torrent = activeTorrents.FirstOrDefault(t => t.Id == reachedId);
            if (torrent == null || torrent.Status != TorrentStatus.Seeding)
            {
                seedingTimeReachedIdsToRemove.Add(reachedId);
                continue;
            }

            var limit = GetConfiguredSeedingTimeLimitSeconds(torrent);
            if (!limit.HasValue || torrent.SeedingTime < limit.Value)
            {
                seedingTimeReachedIdsToRemove.Add(reachedId);
            }
        }

        foreach (var reachedId in seedingTimeReachedIdsToRemove)
        {
            _seedingTimeReachedTorrentIds.Remove(reachedId);
        }

        // Evaluate metric thresholds across active torrents
        long totalDlSpeed = 0;
        long totalUlSpeed = 0;

        foreach (var torrent in activeTorrents)
        {
            totalDlSpeed += torrent.DownloadSpeed;
            totalUlSpeed += torrent.UploadSpeed;

            if (!torrent.IsVpnPaused && torrent.Status == TorrentStatus.Downloading && torrent.DownloadSpeed == 0 && torrent.Progress < 1.0)
            {
                var stalledMinutes = (int)(_clock.UtcNow - torrent.DateAdded).TotalMinutes;
                if (stalledMinutes >= 5 && _stalledTorrentIds.Add(torrent.Id))
                {
                    _eventAggregator.PublishEvent(new TorrentStalledEvent(torrent, stalledMinutes));
                }
            }

            var seedingTimeThreshold = GetConfiguredSeedingTimeLimitSeconds(torrent);
            if (torrent.Status == TorrentStatus.Seeding && seedingTimeThreshold.HasValue && torrent.SeedingTime >= seedingTimeThreshold.Value)
            {
                if (_seedingTimeReachedTorrentIds.Add(torrent.Id))
                {
                    _eventAggregator.PublishEvent(new TorrentSeedingTimeReachedEvent(torrent, TimeSpan.FromSeconds(torrent.SeedingTime)));
                }
            }
        }

        var configuredMaxDl = _configService.MaxDownloadSpeedKbps > 0 ? _configService.MaxDownloadSpeedKbps * 1024L : 0;
        var configuredMaxUl = _configService.MaxUploadSpeedKbps > 0 ? _configService.MaxUploadSpeedKbps * 1024L : 0;
        var isSpeedExceeded = (configuredMaxDl > 0 && totalDlSpeed >= configuredMaxDl) || (configuredMaxUl > 0 && totalUlSpeed >= configuredMaxUl);
        if (isSpeedExceeded && !_speedThresholdExceededState)
        {
            _speedThresholdExceededState = true;
            _eventAggregator.PublishEvent(new SpeedThresholdExceededEvent(totalDlSpeed, totalUlSpeed, activeTorrents.Count));
        }
        else if (!isSpeedExceeded && _speedThresholdExceededState)
        {
            _speedThresholdExceededState = false;
            _eventAggregator.PublishEvent(new SpeedThresholdDroppedEvent());
        }

        foreach (var torrent in activeTorrents)
        {
            if (!string.IsNullOrEmpty(torrent.InfoHash))
            {
                var session = (_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled && !_configService.AnonymousMode)
                    ? _clientBehaviorSimulator.GetOrCreateSession(torrent.InfoHash, torrent.IsPrivate)
                    : null;
                var peerId = session?.PeerId ?? _localPeerId;
                _peerDatabase.AddPeer(torrent.InfoHash, "127.0.0.1", LocalPeerPort, peerId);
            }
        }

        _connectionManager.ProcessDropouts();
        _connectionManager.RotateConnections();
    }

    private long CalculateMovingAverageSpeed(int torrentId, long instantSpeed, Dictionary<int, Queue<(DateTime Timestamp, long Speed)>> history, DateTime now)
    {
        if (!history.TryGetValue(torrentId, out var queue))
        {
            queue = new Queue<(DateTime Timestamp, long Speed)>();
            history[torrentId] = queue;
        }

        queue.Enqueue((now, instantSpeed));

        var cutoff = now.AddSeconds(-5);
        while (queue.Count > 1 && queue.Peek().Timestamp < cutoff)
        {
            queue.Dequeue();
        }

        var avg = (long)queue.Average(s => s.Speed);
        return Math.Max(0, avg);
    }

    private void UpdateComputedFields(List<Torrent> activeTorrents, int thresholdPercent)
    {
        var tickSeconds = TickInterval.TotalSeconds;
        var now = _clock.UtcNow;

        foreach (var torrent in activeTorrents)
        {
            if (!_sessionStartUploaded.ContainsKey(torrent.Id))
            {
                _sessionStartUploaded[torrent.Id] = torrent.Uploaded;
                _sessionStartDownloaded[torrent.Id] = torrent.Downloaded;
            }

            if (_prevUploaded.TryGetValue(torrent.Id, out var prevUp))
            {
                var instantUp = Math.Max(0, (long)((torrent.Uploaded - prevUp) / tickSeconds));
                torrent.UploadSpeed = CalculateMovingAverageSpeed(torrent.Id, instantUp, _uploadSpeedHistory, now);
            }

            if (_prevDownloaded.TryGetValue(torrent.Id, out var prevDown))
            {
                var instantDown = Math.Max(0, (long)((torrent.Downloaded - prevDown) / tickSeconds));
                torrent.DownloadSpeed = CalculateMovingAverageSpeed(torrent.Id, instantDown, _downloadSpeedHistory, now);
            }

            _prevUploaded[torrent.Id] = torrent.Uploaded;
            _prevDownloaded[torrent.Id] = torrent.Downloaded;

            torrent.SessionUploaded = torrent.Uploaded - _sessionStartUploaded[torrent.Id];
            torrent.SessionDownloaded = torrent.Downloaded - _sessionStartDownloaded[torrent.Id];

            if (!string.IsNullOrEmpty(torrent.InfoHash))
            {
                var stats = _peerDatabase.GetStats(torrent.InfoHash);
                if (stats != null)
                {
                    torrent.Seeders = stats.Complete;
                    torrent.Leechers = stats.Incomplete;
                }
            }

            if (torrent.Threshold == 0)
            {
                torrent.Threshold = thresholdPercent;
            }

            torrent.Active = true;
            torrent.LastActive = _clock.UtcNow;
            if (torrent.Status == TorrentStatus.Seeding)
            {
                torrent.SeedingTime += (long)tickSeconds;
            }

            if (torrent.Status == TorrentStatus.Downloading && torrent.DownloadSpeed > 0 && torrent.TotalSize > 0)
            {
                var remaining = torrent.TotalSize - torrent.Downloaded;
                var etaSeconds = remaining > 0 ? remaining / torrent.DownloadSpeed : 0;
                torrent.Eta = (int)Math.Min((long)int.MaxValue, etaSeconds);
            }
            else
            {
                torrent.Eta = 0;
            }

            torrent.Availability = torrent.Progress >= 1.0 ? 1.0 : torrent.Progress;
        }
    }

    private bool HasForceStartTorrents()
    {
        return _torrentService.GetAll().Any(t =>
            t.ForceStart &&
            (t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Downloading));
    }

    private int? GetConfiguredSeedingTimeLimitSeconds(Torrent torrent)
    {
        if (_tagService != null && torrent.TagIds != null && torrent.TagIds.Count > 0)
        {
            var tagLimits = torrent.TagIds
                .Select(id => _tagService.Get(id))
                .Where(tag => tag?.MinSeedTimeSeconds.HasValue == true && tag.MinSeedTimeSeconds.Value > 0)
                .Select(tag => tag.MinSeedTimeSeconds.Value)
                .ToList();

            if (tagLimits.Count > 0)
            {
                return tagLimits.Min();
            }
        }

        if (torrent.SeedingTimeLimit.HasValue && torrent.SeedingTimeLimit.Value > 0)
        {
            return torrent.SeedingTimeLimit.Value;
        }

        return null;
    }

    private static double GetPriorityWeight(int priority) => SpeedPolicy.GetPriorityWeight(priority);

    public static double CalculateCumulativeAvailability(IEnumerable<PeerConnection> peers, int pieceCount)
    {
        if (peers == null)
        {
            return 0.0;
        }

        var peerList = peers.Where(p => p != null).ToList();
        if (peerList.Count == 0)
        {
            return 0.0;
        }

        if (peerList.Any(p => p.IsSeed || p.Progress >= 1.0))
        {
            return 1.0;
        }

        if (pieceCount <= 0)
        {
            var sumProgress = peerList.Sum(p => Math.Clamp(p.Progress, 0.0, 1.0));
            return Math.Min(1.0, sumProgress);
        }

        var coveredPieces = new bool[pieceCount];
        var hasAnyBitfield = false;

        foreach (var peer in peerList)
        {
            if (peer.PeerPieces != null && peer.PeerPieces.Length > 0)
            {
                hasAnyBitfield = true;
                var max = Math.Min(pieceCount, peer.PeerPieces.Length);
                for (var i = 0; i < max; i++)
                {
                    if (peer.PeerPieces[i])
                    {
                        coveredPieces[i] = true;
                    }
                }
            }
        }

        if (hasAnyBitfield)
        {
            var coveredCount = coveredPieces.Count(c => c);
            return (double)coveredCount / pieceCount;
        }

        var totalProgress = peerList.Sum(p => Math.Clamp(p.Progress, 0.0, 1.0));
        return Math.Min(1.0, totalProgress);
    }

    public void EvaluateSuperSeeding(Torrent torrent)
    {
        if (torrent == null || !torrent.SuperSeeding || string.IsNullOrEmpty(torrent.InfoHash) || _connectionManager == null)
        {
            return;
        }

        var connectedPeers = _connectionManager.GetConnections(torrent.InfoHash);
        if (connectedPeers == null || connectedPeers.Count == 0)
        {
            return;
        }

        var hasOtherSeed = connectedPeers.Any(p => p != null && (p.IsSeed || p.Progress >= 1.0));
        var swarmAvailability = CalculateCumulativeAvailability(connectedPeers, torrent.PieceCount);

        if (hasOtherSeed || swarmAvailability >= 1.0)
        {
            var reason = hasOtherSeed ? "secondary seed joined" : "swarm availability >= 1.0";
            ExitSuperSeeding(torrent, reason, connectedPeers);
        }
    }

    public void EvaluateSuperSeeding(IEnumerable<Torrent> torrents)
    {
        if (torrents == null)
        {
            return;
        }

        foreach (var torrent in torrents)
        {
            EvaluateSuperSeeding(torrent);
        }
    }

    internal void ExitSuperSeeding(Torrent torrent, string reason, List<PeerConnection> connectedPeers = null)
    {
        torrent.SuperSeeding = false;

        _torrentRepository?.Update(torrent);
        _torrentService?.Update(torrent);

        var message = $"Super-seeding completed: {reason}";
        _eventLogService?.Info(torrent.Id, "SuperSeeding", message);
        _logger.Info("Torrent '{0}' (Id: {1}) - {2}", torrent.Name, torrent.Id, message);

        _eventAggregator?.PublishEvent(new SuperSeedingExitedEvent(torrent, reason));
        _eventAggregator?.PublishEvent(new TorrentUpdatedEvent(torrent));

        if (connectedPeers != null && torrent.PieceCount > 0)
        {
            foreach (var peer in connectedPeers)
            {
                try
                {
                    if (peer != null)
                    {
                        if (peer.SupportsFastExtension)
                        {
                            peer.SendMessage(new PeerMessage { Type = PeerMessageType.HaveAll });
                        }
                        else
                        {
                            peer.SendBitfield(torrent.PieceCount);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Error sending full availability to peer {0}:{1} after exiting super-seeding", peer?.RemoteIp, peer?.RemotePort);
                }
            }
        }
    }
}
