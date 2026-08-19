using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
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
    SpeedLimits GetLimitsAt(DateTime utcTime);
    List<SpeedSchedule> GetAll();
    SpeedSchedule Get(int id);
    SpeedSchedule Add(SpeedSchedule schedule);
    SpeedSchedule Update(SpeedSchedule schedule);
    void Delete(int id);
}

public class SpeedScheduler : ISpeedScheduler
{
    private const long DefaultUploadSpeed = 1_048_576;
    private const long DefaultDownloadSpeed = 1_048_576;

    private readonly ISpeedScheduleRepository _repository;
    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public SpeedScheduler(ISpeedScheduleRepository repository, IConfigService configService)
    {
        _repository = repository;
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public SpeedLimits GetCurrentLimits()
    {
        return GetLimitsAt(DateTime.UtcNow);
    }

    public SpeedLimits GetLimitsAt(DateTime utcTime)
    {
        var schedules = _repository.GetEnabled().ToList();

        if (schedules.Count == 0)
        {
            // No SpeedSchedule entities exist; fall back to global scheduler config
            return GetLimitsFromGlobalConfig(utcTime);
        }

        var activeSchedules = GetActiveSchedules(schedules, utcTime);

        if (activeSchedules.Count == 0)
        {
            return new SpeedLimits
            {
                MaxUploadSpeed = DefaultUploadSpeed,
                MaxDownloadSpeed = DefaultDownloadSpeed,
                IsScheduleActive = false
            };
        }

        return ResolveLimits(activeSchedules);
    }

    private SpeedLimits GetLimitsFromGlobalConfig(DateTime utcTime)
    {
        if (!_configService.SchedulerEnabled)
        {
            return new SpeedLimits
            {
                MaxUploadSpeed = DefaultUploadSpeed,
                MaxDownloadSpeed = DefaultDownloadSpeed,
                IsScheduleActive = false
            };
        }

        var dayOfWeek = utcTime.DayOfWeek;

        if (!IsDayEnabledInGlobalConfig(dayOfWeek))
        {
            return new SpeedLimits
            {
                MaxUploadSpeed = DefaultUploadSpeed,
                MaxDownloadSpeed = DefaultDownloadSpeed,
                IsScheduleActive = false
            };
        }

        var startTime = new TimeOnly(_configService.SchedulerStartHour, _configService.SchedulerStartMinute);
        var endTime = new TimeOnly(_configService.SchedulerEndHour, _configService.SchedulerEndMinute);
        var currentTime = TimeOnly.FromDateTime(utcTime);

        if (IsTimeInRange(currentTime, startTime, endTime))
        {
            // Global scheduler is active: use alternative speed limits from config
            var altUpload = _configService.AltUploadSpeedKbps > 0
                ? (long)_configService.AltUploadSpeedKbps * 1024
                : DefaultUploadSpeed;
            var altDownload = _configService.AltDownloadSpeedKbps > 0
                ? (long)_configService.AltDownloadSpeedKbps * 1024
                : DefaultDownloadSpeed;

            _logger.Debug("Global scheduler active: alt speeds upload={0} download={1}", altUpload, altDownload);

            return new SpeedLimits
            {
                MaxUploadSpeed = altUpload,
                MaxDownloadSpeed = altDownload,
                IsScheduleActive = true,
                ActiveScheduleName = "Global Scheduler"
            };
        }

        return new SpeedLimits
        {
            MaxUploadSpeed = DefaultUploadSpeed,
            MaxDownloadSpeed = DefaultDownloadSpeed,
            IsScheduleActive = false
        };
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
        return _repository.Insert(schedule);
    }

    public SpeedSchedule Update(SpeedSchedule schedule)
    {
        _logger.Info("Updating speed schedule: {0}", schedule.Name);
        return _repository.Update(schedule);
    }

    public void Delete(int id)
    {
        _logger.Info("Deleting speed schedule: {0}", id);
        _repository.Delete(id);
    }

    private static List<SpeedSchedule> GetActiveSchedules(List<SpeedSchedule> schedules, DateTime utcTime)
    {
        var dayOfWeek = utcTime.DayOfWeek;
        var scheduleDayFlag = MapDayOfWeek(dayOfWeek);
        var currentTime = TimeOnly.FromDateTime(utcTime);

        var active = new List<SpeedSchedule>();

        foreach (var schedule in schedules)
        {
            if (!schedule.Days.HasFlag(scheduleDayFlag))
            {
                continue;
            }

            if (IsTimeInRange(currentTime, schedule.StartTime, schedule.EndTime))
            {
                active.Add(schedule);
            }
        }

        return active;
    }

    private static bool IsTimeInRange(TimeOnly current, TimeOnly start, TimeOnly end)
    {
        if (start <= end)
        {
            return current >= start && current < end;
        }

        // Handles overnight ranges (e.g., 22:00 - 06:00)
        return current >= start || current < end;
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
        // Most restrictive wins: take the lowest non-zero speed from all active schedules.
        // A value of 0 means unlimited for that schedule, so it does not constrain.
        var uploadSpeeds = activeSchedules
            .Where(s => s.MaxUploadSpeed > 0)
            .Select(s => s.MaxUploadSpeed)
            .ToList();

        var downloadSpeeds = activeSchedules
            .Where(s => s.MaxDownloadSpeed > 0)
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
