using System;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;
using Seedarr.Http;

namespace Seedarr.Api.V1.System;

[V1ApiController("system")]
public class SystemController : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<SystemResource> GetStatus()
    {
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
            StartTime = _startTime
        });
    }

    private static readonly DateTime _startTime = DateTime.UtcNow;
}
