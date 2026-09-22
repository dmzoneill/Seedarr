using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Telemetry;

public class SystemResourceService : ISystemResourceService
{
    private static readonly Process CurrentProcess = Process.GetCurrentProcess();
    private static readonly object CpuLock = new();
    private static readonly object DriveLock = new();
    private static readonly TimeSpan DriveCacheDuration = TimeSpan.FromSeconds(30);

    private static long lastSampleTimestamp = Stopwatch.GetTimestamp();
    private static TimeSpan lastTotalProcessorTime = CurrentProcess.TotalProcessorTime;
    private static double cachedCpuPercent;
    private static DateTime lastDriveSampleTime = DateTime.MinValue;
    private static List<DiskMountPointMetrics> cachedDriveMetrics = new();

    private readonly ITorrentService _torrentService;
    private readonly IConfigService _configService;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly Logger _logger;

    public SystemResourceService(
        ITorrentService torrentService,
        IConfigService configService,
        IAppFolderInfo appFolderInfo = null)
    {
        _torrentService = torrentService;
        _configService = configService;
        _appFolderInfo = appFolderInfo;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public HostProcessResourceMetrics GetHostMetrics()
    {
        double cpuPercent;
        lock (CpuLock)
        {
            var nowTimestamp = Stopwatch.GetTimestamp();
            var nowTotalProcessorTime = CurrentProcess.TotalProcessorTime;
            var elapsedSeconds = (double)(nowTimestamp - lastSampleTimestamp) / Stopwatch.Frequency;

            if (elapsedSeconds > 0.5)
            {
                var cpuUsedSeconds = (nowTotalProcessorTime - lastTotalProcessorTime).TotalSeconds;
                var totalCapacity = elapsedSeconds * Environment.ProcessorCount;
                cachedCpuPercent = totalCapacity > 0
                    ? Math.Clamp(Math.Round((cpuUsedSeconds / totalCapacity) * 100.0, 1), 0.0, 100.0)
                    : 0.0;
                lastSampleTimestamp = nowTimestamp;
                lastTotalProcessorTime = nowTotalProcessorTime;
            }

            cpuPercent = cachedCpuPercent;
        }

        try
        {
            CurrentProcess.Refresh();
        }
        catch (Exception ex)
        {
            _logger.Trace(ex, "Failed to refresh current process metrics.");
        }

        long uptimeSeconds = 0;
        try
        {
            uptimeSeconds = (long)(DateTime.UtcNow - CurrentProcess.StartTime.ToUniversalTime()).TotalSeconds;
        }
        catch
        {
            uptimeSeconds = 0;
        }

        ThreadPool.GetAvailableThreads(out int availWorker, out int availPort);
        ThreadPool.GetMaxThreads(out int maxWorker, out int maxPort);

        return new HostProcessResourceMetrics
        {
            CpuProcessPercent = cpuPercent,
            CpuCores = Environment.ProcessorCount,
            WorkingSetBytes = CurrentProcess.WorkingSet64,
            PrivateMemoryBytes = CurrentProcess.PrivateMemorySize64,
            VirtualMemoryBytes = CurrentProcess.VirtualMemorySize64,
            ManagedHeapBytes = GC.GetTotalMemory(false),
            GcGen0Collections = GC.CollectionCount(0),
            GcGen1Collections = GC.CollectionCount(1),
            GcGen2Collections = GC.CollectionCount(2),
            ThreadCount = CurrentProcess.Threads.Count,
            ThreadPoolWorkerThreads = Math.Max(0, maxWorker - availWorker),
            ThreadPoolCompletionPortThreads = Math.Max(0, maxPort - availPort),
            HandleCount = CurrentProcess.HandleCount,
            UptimeSeconds = uptimeSeconds,
            DiskDrives = GetDiskDrivesCached(),
            Timestamp = DateTime.UtcNow
        };
    }

    public TorrentEngineMetrics GetTorrentEngineMetrics()
    {
        List<Torrent> torrents;
        try
        {
            torrents = _torrentService.GetAll() ?? new List<Torrent>();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to retrieve torrents for engine telemetry.");
            torrents = new List<Torrent>();
        }

        var downloading = torrents.Count(t => t.Status == TorrentStatus.Downloading);
        var seeding = torrents.Count(t => t.Status == TorrentStatus.Seeding);
        var paused = torrents.Count(t => t.Status == TorrentStatus.Paused || t.Status == TorrentStatus.Stopped);
        var active = torrents.Count(t => t.Active || t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding);

        var totalDownSpeed = torrents.Sum(t => (long)t.DownloadSpeed);
        var totalUpSpeed = torrents.Sum(t => (long)t.UploadSpeed);
        var totalDownloaded = torrents.Sum(t => t.Downloaded);
        var totalUploaded = torrents.Sum(t => t.Uploaded);

        var seeds = torrents.Sum(t => t.Seeders);
        var leechs = torrents.Sum(t => t.Leechers);
        var swarmPeers = seeds + leechs;

        var protoDownSpeed = (long)(totalDownSpeed * 0.035);
        var protoUpSpeed = (long)(totalUpSpeed * 0.035);
        var protoDownloaded = (long)(totalDownloaded * 0.035);
        var protoUploaded = (long)(totalUploaded * 0.035);

        return new TorrentEngineMetrics
        {
            EngineId = "SeedarrSimulationEngine",
            DisplayName = "Seedarr Swarm Simulator",
            Version = "1.3.0",
            IsRunning = true,
            ActiveTorrents = active,
            DownloadingTorrents = downloading,
            SeedingTorrents = seeding,
            PausedTorrents = paused,
            TotalDownloadSpeed = totalDownSpeed,
            TotalUploadSpeed = totalUpSpeed,
            TotalProtocolDownloadSpeed = protoDownSpeed,
            TotalProtocolUploadSpeed = protoUpSpeed,
            TotalDataDownloaded = totalDownloaded,
            TotalDataUploaded = totalUploaded,
            TotalProtocolDownloaded = protoDownloaded,
            TotalProtocolUploaded = protoUploaded,
            ProtocolOverheadPercentage = 3.5,
            OpenConnections = swarmPeers,
            HalfOpenConnections = Math.Min(10, swarmPeers / 4),
            MaxConnections = 500,
            ConnectedSeeds = seeds,
            ConnectedLeechers = leechs,
            TotalSwarmPeers = swarmPeers,
            DhtNodeCount = 384,
            DhtState = "Ready",
            DiskCacheBytesAllocated = 64L * 1024 * 1024
        };
    }

    public IReadOnlyList<TorrentResourceMetrics> GetPerTorrentMetrics()
    {
        List<Torrent> torrents;
        try
        {
            torrents = _torrentService.GetAll() ?? new List<Torrent>();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to retrieve torrents for per-torrent telemetry.");
            torrents = new List<Torrent>();
        }

        return torrents.Select(MapTorrentToMetrics).ToList();
    }

    public TorrentResourceMetrics GetTorrentMetrics(int torrentId)
    {
        try
        {
            var torrent = _torrentService.Get(torrentId);
            return torrent != null ? MapTorrentToMetrics(torrent) : null;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to get torrent metrics for ID {0}", torrentId);
            return null;
        }
    }

    public List<SubsystemTelemetryReport> GetSubsystemTelemetry()
    {
        var reports = new List<SubsystemTelemetryReport>();

        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "bittorrent",
            SubsystemName = "BitTorrent Engine",
            ActiveProvider = "SimulationEngine",
            Status = "Healthy",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                { "activeTorrents", _torrentService.GetAll()?.Count ?? 0 },
                { "protocol", "BitTorrent v1/v2" },
                { "dhtEnabled", true }
            }
        });

        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "ai",
            SubsystemName = "Seedarr Copilot AI",
            ActiveProvider = "LocalRuleModel",
            Status = "Healthy",
            ResourceLoad = "Idle",
            Metrics = new Dictionary<string, object>
            {
                { "engine", "DeterministicRuleEngine" },
                { "rulesLoaded", 12 }
            }
        });

        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "network",
            SubsystemName = "Network & Port Management",
            ActiveProvider = "UPnP/NAT-PMP",
            Status = "Healthy",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                { "portMappingActive", true },
                { "ipv6Supported", true }
            }
        });

        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "trackerboost",
            SubsystemName = "TrackerBoost Engine",
            ActiveProvider = "SwarmIntelligence",
            Status = "Healthy",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                { "harvesterActive", true },
                { "optimizationLevel", "High" }
            }
        });

        reports.Add(new SubsystemTelemetryReport
        {
            SubsystemId = "storage",
            SubsystemName = "Storage Manager",
            ActiveProvider = "FileSystem",
            Status = "Healthy",
            ResourceLoad = "Nominal",
            Metrics = new Dictionary<string, object>
            {
                { "monitoredDrives", DriveInfo.GetDrives().Count(d => d.IsReady) }
            }
        });

        return reports;
    }

    public Task<List<SubsystemTelemetryReport>> GetSubsystemTelemetryAsync(string subsystemId = null, CancellationToken cancellationToken = default)
    {
        var list = GetSubsystemTelemetry();
        if (!string.IsNullOrEmpty(subsystemId))
        {
            list = list.Where(s => string.Equals(s.SubsystemId, subsystemId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return Task.FromResult(list);
    }

    public SystemResourceTelemetrySnapshot GetFullTelemetrySnapshot()
    {
        return new SystemResourceTelemetrySnapshot
        {
            Host = GetHostMetrics(),
            TorrentEngine = GetTorrentEngineMetrics(),
            PerTorrent = GetPerTorrentMetrics().ToList(),
            Subsystems = GetSubsystemTelemetry(),
            Timestamp = DateTime.UtcNow
        };
    }

    public Task<SystemResourceTelemetrySnapshot> GetFullTelemetrySnapshotAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GetFullTelemetrySnapshot());
    }

    private static TorrentResourceMetrics MapTorrentToMetrics(Torrent t)
    {
        var downSpeed = (long)t.DownloadSpeed;
        var upSpeed = (long)t.UploadSpeed;
        var protoDownSpeed = (long)(downSpeed * 0.035);
        var protoUpSpeed = (long)(upSpeed * 0.035);
        var protoDownloaded = (long)(t.Downloaded * 0.035);
        var protoUploaded = (long)(t.Uploaded * 0.035);

        return new TorrentResourceMetrics
        {
            TorrentId = t.Id,
            InfoHash = t.InfoHash ?? string.Empty,
            Name = t.Name ?? string.Empty,
            Category = t.Category ?? string.Empty,
            Status = t.Status.ToString(),
            Progress = t.Progress,
            TotalBytes = t.TotalSize,
            PayloadDownloadSpeed = downSpeed,
            PayloadUploadSpeed = upSpeed,
            ProtocolDownloadSpeed = protoDownSpeed,
            ProtocolUploadSpeed = protoUpSpeed,
            DownloadedPayload = t.Downloaded,
            UploadedPayload = t.Uploaded,
            ProtocolDownloaded = protoDownloaded,
            ProtocolUploaded = protoUploaded,
            EfficiencyRatio = t.Ratio,
            ConnectedPeers = t.Seeders + t.Leechers
        };
    }

    private static List<DiskMountPointMetrics> GetDiskDrivesCached()
    {
        lock (DriveLock)
        {
            if (DateTime.UtcNow - lastDriveSampleTime < DriveCacheDuration && cachedDriveMetrics.Count > 0)
            {
                return cachedDriveMetrics;
            }

            var drives = new List<DiskMountPointMetrics>();
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady) continue;

                    var total = drive.TotalSize;
                    var free = drive.AvailableFreeSpace;
                    var used = Math.Max(0L, total - free);
                    var pct = total > 0 ? Math.Round(((double)used / total) * 100.0, 1) : 0;

                    drives.Add(new DiskMountPointMetrics
                    {
                        MountPoint = drive.Name,
                        DriveType = drive.DriveType.ToString(),
                        TotalSpaceBytes = total,
                        FreeSpaceBytes = free,
                        UsedSpaceBytes = used,
                        UsedPercent = pct
                    });
                }

                cachedDriveMetrics = drives;
                lastDriveSampleTime = DateTime.UtcNow;
            }
            catch
            {
                // Fallback to previous cache if enumeration fails
            }

            return cachedDriveMetrics;
        }
    }
}
