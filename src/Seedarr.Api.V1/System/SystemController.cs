using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;
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
    private readonly IManageCommandQueue _commandQueueManager;
    private readonly IAppFolderInfo _appFolderInfo;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemController"/> class.
    /// </summary>
    /// <param name="taskManager">Task manager.</param>
    /// <param name="commandQueueManager">Command queue manager.</param>
    /// <param name="appFolderInfo">Application folder info.</param>
    public SystemController(
        ITaskManager taskManager,
        IManageCommandQueue commandQueueManager,
        IAppFolderInfo appFolderInfo)
    {
        _taskManager = taskManager;
        _commandQueueManager = commandQueueManager;
        _appFolderInfo = appFolderInfo;
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
                TypeName = t.TypeName,
                Interval = t.Interval,
                LastExecution = t.LastExecution,
                LastStartTime = t.LastStartTime,
                LastDuration = lastDuration,
                NextExecution = nextExecution
            };
        }).ToList());
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
}
