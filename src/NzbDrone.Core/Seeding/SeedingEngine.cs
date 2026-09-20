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
using NzbDrone.Core.Dht;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Seeding.Distribution;
using NzbDrone.Core.Seeding.Scheduling;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Simulation.Swarm;
using NzbDrone.Core.Simulation.Traffic;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using NzbDrone.Core.TrackerServer;

namespace NzbDrone.Core.Seeding;

public class SeedingEngine : BackgroundService, IHandle<ApplicationShutdownRequested>
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
    private readonly Dictionary<int, double> _uploadSpeedHistory = new();
    private readonly Dictionary<int, double> _downloadSpeedHistory = new();
    private readonly HashSet<int> _previousActiveTorrentIds = new();
    private readonly HashSet<int> _stalledTorrentIds = new();
    private readonly HashSet<int> _extinctNotifiedTorrentIds = new();
    private readonly HashSet<int> _seedingTimeReachedTorrentIds = new();
    private readonly IPieceStorage _pieceStorage;
    private readonly IDhtService _dhtService;
    private readonly ITrackerAnnounceService _trackerAnnounceService;
    private readonly Peers.IPeerServer _peerServer;
    private readonly CancellationTokenSource _shutdownCts = new();
    private bool _speedThresholdExceededState;
    private long _lastTickTimestamp;
    private volatile bool _shutdownRequested;

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
        ITorrentRepository torrentRepository = null,
        IPieceStorage pieceStorage = null,
        IDhtService dhtService = null,
        ITrackerAnnounceService trackerAnnounceService = null,
        Peers.IPeerServer peerServer = null,
        IPieceBoundaryMasker pieceBoundaryMasker = null,
        ITorrentFileService torrentFileService = null)
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
        _stateMachine = stateMachine ?? new TorrentStateMachine(eventLogService, eventAggregator, torrentService != null ? new Lazy<ITorrentService>(() => torrentService) : null, categoryService, _seedingTimeReachedTorrentIds);
        _stopPolicy = stopPolicy ?? new StopPolicy(configService, _random, tagService);
        _swarmAnalyzer = swarmAnalyzer ?? new SwarmAnalyzer(configService);
        _trafficPatternSimulator = trafficPatternSimulator ?? new TrafficPatternSimulator(configService, _random, _clock);
        _clientBehaviorSimulator = clientBehaviorSimulator;
        _categoryService = categoryService;
        _tagService = tagService;
        _pieceStorage = pieceStorage;
        _dhtService = dhtService;
        _trackerAnnounceService = trackerAnnounceService;
        _peerServer = peerServer;
        _speedPolicy = speedPolicy ?? new SpeedPolicy(distributionManager, speedScheduler, configService, eventLogService, _stateMachine, _stopPolicy, _random, _swarmAnalyzer, eventAggregator, categoryService, tagService, pieceBoundaryMasker, torrentFileService);
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

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _shutdownCts.Token);
        var token = linkedCts.Token;

        while (!token.IsCancellationRequested && !_shutdownRequested)
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
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(remainingDelay), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    public void Handle(ApplicationShutdownRequested message)
    {
        _logger.Info("Shutdown requested: signaling SeedingEngine to stop");
        _shutdownRequested = true;
        try
        {
            _shutdownCts.Cancel();
        }
        catch
        {
        }
    }

    public override void Dispose()
    {
        _shutdownCts.Dispose();
        base.Dispose();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Seeding engine stopping");
        _shutdownRequested = true;
        try
        {
            await _shutdownCts.CancelAsync();
        }
        catch
        {
        }

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
        TickInternal(null);
    }

    internal void TickForTesting(TimeSpan delta)
    {
        TickInternal(delta);
    }

    private void TickInternal(TimeSpan? overrideDelta)
    {
        var now = Stopwatch.GetTimestamp();
        TimeSpan actualDelta;

        if (overrideDelta.HasValue)
        {
            actualDelta = overrideDelta.Value;
        }
        else if (_lastTickTimestamp == 0)
        {
            actualDelta = TickInterval;
        }
        else
        {
            var elapsedSeconds = (double)(now - _lastTickTimestamp) / Stopwatch.Frequency;
            elapsedSeconds = Math.Clamp(elapsedSeconds, 0.001, 60.0);
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
                    torrent.Seeders = Math.Max(torrent.Seeders, stats.Complete);
                    torrent.Leechers = Math.Max(torrent.Leechers, stats.Incomplete);
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

        var queuedForceStart = allTorrents
            .Where(t => t.Status == TorrentStatus.Queued && t.ForceStart)
            .ToList();

        if (queuedForceStart.Count > 0)
        {
            foreach (var torrent in queuedForceStart)
            {
                var newStatus = torrent.Progress >= 1.0 ? TorrentStatus.Seeding : TorrentStatus.Downloading;
                torrent.Status = newStatus;
                torrent.LastActive = _clock.UtcNow;
                _torrentService.Update(torrent);
                if (newStatus == TorrentStatus.Downloading)
                {
                    downloadingTorrents.Add(torrent);
                }
                else
                {
                    seedingTorrents.Add(torrent);
                }

                _eventAggregator.PublishEvent(new TorrentStatusChangedEvent(torrent, TorrentStatus.Queued, newStatus) { IsQueueManagerInternal = true });
            }
        }

        var maxActiveDownloads = _configService.MaxActiveDownloads;
        var maxActiveSeeds = _configService.MaxActiveSeeds > 0 ? _configService.MaxActiveSeeds : _configService.MaxActiveUploads;
        var maxActiveTorrents = _configService.MaxActiveTorrents > 0 ? _configService.MaxActiveTorrents : _configService.GetValueInt("MaxActiveLimit", 0);

        if (autoStart)
        {
            // 1. Demotion: If non-forced active torrents exceed configured limits, demote excess lowest-priority/highest-SortOrder torrents
            if (maxActiveDownloads > 0)
            {
                var nonForcedDl = downloadingTorrents.Where(t => !t.ForceStart).ToList();
                if (nonForcedDl.Count > maxActiveDownloads)
                {
                    var dlToDemote = nonForcedDl
                        .OrderBy(t => t.Priority)
                        .ThenByDescending(t => t.SortOrder)
                        .ThenByDescending(t => t.Id)
                        .Take(nonForcedDl.Count - maxActiveDownloads)
                        .ToList();

                    foreach (var torrent in dlToDemote)
                    {
                        torrent.Status = TorrentStatus.Queued;
                        torrent.Active = false;
                        torrent.UploadSpeed = 0;
                        torrent.DownloadSpeed = 0;
                        downloadingTorrents.Remove(torrent);
                        _torrentService.Update(torrent);
                        _eventAggregator.PublishEvent(new TorrentStatusChangedEvent(torrent, TorrentStatus.Downloading, TorrentStatus.Queued) { IsQueueManagerInternal = true });
                    }
                }
            }

            if (maxActiveSeeds > 0)
            {
                var nonForcedSeeds = seedingTorrents.Where(t => !t.ForceStart).ToList();
                if (nonForcedSeeds.Count > maxActiveSeeds)
                {
                    var seedsToDemote = nonForcedSeeds
                        .OrderBy(t => t.Priority)
                        .ThenByDescending(t => t.SortOrder)
                        .ThenByDescending(t => t.Id)
                        .Take(nonForcedSeeds.Count - maxActiveSeeds)
                        .ToList();

                    foreach (var torrent in seedsToDemote)
                    {
                        torrent.Status = TorrentStatus.Queued;
                        torrent.Active = false;
                        torrent.UploadSpeed = 0;
                        torrent.DownloadSpeed = 0;
                        seedingTorrents.Remove(torrent);
                        _torrentService.Update(torrent);
                        _eventAggregator.PublishEvent(new TorrentStatusChangedEvent(torrent, TorrentStatus.Seeding, TorrentStatus.Queued) { IsQueueManagerInternal = true });
                    }
                }
            }

            if (maxActiveTorrents > 0)
            {
                var nonForcedActive = downloadingTorrents.Where(t => !t.ForceStart)
                    .Concat(seedingTorrents.Where(t => !t.ForceStart))
                    .ToList();

                if (nonForcedActive.Count > maxActiveTorrents)
                {
                    var excess = nonForcedActive.Count - maxActiveTorrents;
                    var activeToDemote = nonForcedActive
                        .OrderBy(t => t.Priority)
                        .ThenByDescending(t => t.SortOrder)
                        .ThenByDescending(t => t.Id)
                        .Take(excess)
                        .ToList();

                    foreach (var torrent in activeToDemote)
                    {
                        var oldStatus = torrent.Status;
                        torrent.Status = TorrentStatus.Queued;
                        torrent.Active = false;
                        torrent.UploadSpeed = 0;
                        torrent.DownloadSpeed = 0;
                        if (oldStatus == TorrentStatus.Downloading)
                        {
                            downloadingTorrents.Remove(torrent);
                        }
                        else if (oldStatus == TorrentStatus.Seeding)
                        {
                            seedingTorrents.Remove(torrent);
                        }

                        _torrentService.Update(torrent);
                        _eventAggregator.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, TorrentStatus.Queued) { IsQueueManagerInternal = true });
                    }
                }
            }

            // 2. Promotion: If autoStart is true, evaluate download and seed queue slots
            var remainingTotalSlots = maxActiveTorrents > 0
                ? Math.Max(0, maxActiveTorrents - (downloadingTorrents.Count(t => !t.ForceStart) + seedingTorrents.Count(t => !t.ForceStart)))
                : int.MaxValue;

            // A. Download Queue Promotion (Progress < 1.0)
            var availableDlSlots = maxActiveDownloads > 0
                ? Math.Max(0, maxActiveDownloads - downloadingTorrents.Count(t => !t.ForceStart))
                : int.MaxValue;
            availableDlSlots = Math.Min(availableDlSlots, remainingTotalSlots);

            var queuedDownloads = allTorrents
                .Where(t => t.Status == TorrentStatus.Queued && !t.ForceStart && t.Progress < 1.0)
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.SortOrder)
                .ThenBy(t => t.Id)
                .ToList();

            if (queuedDownloads.Count > 0 && availableDlSlots > 0)
            {
                var toPromoteDl = new List<Torrent>();
                if (_categoryService != null)
                {
                    var currentNonForcedDl = downloadingTorrents.Count(t => !t.ForceStart);
                    var effectiveDlLimit = maxActiveDownloads > 0
                        ? (maxActiveTorrents > 0 ? (int?)Math.Min(maxActiveDownloads, currentNonForcedDl + availableDlSlots) : maxActiveDownloads)
                        : (maxActiveTorrents > 0 ? (int?)(currentNonForcedDl + availableDlSlots) : (int?)null);

                    toPromoteDl = _categoryService.EvaluateDownloadQueue(queuedDownloads, downloadingTorrents, effectiveDlLimit, _configService.IgnoreSlowTorrents);
                    if (toPromoteDl.Count > availableDlSlots)
                    {
                        toPromoteDl = toPromoteDl.Take(availableDlSlots).ToList();
                    }
                }
                else
                {
                    toPromoteDl = queuedDownloads.Take(availableDlSlots).ToList();
                }

                if (toPromoteDl.Count > 0)
                {
                    foreach (var torrent in toPromoteDl)
                    {
                        torrent.Status = TorrentStatus.Downloading;
                        torrent.LastActive = _clock.UtcNow;
                        _torrentService.Update(torrent);
                        downloadingTorrents.Add(torrent);
                        _eventAggregator.PublishEvent(new TorrentStatusChangedEvent(torrent, TorrentStatus.Queued, TorrentStatus.Downloading) { IsQueueManagerInternal = true });
                    }
                }
            }

            // Re-calculate remaining total active slots after download promotions
            remainingTotalSlots = maxActiveTorrents > 0
                ? Math.Max(0, maxActiveTorrents - (downloadingTorrents.Count(t => !t.ForceStart) + seedingTorrents.Count(t => !t.ForceStart)))
                : int.MaxValue;

            // B. Seeding Queue Promotion (Progress >= 1.0)
            var availableSeedSlots = maxActiveSeeds > 0
                ? Math.Max(0, maxActiveSeeds - seedingTorrents.Count(t => !t.ForceStart))
                : int.MaxValue;
            availableSeedSlots = Math.Min(availableSeedSlots, remainingTotalSlots);

            var queuedSeeds = allTorrents
                .Where(t => t.Status == TorrentStatus.Queued && !t.ForceStart && t.Progress >= 1.0)
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.SortOrder)
                .ThenBy(t => t.Id)
                .ToList();

            if (queuedSeeds.Count > 0 && availableSeedSlots > 0)
            {
                foreach (var torrent in queuedSeeds)
                {
                    if (availableSeedSlots <= 0)
                    {
                        break;
                    }

                    if (_categoryService != null && !_categoryService.CanUpload(torrent, seedingTorrents, maxActiveSeeds > 0 ? (int?)maxActiveSeeds : null))
                    {
                        continue;
                    }

                    torrent.Status = TorrentStatus.Seeding;
                    torrent.LastActive = _clock.UtcNow;
                    _torrentService.Update(torrent);
                    seedingTorrents.Add(torrent);
                    _eventAggregator.PublishEvent(new TorrentStatusChangedEvent(torrent, TorrentStatus.Queued, TorrentStatus.Seeding) { IsQueueManagerInternal = true });
                    availableSeedSlots--;
                }
            }
        }

        var isAnyPrivate = allTorrents.Any(t => t.IsPrivate && (t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Downloading));

        var stalledTorrents = allTorrents
            .Where(t => t.Status == TorrentStatus.StalledNoSeeds && (autoStart || t.ForceStart))
            .ToList();

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
        else if (_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled && downloadingTorrents.Count == 0 && seedingTorrents.Count == 0 && stalledTorrents.Count == 0)
        {
            var activeProfile = _clientBehaviorSimulator.GetActiveProfile(isAnyPrivate);
            if (activeProfile != null)
            {
                _localPeerId = activeProfile.GeneratePeerId();
            }
        }

        if (downloadingTorrents.Count == 0 && seedingTorrents.Count == 0 && stalledTorrents.Count == 0)
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

            _previousActiveTorrentIds.Clear();
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
        if (seedingTorrents.Count > 0)
        {
            stoppedByRatio = _stateMachine.ApplyRatioLimit(seedingTorrents, globalRatioLimit, _configService.SeedGoalReachedAction);
        }

        var thresholdPercent = _configService.DownloadThresholdPercent;
        var activeTorrents = allTorrents
            .Where(t => t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.StalledNoSeeds)
            .ToList();

        var recoveredTorrents = UpdateComputedFields(activeTorrents, thresholdPercent, actualDelta);

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

        foreach (var t in allTorrents.Where(t => t.Status != TorrentStatus.Seeding && t.Status != TorrentStatus.Downloading && t.Status != TorrentStatus.StalledNoSeeds))
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
            _extinctNotifiedTorrentIds.Remove(id);
            _seedingTimeReachedTorrentIds.Remove(id);
        }

        var totalActive = downloadingTorrents.Count + seedingTorrents.Count + stalledTorrents.Count;
        _eventAggregator.PublishEvent(new SeedingTickEvent(totalActive));

        // Resolve previously stalled torrents that are no longer stalled or downloading
        var stalledIdsToRemove = new List<int>();
        foreach (var stalledId in _stalledTorrentIds)
        {
            var torrent = activeTorrents.FirstOrDefault(t => t.Id == stalledId);
            if (torrent == null || (torrent.Status != TorrentStatus.Downloading && torrent.Status != TorrentStatus.StalledNoSeeds) || torrent.DownloadSpeed > 0 || torrent.Progress >= 1.0 || torrent.IsVpnPaused)
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

            if (!torrent.IsVpnPaused && (torrent.Status == TorrentStatus.Downloading || torrent.Status == TorrentStatus.StalledNoSeeds) && torrent.Progress < 1.0)
            {
                if (torrent.DownloadSpeed > 0)
                {
                    torrent.StallDurationSeconds = 0;
                    torrent.LastActiveTransferTime = _clock.UtcNow;
                }
                else if (torrent.Status == TorrentStatus.Downloading && !recoveredTorrents.Contains(torrent.Id))
                {
                    torrent.StallDurationSeconds += (int)Math.Max(1, actualDelta.TotalSeconds);
                }
                else if (recoveredTorrents.Contains(torrent.Id))
                {
                    torrent.StallDurationSeconds = 0;
                }

                var stalledMinutes = torrent.StallDurationSeconds / 60;
                if (stalledMinutes >= 5 && _stalledTorrentIds.Add(torrent.Id))
                {
                    _eventAggregator.PublishEvent(new TorrentStalledEvent(torrent, stalledMinutes));
                }

                var timeout = _configService.StalledNoSeedsTimeoutSeconds;
                if (timeout <= 0)
                {
                    timeout = 60;
                }

                if (torrent.Status == TorrentStatus.Downloading && torrent.IsExtinct && torrent.StallDurationSeconds >= timeout)
                {
                    var oldStatus = torrent.Status;
                    torrent.Status = TorrentStatus.StalledNoSeeds;
                    _eventAggregator.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, TorrentStatus.StalledNoSeeds));

                    if (_extinctNotifiedTorrentIds.Add(torrent.Id))
                    {
                        var verified = _pieceStorage?.GetVerifiedPieces(torrent.InfoHash);
                        var peers = _connectionManager?.GetConnections(torrent.InfoHash)?.Where(p => p != null).ToList() ?? new List<PeerConnection>();
                        var (_, _, extinctCount) = CalculateSwarmAvailability(torrent, peers, verified);
                        _eventAggregator.PublishEvent(new TorrentPieceExtinctionEvent(torrent, extinctCount, torrent.PieceCount));
                    }

                    TriggerDiscoveryRetry(torrent);
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

        _previousActiveTorrentIds.Clear();
        foreach (var t in activeTorrents)
        {
            _previousActiveTorrentIds.Add(t.Id);
        }
    }

    internal static double CalculateExponentialMovingAverage(long instantSpeed, double currentEma, double elapsedSeconds, double tau = 3.0)
    {
        var alpha = 1.0 - Math.Exp(-elapsedSeconds / tau);
        return (alpha * instantSpeed) + ((1.0 - alpha) * currentEma);
    }

    private long CalculateMovingAverageSpeed(int torrentId, long instantSpeed, Dictionary<int, double> history, double elapsedSeconds)
    {
        const double tau = 3.0;

        if (!history.TryGetValue(torrentId, out var currentEma))
        {
            history[torrentId] = instantSpeed;
            return instantSpeed;
        }

        var newEma = CalculateExponentialMovingAverage(instantSpeed, currentEma, elapsedSeconds, tau);
        if (instantSpeed == 0 && newEma < 0.5)
        {
            newEma = 0;
        }

        history[torrentId] = newEma;
        return (long)Math.Round(newEma);
    }

    private HashSet<int> UpdateComputedFields(List<Torrent> activeTorrents, int thresholdPercent, TimeSpan actualDelta)
    {
        var elapsedSeconds = Math.Max(0.001, actualDelta.TotalSeconds);
        var now = _clock.UtcNow;
        var recoveredTorrents = new HashSet<int>();

        foreach (var torrent in activeTorrents)
        {
            if (!_sessionStartUploaded.ContainsKey(torrent.Id))
            {
                _sessionStartUploaded[torrent.Id] = torrent.Uploaded;
                _sessionStartDownloaded[torrent.Id] = torrent.Downloaded;
            }

            var isNewlyActive = !_previousActiveTorrentIds.Contains(torrent.Id);
            if (isNewlyActive)
            {
                _prevUploaded.Remove(torrent.Id);
                _prevDownloaded.Remove(torrent.Id);
                _uploadSpeedHistory.Remove(torrent.Id);
                _downloadSpeedHistory.Remove(torrent.Id);
            }

            if (_prevUploaded.TryGetValue(torrent.Id, out var prevUp))
            {
                var deltaUp = Math.Max(0, torrent.Uploaded - prevUp);
                var instantUp = Math.Max(0, (long)(deltaUp / elapsedSeconds));
                torrent.UploadSpeed = CalculateMovingAverageSpeed(torrent.Id, instantUp, _uploadSpeedHistory, elapsedSeconds);
            }
            else
            {
                torrent.UploadSpeed = 0;
            }

            if (_prevDownloaded.TryGetValue(torrent.Id, out var prevDown))
            {
                var deltaDown = Math.Max(0, torrent.Downloaded - prevDown);
                var instantDown = Math.Max(0, (long)(deltaDown / elapsedSeconds));
                torrent.DownloadSpeed = CalculateMovingAverageSpeed(torrent.Id, instantDown, _downloadSpeedHistory, elapsedSeconds);
            }
            else
            {
                torrent.DownloadSpeed = 0;
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
                    torrent.Seeders = Math.Max(torrent.Seeders, stats.Complete);
                    torrent.Leechers = Math.Max(torrent.Leechers, stats.Incomplete);
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
                torrent.SeedingTime += (long)Math.Max(1, actualDelta.TotalSeconds);
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

            if (torrent.Status == TorrentStatus.StalledNoSeeds)
            {
                torrent.DownloadSpeed = 0;
                torrent.Eta = 0;
            }

            if (UpdateAvailabilityAndExtinction(torrent))
            {
                recoveredTorrents.Add(torrent.Id);
            }
        }

        return recoveredTorrents;
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

        _peerServer?.CheckSuperSeedingTimeouts(torrent);

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
                        peer.AssignedSuperSeedingPiece = null;
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

    public static bool LocalHasPiece(Torrent torrent, bool[] verified, int pieceIndex)
    {
        if (torrent == null || torrent.Status == TorrentStatus.Checking || torrent.Status == TorrentStatus.QueuedForChecking)
        {
            return false;
        }

        if (verified != null && pieceIndex >= 0 && pieceIndex < verified.Length)
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

    public static (double Availability, bool IsExtinct, int ExtinctPieceCount) CalculateSwarmAvailability(
        Torrent torrent,
        IEnumerable<PeerConnection> peers,
        bool[] verifiedPieces)
    {
        if (torrent == null)
        {
            return (0.0, false, 0);
        }

        if (torrent.Progress >= 1.0 || torrent.Status == TorrentStatus.Seeding)
        {
            return (1.0, false, 0);
        }

        var peerList = peers?.Where(p => p != null).ToList() ?? new List<PeerConnection>();
        var hasSeed = peerList.Any(p => p.IsSeed || p.Progress >= 1.0);

        if (torrent.PieceCount <= 0)
        {
            if (hasSeed)
            {
                return (1.0, false, 0);
            }

            var sumProgress = peerList.Sum(p => Math.Clamp(p.Progress, 0.0, 1.0));
            var totalAvail = Math.Min(1.0, torrent.Progress + sumProgress);
            var isExt = totalAvail < 1.0 && (peerList.Count == 0 || sumProgress == 0);
            return (totalAvail, isExt, isExt ? 1 : 0);
        }

        var pieceCount = torrent.PieceCount;
        var localPieces = new bool[pieceCount];
        var localCount = 0;
        for (var i = 0; i < pieceCount; i++)
        {
            if (LocalHasPiece(torrent, verifiedPieces, i))
            {
                localPieces[i] = true;
                localCount++;
            }
        }

        if (localCount == pieceCount)
        {
            return (1.0, false, 0);
        }

        var missingCount = pieceCount - localCount;

        if (hasSeed)
        {
            return (1.0, false, 0);
        }

        var copyCount = new int[pieceCount];
        for (var i = 0; i < pieceCount; i++)
        {
            if (localPieces[i])
            {
                copyCount[i] = 1;
            }
        }

        foreach (var peer in peerList)
        {
            if (peer.PeerPieces != null && peer.PeerPieces.Length > 0)
            {
                var len = Math.Min(pieceCount, peer.PeerPieces.Length);
                for (var i = 0; i < len; i++)
                {
                    if (peer.PeerPieces[i])
                    {
                        copyCount[i]++;
                    }
                }
            }
        }

        var extinctPieceCount = 0;
        for (var i = 0; i < pieceCount; i++)
        {
            if (!localPieces[i] && copyCount[i] == 0)
            {
                extinctPieceCount++;
            }
        }

        var coveredPieces = copyCount.Count(c => c > 0);
        var availability = (double)coveredPieces / pieceCount;
        var allMissingExtinct = missingCount > 0 && extinctPieceCount == missingCount;

        return (availability, allMissingExtinct, extinctPieceCount);
    }

    private bool UpdateAvailabilityAndExtinction(Torrent torrent)
    {
        if (torrent == null)
        {
            return false;
        }

        if (torrent.Progress >= 1.0 || torrent.Status == TorrentStatus.Seeding)
        {
            torrent.IsExtinct = false;
            torrent.StallDurationSeconds = 0;
            torrent.Availability = Math.Max(1.0, torrent.Availability);
            return false;
        }

        var peers = _connectionManager?.GetConnections(torrent.InfoHash);
        var peerList = peers?.Where(p => p != null).ToList() ?? new List<PeerConnection>();
        var verified = _pieceStorage?.GetVerifiedPieces(torrent.InfoHash);

        var (availability, isExtinct, _) = CalculateSwarmAvailability(torrent, peerList, verified);
        torrent.Availability = availability;

        var wasExtinct = torrent.IsExtinct;
        torrent.IsExtinct = isExtinct;
        var recovered = false;

        if (isExtinct)
        {
            if (torrent.LastActiveTransferTime == null && torrent.DownloadSpeed > 0)
            {
                torrent.LastActiveTransferTime = _clock.UtcNow;
            }
        }
        else if (wasExtinct)
        {
            _extinctNotifiedTorrentIds.Remove(torrent.Id);
            if (torrent.Status == TorrentStatus.StalledNoSeeds)
            {
                torrent.Status = TorrentStatus.Downloading;
                torrent.StallDurationSeconds = 0;
                recovered = true;
                _eventAggregator.PublishEvent(new TorrentStatusChangedEvent(torrent, TorrentStatus.StalledNoSeeds, TorrentStatus.Downloading));
            }
        }

        return recovered;
    }

    private void TriggerDiscoveryRetry(Torrent torrent)
    {
        if (torrent == null || string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            return;
        }

        if (_dhtService != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _dhtService.AnnounceTorrent(torrent.InfoHash, LocalPeerPort);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to dispatch DHT announce for extinct torrent {0}", torrent.Name);
                }
            });
        }

        if (_trackerAnnounceService != null)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    _trackerAnnounceService.AnnounceTorrent(torrent, force: true);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to dispatch tracker re-announce for extinct torrent {0}", torrent.Name);
                }
            });
        }
    }
}
