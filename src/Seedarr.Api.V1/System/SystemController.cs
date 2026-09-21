using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Instrumentation;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.SignalR;
using Seedarr.Http;

namespace Seedarr.Api.V1.System;

/// <summary>
/// Controller for system status, scheduled tasks, and commands.
/// </summary>
[V1ApiController("system")]
public class SystemController : ControllerBase
{
    private static readonly DateTime StartTime = DateTime.UtcNow;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public Action<ProcessStartInfo> ProcessStarter { get; set; } = psi => Process.Start(psi);
    public int RestartDelayMs { get; set; } = 500;

    private readonly ITaskManager _taskManager;
    private readonly IEnumerable<IScheduledTask> _scheduledTasks;
    private readonly IManageCommandQueue _commandQueueManager;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IConfigService _configService;
    private readonly IDatabase _mainDatabase;
    private readonly IScheduledTaskHistoryRepository _taskHistoryRepository;
    private readonly IBroadcastSignalRMessage _signalRBroadcaster;
    private readonly IDatabaseMaintenanceService _databaseMaintenanceService;
    private readonly IFileDescriptorProvider _fileDescriptorProvider;
    private readonly ICpuUsageService _cpuUsageService;

    public SystemController(
        ITaskManager taskManager,
        IEnumerable<IScheduledTask> scheduledTasks,
        IManageCommandQueue commandQueueManager,
        IAppFolderInfo appFolderInfo,
        IHostApplicationLifetime lifetime,
        IConfigService configService = null,
        IDatabase mainDatabase = null,
        IScheduledTaskHistoryRepository taskHistoryRepository = null,
        IBroadcastSignalRMessage signalRBroadcaster = null,
        IDatabaseMaintenanceService databaseMaintenanceService = null,
        IFileDescriptorProvider fileDescriptorProvider = null,
        ICpuUsageService cpuUsageService = null)
    {
        _taskManager = taskManager;
        _scheduledTasks = scheduledTasks ?? Enumerable.Empty<IScheduledTask>();
        _commandQueueManager = commandQueueManager;
        _appFolderInfo = appFolderInfo;
        _lifetime = lifetime;
        _configService = configService;
        _mainDatabase = mainDatabase;
        _taskHistoryRepository = taskHistoryRepository;
        _signalRBroadcaster = signalRBroadcaster;
        _databaseMaintenanceService = databaseMaintenanceService;
        _fileDescriptorProvider = fileDescriptorProvider ?? new FileDescriptorProvider();
        _cpuUsageService = cpuUsageService ?? new CpuUsageService();
    }

