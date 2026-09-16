using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Seeding.Scheduling;

public class SpeedLimits
{
    public static readonly long Unlimited = -1L;

    public long MaxUploadSpeed { get; set; }
    public long MaxDownloadSpeed { get; set; }
    public bool IsScheduleActive { get; set; }
    public string ActiveScheduleName { get; set; }
}

public interface ISpeedScheduler
{
    SpeedLimits GetCurrentLimits();
    SpeedLimits GetEffectiveLimits() => GetCurrentLimits();
    SpeedLimits GetLimitsAt(DateTime utcTime);
    List<SpeedSchedule> GetAll();
    SpeedSchedule Get(int id);
    SpeedSchedule Add(SpeedSchedule schedule);
    SpeedSchedule Update(SpeedSchedule schedule);
    void Delete(int id);
}

public class SpeedScheduler : ISpeedScheduler
{
    // When no schedule is active the scheduler imposes no constraint; the global
    // MaxUpload/MaxDownload config limits are merged in by SeedingEngine.
    // Returns a fresh instance because callers mutate the result.
    private static SpeedLimits NoConstraint() => new SpeedLimits
    {
        MaxUploadSpeed = SpeedLimits.Unlimited,
        MaxDownloadSpeed = SpeedLimits.Unlimited,
        IsScheduleActive = false
    };

    private readonly ISpeedScheduleRepository _repository;
    private readonly IConfigService _configService;
    private readonly ISystemClock _clock;
    private readonly Logger _logger;
    private readonly object _cacheLock = new();
    private List<SpeedSchedule> _cachedEnabledSchedules;

    public SpeedScheduler(
        ISpeedScheduleRepository repository,
        IConfigService configService,
        ISystemClock clock = null)
    {
        _repository = repository;
        _configService = configService;
        _clock = clock ?? new SystemClock();
        _logger = LogManager.GetCurrentClassLogger();
    }

    public SpeedLimits GetCurrentLimits()
    {
        return GetLimitsAt(_clock.UtcNow);
    }

    public SpeedLimits GetEffectiveLimits()
    {
        return GetCurrentLimits();
    }

    public SpeedLimits GetLimitsAt(DateTime utcTime)
    {
        var localTime = ToConfiguredTime(utcTime);
        var schedules = GetEnabledSchedules();

        if (schedules.Count == 0)
        {
            // No SpeedSchedule entities exist; fall back to global scheduler config
            return GetLimitsFromGlobalConfig(localTime);
        }

        var activeSchedules = GetActiveSchedules(schedules, localTime);

        if (activeSchedules.Count == 0)
        {
            return NoConstraint();
        }

        return ResolveLimits(activeSchedules);
    }

