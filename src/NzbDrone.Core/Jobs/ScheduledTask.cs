using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Jobs;

public class ScheduledTask : ModelBase
{
    private DateTime? _nextExecution;

    public string TypeName { get; set; }
    public int Interval { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime LastExecution { get; set; }
    public DateTime? LastStartTime { get; set; }
    public TaskExecutionStatus LastStatus { get; set; } = TaskExecutionStatus.None;
    public string LastErrorMessage { get; set; }

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
        var next = lastUtc.AddMinutes((intervalsPassed + 1) * (double)interval);
        if (next <= now)
        {
            next = now.AddMinutes(interval);
        }

        return DateTime.SpecifyKind(next, DateTimeKind.Utc);
    }

    private static DateTime GetDueCheckReference(DateTime lastUtc, int interval, DateTime now)
    {
        var normalizedInterval = interval <= 0 ? 15 : interval;
        var lookbackSeconds = Math.Clamp(normalizedInterval * 60 / 4, 45, 180);
        var reference = now.AddSeconds(-lookbackSeconds);
        return reference < lastUtc ? lastUtc : reference;
    }

    public static bool IsSchedulerDue(DateTime lastExecution, int interval, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;

        if (lastExecution == DateTime.MinValue || lastExecution <= DateTime.MinValue.AddDays(1))
        {
            return true;
        }

        var lastUtc = DateTime.SpecifyKind(lastExecution, DateTimeKind.Utc);
        var reference = GetDueCheckReference(lastUtc, interval, now);

        return now >= CalculateNextExecution(lastExecution, interval, reference);
    }

    public static DateTime GetSchedulerOrderingKey(DateTime lastExecution, int interval, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;

        if (lastExecution == DateTime.MinValue || lastExecution <= DateTime.MinValue.AddDays(1))
        {
            return DateTime.SpecifyKind(now, DateTimeKind.Utc);
        }

        var lastUtc = DateTime.SpecifyKind(lastExecution, DateTimeKind.Utc);
        var reference = GetDueCheckReference(lastUtc, interval, now);
        var activeSlot = CalculateNextExecution(lastExecution, interval, reference);
        if (now >= activeSlot)
        {
            return activeSlot;
        }

        return CalculateNextExecution(lastExecution, interval, now);
    }
}
