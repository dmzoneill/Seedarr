using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace NzbDrone.Core.Seeding.Scheduling;

public class SpeedLimits
{
    public static readonly long Unlimited;

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
    private readonly Logger _logger;

    public SpeedScheduler(ISpeedScheduleRepository repository)
    {
        _repository = repository;
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
            return new SpeedLimits
            {
                MaxUploadSpeed = DefaultUploadSpeed,
                MaxDownloadSpeed = DefaultDownloadSpeed,
                IsScheduleActive = false
            };
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