    private DateTime ToConfiguredTime(DateTime utcTime)
    {
        if (TryGetConfiguredTimeZone(out var tz))
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utcTime, tz);
        }

        return TimeZoneInfo.ConvertTimeFromUtc(utcTime, TimeZoneInfo.Local);
    }

    private bool TryGetConfiguredTimeZone(out TimeZoneInfo timeZone)
    {
        timeZone = null;
        var tzId = _configService?.TimeZone;
        if (string.IsNullOrWhiteSpace(tzId))
        {
            return false;
        }

        try
        {
            return TimeZoneInfo.TryFindSystemTimeZoneById(tzId, out timeZone);
        }
        catch
        {
            return false;
        }
    }

    private SpeedLimits GetLimitsFromGlobalConfig(DateTime localTime)
    {
        if (!_configService.SchedulerEnabled)
        {
            return NoConstraint();
        }

        var startTime = new TimeOnly(_configService.SchedulerStartHour, _configService.SchedulerStartMinute);
        var endTime = new TimeOnly(_configService.SchedulerEndHour, _configService.SchedulerEndMinute);
        var currentTime = TimeOnly.FromDateTime(localTime);
        var today = localTime.DayOfWeek;
        var prevDay = (DayOfWeek)(((int)localTime.DayOfWeek + 6) % 7);

        bool isActive;
        if (startTime == endTime)
        {
            isActive = IsDayEnabledInGlobalConfig(today);
        }
        else if (startTime < endTime)
        {
            isActive = IsDayEnabledInGlobalConfig(today) && currentTime >= startTime && IsCurrentTimeBeforeEnd(currentTime, endTime);
        }
        else
        {
            isActive = (currentTime >= startTime && IsDayEnabledInGlobalConfig(today)) ||
                (IsCurrentTimeBeforeEnd(currentTime, endTime) && IsDayEnabledInGlobalConfig(prevDay));
        }

        if (isActive)
        {
            // Global scheduler is active: use alternative speed limits from config.
            // 0 means unlimited for alt speeds as well.
            var altUpload = _configService.AltUploadSpeedKbps > 0
                ? (long)_configService.AltUploadSpeedKbps * 1024
                : SpeedLimits.Unlimited;
            var altDownload = _configService.AltDownloadSpeedKbps > 0
                ? (long)_configService.AltDownloadSpeedKbps * 1024
                : SpeedLimits.Unlimited;

            _logger.Debug("Global scheduler active: alt speeds upload={0} download={1}", altUpload, altDownload);

            return new SpeedLimits
            {
                MaxUploadSpeed = altUpload,
                MaxDownloadSpeed = altDownload,
                IsScheduleActive = true,
                ActiveScheduleName = "Global Scheduler"
            };
        }

        return NoConstraint();
    }

    private bool IsDayEnabledInGlobalConfig(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Monday => _configService.SchedulerMonday,
            DayOfWeek.Tuesday => _configService.SchedulerTuesday,
            DayOfWeek.Wednesday => _configService.SchedulerWednesday,
            DayOfWeek.Thursday => _configService.SchedulerThursday,
            DayOfWeek.Friday => _configService.SchedulerFriday,
            DayOfWeek.Saturday => _configService.SchedulerSaturday,
            DayOfWeek.Sunday => _configService.SchedulerSunday,
            _ => false
        };
    }

    public List<SpeedSchedule> GetAll()
    {
        return _repository.All().ToList();
    }

    public SpeedSchedule Get(int id)
    {
        return _repository.Get(id);
    }

    public SpeedSchedule Add(SpeedSchedule schedule)
    {
        _logger.Info("Adding speed schedule: {0}", schedule.Name);
        var added = _repository.Insert(schedule);
        InvalidateCache();
        return added;
    }

    public SpeedSchedule Update(SpeedSchedule schedule)
    {
        _logger.Info("Updating speed schedule: {0}", schedule.Name);
        var updated = _repository.Update(schedule);
        InvalidateCache();
        return updated;
    }

    public void Delete(int id)
    {
        _logger.Info("Deleting speed schedule: {0}", id);
        _repository.Delete(id);
        InvalidateCache();
    }

    private List<SpeedSchedule> GetEnabledSchedules()
    {
        lock (_cacheLock)
        {
            return _cachedEnabledSchedules ??= _repository.GetEnabled().ToList();
        }
    }

    private void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedEnabledSchedules = null;
        }
    }

    private static List<SpeedSchedule> GetActiveSchedules(List<SpeedSchedule> schedules, DateTime localTime)
    {
        var todayFlag = MapDayOfWeek(localTime.DayOfWeek);
        var prevDay = (DayOfWeek)(((int)localTime.DayOfWeek + 6) % 7);
        var prevDayFlag = MapDayOfWeek(prevDay);
        var currentTime = TimeOnly.FromDateTime(localTime);

        var active = new List<SpeedSchedule>();

        foreach (var schedule in schedules)
        {
            if (schedule.StartTime == schedule.EndTime)
            {
                // When StartTime == EndTime (e.g. 00:00 to 00:00), treat as active for all 24 hours of matching days
                if (schedule.Days.HasFlag(todayFlag))
                {
                    active.Add(schedule);
                }
            }
            else if (schedule.StartTime < schedule.EndTime)
            {
                if (schedule.Days.HasFlag(todayFlag) && currentTime >= schedule.StartTime && IsCurrentTimeBeforeEnd(currentTime, schedule.EndTime))
                {
                    active.Add(schedule);
                }
            }
            else
            {
                // Overnight schedule (e.g. 22:00 - 06:00)
                // Evening half: On the start day, active only once StartTime is reached.
                // Morning half: On the following day, active until EndTime is reached.
                if (schedule.Days.HasFlag(todayFlag) && currentTime >= schedule.StartTime)
                {
                    active.Add(schedule);
                }
                else if (schedule.Days.HasFlag(prevDayFlag) && IsCurrentTimeBeforeEnd(currentTime, schedule.EndTime))
                {
                    active.Add(schedule);
                }
            }
        }

        return active;
    }

    private static bool IsCurrentTimeBeforeEnd(TimeOnly currentTime, TimeOnly endTime)
    {
        if (endTime.Hour == 23 && endTime.Minute == 59)
        {
            return true;
        }

        return currentTime < endTime;
    }

    private static bool IsTimeInRange(TimeOnly current, TimeOnly start, TimeOnly end)
    {
        if (start == end)
        {
            return true;
        }

        if (start < end)
        {
            return current >= start && IsCurrentTimeBeforeEnd(current, end);
        }

        // Handles overnight ranges (e.g., 22:00 - 06:00)
        return current >= start || IsCurrentTimeBeforeEnd(current, end);
    }

    private static ScheduleDays MapDayOfWeek(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Monday => ScheduleDays.Monday,
            DayOfWeek.Tuesday => ScheduleDays.Tuesday,
            DayOfWeek.Wednesday => ScheduleDays.Wednesday,
            DayOfWeek.Thursday => ScheduleDays.Thursday,
            DayOfWeek.Friday => ScheduleDays.Friday,
            DayOfWeek.Saturday => ScheduleDays.Saturday,
            DayOfWeek.Sunday => ScheduleDays.Sunday,
            _ => ScheduleDays.None
        };
    }

    private static SpeedLimits ResolveLimits(List<SpeedSchedule> activeSchedules)
    {
        // Most restrictive wins: take the lowest speed from all active schedules.
        // -1 indicates Unlimited (-1L), whereas 0 indicates an explicit 0 B/s throttle (paused).
        var uploadSpeeds = activeSchedules
            .Where(s => s.MaxUploadSpeed >= 0)
            .Select(s => s.MaxUploadSpeed)
            .ToList();

        var downloadSpeeds = activeSchedules
            .Where(s => s.MaxDownloadSpeed >= 0)
            .Select(s => s.MaxDownloadSpeed)
            .ToList();

        var effectiveUpload = uploadSpeeds.Count > 0 ? uploadSpeeds.Min() : SpeedLimits.Unlimited;
        var effectiveDownload = downloadSpeeds.Count > 0 ? downloadSpeeds.Min() : SpeedLimits.Unlimited;

        // Pick the highest-priority (lowest number) schedule for the display name
        var primarySchedule = activeSchedules.OrderBy(s => s.Priority).First();

        return new SpeedLimits
        {
            MaxUploadSpeed = effectiveUpload,
            MaxDownloadSpeed = effectiveDownload,
            IsScheduleActive = true,
            ActiveScheduleName = primarySchedule.Name
        };
    }
}