    /// <summary>
    /// Gets the current system status.
    /// </summary>
    /// <returns>The system status resource.</returns>
    [HttpGet("status")]
    public ActionResult<SystemResource> GetStatus()
    {
        var isDocker = global::System.IO.File.Exists("/.dockerenv") ||
                string.Equals(
                    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
                    "true",
                    StringComparison.OrdinalIgnoreCase);

#if DEBUG
        var isDebug = true;
#else
        var isDebug = false;
#endif

        var dbType = _mainDatabase?.DatabaseType.ToString() ?? "SQLite";
        var gcInfo = GC.GetGCMemoryInfo();
        var threadPoolStats = _cpuUsageService.GetThreadPoolStats();

        var workingSetBytes = Process.GetCurrentProcess().WorkingSet64;
        var gcTotalMemoryBytes = GC.GetTotalMemory(false);
        var containerMemoryLimitBytes = gcInfo.TotalAvailableMemoryBytes;
        var memoryUsagePercentage = containerMemoryLimitBytes > 0
            ? Math.Clamp((double)workingSetBytes / containerMemoryLimitBytes * 100.0, 0.0, 100.0)
            : 0.0;

        return Ok(new SystemResource
        {
            AppName = BuildInfo.AppName,
            Version = BuildInfo.Version.ToString(),
            InstanceUuid = _configService?.InstanceUuid ?? string.Empty,
            OsName = OsInfo.Os,
            OsVersion = OsInfo.Version,
            OsArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            CommitHash = BuildInfo.CommitHash,
            IsWindows = OsInfo.IsWindows,
            IsLinux = OsInfo.IsLinux,
            IsOsx = OsInfo.IsOsx,
            Branch = BuildInfo.Branch,
            RuntimeName = RuntimeInformation.FrameworkDescription,
            RuntimeVersion = Environment.Version.ToString(),
            StartTime = StartTime,
            StartupPath = _appFolderInfo.StartUpFolder,
            AppDataPath = _appFolderInfo.AppDataFolder,
            IsDocker = isDocker,
            IsDebug = isDebug,
            DatabaseVersion = dbType,
            DatabaseType = dbType,
            DatabaseMigration = GetDatabaseMigrationVersion(),
            UptimeSeconds = (DateTime.UtcNow - StartTime).TotalSeconds,
            GcGen0Collections = GC.CollectionCount(0),
            GcGen1Collections = GC.CollectionCount(1),
            GcGen2Collections = GC.CollectionCount(2),
            GcTotalAllocatedBytes = GC.GetTotalAllocatedBytes(),
            GcHeapSizeBytes = gcInfo.HeapSizeBytes,
            GcPauseTimePercentage = gcInfo.PauseTimePercentage,
            OpenFileDescriptors = _fileDescriptorProvider.GetOpenFileDescriptorCount(),
            MaxFileDescriptors = _fileDescriptorProvider.GetMaxFileDescriptors(),
            FileDescriptorUsagePercentage = _fileDescriptorProvider.GetFileDescriptorUsagePercentage(),
            CpuUsagePercentage = _cpuUsageService.GetCpuUsagePercentage(),
            ProcessorCount = _cpuUsageService.GetProcessorCount(),
            ThreadCount = _cpuUsageService.GetThreadCount(),
            AvailableWorkerThreads = threadPoolStats.AvailableWorker,
            AvailableCompletionPortThreads = threadPoolStats.AvailableCompletionPort,
            WorkingSetBytes = workingSetBytes,
            GcTotalMemoryBytes = gcTotalMemoryBytes,
            ContainerMemoryLimitBytes = containerMemoryLimitBytes,
            MemoryUsagePercentage = memoryUsagePercentage,
        });
    }

