using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Seeding.Distribution;
using NzbDrone.Core.Seeding.Scheduling;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Seeding;

public class SpeedPolicy : ISpeedPolicy
{
    private const double SuperSeedingBoost = 1.5;

    private readonly ISpeedDistributionManager _distributionManager;
    private readonly ISpeedScheduler _speedScheduler;
    private readonly IConfigService _configService;
    private readonly ITorrentEventLogService _eventLogService;
    private readonly ITorrentStateMachine _stateMachine;
    private readonly IStopPolicy _stopPolicy;
    private readonly Random _random;
    private readonly Logger _logger;

    public SpeedPolicy(
        ISpeedDistributionManager distributionManager,
        ISpeedScheduler speedScheduler,
        IConfigService configService,
        ITorrentEventLogService eventLogService,
        ITorrentStateMachine stateMachine,
        IStopPolicy stopPolicy,
        Random random = null)
    {
        _distributionManager = distributionManager;
        _speedScheduler = speedScheduler;
        _configService = configService;
        _eventLogService = eventLogService;
        _stateMachine = stateMachine;
        _stopPolicy = stopPolicy;
        _random = random ?? Random.Shared;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public SpeedLimits GetEffectiveLimits()
    {
        var limits = _speedScheduler.GetCurrentLimits();

        var configUploadSpeedKbps = _configService.AlternativeSpeedEnabled
            ? _configService.AltUploadSpeedKbps
            : _configService.MaxUploadSpeedKbps;
        var configDownloadSpeedKbps = _configService.AlternativeSpeedEnabled
            ? _configService.AltDownloadSpeedKbps
            : _configService.MaxDownloadSpeedKbps;

        SpeedLimitMerger.Apply(limits, (long)configUploadSpeedKbps * 1024, (long)configDownloadSpeedKbps * 1024);
        return limits;
    }

    public void ProcessDownloading(List<Torrent> torrents, SpeedLimits limits, TimeSpan tickInterval)
    {
        var stoppedIndices = _stopPolicy.SelectDownloadStoppedTorrents(torrents);
        var priorityWeights = GetPriorityWeights(torrents);
        var variationMin = _configService.SpeedVariationMin;
        var variationMax = _configService.SpeedVariationMax;
        var thresholdPercent = _configService.DownloadThresholdPercent;
        var threshold = thresholdPercent / 100.0;
        var maxDownloadSpeed = limits.MaxDownloadSpeed;

        var speeds = maxDownloadSpeed == SpeedLimits.Unlimited
            ? Enumerable.Repeat(1_000_000_000L, torrents.Count).ToArray()
            : _distributionManager.DistributeDownloadSpeeds(torrents.Count, maxDownloadSpeed, priorityWeights);

        for (var i = 0; i < torrents.Count; i++)
        {
            var torrent = torrents[i];

            if (torrent.ForceCompleted)
            {
                _stateMachine.HandleForceCompleted(torrent);
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

            var variationFactor = variationMin + (_random.NextDouble() * (variationMax - variationMin));
            var bytesThisTick = (long)(bytesPerSecond * variationFactor * tickInterval.TotalSeconds);

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

            _stateMachine.CheckDownloadThreshold(torrent, threshold);
        }
    }

    public void ProcessSeeding(List<Torrent> torrents, SpeedLimits limits, TimeSpan tickInterval)
    {
        var seederActive = _random.NextDouble() < _configService.SeederUploadActivityProbability;
        var stoppedIndices = _stopPolicy.SelectStoppedTorrents(torrents);
        var variationMin = _configService.SpeedVariationMin;
        var variationMax = _configService.SpeedVariationMax;

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

                var variationFactor = variationMin + (_random.NextDouble() * (variationMax - variationMin));
                uploadBytesThisTick = (long)(bytesPerSecond * variationFactor * tickInterval.TotalSeconds);
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
                var dlVariationFactor = variationMin + (_random.NextDouble() * (variationMax - variationMin));
                var effectiveDownloadBps = limits.MaxDownloadSpeed == SpeedLimits.Unlimited ? 1_000_000_000L : limits.MaxDownloadSpeed;
                var dlBytesThisTick = (long)(effectiveDownloadBps * dlVariationFactor * tickInterval.TotalSeconds / Math.Max(1, torrents.Count));

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

    public static double[] GetPriorityWeights(List<Torrent> torrents)
    {
        var weights = new double[torrents.Count];
        for (var i = 0; i < torrents.Count; i++)
        {
            weights[i] = GetPriorityWeight(torrents[i].Priority);
        }

        return weights;
    }

    public static double GetPriorityWeight(int priority)
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
