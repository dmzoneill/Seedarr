using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;
using Seedarr.Http;

namespace Seedarr.Api.V1.System;

/// <summary>
/// Controller for system status, scheduled tasks, and commands.
/// </summary>
[V1ApiController("system")]
public class SystemController : ControllerBase
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    private readonly ITaskManager _taskManager;
    private readonly IEnumerable<IScheduledTask> _scheduledTasks;
    private readonly IManageCommandQueue _commandQueueManager;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IConfigService _configService;

    public SystemController(
        ITaskManager taskManager,
        IEnumerable<IScheduledTask> scheduledTasks,
        IManageCommandQueue commandQueueManager,
        IAppFolderInfo appFolderInfo,
        IHostApplicationLifetime lifetime,
        IConfigService configService = null)
    {
        _taskManager = taskManager;
        _scheduledTasks = scheduledTasks ?? Enumerable.Empty<IScheduledTask>();
        _commandQueueManager = commandQueueManager;
        _appFolderInfo = appFolderInfo;
        _lifetime = lifetime;
        _configService = configService;
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

        return Ok(new SystemResource
        {
            AppName = BuildInfo.AppName,
            Version = BuildInfo.Version.ToString(),
            InstanceUuid = _configService?.InstanceUuid ?? string.Empty,
            OsName = OsInfo.Os,
            OsVersion = OsInfo.Version,
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
            DatabaseVersion = "SQLite",
            DatabaseMigration = NzbDroneMigrationBase.LatestMigration.ToString(),
            UptimeSeconds = (DateTime.UtcNow - StartTime).TotalSeconds,
        });
    }

    /// <summary>
    /// Gets the list of scheduled tasks.
    /// </summary>
    /// <returns>A list of scheduled task resources.</returns>
    [HttpGet("task")]
    public ActionResult<List<ScheduledTaskResource>> GetTasks()
    {
        var tasks = _taskManager.GetAll();
        return Ok(tasks.Select(t =>
        {
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

            var nextExecution = t.LastExecution.AddMinutes(t.Interval);

            return new ScheduledTaskResource
            {
                Id = t.Id,
                TypeName = t.TypeName,
                Name = simpleName,
                Interval = t.Interval,
                LastExecution = t.LastExecution,
                LastStartTime = t.LastStartTime,
                LastDuration = lastDuration,
                NextExecution = nextExecution,
                IsRunning = isRunning
            };
        }).ToList());
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

        var command = _commandQueueManager.Push(new ScheduledTaskCommand { TaskName = task.TypeName }, CommandTrigger.Manual);

        return Ok(new { message = $"Task {task.TypeName} execution started", commandId = command.Id });
    }

    /// <summary>
    /// Gets the list of queued and running commands.
    /// </summary>
    /// <returns>A list of command resources.</returns>
    [HttpGet("command")]
    public ActionResult<List<CommandResource>> GetCommands()
    {
        var commands = _commandQueueManager.GetAll();
        return Ok(commands.Select(c =>
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
        }).ToList());
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
            await global::System.Threading.Tasks.Task.Delay(500);
            _lifetime.StopApplication();
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
}