    private string GetDatabaseMigrationVersion()
    {
        if (_mainDatabase != null)
        {
            try
            {
                using var conn = _mainDatabase.OpenConnection();
                if (conn != null)
                {
                    using var cmd = conn.CreateCommand();
                    if (cmd != null)
                    {
                        cmd.CommandText = "SELECT Version FROM VersionInfo ORDER BY AppliedOn DESC, Version DESC LIMIT 1";
                        var result = cmd.ExecuteScalar();
                        if (result != null && result != DBNull.Value)
                        {
                            var versionStr = result.ToString();
                            if (!string.IsNullOrWhiteSpace(versionStr))
                            {
                                return versionStr;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fall back to compile-time latest migration if query fails or database is uninitialized
            }
        }

        return NzbDroneMigrationBase.LatestMigration.ToString();
    }

    /// <summary>
    /// Gets the list of scheduled tasks.
    /// </summary>
    /// <returns>A list of scheduled task resources.</returns>
    [HttpGet("task")]
    public ActionResult<List<ScheduledTaskResource>> GetTasks()
    {
        var tasks = _taskManager.GetAll();
        return Ok(tasks.Select(ToScheduledTaskResource).ToList());
    }

    /// <summary>
    /// Updates interval and enabled status for a scheduled task by ID.
    /// </summary>
    [HttpPut("task/{id:int}")]
    public ActionResult<ScheduledTaskResource> UpdateTask(int id, [FromBody] UpdateScheduledTaskRequest request)
    {
        if (request == null || request.Interval < 1)
        {
            return BadRequest("Interval must be at least 1 minute.");
        }

        var existing = _taskManager.GetAll().FirstOrDefault(t => t.Id == id);
        if (existing == null)
        {
            return NotFound(new { message = $"Task with ID {id} not found" });
        }

        _taskManager.Update(id, request.Interval, request.IsEnabled);
        var updated = _taskManager.GetAll().FirstOrDefault(t => t.Id == id);
        return Ok(ToScheduledTaskResource(updated));
    }

    private ScheduledTaskResource ToScheduledTaskResource(ScheduledTask t)
    {
        if (t == null)
        {
            return null;
        }

        var taskInstance = _scheduledTasks.FirstOrDefault(st =>
            string.Equals(st.GetType().FullName, t.TypeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(st.GetType().Name, t.TypeName, StringComparison.OrdinalIgnoreCase));

        var simpleName = taskInstance != null
            ? taskInstance.GetType().Name
            : (t.TypeName.Contains('.') ? t.TypeName.Substring(t.TypeName.LastIndexOf('.') + 1) : t.TypeName);

        var isRunning = _taskManager.IsRunning(t.TypeName) ||
                        (t.LastStartTime.HasValue && t.LastStartTime.Value > t.LastExecution);

        TimeSpan? lastDuration = null;

        if (isRunning && t.LastStartTime.HasValue)
        {
            lastDuration = DateTime.UtcNow - t.LastStartTime.Value;
        }
        else if (t.LastStartTime.HasValue)
        {
            lastDuration = t.LastExecution - t.LastStartTime.Value;

            if (lastDuration < TimeSpan.Zero)
            {
                lastDuration = null;
            }
        }

        var nextExecution = t.NextExecution;

        return new ScheduledTaskResource
        {
            Id = t.Id,
            TypeName = t.TypeName,
            Name = simpleName,
            Interval = t.Interval,
            IsEnabled = t.IsEnabled,
            LastExecution = t.LastExecution,
            LastStartTime = t.LastStartTime,
            LastDuration = lastDuration,
            NextExecution = nextExecution,
            IsRunning = isRunning,
            LastStatus = t.LastStatus.ToString(),
            LastErrorMessage = t.LastErrorMessage
        };
    }

    /// <summary>
    /// Executes a scheduled task immediately by ID.
    /// </summary>
    [HttpPost("task/{id:int}/execute")]
    public ActionResult ExecuteTask(int id)
    {
        var task = _taskManager.GetAll().FirstOrDefault(t => t.Id == id);
        if (task == null)
        {
            return NotFound(new { message = $"Task with ID {id} not found" });
        }

        return RunScheduledTask(task);
    }

    /// <summary>
    /// Executes a scheduled task immediately by type or name.
    /// </summary>
    [HttpPost("task/{name}/execute")]
    public ActionResult ExecuteTaskByName(string name)
    {
        var task = _taskManager.GetAll().FirstOrDefault(t =>
            string.Equals(t.TypeName, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.TypeName.Split('.').LastOrDefault(), name, StringComparison.OrdinalIgnoreCase));

        if (task == null)
        {
            var taskInstance = _scheduledTasks.FirstOrDefault(st =>
                string.Equals(st.GetType().FullName, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(st.GetType().Name, name, StringComparison.OrdinalIgnoreCase));

            if (taskInstance != null)
            {
                task = new ScheduledTask
                {
                    TypeName = taskInstance.GetType().FullName,
                    Interval = taskInstance.DefaultInterval,
                    LastExecution = DateTime.UtcNow
                };
            }
            else
            {
                return NotFound(new { message = $"Task '{name}' not found" });
            }
        }

        return RunScheduledTask(task);
    }

    private ActionResult RunScheduledTask(ScheduledTask task)
    {
        var taskInstance = _scheduledTasks.FirstOrDefault(st =>
            string.Equals(st.GetType().FullName, task.TypeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(st.GetType().Name, task.TypeName, StringComparison.OrdinalIgnoreCase));

        if (taskInstance == null)
        {
            return NotFound(new { message = $"Task implementation for {task.TypeName} not found" });
        }

        if (_taskManager.IsRunning(task.TypeName))
        {
            return Conflict(new { message = $"Task {task.TypeName} is already running" });
        }

        var simpleName = taskInstance != null
            ? taskInstance.GetType().Name
            : (task.TypeName.Contains('.') ? task.TypeName.Substring(task.TypeName.LastIndexOf('.') + 1) : task.TypeName);

        var taskInfo = new
        {
            Id = task.Id,
            TypeName = task.TypeName,
            Name = simpleName
        };

        if (_commandQueueManager != null)
        {
            var command = _commandQueueManager.Push(
                new ScheduledTaskCommand
                {
                    TaskName = task.TypeName,
                    TriggerSource = ScheduledTaskTriggerSource.Manual
                },
                CommandTrigger.Manual);

            _signalRBroadcaster?.BroadcastMessage(new SignalRMessage
            {
                Name = "TaskStarted",
                Action = ModelAction.Created,
                Body = taskInfo
            });

            return Ok(new { message = $"Task {task.TypeName} execution started", commandId = command?.Id ?? 0 });
        }

        global::System.Threading.Tasks.Task.Run(() =>
        {
            var startTime = DateTime.UtcNow;
            _taskManager.RecordTaskStarted(task.TypeName);
            _signalRBroadcaster?.BroadcastMessage(new SignalRMessage
            {
                Name = "TaskStarted",
                Action = ModelAction.Created,
                Body = taskInfo
            });

            try
            {
                taskInstance.Execute();
            }
            catch (Exception ex)
            {
                _taskManager.RecordTaskFailed(task.TypeName, startTime, ex.Message);
            }
            finally
            {
                _taskManager.RecordTaskFinished(task.TypeName, startTime);
                _signalRBroadcaster?.BroadcastMessage(new SignalRMessage
                {
                    Name = "TaskCompleted",
                    Action = ModelAction.Updated,
                    Body = taskInfo
                });
            }
        });

        return Ok(new { message = $"Task {task.TypeName} execution started" });
    }

    /// <summary>
    /// Aborts a running scheduled task by ID.
    /// </summary>
    /// <param name="id">The task ID.</param>
    /// <returns>Ok if task cancellation was signaled; NotFound if task is not found or not running.</returns>
    [HttpPost("task/{id:int}/abort")]
    public ActionResult AbortTask(int id)
    {
        var cancelled = _taskManager.CancelTask(id);
        if (!cancelled)
        {
            return NotFound(new { message = $"Task with ID {id} not found or not currently running" });
        }

        return Ok(new { message = $"Task with ID {id} abort requested" });
    }

    /// <summary>
    /// Aborts a running scheduled task by type or name.
    /// </summary>
    /// <param name="name">The task type name or short name.</param>
    /// <returns>Ok if task cancellation was signaled; NotFound if task is not found or not running.</returns>
    [HttpPost("task/{name}/abort")]
    public ActionResult AbortTaskByName(string name)
    {
        var task = _taskManager.GetAll().FirstOrDefault(t =>
            string.Equals(t.TypeName, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.TypeName.Split('.').LastOrDefault(), name, StringComparison.OrdinalIgnoreCase));

        var targetName = task?.TypeName ?? name;
        var cancelled = _taskManager.CancelTask(targetName);
        if (!cancelled)
        {
            return NotFound(new { message = $"Task '{name}' is not currently running or not found" });
        }

        return Ok(new { message = $"Task '{targetName}' abort requested" });
    }

    /// <summary>
    /// Gets execution history for a scheduled task by ID.
    /// </summary>
    /// <param name="id">The scheduled task ID.</param>
    /// <param name="limit">The maximum number of history records to return (default 50).</param>
    /// <returns>A list of scheduled task history resources.</returns>
    [HttpGet("task/{id:int}/history")]
    public ActionResult<List<ScheduledTaskHistoryResource>> GetTaskHistoryById(int id, [FromQuery] int limit = 50)
    {
        if (limit <= 0)
        {
            limit = 50;
        }

        var history = _taskHistoryRepository != null
            ? _taskHistoryRepository.GetByTaskId(id, limit)
            : _taskManager.GetTaskHistory(id, limit);

        return Ok(history.Select(MapToHistoryResource).ToList());
    }

    /// <summary>
    /// Gets execution history for a scheduled task by name or type name.
    /// </summary>
    /// <param name="name">The task type name or short name.</param>
    /// <param name="limit">The maximum number of history records to return (default 50).</param>
    /// <returns>A list of scheduled task history resources.</returns>
    [HttpGet("task/{name}/history")]
    public ActionResult<List<ScheduledTaskHistoryResource>> GetTaskHistoryByName(string name, [FromQuery] int limit = 50)
    {
        if (limit <= 0)
        {
            limit = 50;
        }

        var history = _taskHistoryRepository != null
            ? _taskHistoryRepository.GetByTypeName(name, limit)
            : _taskManager.GetTaskHistory(name, limit);

        return Ok(history.Select(MapToHistoryResource).ToList());
    }

    private static ScheduledTaskHistoryResource MapToHistoryResource(ScheduledTaskHistory h)
    {
        return new ScheduledTaskHistoryResource
        {
            Id = h.Id,
            TaskId = h.TaskId,
            TypeName = h.TypeName,
            StartedAt = h.StartedAt,
            FinishedAt = h.FinishedAt,
            DurationMs = h.DurationMs,
            Status = h.Status.ToString(),
            TriggerSource = h.TriggerSource.ToString(),
            ErrorMessage = h.ErrorMessage,
            ExceptionDetails = h.ExceptionDetails
        };
    }

    /// <summary>
    /// Gets the list of queued and running commands.
    /// </summary>
    /// <returns>A list of command resources.</returns>
    [HttpGet("command")]
    public ActionResult<List<CommandResource>> GetCommands()
    {
        var commands = _commandQueueManager.GetAll();
        return Ok(commands.Select(MapToResource).ToList());
    }

    /// <summary>
    /// Gets a command by ID.
    /// </summary>
    /// <param name="id">The command ID.</param>
    /// <returns>The command resource if found; otherwise, NotFound.</returns>
    [HttpGet("command/{id:int}")]
    public ActionResult<CommandResource> GetCommand(int id)
    {
        var command = _commandQueueManager.Get(id);
        if (command == null)
        {
            return NotFound(new { message = $"Command with ID {id} not found" });
        }

        return Ok(MapToResource(command));
    }

    /// <summary>
    /// Cancels a queued or running command by ID.
    /// </summary>
    /// <param name="id">The command ID.</param>
    /// <returns>Result of the cancellation.</returns>
    [HttpDelete("command/{id:int}")]
    public ActionResult CancelCommand(int id)
    {
        var command = _commandQueueManager.Get(id);
        if (command == null)
        {
            return NotFound(new { message = $"Command with ID {id} not found" });
        }

        if (command.Status != CommandStatus.Queued && command.Status != CommandStatus.Started)
        {
            return BadRequest(new { message = $"Command {id} is in status '{command.Status}' and cannot be cancelled" });
        }

        var cancelled = _commandQueueManager.Cancel(id);
        if (!cancelled)
        {
            return BadRequest(new { message = $"Command {id} could not be cancelled" });
        }

        return Ok(new { message = $"Command {id} cancelled" });
    }

    [HttpPost("command")]
    public ActionResult<CommandResource> PushCommand([FromBody] JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return BadRequest("Command payload must be a JSON object.");
        }

        if (!body.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String)
        {
            return BadRequest(new { message = "Command 'name' is required" });
        }

        var name = nameProp.GetString();
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Command 'name' must not be empty" });
        }

        var model = _commandQueueManager.PushRaw(name, body.GetRawText());

        return Ok(new CommandResource
        {
            Id = model.Id,
            Name = model.Name,
            Status = model.Status.ToString().ToLowerInvariant(),
            QueuedAt = model.QueuedAt
        });
    }

    private static CommandResource MapToResource(CommandModel c)
    {
        TimeSpan? duration = null;

        if (c.StartedAt.HasValue && c.EndedAt.HasValue)
        {
            duration = c.EndedAt.Value - c.StartedAt.Value;
        }
        else if (c.StartedAt.HasValue)
        {
            duration = DateTime.UtcNow - c.StartedAt.Value;
        }

        var name = c.Name;
        if ((c.Name == "ScheduledTaskCommand" || c.Name == "ScheduledTask") && !string.IsNullOrEmpty(c.Body))
        {
            try
            {
                using var doc = JsonDocument.Parse(c.Body);
                if (doc.RootElement.TryGetProperty("taskName", out var tnProp) ||
                    doc.RootElement.TryGetProperty("TaskName", out tnProp))
                {
                    var taskName = tnProp.GetString();
                    if (!string.IsNullOrEmpty(taskName))
                    {
                        name = taskName.Contains('.') ? taskName.Substring(taskName.LastIndexOf('.') + 1) : taskName;
                    }
                }
            }
            catch
            {
                // Ignore JSON parsing errors
            }
        }

        return new CommandResource
        {
            Id = c.Id,
            Name = name,
            Status = c.Status.ToString().ToLowerInvariant(),
            QueuedAt = c.QueuedAt,
            StartedAt = c.StartedAt,
            EndedAt = c.EndedAt,
            Duration = duration,
            Message = c.Message
        };
    }

    [HttpPost("restart")]
    [Authorize(Roles = "Admin")]
    public ActionResult Restart()
    {
        if (User?.Identity?.IsAuthenticated == true && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        global::System.Threading.Tasks.Task.Run(async () =>
        {
            if (RestartDelayMs > 0)
            {
                await global::System.Threading.Tasks.Task.Delay(RestartDelayMs);
            }

            try
            {
                var isContainer = EnvironmentProvider.CheckIsDocker() ||
                    string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase) ||
                    global::System.IO.File.Exists("/.dockerenv");

                if (!isContainer)
                {
                    var processPath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = processPath,
                        WorkingDirectory = Environment.CurrentDirectory,
                        UseShellExecute = false
                    };

                    _logger.Info("Restarting application: spawning new process {0}", processPath);
                    ProcessStarter(startInfo);
                }
                else
                {
                    _logger.Info("Restarting application in container environment: stopping application to allow container runtime to restart");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to spawn process on restart");
            }
            finally
            {
                _lifetime.StopApplication();
            }
        });

        return Ok(new { message = "Restarting..." });
    }

    [HttpPost("shutdown")]
    [Authorize(Roles = "Admin")]
    public ActionResult Shutdown()
    {
        if (User?.Identity?.IsAuthenticated == true && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        global::System.Threading.Tasks.Task.Run(async () =>
        {
            await global::System.Threading.Tasks.Task.Delay(500);
            _lifetime.StopApplication();
        });

        return Ok(new { message = "Shutting down..." });
    }

    /// <summary>
    /// Triggers database defragmentation, incremental vacuum, and freelist space reclamation.
    /// </summary>
    /// <param name="request">Optional request parameters specifying maxPages to reclaim.</param>
    /// <param name="maxPages">Optional query parameter specifying maxPages to reclaim.</param>
    /// <returns>Result containing reclaimed page counts and reclaimed bytes.</returns>
    [HttpPost("database/vacuum")]
    public ActionResult<DatabaseVacuumResource> VacuumDatabase(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] DatabaseVacuumRequest request = null,
        [FromQuery] int? maxPages = null)
    {
        if (_databaseMaintenanceService == null)
        {
            return StatusCode(500, new DatabaseVacuumResource
            {
                Success = false,
                DatabaseType = _mainDatabase?.DatabaseType.ToString() ?? "Unknown",
                Message = "Database maintenance service is unavailable."
            });
        }

        var pages = maxPages ?? request?.MaxPages;
        var result = _databaseMaintenanceService.PerformMaintenance(pages);

        var resource = new DatabaseVacuumResource
        {
            Success = result.Success,
            DatabaseType = result.DatabaseType,
            InitialFreelistPages = result.InitialFreelistPages,
            FinalFreelistPages = result.FinalFreelistPages,
            ReclaimedPages = result.ReclaimedPages,
            PageSize = result.PageSize,
            ReclaimedBytes = result.ReclaimedBytes,
            Message = result.Message
        };

        return Ok(resource);
    }
}
