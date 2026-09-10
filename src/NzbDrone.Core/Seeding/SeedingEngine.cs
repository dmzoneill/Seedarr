using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Seeding.Distribution;
using NzbDrone.Core.Seeding.Scheduling;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Simulation.Swarm;
using NzbDrone.Core.Simulation.Traffic;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerServer;

namespace NzbDrone.Core.Seeding;

public class SeedingEngine : BackgroundService
{
    private const int LocalPeerPort = 6881;

    private TimeSpan TickInterval => TimeSpan.FromSeconds(Math.Max(1, _configService.UiRefreshRateSec));

    private readonly ITorrentService _torrentService;
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
    private readonly ISystemClock _clock;
    private readonly IRandomNumberGenerator _random;
    private readonly Logger _logger;
    private readonly Dictionary<int, long> _prevUploaded = new();
    private readonly Dictionary<int, long> _prevDownloaded = new();
    private readonly Dictionary<int, long> _sessionStartUploaded = new();
    private readonly Dictionary<int, long> _sessionStartDownloaded = new();

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
        ISwarmAnalyzer swarmAnalyzer = null)
    {
        _torrentService = torrentService;
        _distributionManager = distributionManager;
        _speedScheduler = speedScheduler;
        _configService = configService;
        _eventAggregator = eventAggregator;
        _peerDatabase = peerDatabase;
        _connectionManager = connectionManager;
        _eventLogService = eventLogService;
        _clock = clock ?? new SystemClock();
        _random = random ?? new NzbDrone.Common.EnvironmentInfo.RandomNumberGenerator();
        _stateMachine = stateMachine ?? new TorrentStateMachine(eventLogService);
        _stopPolicy = stopPolicy ?? new StopPolicy(configService, _random);
        _swarmAnalyzer = swarmAnalyzer ?? new SwarmAnalyzer(configService);
        _trafficPatternSimulator = trafficPatternSimulator ?? new TrafficPatternSimulator(configService, _random, _clock);
        _clientBehaviorSimulator = clientBehaviorSimulator;
        _speedPolicy = speedPolicy ?? new SpeedPolicy(distributionManager, speedScheduler, configService, eventLogService, _stateMachine, _stopPolicy, _random, _swarmAnalyzer);
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
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Seeding engine tick error");
            }

            await Task.Delay(TickInterval, stoppingToken);
        }
    }

    private void Tick()
    {
        var allTorrents = _torrentService.GetAll();
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

        if (string.IsNullOrEmpty(_localPeerId))
        {
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
        }
        else if (_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled)
        {
            var activeProfile = _clientBehaviorSimulator.GetActiveProfile();
            if (activeProfile != null)
            {
                _localPeerId = activeProfile.GeneratePeerId();
            }
        }

        var downloadingTorrents = allTorrents
            .Where(t => t.Status == TorrentStatus.Downloading && (autoStart || t.ForceStart))
            .ToList();

        var seedingTorrents = allTorrents
            .Where(t => t.Status == TorrentStatus.Seeding && (autoStart || t.ForceStart))
            .ToList();

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
            _speedPolicy.ProcessDownloading(downloadingTorrents, limits, TickInterval);
        }

        if (seedingTorrents.Count > 0)
        {
            _speedPolicy.ProcessSeeding(seedingTorrents, limits, TickInterval);
        }

        var globalRatioLimit = _configService.GlobalSeedRatioLimit;
        if (globalRatioLimit > 0)
        {
            _stateMachine.ApplyRatioLimit(seedingTorrents, globalRatioLimit);
        }

        var thresholdPercent = _configService.DownloadThresholdPercent;
        var activeTorrents = allTorrents
            .Where(t => t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Downloading)
            .ToList();

        UpdateComputedFields(activeTorrents, thresholdPercent);

        var dirtyTorrents = new List<Torrent>();
        dirtyTorrents.AddRange(activeTorrents);

        foreach (var t in allTorrents.Where(t => t.Status != TorrentStatus.Seeding && t.Status != TorrentStatus.Downloading))
        {
            if (t.UploadSpeed != 0 || t.DownloadSpeed != 0 || t.Active)
            {
                t.UploadSpeed = 0;
                t.DownloadSpeed = 0;
                t.Active = false;
                dirtyTorrents.Add(t);
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
        }

        var totalActive = downloadingTorrents.Count + seedingTorrents.Count;
        _eventAggregator.PublishEvent(new SeedingTickEvent(totalActive));

        foreach (var torrent in activeTorrents)
        {
            if (!string.IsNullOrEmpty(torrent.InfoHash))
            {
                _peerDatabase.AddPeer(torrent.InfoHash, "127.0.0.1", LocalPeerPort, _localPeerId);
            }
        }

        _connectionManager.ProcessDropouts();
        _connectionManager.RotateConnections();
    }

    private void UpdateComputedFields(List<Torrent> activeTorrents, int thresholdPercent)
    {
        var tickSeconds = TickInterval.TotalSeconds;

        foreach (var torrent in activeTorrents)
        {
            if (!_sessionStartUploaded.ContainsKey(torrent.Id))
            {
                _sessionStartUploaded[torrent.Id] = torrent.Uploaded;
                _sessionStartDownloaded[torrent.Id] = torrent.Downloaded;
            }

            if (_prevUploaded.TryGetValue(torrent.Id, out var prevUp))
            {
                torrent.UploadSpeed = Math.Max(0, (long)((torrent.Uploaded - prevUp) / tickSeconds));
            }

            if (_prevDownloaded.TryGetValue(torrent.Id, out var prevDown))
            {
                torrent.DownloadSpeed = Math.Max(0, (long)((torrent.Downloaded - prevDown) / tickSeconds));
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
            torrent.SeedingTime += (long)tickSeconds;

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

    private static double GetPriorityWeight(int priority) => SpeedPolicy.GetPriorityWeight(priority);
}
