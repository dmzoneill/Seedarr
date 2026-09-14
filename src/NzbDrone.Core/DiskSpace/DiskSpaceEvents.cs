using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.DiskSpace;

public class DiskSpaceLowEvent : IEvent
{
    public string DrivePath { get; set; }

    public long FreeBytes { get; set; }

    public long TotalBytes { get; set; }

    public double FreePercentage { get; set; }

    public DiskSpaceLowEvent()
    {
    }

    public DiskSpaceLowEvent(string drivePath, long freeBytes, long totalBytes, double freePercentage)
    {
        this.DrivePath = drivePath;
        this.FreeBytes = freeBytes;
        this.TotalBytes = totalBytes;
        this.FreePercentage = freePercentage;
    }
}

public class DiskSpaceCriticalEvent : IEvent
{
    public string DrivePath { get; set; }

    public long FreeBytes { get; set; }

    public DiskSpaceCriticalEvent()
    {
    }

    public DiskSpaceCriticalEvent(string drivePath, long freeBytes)
    {
        this.DrivePath = drivePath;
        this.FreeBytes = freeBytes;
    }
}

public class DiskSpaceRestoredEvent : IEvent
{
    public string DrivePath { get; set; }

    public long FreeBytes { get; set; }

    public long TotalBytes { get; set; }

    public DiskSpaceRestoredEvent()
    {
    }

    public DiskSpaceRestoredEvent(string drivePath, long freeBytes, long totalBytes)
    {
        this.DrivePath = drivePath;
        this.FreeBytes = freeBytes;
        this.TotalBytes = totalBytes;
    }
}
