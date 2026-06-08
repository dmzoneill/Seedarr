using System;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.System;

public class SystemResource : RestResource
{
    public string AppName { get; set; }
    public string Version { get; set; }
    public string OsName { get; set; }
    public string OsVersion { get; set; }
    public bool IsWindows { get; set; }
    public bool IsLinux { get; set; }
    public bool IsOsx { get; set; }
    public string Branch { get; set; }
    public string RuntimeName { get; set; }
    public DateTime StartTime { get; set; }
}
