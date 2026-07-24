using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Seeding;

public class SpeedScheduleResource : RestResource
{
    public string Name { get; set; }
    public int Days { get; set; }
    public string StartTime { get; set; }
    public string EndTime { get; set; }
    public long MaxUploadSpeed { get; set; }
    public long MaxDownloadSpeed { get; set; }
    public bool IsEnabled { get; set; }
    public int Priority { get; set; }
}
