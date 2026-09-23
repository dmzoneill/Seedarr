using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NLog.Config;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Telemetry;
using Seedarr.Api.V1.DiskSpace;
using Seedarr.Api.V1.Health;
using Seedarr.Api.V1.System;

namespace NzbDrone.Core.Test.SystemTests;

[TestFixture]
public class SystemAndDiagnosticsControllersTests
{
    private ISystemResourceService _resourceService;
    private IHealthCheckService _healthCheckService;
    private IDiskSpaceService _diskSpaceService;
    private IConfigService _configService;
    private IAppFolderInfo _appFolderInfo;

    private SystemResourcesController _systemResourcesController;
    private HealthController _healthController;
    private DiskSpaceController _diskSpaceController;
    private LogController _logController;
    private LogFileController _logFileController;

    private string _tempDir;
    private string _logsDir;
    private RingBufferTarget _ringBufferTarget;
    private Logger _testLogger;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_diag_test_" + Guid.NewGuid().ToString("N"));
        _logsDir = Path.Combine(_tempDir, "logs");
        Directory.CreateDirectory(_logsDir);

        _resourceService = Substitute.For<ISystemResourceService>();
        _healthCheckService = Substitute.For<IHealthCheckService>();
        _diskSpaceService = Substitute.For<IDiskSpaceService>();
        _configService = Substitute.For<IConfigService>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();

        _appFolderInfo.AppDataFolder.Returns(_tempDir);

        _systemResourcesController = new SystemResourcesController(_resourceService);
        _healthController = new HealthController(_healthCheckService);
        _diskSpaceController = new DiskSpaceController(_diskSpaceService);
        _logController = new LogController(_configService);
        _logFileController = new LogFileController(_appFolderInfo);

        _ringBufferTarget = new RingBufferTarget(256);
        RingBufferTarget.Instance = _ringBufferTarget;

