using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Seeding.Distribution;
using NzbDrone.Core.Seeding.Scheduling;
using NzbDrone.Core.Simulation.Swarm;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Seeding;

public class SpeedPolicy : ISpeedPolicy,
    IHandle<TorrentStatusChangedEvent>,
    IHandle<TorrentPausedEvent>,
    IHandle<TorrentDeletedEvent>
{
    public const long DefaultMaxUploadPerLeecherBps = 2_500_000;
    public const double WarmUpWindowSeconds = 180.0;

    public const int TcpIpHeaderBytes = 40;
    public const int UdpHeaderBytes = 28;
    public const int BitTorrentPieceFramingBytes = 13;
    public const int StandardPieceChunkBytes = 16 * 1024;
    public const int StandardMssBytes = 1460;

    public double ProtocolOverheadFactor { get; set; }
    public long OutOfBandOverheadBytesPerSec { get; set; }

    private double EffectiveOverheadFactor =>
        ProtocolOverheadFactor > 0 ? ProtocolOverheadFactor : (_configService?.ProtocolOverheadFactor ?? 0.0);

    public static long EstimateFramingAndTransportOverhead(
        long payloadBytes,
        int chunkSizeBytes = StandardPieceChunkBytes,
        int framingBytes = BitTorrentPieceFramingBytes,
        int headerBytes = TcpIpHeaderBytes,
        int mssBytes = StandardMssBytes)
    {
        if (payloadBytes <= 0)
        {
            return 0;
        }

        var chunks = (long)Math.Ceiling((double)payloadBytes / chunkSizeBytes);
        var framingOverhead = chunks * framingBytes;
        var packets = (long)Math.Ceiling((double)(payloadBytes + framingOverhead) / mssBytes);
        var transportOverhead = packets * headerBytes;

        return framingOverhead + transportOverhead;
    }

    public static double CalculateOverheadFactor(
        int chunkSizeBytes = StandardPieceChunkBytes,
        int framingBytes = BitTorrentPieceFramingBytes,
        int headerBytes = TcpIpHeaderBytes,
        int mssBytes = StandardMssBytes)
    {
        var overhead = EstimateFramingAndTransportOverhead(chunkSizeBytes, chunkSizeBytes, framingBytes, headerBytes, mssBytes);
        return (double)overhead / chunkSizeBytes;
    }

    public static long CalculateWireUsage(long payloadBytes, double overheadFactor)
    {
        if (payloadBytes <= 0)
        {
            return 0;
        }

        return (long)Math.Ceiling(payloadBytes * (1.0 + Math.Max(0.0, overheadFactor)));
    }

    public static long CalculateMaxPayloadAllowance(long wireLimit, double overheadFactor)
    {
        if (wireLimit <= 0)
        {
            return 0;
        }

        if (overheadFactor <= 0.0)
        {
            return wireLimit;
        }

        return (long)Math.Floor(wireLimit / (1.0 + overheadFactor));
    }

    private readonly ConcurrentDictionary<int, DateTime> _seedingStartTimes = new();
    private readonly ISpeedDistributionManager _distributionManager;
    private readonly ISpeedScheduler _speedScheduler;
    private readonly IConfigService _configService;
    private readonly ITorrentEventLogService _eventLogService;
    private readonly ITorrentStateMachine _stateMachine;
    private readonly IStopPolicy _stopPolicy;
    private readonly IRandomNumberGenerator _random;
    private readonly ISwarmAnalyzer _swarmAnalyzer;
    private readonly IEventAggregator _eventAggregator;
    private readonly ICategoryService _categoryService;
    private readonly ITagService _tagService;
    private readonly Logger _logger;
    private readonly IPieceBoundaryMasker _pieceBoundaryMasker;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ConcurrentDictionary<int, double> _downloadFractionalTokens = new();
    private readonly ConcurrentDictionary<int, double> _uploadFractionalTokens = new();
    public IBandwidthLimiter BandwidthLimiter { get; set; }

    public SpeedPolicy(
        ISpeedDistributionManager distributionManager,
        ISpeedScheduler speedScheduler,
        IConfigService configService,
        ITorrentEventLogService eventLogService,
        ITorrentStateMachine stateMachine,
        IStopPolicy stopPolicy,
        IRandomNumberGenerator random = null,
        ISwarmAnalyzer swarmAnalyzer = null,
        IEventAggregator eventAggregator = null,
        ICategoryService categoryService = null,
        ITagService tagService = null,
        IPieceBoundaryMasker pieceBoundaryMasker = null,
        ITorrentFileService torrentFileService = null,
        IBandwidthLimiter bandwidthLimiter = null)
    {
        BandwidthLimiter = bandwidthLimiter ?? NzbDrone.Core.Bandwidth.BandwidthLimiter.Instance;
        _distributionManager = distributionManager;
        _speedScheduler = speedScheduler;
        _configService = configService;
        _eventLogService = eventLogService;
        _stateMachine = stateMachine;
        _stopPolicy = stopPolicy;
        _random = random ?? new RandomNumberGenerator();
        _swarmAnalyzer = swarmAnalyzer ?? new SwarmAnalyzer(configService);
        _eventAggregator = eventAggregator;
        _categoryService = categoryService;
        _tagService = tagService;
        _pieceBoundaryMasker = pieceBoundaryMasker ?? new PieceBoundaryMasker();
        _torrentFileService = torrentFileService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public SpeedLimits GetEffectiveLimits()
    {
        var limits = _speedScheduler.GetCurrentLimits();
        if (limits == null)
        {
            return null;
        }

        var isAltEnabled = _configService.AlternativeSpeedEnabled;
        var configUploadSpeedKbps = isAltEnabled
            ? _configService.AltUploadSpeedKbps
            : _configService.MaxUploadSpeedKbps;
        var configDownloadSpeedKbps = isAltEnabled
            ? _configService.AltDownloadSpeedKbps
            : _configService.MaxDownloadSpeedKbps;

        var uploadLimitBps = configUploadSpeedKbps > 0 ? (long)configUploadSpeedKbps * 1024 : SpeedLimits.Unlimited;
        var downloadLimitBps = configDownloadSpeedKbps > 0 ? (long)configDownloadSpeedKbps * 1024 : SpeedLimits.Unlimited;

        return SpeedLimitMerger.Apply(limits, uploadLimitBps, downloadLimitBps, isAltEnabled);
    }

    public void ProcessDownloading(List<Torrent> torrents, SpeedLimits limits, TimeSpan tickInterval)
    {
        if (BandwidthLimiter != null && limits != null)
        {
            BandwidthLimiter.SetGlobalLimits(
                limits.MaxUploadSpeed == SpeedLimits.Unlimited ? 0 : limits.MaxUploadSpeed,
                limits.MaxDownloadSpeed == SpeedLimits.Unlimited ? 0 : limits.MaxDownloadSpeed);
        }

        var stoppedIndices = _stopPolicy.SelectDownloadStoppedTorrents(torrents);

        var activeTorrentIndices = new List<int>();
        for (var i = 0; i < torrents.Count; i++)
        {
            var torrent = torrents[i];

            if (torrent.ForceCompleted)
            {
                _stateMachine.HandleForceCompleted(torrent);
                continue;
            }

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

        var variationMin = _configService.SpeedVariationMin;
        var variationMax = _configService.SpeedVariationMax;
        var thresholdPercent = _configService.DownloadThresholdPercent;
        var threshold = thresholdPercent / 100.0;
        var effectiveFactor = EffectiveOverheadFactor;
        var maxDownloadSpeed = limits.MaxDownloadSpeed;
        if (maxDownloadSpeed != SpeedLimits.Unlimited && effectiveFactor > 0)
        {
            maxDownloadSpeed = (long)Math.Round(maxDownloadSpeed / (1.0 + effectiveFactor));
            if (OutOfBandOverheadBytesPerSec > 0)
            {
                maxDownloadSpeed = Math.Max(0L, maxDownloadSpeed - OutOfBandOverheadBytesPerSec);
            }
        }

        long[] speeds;
        if (activeCount > 0)
        {
            if (maxDownloadSpeed == SpeedLimits.Unlimited)
            {
                speeds = new long[activeCount];
                for (var j = 0; j < activeCount; j++)
                {
                    speeds[j] = (long)(1_000_000_000L * activePriorityWeights[j]);
                }
            }
            else
            {
                speeds = _distributionManager.DistributeDownloadSpeeds(activeCount, maxDownloadSpeed, activePriorityWeights)
                    ?? Array.Empty<long>();
            }
        }
        else
        {
            speeds = Array.Empty<long>();
        }

        for (var j = 0; j < activeCount; j++)
        {
            var torrent = torrents[activeTorrentIndices[j]];
            var bytesPerSecond = j < speeds.Length ? speeds[j] : 0L;

            var effectiveDlLimit = GetDownloadLimit(torrent);

            if (effectiveDlLimit > 0)
            {
                var perTorrentLimitBps = (long)effectiveDlLimit * 1024;
                bytesPerSecond = Math.Min(bytesPerSecond, perTorrentLimitBps);
            }

            if (BandwidthLimiter != null && !string.IsNullOrEmpty(torrent.InfoHash))
            {
                var dlLimit = effectiveDlLimit > 0 ? (long)effectiveDlLimit * 1024 : 0;
                var ulLimit = GetUploadLimit(torrent) > 0 ? (long)GetUploadLimit(torrent) * 1024 : 0;
                BandwidthLimiter.SetTorrentLimits(torrent.InfoHash, ulLimit, dlLimit);
            }

            var variationFactor = variationMin + (_random.NextDouble() * (variationMax - variationMin));
            var currentFraction = _downloadFractionalTokens.TryGetValue(torrent.Id, out var frac) ? frac : 0.0;
            var rawBytes = (bytesPerSecond * variationFactor * tickInterval.TotalSeconds) + currentFraction;
            var bytesThisTick = (long)rawBytes;
            _downloadFractionalTokens[torrent.Id] = rawBytes - bytesThisTick;

            if (bytesPerSecond > 0)
            {
                var maxBurst = Math.Max(16384L, (long)Math.Round(bytesPerSecond * Math.Max(tickInterval.TotalSeconds * 2.0, 0.100)));
                if (bytesThisTick > maxBurst)
                {
                    bytesThisTick = maxBurst;
                }
            }

            torrent.Downloaded += bytesThisTick;

            UpdateDownloadProgress(torrent);

            torrent.UpdateRatio();

            _stateMachine.CheckDownloadThreshold(torrent, threshold);
        }
    }

    public void ProcessSeeding(List<Torrent> torrents, SpeedLimits limits, TimeSpan tickInterval)
    {
        if (BandwidthLimiter != null && limits != null)
        {
            BandwidthLimiter.SetGlobalLimits(
                limits.MaxUploadSpeed == SpeedLimits.Unlimited ? 0 : limits.MaxUploadSpeed,
                limits.MaxDownloadSpeed == SpeedLimits.Unlimited ? 0 : limits.MaxDownloadSpeed);
        }

        var seederActive = _random.NextDouble() < _configService.SeederUploadActivityProbability;
        var stoppedIndices = _stopPolicy.SelectStoppedTorrents(torrents);
        var variationMin = _configService.SpeedVariationMin;
        var variationMax = _configService.SpeedVariationMax;
        var currentTime = DateTime.UtcNow;

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

        var effectiveFactor = EffectiveOverheadFactor;
        long[] speeds;
        if (activeCount > 0)
        {
            if (limits.MaxUploadSpeed == SpeedLimits.Unlimited)
            {
                speeds = new long[activeCount];
                for (var j = 0; j < activeCount; j++)
                {
                    speeds[j] = (long)(1_000_000_000L * activePriorityWeights[j]);
                }
            }
            else
            {
                var effectiveUploadSpeed = effectiveFactor > 0
                    ? (long)Math.Round(limits.MaxUploadSpeed / (1.0 + effectiveFactor))
                    : limits.MaxUploadSpeed;

                if (OutOfBandOverheadBytesPerSec > 0)
                {
                    effectiveUploadSpeed = Math.Max(0L, effectiveUploadSpeed - OutOfBandOverheadBytesPerSec);
                }

                speeds = _distributionManager.DistributeUploadSpeeds(activeCount, effectiveUploadSpeed, activePriorityWeights)
                    ?? Array.Empty<long>();
            }
        }
        else
        {
            speeds = Array.Empty<long>();
        }

        var activeIndex = 0;
        var uploadBytesPerTorrent = new long[torrents.Count];

        for (var i = 0; i < torrents.Count; i++)
        {
            var torrent = torrents[i];
            long uploadBytesThisTick;
            var isPausedOrZeroLeechers = false;

            if (stoppedIndices.Contains(i))
            {
                ResetSeedingStartTime(torrent.Id);
                uploadBytesThisTick = 0;
            }
            else
            {
                var bytesPerSecond = activeIndex < speeds.Length ? speeds[activeIndex++] : 0L;

                var effectiveUlLimit = GetUploadLimit(torrent);

                if (effectiveUlLimit > 0)
                {
                    var perTorrentLimitBps = (long)effectiveUlLimit * 1024;
                    bytesPerSecond = Math.Min(bytesPerSecond, perTorrentLimitBps);
                }

                if (_swarmAnalyzer != null && _configService.SwarmIntelligenceEnabled)
                {
                    var snapshot = new SwarmSnapshot
                    {
                        SeedCount = torrent.Seeders,
                        LeechCount = torrent.Leechers,
                        PieceAvailability = torrent.Availability > 0 ? torrent.Availability : (torrent.Progress >= 1.0 ? 1.0 : torrent.Progress),
                        TorrentSizeBytes = torrent.TotalSize,
                        UploadRateBytesPerSec = torrent.UploadSpeed,
                        DownloadRateBytesPerSec = torrent.DownloadSpeed,
                        ShareRatio = torrent.Ratio,
                        SeedingDuration = TimeSpan.FromSeconds(torrent.SeedingTime)
                    };

                    var rec = _swarmAnalyzer.Analyze(snapshot);
                    switch (rec.Recommendation)
                    {
                        case SeedingRecommendation.Boost:
                            bytesPerSecond = (long)(bytesPerSecond * (1.0 + (0.5 * rec.Confidence)));
                            break;
                        case SeedingRecommendation.Reduce:
                            bytesPerSecond = (long)(bytesPerSecond * Math.Max(0.1, 1.0 - (0.5 * rec.Confidence)));
                            break;
                        case SeedingRecommendation.Pause:
                            bytesPerSecond = 0;
                            break;
                        case SeedingRecommendation.Maintain:
                        default:
                            break;
                    }

                    if (rec.Recommendation == SeedingRecommendation.Pause)
                    {
                        bytesPerSecond = 0;
                        isPausedOrZeroLeechers = true;
                    }
                }

                _seedingStartTimes.GetOrAdd(torrent.Id, _ =>
                    torrent.SeedingTime > 0
                        ? currentTime.AddSeconds(-torrent.SeedingTime)
                        : currentTime);

                bytesPerSecond = CalculateEffectiveUploadSpeed(torrent, bytesPerSecond, currentTime);

                if (torrent.Leechers <= 0 || bytesPerSecond <= 0)
                {
                    isPausedOrZeroLeechers = true;
                    uploadBytesThisTick = 0;
                }
                else
                {
                    if (BandwidthLimiter != null && !string.IsNullOrEmpty(torrent.InfoHash))
                    {
                        var ulLimit = effectiveUlLimit > 0 ? (long)effectiveUlLimit * 1024 : 0;
                        var dlLimit = GetDownloadLimit(torrent) > 0 ? (long)GetDownloadLimit(torrent) * 1024 : 0;
                        BandwidthLimiter.SetTorrentLimits(torrent.InfoHash, ulLimit, dlLimit);
                    }

                    var variationFactor = variationMin + (_random.NextDouble() * (variationMax - variationMin));
                    var currentFraction = _uploadFractionalTokens.TryGetValue(torrent.Id, out var frac) ? frac : 0.0;
                    var rawBytes = (bytesPerSecond * variationFactor * tickInterval.TotalSeconds) + currentFraction;
                    uploadBytesThisTick = (long)rawBytes;
                    _uploadFractionalTokens[torrent.Id] = rawBytes - uploadBytesThisTick;

                    if (bytesPerSecond > 0)
                    {
                        var maxBurst = Math.Max(16384L, (long)Math.Round(bytesPerSecond * Math.Max(tickInterval.TotalSeconds * 2.0, 0.100)));
                        if (uploadBytesThisTick > maxBurst)
                        {
                            uploadBytesThisTick = maxBurst;
                        }
                    }
                }
            }

            if (!seederActive || isPausedOrZeroLeechers)
            {
                uploadBytesThisTick = 0;
            }

            uploadBytesPerTorrent[i] = uploadBytesThisTick;
        }

        if (limits != null && limits.MaxUploadSpeed != SpeedLimits.Unlimited && limits.MaxUploadSpeed >= 0)
        {
            var maxAllowedBytes = (long)Math.Round(limits.MaxUploadSpeed * tickInterval.TotalSeconds);
            if (maxAllowedBytes <= 0)
            {
                Array.Clear(uploadBytesPerTorrent, 0, uploadBytesPerTorrent.Length);
            }
            else
            {
                var maxPayloadBytes = effectiveFactor > 0
                    ? (long)Math.Floor(maxAllowedBytes / (1.0 + effectiveFactor))
                    : maxAllowedBytes;

                if (OutOfBandOverheadBytesPerSec > 0)
                {
                    var oobBytesThisTick = (long)Math.Round(OutOfBandOverheadBytesPerSec * tickInterval.TotalSeconds);
                    maxPayloadBytes = Math.Max(0L, maxPayloadBytes - oobBytesThisTick);
                }

                var totalAllocated = 0L;
                for (var i = 0; i < uploadBytesPerTorrent.Length; i++)
                {
                    totalAllocated += uploadBytesPerTorrent[i];
                }

                if (totalAllocated > maxPayloadBytes)
                {
                    var scale = maxPayloadBytes == 0 ? 0.0 : (double)maxPayloadBytes / totalAllocated;
                    var scaledTotal = 0L;
                    for (var i = 0; i < uploadBytesPerTorrent.Length; i++)
                    {
                        uploadBytesPerTorrent[i] = (long)(uploadBytesPerTorrent[i] * scale);
                        scaledTotal += uploadBytesPerTorrent[i];
                    }

                    var remainder = maxPayloadBytes - scaledTotal;
                    if (remainder > 0)
                    {
                        var eligibleIndices = Enumerable.Range(0, uploadBytesPerTorrent.Length)
                            .Where(idx => uploadBytesPerTorrent[idx] > 0)
                            .OrderByDescending(idx => uploadBytesPerTorrent[idx])
                            .ToList();

                        for (var r = 0; r < remainder && eligibleIndices.Count > 0; r++)
                        {
                            uploadBytesPerTorrent[eligibleIndices[r % eligibleIndices.Count]]++;
                        }
                    }
                }
            }
        }

        for (var i = 0; i < torrents.Count; i++)
        {
            var torrent = torrents[i];
            var uploadBytesThisTick = uploadBytesPerTorrent[i];

            torrent.Uploaded += uploadBytesThisTick;
            torrent.SimulatedUploaded += uploadBytesThisTick;

            torrent.UpdateRatio();

            if (!torrent.ForceCompleted && torrent.Progress > 0.0 && torrent.Downloaded > 0 && torrent.Progress < 1.0 && torrent.TotalSize > 0)
            {
                var dlVariationFactor = variationMin + (_random.NextDouble() * (variationMax - variationMin));
                var dlBytesThisTick = (long)(limits.MaxDownloadSpeed * dlVariationFactor * tickInterval.TotalSeconds / Math.Max(1, torrents.Count));

                torrent.Downloaded += dlBytesThisTick;
                UpdateDownloadProgress(torrent);
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

    private void UpdateDownloadProgress(Torrent torrent)
    {
        var files = torrent.Files ?? _torrentFileService?.GetByTorrentId(torrent.Id);
        var isSelective = files != null && files.Count > 0 && _pieceBoundaryMasker.IsSelectiveDownload(files);
        var wantedSize = isSelective ? _pieceBoundaryMasker.CalculateWantedSize(torrent, files) : torrent.TotalSize;
        var targetSize = isSelective ? wantedSize : torrent.TotalSize;

        if (targetSize > 0)
        {
            var wasComplete = torrent.Progress >= 1.0;
            torrent.Progress = torrent.Downloaded >= targetSize
                ? 1.0
                : Math.Min(1.0, Math.Round((double)torrent.Downloaded / targetSize, 6));

            if (!wasComplete && torrent.Progress >= 1.0)
            {
                _eventLogService.Info(torrent.Id, "Download", $"Download complete ({FormatBytes(targetSize)})");
                _eventAggregator?.PublishEvent(new TorrentDownloadCompletedEvent(torrent));
                _eventAggregator?.PublishEvent(new TorrentFinishedEvent(torrent));
            }
        }
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

    public int GetDownloadLimit(Torrent torrent)
    {
        if (torrent == null)
        {
            return 0;
        }

        if (torrent.DownloadLimit != 0)
        {
            return torrent.DownloadLimit;
        }

        var tags = GetTagsForTorrent(torrent);
        var tagLimits = tags
            .Where(t => t.DownloadLimitKbps.HasValue && t.DownloadLimitKbps.Value > 0)
            .Select(t => t.DownloadLimitKbps.Value)
            .ToList();

        if (tagLimits.Count > 0)
        {
            return tagLimits.Min();
        }

        if (_categoryService != null && !string.IsNullOrWhiteSpace(torrent.Category))
        {
            var cat = _categoryService.GetByName(torrent.Category);
            if (cat != null && cat.DefaultDownloadLimit > 0)
            {
                return cat.DefaultDownloadLimit;
            }
        }

        return 0;
    }

    public int GetUploadLimit(Torrent torrent)
    {
        if (torrent == null)
        {
            return 0;
        }

        if (torrent.UploadLimit != 0)
        {
            return torrent.UploadLimit;
        }

        var tags = GetTagsForTorrent(torrent);
        var tagLimits = tags
            .Where(t => t.UploadLimitKbps.HasValue && t.UploadLimitKbps.Value > 0)
            .Select(t => t.UploadLimitKbps.Value)
            .ToList();

        if (tagLimits.Count > 0)
        {
            return tagLimits.Min();
        }

        if (_categoryService != null && !string.IsNullOrWhiteSpace(torrent.Category))
        {
            var cat = _categoryService.GetByName(torrent.Category);
            if (cat != null && cat.DefaultUploadLimit > 0)
            {
                return cat.DefaultUploadLimit;
            }
        }

        return 0;
    }

    private List<Tag> GetTagsForTorrent(Torrent torrent)
    {
        if (_tagService == null || torrent.TagIds == null || torrent.TagIds.Count == 0)
        {
            return new List<Tag>();
        }

        var allTags = _tagService.GetAll();
        if (allTags != null && allTags.Count > 0)
        {
            var matched = allTags.Where(t => torrent.TagIds.Contains(t.Id)).ToList();
            if (matched.Count > 0)
            {
                return matched;
            }
        }

        var tags = new List<Tag>();
        foreach (var id in torrent.TagIds)
        {
            var tag = _tagService.Get(id);
            if (tag != null)
            {
                tags.Add(tag);
            }
        }

        return tags;
    }

    public void SetSeedingStartTime(int torrentId, DateTime startTime)
    {
        _seedingStartTimes[torrentId] = startTime;
    }

    public void ResetSeedingStartTime(int torrentId)
    {
        _seedingStartTimes.TryRemove(torrentId, out _);
        _uploadFractionalTokens.TryRemove(torrentId, out _);
        _downloadFractionalTokens.TryRemove(torrentId, out _);
    }

    public void Handle(TorrentStatusChangedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        if (message.NewStatus != TorrentStatus.Seeding)
        {
            ResetSeedingStartTime(message.Torrent.Id);
        }
    }

    public void Handle(TorrentPausedEvent message)
    {
        if (message?.Torrent != null)
        {
            ResetSeedingStartTime(message.Torrent.Id);
        }
    }

    public void Handle(TorrentDeletedEvent message)
    {
        var torrentId = message.TorrentId > 0 ? message.TorrentId : message.Torrent?.Id ?? 0;
        if (torrentId > 0)
        {
            ResetSeedingStartTime(torrentId);
        }
    }

    public long ComputeUploadSpeed(Torrent torrent, long targetSpeed, DateTime? now = null)
    {
        return CalculateEffectiveUploadSpeed(torrent, targetSpeed, now);
    }

    public long CalculateEffectiveUploadSpeed(Torrent torrent, long targetSpeed, DateTime? now = null)
    {
        if (torrent == null || targetSpeed <= 0)
        {
            return 0;
        }

        if (torrent.Leechers <= 0)
        {
            return 0;
        }

        var maxPlausibleUpload = (long)torrent.Leechers * DefaultMaxUploadPerLeecherBps;
        var speed = Math.Min(targetSpeed, maxPlausibleUpload);

        var currentTime = now ?? DateTime.UtcNow;
        DateTime startTime;
        if (_seedingStartTimes.TryGetValue(torrent.Id, out var existingStart))
        {
            startTime = existingStart;
        }
        else if (torrent.SeedingTime > 0)
        {
            startTime = currentTime.AddSeconds(-torrent.SeedingTime);
            _seedingStartTimes.TryAdd(torrent.Id, startTime);
        }
        else
        {
            return speed;
        }

        var elapsedSeconds = (currentTime - startTime).TotalSeconds;
        var rampFactor = Math.Clamp(elapsedSeconds / WarmUpWindowSeconds, 0.05, 1.0);
        return (long)(speed * rampFactor);
    }
}
