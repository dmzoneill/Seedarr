using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Seeding.Scheduling;

[Flags]
public enum ScheduleDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekends = Saturday | Sunday,
    All = Weekdays | Weekends
}

public class SpeedSchedule : ModelBase
{
    public string Name { get; set; }
    public ScheduleDays Days { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public long MaxUploadSpeed { get; set; }
    public long MaxDownloadSpeed { get; set; }
    public bool IsEnabled { get; set; }
    public int Priority { get; set; }
}