        var loggingConfig = new LoggingConfiguration();
        loggingConfig.AddTarget("ringbuffer", _ringBufferTarget);
        loggingConfig.AddRule(LogLevel.Trace, LogLevel.Fatal, _ringBufferTarget);
        LogManager.Configuration = loggingConfig;
        _testLogger = LogManager.GetLogger("DiagnosticTestLogger");
    }

    [TearDown]
    public void TearDown()
    {
        LogManager.Configuration = null;
        RingBufferTarget.Instance = null;

        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
        }
    }

    [Test]
    public async Task SystemResources_GetFullSnapshot_ReturnsOkWithTelemetrySnapshot()
    {
        var snapshot = new SystemResourceTelemetrySnapshot
        {
            Timestamp = DateTime.UtcNow,
            Host = new HostProcessResourceMetrics
            {
                CpuCores = 8,
                CpuProcessPercent = 14.5,
                WorkingSetBytes = 256 * 1024 * 1024,
                ThreadCount = 32
            },
            TorrentEngine = new TorrentEngineMetrics
            {
                ActiveTorrents = 12,
                DownloadingTorrents = 3,
                SeedingTorrents = 9,
                TotalDownloadSpeed = 10485760,
                TotalUploadSpeed = 5242880,
                OpenConnections = 140
            },
            PerTorrent = new List<TorrentResourceMetrics>
            {
                new TorrentResourceMetrics
                {
                    TorrentId = 1,
                    Name = "Sample Torrent",
                    PayloadDownloadSpeed = 500000,
                    ConnectedPeers = 25
                }
            },
            Subsystems = new List<SubsystemTelemetryReport>
            {
                new SubsystemTelemetryReport
                {
                    SubsystemId = "dht",
                    SubsystemName = "Mainline DHT",
                    Status = "Healthy",
                    ResourceLoad = "Low"
                }
            }
        };

        _resourceService.GetFullTelemetrySnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));

        var actionResult = await _systemResourcesController.GetFullSnapshot();

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        Assert.That(okResult.Value, Is.InstanceOf<SystemResourceTelemetrySnapshot>());
        var retrieved = (SystemResourceTelemetrySnapshot)okResult.Value;

        Assert.That(retrieved.Host.CpuCores, Is.EqualTo(8));
        Assert.That(retrieved.TorrentEngine.ActiveTorrents, Is.EqualTo(12));
        Assert.That(retrieved.PerTorrent.Count, Is.EqualTo(1));
        Assert.That(retrieved.Subsystems.Count, Is.EqualTo(1));
        Assert.That(retrieved.Subsystems[0].SubsystemId, Is.EqualTo("dht"));
    }

    [Test]
    public void SystemResources_GetHostMetrics_ReturnsOkWithProcessMetrics()
    {
        var hostMetrics = new HostProcessResourceMetrics
        {
            CpuCores = 16,
            CpuProcessPercent = 22.0,
            WorkingSetBytes = 512 * 1024 * 1024,
            PrivateMemoryBytes = 400 * 1024 * 1024,
            ThreadCount = 48,
            UptimeSeconds = 86400,
            DiskDrives = new List<DiskMountPointMetrics>
            {
                new DiskMountPointMetrics
                {
                    MountPoint = "/",
                    DriveType = "ext4",
                    TotalSpaceBytes = 1000000000,
                    FreeSpaceBytes = 500000000,
                    UsedPercent = 50.0
                }
            }
        };

        _resourceService.GetHostMetrics().Returns(hostMetrics);

        var actionResult = _systemResourcesController.GetHostMetrics();

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        Assert.That(okResult.Value, Is.InstanceOf<HostProcessResourceMetrics>());
        var retrieved = (HostProcessResourceMetrics)okResult.Value;

        Assert.That(retrieved.CpuCores, Is.EqualTo(16));
        Assert.That(retrieved.CpuProcessPercent, Is.EqualTo(22.0));
        Assert.That(retrieved.DiskDrives.Count, Is.EqualTo(1));
        Assert.That(retrieved.DiskDrives[0].MountPoint, Is.EqualTo("/"));
    }

    [Test]
    public void SystemResources_GetEngineMetrics_ReturnsOkWithTorrentEngineMetrics()
    {
        var engineMetrics = new TorrentEngineMetrics
        {
            EngineId = "SeedarrSimulationEngine",
            DisplayName = "Seedarr Swarm Simulator",
            IsRunning = true,
            ActiveTorrents = 5,
            TotalDownloadSpeed = 12000000,
            TotalUploadSpeed = 6000000,
            DhtNodeCount = 412,
            DhtState = "Ready",
            OpenConnections = 85
        };

        _resourceService.GetTorrentEngineMetrics().Returns(engineMetrics);

        var actionResult = _systemResourcesController.GetEngineMetrics();

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        Assert.That(okResult.Value, Is.InstanceOf<TorrentEngineMetrics>());
        var retrieved = (TorrentEngineMetrics)okResult.Value;

        Assert.That(retrieved.IsRunning, Is.True);
        Assert.That(retrieved.ActiveTorrents, Is.EqualTo(5));
        Assert.That(retrieved.DhtNodeCount, Is.EqualTo(412));
    }

    [Test]
    public async Task SystemResources_GetSubsystemsTelemetry_ReturnsOkWithSubsystemReports()
    {
        var reports = new List<SubsystemTelemetryReport>
        {
            new SubsystemTelemetryReport
            {
                SubsystemId = "disk-io",
                SubsystemName = "Disk I/O Dispatcher",
                ActiveProvider = "AsyncFileStream",
                Status = "Healthy",
                ResourceLoad = "Nominal"
            },
            new SubsystemTelemetryReport
            {
                SubsystemId = "peer-mesh",
                SubsystemName = "Peer Connection Mesh",
                Status = "Healthy",
                ResourceLoad = "Moderate"
            }
        };

        _resourceService.GetSubsystemTelemetryAsync(null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(reports));

        var actionResult = await _systemResourcesController.GetSubsystemsTelemetry();

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        Assert.That(okResult.Value, Is.InstanceOf<List<SubsystemTelemetryReport>>());
        var retrieved = (List<SubsystemTelemetryReport>)okResult.Value;

        Assert.That(retrieved.Count, Is.EqualTo(2));
        Assert.That(retrieved[0].SubsystemId, Is.EqualTo("disk-io"));
        Assert.That(retrieved[1].SubsystemId, Is.EqualTo("peer-mesh"));
    }

    [Test]
    public void SystemResources_GetPerTorrentMetrics_ReturnsOkWithList()
    {
        var list = new List<TorrentResourceMetrics>
        {
            new TorrentResourceMetrics
            {
                TorrentId = 1,
                Name = "Torrent 1",
                Status = "Downloading",
                Progress = 0.75,
                PayloadDownloadSpeed = 1000000,
                EfficiencyRatio = 0.98
            },
            new TorrentResourceMetrics
            {
                TorrentId = 2,
                Name = "Torrent 2",
                Status = "Seeding",
                Progress = 1.0,
                PayloadUploadSpeed = 500000,
                EfficiencyRatio = 1.0
            }
        };

        _resourceService.GetPerTorrentMetrics().Returns(list);

        var actionResult = _systemResourcesController.GetPerTorrentMetrics();

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        var retrieved = (IReadOnlyList<TorrentResourceMetrics>)okResult.Value;

        Assert.That(retrieved.Count, Is.EqualTo(2));
        Assert.That(retrieved[0].TorrentId, Is.EqualTo(1));
        Assert.That(retrieved[1].TorrentId, Is.EqualTo(2));
    }

    [Test]
    public void SystemResources_GetTorrentMetrics_WhenFound_ReturnsOk()
    {
        var metric = new TorrentResourceMetrics
        {
            TorrentId = 42,
            Name = "Ubuntu ISO",
            Status = "Seeding",
            Progress = 1.0
        };

        _resourceService.GetTorrentMetrics(42).Returns(metric);

        var actionResult = _systemResourcesController.GetTorrentMetrics(42);

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        var retrieved = (TorrentResourceMetrics)okResult.Value;

        Assert.That(retrieved.TorrentId, Is.EqualTo(42));
        Assert.That(retrieved.Name, Is.EqualTo("Ubuntu ISO"));
    }

    [Test]
    public void SystemResources_GetTorrentMetrics_WhenNotFound_ReturnsNotFound()
    {
        _resourceService.GetTorrentMetrics(999).Returns((TorrentResourceMetrics)null);

        var actionResult = _systemResourcesController.GetTorrentMetrics(999);

        Assert.That(actionResult.Result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void SystemResources_Controller_HasExpectedAuthorizeAttribute()
    {
        var type = typeof(SystemResourcesController);
        var attr = type.GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault() as AuthorizeAttribute;

        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Policy, Is.EqualTo(Policies.Reader));
    }

    [Test]
    public async Task Health_GetHealth_ExecutesChecksAndReturnsResults()
    {
        var results = new List<HealthCheckResult>
        {
            new HealthCheckResult(typeof(DiskSpaceCheck), HealthCheckResultType.Ok, "Sufficient disk space"),
            new HealthCheckResult(typeof(IndexerHealthCheck), HealthCheckResultType.Warning, "Indexer response latency high"),
            new HealthCheckResult(typeof(PeerListeningPortHealthCheck), HealthCheckResultType.Error, "Listening port is unreachable")
        };

        _healthCheckService.PerformChecksAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(results));

        var actionResult = await _healthController.GetHealth();

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        Assert.That(okResult.Value, Is.InstanceOf<List<HealthCheckResult>>());
        var retrieved = (List<HealthCheckResult>)okResult.Value;

        Assert.That(retrieved.Count, Is.EqualTo(3));
        Assert.That(retrieved[0].Type, Is.EqualTo(HealthCheckResultType.Ok));
        Assert.That(retrieved[1].Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(retrieved[2].Type, Is.EqualTo(HealthCheckResultType.Error));
    }

    [Test]
    public async Task Health_GetHealth_WhenAllHealthy_ReturnsEmptyList()
    {
        _healthCheckService.PerformChecksAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<HealthCheckResult>()));

        var actionResult = await _healthController.GetHealth();

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        var retrieved = (List<HealthCheckResult>)okResult.Value;

        Assert.That(retrieved, Is.Empty);
    }

    [Test]
    public async Task Health_GetHealth_PassesCancellationTokenThrough()
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        _healthCheckService.PerformChecksAsync(token)
            .Returns(Task.FromResult(new List<HealthCheckResult>()));

        await _healthController.GetHealth(token);

        await _healthCheckService.Received(1).PerformChecksAsync(token);
    }

    [Test]
    public void DiskSpace_GetDiskSpace_WithoutRefresh_QueriesServiceWithForceRefreshFalse()
    {
        var disks = new List<DiskSpaceInfo>
        {
            new DiskSpaceInfo
            {
                Path = "/media/data",
                Label = "Media Storage",
                FreeSpace = 500L * 1024 * 1024 * 1024,
                TotalSpace = 1000L * 1024 * 1024 * 1024,
                FileSystemType = "zfs",
                IsReadOnly = false
            }
        };

        _diskSpaceService.GetDiskSpace(false).Returns(disks);

        var actionResult = _diskSpaceController.GetDiskSpace(refresh: false);

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        Assert.That(okResult.Value, Is.InstanceOf<List<DiskSpaceResource>>());
        var resources = (List<DiskSpaceResource>)okResult.Value;

        Assert.That(resources.Count, Is.EqualTo(1));
        Assert.That(resources[0].Path, Is.EqualTo("/media/data"));
        Assert.That(resources[0].Label, Is.EqualTo("Media Storage"));
        Assert.That(resources[0].FreeSpace, Is.EqualTo(500L * 1024 * 1024 * 1024));
        Assert.That(resources[0].TotalSpace, Is.EqualTo(1000L * 1024 * 1024 * 1024));
        Assert.That(resources[0].FileSystemType, Is.EqualTo("zfs"));
        Assert.That(resources[0].IsReadOnly, Is.False);

        _diskSpaceService.Received(1).GetDiskSpace(false);
        _diskSpaceService.DidNotReceive().GetDiskSpace(true);
    }

    [Test]
    public void DiskSpace_GetDiskSpace_WithRefreshTrue_QueriesServiceWithForceRefreshTrue()
    {
        var disks = new List<DiskSpaceInfo>
        {
            new DiskSpaceInfo
            {
                Path = "/home/downloads",
                Label = "Downloads",
                FreeSpace = 50L * 1024 * 1024 * 1024,
                TotalSpace = 200L * 1024 * 1024 * 1024,
                FileSystemType = "btrfs",
                IsReadOnly = false
            }
        };

        _diskSpaceService.GetDiskSpace(true).Returns(disks);

        var actionResult = _diskSpaceController.GetDiskSpace(refresh: true);

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        var resources = (List<DiskSpaceResource>)okResult.Value;

        Assert.That(resources.Count, Is.EqualTo(1));
        Assert.That(resources[0].Path, Is.EqualTo("/home/downloads"));

        _diskSpaceService.Received(1).GetDiskSpace(true);
    }

    [Test]
    public void DiskSpace_GetDiskSpace_WhenEmpty_ReturnsEmptyList()
    {
        _diskSpaceService.GetDiskSpace(Arg.Any<bool>()).Returns(new List<DiskSpaceInfo>());

        var actionResult = _diskSpaceController.GetDiskSpace();

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        var resources = (List<DiskSpaceResource>)okResult.Value;

        Assert.That(resources, Is.Empty);
    }

    [Test]
    public void LogController_GetLogs_WhenRingBufferNull_ReturnsEmptyList()
    {
        RingBufferTarget.Instance = null;

        var actionResult = _logController.GetLogs();

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        var resources = (List<LogResource>)okResult.Value;

        Assert.That(resources, Is.Empty);
    }

    [Test]
    public void LogController_GetLogs_RetrievesAndMapsEntriesAccurately()
    {
        _testLogger.Info("Test message 1");
        _testLogger.Warn("Test warning 2");
        _testLogger.Error("Test error 3");

        var actionResult = _logController.GetLogs(level: "Info", count: 100);

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        var resources = (List<LogResource>)okResult.Value;

        Assert.That(resources.Count, Is.EqualTo(3));
        Assert.That(resources.Any(r => r.Level == "Info" && r.Message.Contains("Test message 1")), Is.True);
        Assert.That(resources.Any(r => r.Level == "Warn" && r.Message.Contains("Test warning 2")), Is.True);
        Assert.That(resources.Any(r => r.Level == "Error" && r.Message.Contains("Test error 3")), Is.True);
        Assert.That(resources.All(r => !string.IsNullOrEmpty(r.Time)), Is.True);
    }

    [Test]
    public void LogController_GetLogs_FiltersByMinimumLevel()
    {
        _testLogger.Trace("Trace entry");
        _testLogger.Debug("Debug entry");
        _testLogger.Info("Info entry");
        _testLogger.Warn("Warn entry");
        _testLogger.Error("Error entry");

        var actionResult = _logController.GetLogs(level: "Warn", count: 100);

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        var resources = (List<LogResource>)okResult.Value;

        Assert.That(resources.Count, Is.EqualTo(2));
        Assert.That(resources.All(r => r.Level == "Warn" || r.Level == "Error"), Is.True);
    }

    [Test]
    public void LogController_GetLogs_ClampsCountParameter()
    {
        for (var i = 0; i < 10; i++)
        {
            _testLogger.Info($"Message {i}");
        }

        var clampedMinResult = _logController.GetLogs(count: -10);
        var minList = (List<LogResource>)((OkObjectResult)clampedMinResult.Result).Value;
        Assert.That(minList.Count, Is.EqualTo(1));

        var regularResult = _logController.GetLogs(count: 5);
        var regularList = (List<LogResource>)((OkObjectResult)regularResult.Result).Value;
        Assert.That(regularList.Count, Is.EqualTo(5));
    }

    [Test]
    public void LogController_GetLogs_SanitizesSensitiveParameters()
    {
        _testLogger.Info("Connecting with api_key=secret123456 to tracker");

        var actionResult = _logController.GetLogs(level: "Info", count: 10);
        var list = (List<LogResource>)((OkObjectResult)actionResult.Result).Value;

        var matching = list.FirstOrDefault(r => r.Message.Contains("Connecting with"));
        Assert.That(matching, Is.Not.Null);
        Assert.That(matching.Message, Does.Contain("api_key=[REDACTED]"));
        Assert.That(matching.Message, Does.Not.Contain("secret123456"));
    }

    [Test]
    public void LogFileController_GetLogFiles_ListsAvailableLogFilesOrderedByDate()
    {
        var file1 = Path.Combine(_logsDir, "seedarr.txt");
        var file2 = Path.Combine(_logsDir, "seedarr.1.txt");

        File.WriteAllText(file1, "log 1");
        File.WriteAllText(file2, "log 2");

        File.SetLastWriteTimeUtc(file1, DateTime.UtcNow.AddMinutes(5));
        File.SetLastWriteTimeUtc(file2, DateTime.UtcNow.AddMinutes(-5));

        var actionResult = _logFileController.GetLogFiles();

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        var files = (List<LogFileResource>)okResult.Value;

        Assert.That(files.Count, Is.EqualTo(2));
        Assert.That(files[0].Filename, Is.EqualTo("seedarr.txt"));
        Assert.That(files[1].Filename, Is.EqualTo("seedarr.1.txt"));
    }

    [Test]
    public void LogFileController_GetLogFiles_WhenDirectoryMissing_ReturnsEmptyList()
    {
        _appFolderInfo.AppDataFolder.Returns(Path.Combine(_tempDir, "nonexistent"));

        var actionResult = _logFileController.GetLogFiles();

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        var files = (List<LogFileResource>)okResult.Value;

        Assert.That(files, Is.Empty);
    }

    [Test]
    public void LogFileController_GetLogFile_StreamsWithRangeProcessingAndInlineName()
    {
        var filePath = Path.Combine(_logsDir, "seedarr.txt");
        File.WriteAllText(filePath, "2026-09-23 12:00:00 [INFO] Seedarr running");

        var result = _logFileController.GetLogFile("seedarr.txt", download: false);

        Assert.That(result, Is.InstanceOf<FileStreamResult>());
        var fileResult = (FileStreamResult)result;
        try
        {
            Assert.That(fileResult.EnableRangeProcessing, Is.True);
            Assert.That(fileResult.ContentType, Is.EqualTo("text/plain"));
            Assert.That(fileResult.FileDownloadName, Is.Null.Or.Empty, "Inline viewing should have null or empty download name");
        }
        finally
        {
            fileResult.FileStream.Dispose();
        }
    }

    [Test]
    public void LogFileController_GetLogFile_WhenDownloadTrue_SetsDownloadName()
    {
        var filePath = Path.Combine(_logsDir, "seedarr.txt");
        File.WriteAllText(filePath, "Log data for download");

        var result = _logFileController.GetLogFile("seedarr.txt", download: true);

        Assert.That(result, Is.InstanceOf<FileStreamResult>());
        var fileResult = (FileStreamResult)result;
        try
        {
            Assert.That(fileResult.FileDownloadName, Is.EqualTo("seedarr.txt"));
        }
        finally
        {
            fileResult.FileStream.Dispose();
        }
    }

    [Test]
    public void LogFileController_GetLogFile_RejectsPathTraversalAndInvalidNames()
    {
        var res1 = _logFileController.GetLogFile("../secret.txt");
        Assert.That(res1, Is.InstanceOf<BadRequestObjectResult>());

        var res2 = _logFileController.GetLogFile("/etc/passwd");
        Assert.That(res2, Is.InstanceOf<BadRequestObjectResult>());

        var res3 = _logFileController.GetLogFile("sub/folder.txt");
        Assert.That(res3, Is.InstanceOf<BadRequestObjectResult>());

        var res4 = _logFileController.GetLogFile("");
        Assert.That(res4, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void LogFileController_GetLogFile_ReturnsNotFoundWhenFileDoesNotExist()
    {
        var result = _logFileController.GetLogFile("nonexistent.txt");
        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void LogFileController_ClearLogFiles_PreservesActiveLogsAndDeletesRotatedLogs()
    {
        var activeFile = Path.Combine(_logsDir, "seedarr.txt");
        var activeTraceFile = Path.Combine(_logsDir, "seedarr.trace.txt");
        var rotatedFile = Path.Combine(_logsDir, "seedarr.2026-09-01.txt");

        File.WriteAllText(activeFile, "active log");
        File.WriteAllText(activeTraceFile, "active trace");
        File.WriteAllText(rotatedFile, "rotated old log");

        var result = _logFileController.ClearLogFiles();

        Assert.That(result, Is.InstanceOf<OkResult>());
        Assert.That(File.Exists(activeFile), Is.True, "Active log file must be preserved");
        Assert.That(File.Exists(activeTraceFile), Is.True, "Active trace log file must be preserved");
        Assert.That(File.Exists(rotatedFile), Is.False, "Rotated log file should be deleted");
    }
}
