using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Seeding.Distribution;

public interface ISpeedDistributionManager
{
    long[] DistributeSpeeds(int torrentCount);
    long[] DistributeSpeeds(int torrentCount, long maxSpeed);
    long[] DistributeUploadSpeeds(int torrentCount, long maxSpeed);
    long[] DistributeUploadSpeeds(int torrentCount, long maxSpeed, double[] priorityWeights);
    long[] DistributeDownloadSpeeds(int torrentCount, long maxSpeed);
    long[] DistributeDownloadSpeeds(int torrentCount, long maxSpeed, double[] priorityWeights);
    List<string> GetAvailableDistributions();
    string CurrentDistribution { get; }
    void InvalidateCache();
}

public class SpeedDistributionManager : ISpeedDistributionManager
{
    private const long DefaultBytesPerSecond = 1_048_576;

    private readonly IEnumerable<ISpeedDistributor> _distributors;
    private readonly IConfigService _configService;
    private readonly ISystemClock _clock;
    private readonly Logger _logger;

    private readonly object _uploadCacheLock = new object();
    private readonly object _downloadCacheLock = new object();

    private DateTime _lastUploadRedistribution = DateTime.MinValue;
    private long[] _cachedUploadSpeeds;
    private int _cachedUploadCount;
    private long _cachedUploadMaxSpeed;
    private string _cachedUploadAlgorithm;
    private int _cachedUploadSpreadPercentage;

    private DateTime _lastDownloadRedistribution = DateTime.MinValue;
    private long[] _cachedDownloadSpeeds;
    private int _cachedDownloadCount;
    private long _cachedDownloadMaxSpeed;
    private string _cachedDownloadAlgorithm;
    private int _cachedDownloadSpreadPercentage;

    public string CurrentDistribution => _configService.UploadDistributionAlgorithm;

    public SpeedDistributionManager(
        IEnumerable<ISpeedDistributor> distributors,
        IConfigService configService,
        ISystemClock clock = null)
    {
        _distributors = distributors;
        _configService = configService;
        _clock = clock ?? new SystemClock();
        _logger = LogManager.GetCurrentClassLogger();
    }

    public long[] DistributeSpeeds(int torrentCount)
    {
        return DistributeSpeeds(torrentCount, 0);
    }

    public long[] DistributeSpeeds(int torrentCount, long maxSpeed)
    {
        return DistributeWithConfig(
            torrentCount,
            maxSpeed,
            _configService.UploadDistributionAlgorithm,
            _configService.UploadDistributionSpreadPercentage);
    }

    public long[] DistributeUploadSpeeds(int torrentCount, long maxSpeed)
    {
        var mode = _configService.UploadRedistributionMode;
        var intervalMinutes = _configService.UploadCustomIntervalMinutes;
        var algorithm = _configService.UploadDistributionAlgorithm;
        var spread = _configService.UploadDistributionSpreadPercentage;

        lock (_uploadCacheLock)
        {
            if (ShouldRedistribute(
                mode,
                intervalMinutes,
                _lastUploadRedistribution,
                _cachedUploadSpeeds,
                _cachedUploadCount,
                _cachedUploadMaxSpeed,
                _cachedUploadAlgorithm,
                _cachedUploadSpreadPercentage,
                torrentCount,
                maxSpeed,
                algorithm,
                spread))
            {
                _cachedUploadSpeeds = DistributeWithConfig(torrentCount, maxSpeed, algorithm, spread);
                _cachedUploadCount = torrentCount;
                _cachedUploadMaxSpeed = maxSpeed;
                _cachedUploadAlgorithm = algorithm;
                _cachedUploadSpreadPercentage = spread;
                _lastUploadRedistribution = _clock.UtcNow;
                _logger.Debug(
                    "Redistributed upload speeds using {0} (spread {1}%) across {2} torrents",
                    algorithm,
                    spread,
                    torrentCount);
            }

            return (long[])_cachedUploadSpeeds.Clone();
        }
    }

    public long[] DistributeUploadSpeeds(int torrentCount, long maxSpeed, double[] priorityWeights)
    {
        var speeds = DistributeUploadSpeeds(torrentCount, maxSpeed);
        return ApplyPriorityWeights(speeds, priorityWeights);
    }

