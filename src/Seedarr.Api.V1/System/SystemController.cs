using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
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
            DatabaseMigration = "015",
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

            TimeSpan? lastDuration = null;

            if (t.LastStartTime.HasValue)
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
                NextExecution = nextExecution
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
            return NotFound(new { message = $"Task '{name}' not found" });
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

        global::System.Threading.Tasks.Task.Run(() =>
        {
            var startTime = DateTime.UtcNow;
            _taskManager.RecordTaskStarted(task.TypeName);

            try
            {
                taskInstance.Execute();
            }
            finally
            {
                _taskManager.RecordTaskFinished(task.TypeName, startTime);
            }
        });

        return Ok(new { message = $"Task {task.TypeName} execution started" });
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

            return new CommandResource
            {
                Id = c.Id,
                Name = c.Name,
                Status = c.Status.ToString().ToLowerInvariant(),
                QueuedAt = c.QueuedAt,
                StartedAt = c.StartedAt,
                EndedAt = c.EndedAt,
                Duration = duration,
                Message = c.Message
            };
        }).ToList());
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
    public ActionResult Restart()
    {
        global::System.Threading.Tasks.Task.Run(async () =>
        {
            await global::System.Threading.Tasks.Task.Delay(500);
            _lifetime.StopApplication();
        });

        return Ok(new { message = "Restarting..." });
    }

    [HttpPost("shutdown")]
    public ActionResult Shutdown()
    {
        global::System.Threading.Tasks.Task.Run(async () =>
        {
            await global::System.Threading.Tasks.Task.Delay(500);
            _lifetime.StopApplication();
        });

        return Ok(new { message = "Shutting down..." });
    }
}
