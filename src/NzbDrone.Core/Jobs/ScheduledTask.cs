using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Jobs;

public class ScheduledTask : ModelBase
{
    private DateTime? _nextExecution;

    public string TypeName { get; set; }
    public int Interval { get; set; }
    public DateTime LastExecution { get; set; }
    public DateTime? LastStartTime { get; set; }

    [Ignore]
    public DateTime NextExecution
    {
        get => _nextExecution ?? CalculateNextExecution(LastExecution, Interval);
        set => _nextExecution = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    public static DateTime CalculateNextExecution(DateTime lastExecution, int interval, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;

        if (interval <= 0)
        {
            interval = 15;
        }

        if (lastExecution == DateTime.MinValue || lastExecution <= DateTime.MinValue.AddDays(1))
        {
            return DateTime.SpecifyKind(now.AddMinutes(interval), DateTimeKind.Utc);
        }

        var lastUtc = DateTime.SpecifyKind(lastExecution, DateTimeKind.Utc);
        if (lastUtc > now)
        {
            return DateTime.SpecifyKind(lastUtc.AddMinutes(interval), DateTimeKind.Utc);
        }

        var missedMinutes = (now - lastUtc).TotalMinutes;
        var intervalsPassed = (int)Math.Floor(missedMinutes / interval);
        var next = lastUtc.AddMinutes((intervalsPassed + 1) * interval);
        if (next <= now)
        {
            next = now.AddMinutes(interval);
        }

        return DateTime.SpecifyKind(next, DateTimeKind.Utc);
    }
}