    public long[] DistributeDownloadSpeeds(int torrentCount, long maxSpeed)
    {
        var mode = _configService.DownloadRedistributionMode;
        var intervalMinutes = _configService.DownloadCustomIntervalMinutes;
        var algorithm = _configService.DownloadDistributionAlgorithm;
        var spread = _configService.DownloadDistributionSpreadPercentage;

        lock (_downloadCacheLock)
        {
            if (ShouldRedistribute(
                mode,
                intervalMinutes,
                _lastDownloadRedistribution,
                _cachedDownloadSpeeds,
                _cachedDownloadCount,
                _cachedDownloadMaxSpeed,
                _cachedDownloadAlgorithm,
                _cachedDownloadSpreadPercentage,
                torrentCount,
                maxSpeed,
                algorithm,
                spread))
            {
                _cachedDownloadSpeeds = DistributeWithConfig(torrentCount, maxSpeed, algorithm, spread);
                _cachedDownloadCount = torrentCount;
                _cachedDownloadMaxSpeed = maxSpeed;
                _cachedDownloadAlgorithm = algorithm;
                _cachedDownloadSpreadPercentage = spread;
                _lastDownloadRedistribution = _clock.UtcNow;
                _logger.Debug(
                    "Redistributed download speeds using {0} (spread {1}%) across {2} torrents",
                    algorithm,
                    spread,
                    torrentCount);
            }

            return (long[])_cachedDownloadSpeeds.Clone();
        }
    }

    public long[] DistributeDownloadSpeeds(int torrentCount, long maxSpeed, double[] priorityWeights)
    {
        var speeds = DistributeDownloadSpeeds(torrentCount, maxSpeed);
        return ApplyPriorityWeights(speeds, priorityWeights);
    }

    public void InvalidateCache()
    {
        lock (_uploadCacheLock)
        {
            _cachedUploadSpeeds = null;
            _cachedUploadCount = 0;
            _cachedUploadMaxSpeed = 0;
            _cachedUploadAlgorithm = null;
            _cachedUploadSpreadPercentage = 0;
            _lastUploadRedistribution = DateTime.MinValue;
        }

        lock (_downloadCacheLock)
        {
            _cachedDownloadSpeeds = null;
            _cachedDownloadCount = 0;
            _cachedDownloadMaxSpeed = 0;
            _cachedDownloadAlgorithm = null;
            _cachedDownloadSpreadPercentage = 0;
            _lastDownloadRedistribution = DateTime.MinValue;
        }
    }

    public List<string> GetAvailableDistributions()
    {
        return _distributors.Select(d => d.Name).ToList();
    }

    private long[] DistributeWithConfig(int torrentCount, long maxSpeed, string algorithm, int spreadPercentage)
    {
        var distributor = _distributors.FirstOrDefault(d =>
                string.Equals(d.Name, algorithm, StringComparison.OrdinalIgnoreCase))
            ?? _distributors.First();

        var effectiveSpeed = maxSpeed > 0 ? maxSpeed : DefaultBytesPerSecond;
        var speeds = distributor.Distribute(effectiveSpeed, torrentCount);

        if (spreadPercentage < 100 && torrentCount > 0)
        {
            var equalShare = effectiveSpeed / torrentCount;
            var spreadFactor = spreadPercentage / 100.0;

            for (var i = 0; i < torrentCount; i++)
            {
                speeds[i] = (long)(equalShare + ((speeds[i] - equalShare) * spreadFactor));
            }
        }

        return speeds;
    }

    private static long[] ApplyPriorityWeights(long[] speeds, double[] weights)
    {
        if (weights == null || weights.Length != speeds.Length || speeds.Length == 0)
        {
            return speeds;
        }

        var totalOriginal = 0L;
        for (var i = 0; i < speeds.Length; i++)
        {
            totalOriginal += speeds[i];
        }

        if (totalOriginal <= 0)
        {
            return speeds;
        }

        var weightedSpeeds = new double[speeds.Length];
        var totalWeighted = 0.0;

        for (var i = 0; i < speeds.Length; i++)
        {
            weightedSpeeds[i] = speeds[i] * weights[i];
            totalWeighted += weightedSpeeds[i];
        }

        if (totalWeighted <= 0)
        {
            return speeds;
        }

        var result = new long[speeds.Length];
        for (var i = 0; i < speeds.Length; i++)
        {
            result[i] = (long)(totalOriginal * (weightedSpeeds[i] / totalWeighted));
        }

        return result;
    }

    private bool ShouldRedistribute(
        string mode,
        int intervalMinutes,
        DateTime lastRedistribution,
        long[] cached,
        int cachedCount,
        long cachedMaxSpeed,
        string cachedAlgorithm,
        int cachedSpreadPercentage,
        int currentCount,
        long currentMaxSpeed,
        string currentAlgorithm,
        int currentSpreadPercentage)
    {
        if (cached == null ||
            cachedCount != currentCount ||
            cachedMaxSpeed != currentMaxSpeed ||
            !string.Equals(cachedAlgorithm, currentAlgorithm, StringComparison.OrdinalIgnoreCase) ||
            cachedSpreadPercentage != currentSpreadPercentage)
        {
            return true;
        }

        return mode switch
        {
            "tick" => true,
            "interval" => _clock.UtcNow - lastRedistribution >= TimeSpan.FromMinutes(intervalMinutes),
            "fixed" => false,
            _ => true
        };
    }
}
