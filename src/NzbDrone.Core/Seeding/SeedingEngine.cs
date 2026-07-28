using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Seeding.Distribution;
using NzbDrone.Core.Seeding.Scheduling;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerServer;

namespace NzbDrone.Core.Seeding;

public class SeedingEngine : BackgroundService
{
    private const int LocalPeerPort = 6881;
    private const double SuperSeedingBoost = 1.5;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    private readonly ITorrentService _torrentService;
    private readonly ISpeedDistributionManager _distributionManager;
    private readonly ISpeedScheduler _speedScheduler;
    private readonly IConfigService _configService;
    private readonly IEventAggregator _eventAggregator;
    private readonly IPeerDatabase _peerDatabase;
    private readonly IConnectionManager _connectionManager;
    private readonly ITorrentEventLogService _eventLogService;
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
        ITorrentEventLogService eventLogService)
    {
        _torrentService = torrentService;
        _distributionManager = distributionManager;
        _speedScheduler = speedScheduler;
        _configService = configService;
        _eventAggregator = eventAggregator;
        _peerDatabase = peerDatabase;
        _connectionManager = connectionManager;
        _eventLogService = eventLogService;
        _logger = LogManager.GetCurrentClassLogger();
    }

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

        var prefix = _configService.PeerIdPrefix;
        var suffix = new char[20 - prefix.Length];
        var suffixBytes = RandomNumberGenerator.GetBytes(suffix.Length);
        for (var i = 0; i < suffix.Length; i++)
        {
            suffix[i] = (char)('A' + (suffixBytes[i] % 26));
        }

        _localPeerId = prefix + new string(suffix);
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

        var downloadingTorrents = allTorrents
            .Where(t => t.Status == TorrentStatus.Downloading && (autoStart || t.ForceStart))
            .ToList();

        var seedingTorrents = allTorrents
            .Where(t => t.Status == TorrentStatus.Seeding && (autoStart || t.ForceStart))
            .ToList();

        if (downloadingTorrents.Count == 0 && seedingTorrents.Count == 0)
        {
            foreach (var t in allTorrents)
            {
                if (t.UploadSpeed != 0 || t.DownloadSpeed != 0 || t.Active)
                {
                    t.UploadSpeed = 0;
                    t.DownloadSpeed = 0;
                    t.Active = false;
                    _torrentService.Update(t);
                }
            }

            return;
        }

        var limits = _speedScheduler.GetCurrentLimits();

        var configUploadSpeedKbps = _configService.AlternativeSpeedEnabled
            ? _configService.AltUploadSpeedKbps
            : _configService.MaxUploadSpeedKbps;
        var configDownloadSpeedKbps = _configService.AlternativeSpeedEnabled
            ? _configService.AltDownloadSpeedKbps
            : _configService.MaxDownloadSpeedKbps;

        SpeedLimitMerger.Apply(limits, (long)configUploadSpeedKbps * 1024, (long)configDownloadSpeedKbps * 1024);

        var variationMin = _configService.SpeedVariationMin;
        var variationMax = _configService.SpeedVariationMax;
        var thresholdPercent = _configService.DownloadThresholdPercent;
        var threshold = thresholdPercent / 100.0;

        if (downloadingTorrents.Count > 0)
        {
            TickDownloading(downloadingTorrents, limits.MaxDownloadSpeed, variationMin, variationMax, threshold);
        }

        if (seedingTorrents.Count > 0)
        {
            TickSeeding(seedingTorrents, limits, variationMin, variationMax);
        }

        var globalRatioLimit = _configService.GlobalSeedRatioLimit;
        if (globalRatioLimit > 0)
        {
            for (var i = seedingTorrents.Count - 1; i >= 0; i--)
            {
                var torrent = seedingTorrents[i];
                if (torrent.Ratio >= globalRatioLimit)
                {
                    _logger.Info("Torrent {0} reached global seed ratio limit ({1:F2}), stopping", torrent.Name, globalRatioLimit);
                    _eventLogService.Info(torrent.Id, "Seeding", $"Global seed ratio limit reached ({globalRatioLimit:F2}), torrent stopped");
                    torrent.Status = TorrentStatus.Stopped;
                    torrent.UploadSpeed = 0;
                    torrent.DownloadSpeed = 0;
                    torrent.Active = false;
                    _torrentService.Update(torrent);
                    seedingTorrents.RemoveAt(i);
                }
            }
        }

        var activeTorrents = allTorrents
            .Where(t => t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Downloading)
            .ToList();

        UpdateComputedFields(activeTorrents, thresholdPercent);

        foreach (var t in allTorrents.Where(t => t.Status != TorrentStatus.Seeding && t.Status != TorrentStatus.Downloading))
        {
            if (t.UploadSpeed != 0 || t.DownloadSpeed != 0 || t.Active)
            {
                t.UploadSpeed = 0;
                t.DownloadSpeed = 0;
                t.Active = false;
                _torrentService.Update(t);
            }
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
                torrent.Seeders = stats.Complete;
                torrent.Leechers = stats.Incomplete;
            }

            if (torrent.Threshold == 0)
            {
                torrent.Threshold = thresholdPercent;
            }

            torrent.Active = true;
            torrent.LastActive = DateTime.UtcNow;

            if (torrent.Status == TorrentStatus.Downloading && torrent.DownloadSpeed > 0 && torrent.TotalSize > 0)
            {
                var remaining = torrent.TotalSize - torrent.Downloaded;
                torrent.Eta = remaining > 0 ? (int)(remaining / torrent.DownloadSpeed) : 0;
            }
            else
            {
                torrent.Eta = 0;
            }

            torrent.Availability = torrent.Progress >= 1.0 ? 1.0 : torrent.Progress;

            _torrentService.Update(torrent);
        }
    }

    private void TickDownloading(List<Torrent> torrents, long maxDownloadSpeed, double variationMin, double variationMax, double threshold)
    {
        var stoppedIndices = SelectDownloadStoppedTorrents(torrents.Count);
        var priorityWeights = GetPriorityWeights(torrents);
        var speeds = maxDownloadSpeed == SpeedLimits.Unlimited
            ? Enumerable.Repeat(1_000_000_000L, torrents.Count).ToArray()
            : _distributionManager.DistributeDownloadSpeeds(torrents.Count, maxDownloadSpeed, priorityWeights);

        for (var i = 0; i < torrents.Count; i++)
        {
            var torrent = torrents[i];

            // Skip progress recalculation for force-completed torrents
            if (torrent.ForceCompleted)
            {
                if (torrent.Status == TorrentStatus.Downloading)
                {
                    torrent.Status = TorrentStatus.Seeding;
                    _logger.Info("Torrent {0} is force-completed, switching to seeding", torrent.Name);
                    _eventLogService.Info(torrent.Id, "Seeding", "Force-completed (100%), switched to seeding");
                }

                continue;
            }

            if (stoppedIndices.Contains(i))
            {
                continue;
            }

            var bytesPerSecond = speeds[i];

            if (torrent.DownloadLimit > 0)
            {
                var perTorrentLimitBps = (long)torrent.DownloadLimit * 1024;
                bytesPerSecond = Math.Min(bytesPerSecond, perTorrentLimitBps);
            }

            var variationFactor = variationMin + (Random.Shared.NextDouble() * (variationMax - variationMin));
            var bytesThisTick = (long)(bytesPerSecond * variationFactor * TickInterval.TotalSeconds);

            torrent.Downloaded += bytesThisTick;

            if (torrent.TotalSize > 0)
            {
                var wasComplete = torrent.Progress >= 1.0;
                torrent.Progress = torrent.Downloaded >= torrent.TotalSize
                    ? 1.0
                    : Math.Round((double)torrent.Downloaded / torrent.TotalSize, 6);

                if (!wasComplete && torrent.Progress >= 1.0)
                {
                    _eventLogService.Info(torrent.Id, "Download", $"Download complete ({FormatBytes(torrent.TotalSize)})");
                }
            }

            var effectiveThreshold = torrent.Threshold > 0 ? torrent.Threshold / 100.0 : threshold;
            if (torrent.Progress >= effectiveThreshold && torrent.Status == TorrentStatus.Downloading)
            {
                _logger.Info("Torrent {0} reached download threshold ({1}%), switching to seeding", torrent.Name, (int)(effectiveThreshold * 100));
                _eventLogService.Info(torrent.Id, "Seeding", $"Download reached threshold ({(int)(effectiveThreshold * 100)}%), switching to seeding");
                torrent.Status = TorrentStatus.Seeding;
            }
        }
    }

    private void TickSeeding(List<Torrent> torrents, SpeedLimits limits, double variationMin, double variationMax)
    {
        var seederActive = Random.Shared.NextDouble() < _configService.SeederUploadActivityProbability;
        var stoppedIndices = SelectStoppedTorrents(torrents);

        var activeTorrentIndices = new List<int>();
        for (var i = 0; i < torrents.Count; i++)
        {
            if (!stoppedIndices.Contains(i))
            {
                activeTorrentIndices.Add(i);
            }
        }

        var activeCount = activeTorrentIndices.Count;

        var activePriorityWeights = new double[activeCount];
        for (var j = 0; j < activeCount; j++)
        {
            activePriorityWeights[j] = GetPriorityWeight(torrents[activeTorrentIndices[j]].Priority);
        }

        long[] speeds;
        if (activeCount > 0)
        {
            if (limits.MaxUploadSpeed == SpeedLimits.Unlimited)
            {
                speeds = new long[activeCount];
                Array.Fill(speeds, 1_000_000_000L);
            }
            else
            {
                speeds = _distributionManager.DistributeUploadSpeeds(activeCount, limits.MaxUploadSpeed, activePriorityWeights);
            }
        }
        else
        {
            speeds = Array.Empty<long>();
        }

        var activeIndex = 0;

        for (var i = 0; i < torrents.Count; i++)
        {
            var torrent = torrents[i];
            long uploadBytesThisTick;

            if (stoppedIndices.Contains(i))
            {
                uploadBytesThisTick = 0;
            }
            else
            {
                var bytesPerSecond = speeds[activeIndex++];

                if (torrent.UploadLimit > 0)
                {
                    var perTorrentLimitBps = (long)torrent.UploadLimit * 1024;
                    bytesPerSecond = Math.Min(bytesPerSecond, perTorrentLimitBps);
                }

                if (torrent.SuperSeeding)
                {
                    bytesPerSecond = (long)(bytesPerSecond * SuperSeedingBoost);
                }

                var variationFactor = variationMin + (Random.Shared.NextDouble() * (variationMax - variationMin));
                uploadBytesThisTick = (long)(bytesPerSecond * variationFactor * TickInterval.TotalSeconds);
            }

            if (!seederActive)
            {
                uploadBytesThisTick = 0;
            }

            torrent.Uploaded += uploadBytesThisTick;
            torrent.Ratio = torrent.TotalSize > 0
                ? Math.Round((double)torrent.Uploaded / torrent.TotalSize, 3)
                : 0;

            if (!torrent.ForceCompleted && torrent.Progress < 1.0 && torrent.TotalSize > 0)
            {
                var dlVariationFactor = variationMin + (Random.Shared.NextDouble() * (variationMax - variationMin));
                var effectiveDownloadBps = limits.MaxDownloadSpeed == SpeedLimits.Unlimited ? 1_000_000_000L : limits.MaxDownloadSpeed;
                var dlBytesThisTick = (long)(effectiveDownloadBps * dlVariationFactor * TickInterval.TotalSeconds / Math.Max(1, torrents.Count));

                torrent.Downloaded += dlBytesThisTick;
                torrent.Progress = torrent.Downloaded >= torrent.TotalSize
                    ? 1.0
                    : Math.Round((double)torrent.Downloaded / torrent.TotalSize, 6);

                if (torrent.Progress >= 1.0)
                {
                    _eventLogService.Info(torrent.Id, "Download", $"Download complete ({FormatBytes(torrent.TotalSize)})");
                }
            }
        }
    }

    private HashSet<int> SelectStoppedTorrents(List<Torrent> torrents)
    {
        var torrentCount = torrents.Count;
        var minPct = _configService.UploadStoppedMinPercentage;
        var maxPct = _configService.UploadStoppedMaxPercentage;

        if (maxPct <= 0 || torrentCount == 0)
        {
            return new HashSet<int>();
        }

        // Build list of indices eligible for stopping (ForceStart torrents are never stopped)
        var eligibleIndices = new List<int>();
        for (var i = 0; i < torrentCount; i++)
        {
            if (!torrents[i].ForceStart)
            {
                eligibleIndices.Add(i);
            }
        }

        if (eligibleIndices.Count == 0)
        {
            return new HashSet<int>();
        }

        var stoppedPct = minPct + (Random.Shared.NextDouble() * (maxPct - minPct));
        var stoppedCount = (int)Math.Ceiling(eligibleIndices.Count * (stoppedPct / 100.0));

        // Ensure at least one torrent remains active among eligible ones
        stoppedCount = Math.Min(stoppedCount, eligibleIndices.Count - 1);

        if (stoppedCount <= 0)
        {
            return new HashSet<int>();
        }

        // Shuffle eligible indices
        for (var j = eligibleIndices.Count - 1; j > 0; j--)
        {
            var k = Random.Shared.Next(j + 1);
            (eligibleIndices[j], eligibleIndices[k]) = (eligibleIndices[k], eligibleIndices[j]);
        }

        var stopped = new HashSet<int>();
        for (var i = 0; i < stoppedCount; i++)
        {
            stopped.Add(eligibleIndices[i]);
        }

        return stopped;
    }

    private HashSet<int> SelectDownloadStoppedTorrents(int torrentCount)
    {
        var minPct = _configService.DownloadStoppedMinPercentage;
        var maxPct = _configService.DownloadStoppedMaxPercentage;

        if (maxPct <= 0 || torrentCount == 0)
        {
            return new HashSet<int>();
        }

        var stoppedPct = minPct + (Random.Shared.NextDouble() * (maxPct - minPct));
        var stoppedCount = (int)Math.Ceiling(torrentCount * (stoppedPct / 100.0));

        stoppedCount = Math.Min(stoppedCount, torrentCount - 1);

        if (stoppedCount <= 0)
        {
            return new HashSet<int>();
        }

        var indices = new int[torrentCount];
        for (var i = 0; i < torrentCount; i++)
        {
            indices[i] = i;
        }

        for (var j = torrentCount - 1; j > 0; j--)
        {
            var k = Random.Shared.Next(j + 1);
            (indices[j], indices[k]) = (indices[k], indices[j]);
        }

        var stopped = new HashSet<int>();
        for (var i = 0; i < stoppedCount; i++)
        {
            stopped.Add(indices[i]);
        }

        return stopped;
    }

    private bool HasForceStartTorrents()
    {
        return _torrentService.GetAll().Any(t =>
            t.ForceStart &&
            (t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Downloading));
    }

    private static double[] GetPriorityWeights(List<Torrent> torrents)
    {
        var weights = new double[torrents.Count];
        for (var i = 0; i < torrents.Count; i++)
        {
            weights[i] = GetPriorityWeight(torrents[i].Priority);
        }

        return weights;
    }

    private static double GetPriorityWeight(int priority)
    {
        return priority switch
        {
            2 => 2.0,
            0 => 0.5,
            _ => 1.0
        };
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:F1} {units[unit]}";
    }
}
