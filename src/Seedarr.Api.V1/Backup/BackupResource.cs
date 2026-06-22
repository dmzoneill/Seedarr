using System;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Backup;

public class BackupResource : RestResource
{
    public string Name { get; set; }
    public long Size { get; set; }
    public DateTime Time { get; set; }
}
