using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Instrumentation;
using Seedarr.Http;

namespace Seedarr.Api.V1.System;

[V1ApiController("log")]
public class LogController : ControllerBase
{
    [HttpGet]
    public ActionResult<List<LogResource>> GetLogs(
        [FromQuery] string level = null,
        [FromQuery] int count = 500)
    {
        if (count < 1)
        {
            count = 1;
        }

        if (count > 5000)
        {
            count = 5000;
        }

        var minimumLevel = LogLevel.Trace;

        if (!string.IsNullOrWhiteSpace(level))
        {
            try
            {
                minimumLevel = LogLevel.FromString(level);
            }
            catch (ArgumentException)
            {
                // Invalid level string, fall back to Trace
            }
        }

        var target = RingBufferTarget.Instance;

        if (target == null)
        {
            return Ok(new List<LogResource>());
        }

        var entries = target.GetEntries(count, minimumLevel);

        var resources = entries.Select((e, i) => new LogResource
        {
            Id = i + 1,
            Time = e.Time.ToString("O"),
            Level = e.Level,
            Logger = e.Logger,
            Message = e.Message,
            Exception = e.Exception
        }).ToList();

        return Ok(resources);
    }
}

public class LogResource
{
    public int Id { get; set; }
    public string Time { get; set; }
    public string Level { get; set; }
    public string Logger { get; set; }
    public string Message { get; set; }
    public string Exception { get; set; }
}
