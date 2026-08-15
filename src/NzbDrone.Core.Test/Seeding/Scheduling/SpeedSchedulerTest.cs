using System;
using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Seeding.Scheduling;

namespace NzbDrone.Core.Test.Seeding.Scheduling;

[TestFixture]
public class SpeedSchedulerTest
{
    private ISpeedScheduleRepository _repository;
    private IConfigService _configService;
    private SpeedScheduler _scheduler;

    [SetUp]
    public void Setup()
    {
        _repository = Substitute.For<ISpeedScheduleRepository>();
        _configService = Substitute.For<IConfigService>();
        _scheduler = new SpeedScheduler(_repository, _configService);

        _configService.SchedulerEnabled.Returns(false);
    }

    [Test]
    public void GetLimitsAt_should_return_defaults_when_no_schedules_and_scheduler_disabled()
    {
        _repository.GetEnabled().Returns(Enumerable.Empty<SpeedSchedule>());
        _configService.SchedulerEnabled.Returns(false);

        var limits = _scheduler.GetLimitsAt(new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc));

        Assert.That(limits.MaxUploadSpeed, Is.EqualTo(1_048_576L));
        Assert.That(limits.MaxDownloadSpeed, Is.EqualTo(1_048_576L));
        Assert.That(limits.IsScheduleActive, Is.False);
    }

    [Test]
    public void GetLimitsAt_should_use_global_config_when_no_schedules_and_scheduler_enabled()
    {
        _repository.GetEnabled().Returns(Enumerable.Empty<SpeedSchedule>());
        _configService.SchedulerEnabled.Returns(true);
        _configService.SchedulerStartHour.Returns(10);
        _configService.SchedulerStartMinute.Returns(0);
        _configService.SchedulerEndHour.Returns(18);
        _configService.SchedulerEndMinute.Returns(0);
        _configService.SchedulerMonday.Returns(true);
        _configService.AltUploadSpeedKbps.Returns(100);
        _configService.AltDownloadSpeedKbps.Returns(200);

        var monday12pm = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
        var limits = _scheduler.GetLimitsAt(monday12pm);

        Assert.That(limits.IsScheduleActive, Is.True);
        Assert.That(limits.ActiveScheduleName, Is.EqualTo("Global Scheduler"));
        Assert.That(limits.MaxUploadSpeed, Is.EqualTo(100 * 1024L));
        Assert.That(limits.MaxDownloadSpeed, Is.EqualTo(200 * 1024L));
    }

    [Test]
    public void GetLimitsAt_should_return_defaults_when_global_scheduler_enabled_but_day_disabled()
    {
        _repository.GetEnabled().Returns(Enumerable.Empty<SpeedSchedule>());
        _configService.SchedulerEnabled.Returns(true);
        _configService.SchedulerMonday.Returns(false);
        _configService.SchedulerTuesday.Returns(false);

        var monday12pm = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
        var limits = _scheduler.GetLimitsAt(monday12pm);

        Assert.That(limits.IsScheduleActive, Is.False);
    }

    [Test]
    public void GetLimitsAt_should_return_defaults_when_global_scheduler_enabled_but_time_outside_range()
    {
        _repository.GetEnabled().Returns(Enumerable.Empty<SpeedSchedule>());
        _configService.SchedulerEnabled.Returns(true);
        _configService.SchedulerStartHour.Returns(10);
        _configService.SchedulerStartMinute.Returns(0);
        _configService.SchedulerEndHour.Returns(18);
        _configService.SchedulerEndMinute.Returns(0);
        _configService.SchedulerMonday.Returns(true);

        var mondayEarly = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
        var limits = _scheduler.GetLimitsAt(mondayEarly);

        Assert.That(limits.IsScheduleActive, Is.False);
    }

    [Test]
    public void GetLimitsAt_should_use_default_alt_speed_when_alt_speed_is_zero()
    {
        _repository.GetEnabled().Returns(Enumerable.Empty<SpeedSchedule>());
        _configService.SchedulerEnabled.Returns(true);
        _configService.SchedulerStartHour.Returns(10);
        _configService.SchedulerStartMinute.Returns(0);
        _configService.SchedulerEndHour.Returns(18);
        _configService.SchedulerEndMinute.Returns(0);
        _configService.SchedulerMonday.Returns(true);
        _configService.AltUploadSpeedKbps.Returns(0);
        _configService.AltDownloadSpeedKbps.Returns(0);

        var monday12pm = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
        var limits = _scheduler.GetLimitsAt(monday12pm);

        Assert.That(limits.MaxUploadSpeed, Is.EqualTo(1_048_576L));
        Assert.That(limits.MaxDownloadSpeed, Is.EqualTo(1_048_576L));
    }

    [Test]
    public void GetLimitsAt_should_match_schedule_on_correct_day_and_time()
    {
        var schedule = new SpeedSchedule
        {
            Name = "WeekdaySchedule",
            Days = ScheduleDays.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            MaxUploadSpeed = 512_000,
            MaxDownloadSpeed = 256_000,
            IsEnabled = true,
            Priority = 1
        };
        _repository.GetEnabled().Returns(new List<SpeedSchedule> { schedule });

        var monday12pm = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
        var limits = _scheduler.GetLimitsAt(monday12pm);

        Assert.That(limits.IsScheduleActive, Is.True);
        Assert.That(limits.MaxUploadSpeed, Is.EqualTo(512_000));
        Assert.That(limits.MaxDownloadSpeed, Is.EqualTo(256_000));
        Assert.That(limits.ActiveScheduleName, Is.EqualTo("WeekdaySchedule"));
    }

    [Test]
    public void GetLimitsAt_should_return_defaults_when_schedule_day_does_not_match()
    {
        var schedule = new SpeedSchedule
        {
            Name = "MondayOnly",
            Days = ScheduleDays.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            MaxUploadSpeed = 512_000,
            MaxDownloadSpeed = 256_000,
            IsEnabled = true,
            Priority = 1
        };
        _repository.GetEnabled().Returns(new List<SpeedSchedule> { schedule });

        var tuesday12pm = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
        var limits = _scheduler.GetLimitsAt(tuesday12pm);

        Assert.That(limits.IsScheduleActive, Is.False);
        Assert.That(limits.MaxUploadSpeed, Is.EqualTo(1_048_576L));
    }

    [Test]
    public void GetLimitsAt_should_return_defaults_when_time_is_outside_schedule()
    {
        var schedule = new SpeedSchedule
        {
            Name = "Morning",
            Days = ScheduleDays.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(12, 0),
            MaxUploadSpeed = 512_000,
            MaxDownloadSpeed = 256_000,
            IsEnabled = true,
            Priority = 1
        };
        _repository.GetEnabled().Returns(new List<SpeedSchedule> { schedule });

        var monday14 = new DateTime(2026, 8, 10, 14, 0, 0, DateTimeKind.Utc);
        var limits = _scheduler.GetLimitsAt(monday14);

        Assert.That(limits.IsScheduleActive, Is.False);
    }

    [Test]
    public void GetLimitsAt_should_handle_overnight_schedule()
    {
        var schedule = new SpeedSchedule
        {
            Name = "Overnight",
            Days = ScheduleDays.Monday,
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(6, 0),
            MaxUploadSpeed = 2_000_000,
            MaxDownloadSpeed = 1_000_000,
            IsEnabled = true,
            Priority = 1
        };
        _repository.GetEnabled().Returns(new List<SpeedSchedule> { schedule });

        var mondayLate = new DateTime(2026, 8, 10, 23, 0, 0, DateTimeKind.Utc);
        var limits = _scheduler.GetLimitsAt(mondayLate);

        Assert.That(limits.IsScheduleActive, Is.True);
        Assert.That(limits.MaxUploadSpeed, Is.EqualTo(2_000_000));
    }

    [Test]
    public void GetLimitsAt_should_handle_overnight_early_morning_side()
    {
        var schedule = new SpeedSchedule
        {
            Name = "Overnight",
            Days = ScheduleDays.Monday,
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(6, 0),
            MaxUploadSpeed = 2_000_000,
            MaxDownloadSpeed = 1_000_000,
            IsEnabled = true,
            Priority = 1
        };
        _repository.GetEnabled().Returns(new List<SpeedSchedule> { schedule });

        var mondayEarly = new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc);
        var limits = _scheduler.GetLimitsAt(mondayEarly);

        Assert.That(limits.IsScheduleActive, Is.True);
    }

    [Test]
    public void GetLimitsAt_should_pick_most_restrictive_from_overlapping_schedules()
    {
        var schedule1 = new SpeedSchedule
        {
            Name = "S1",
            Days = ScheduleDays.All,
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            MaxUploadSpeed = 500_000,
            MaxDownloadSpeed = 300_000,
            IsEnabled = true,
            Priority = 2
        };
        var schedule2 = new SpeedSchedule
        {
            Name = "S2",
            Days = ScheduleDays.All,
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            MaxUploadSpeed = 200_000,
            MaxDownloadSpeed = 400_000,
            IsEnabled = true,
            Priority = 1
        };
        _repository.GetEnabled().Returns(new List<SpeedSchedule> { schedule1, schedule2 });

        var limits = _scheduler.GetLimitsAt(new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc));

        Assert.That(limits.MaxUploadSpeed, Is.EqualTo(200_000));
        Assert.That(limits.MaxDownloadSpeed, Is.EqualTo(300_000));
    }

    [Test]
    public void GetLimitsAt_should_use_unlimited_when_schedule_speed_is_zero()
    {
        var schedule = new SpeedSchedule
        {
            Name = "NoLimit",
            Days = ScheduleDays.All,
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            MaxUploadSpeed = 0,
            MaxDownloadSpeed = 0,
            IsEnabled = true,
            Priority = 1
        };
        _repository.GetEnabled().Returns(new List<SpeedSchedule> { schedule });

        var limits = _scheduler.GetLimitsAt(new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc));

        Assert.That(limits.IsScheduleActive, Is.True);
        Assert.That(limits.MaxUploadSpeed, Is.EqualTo(0));
        Assert.That(limits.MaxDownloadSpeed, Is.EqualTo(0));
    }

    [Test]
    public void GetLimitsAt_should_use_primary_schedule_name_by_priority()
    {
        var schedule1 = new SpeedSchedule { Name = "Low", Days = ScheduleDays.All, StartTime = new TimeOnly(0, 0), EndTime = new TimeOnly(23, 59), MaxUploadSpeed = 100, MaxDownloadSpeed = 100, IsEnabled = true, Priority = 5 };
        var schedule2 = new SpeedSchedule { Name = "High", Days = ScheduleDays.All, StartTime = new TimeOnly(0, 0), EndTime = new TimeOnly(23, 59), MaxUploadSpeed = 200, MaxDownloadSpeed = 200, IsEnabled = true, Priority = 1 };
        _repository.GetEnabled().Returns(new List<SpeedSchedule> { schedule1, schedule2 });

        var limits = _scheduler.GetLimitsAt(new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc));

        Assert.That(limits.ActiveScheduleName, Is.EqualTo("High"));
    }

    [Test]
    public void GetCurrentLimits_should_delegate_to_GetLimitsAt()
    {
        _repository.GetEnabled().Returns(Enumerable.Empty<SpeedSchedule>());
        _configService.SchedulerEnabled.Returns(false);

        var limits = _scheduler.GetCurrentLimits();

        Assert.That(limits, Is.Not.Null);
    }

    [Test]
    public void GetAll_should_return_all_schedules()
    {
        var schedules = new List<SpeedSchedule>
        {
            new SpeedSchedule { Name = "S1" },
            new SpeedSchedule { Name = "S2" }
        };
        _repository.All().Returns(schedules);

        var result = _scheduler.GetAll();

        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public void Get_should_return_schedule_by_id()
    {
        var schedule = new SpeedSchedule { Id = 1, Name = "Test" };
        _repository.Get(1).Returns(schedule);

        var result = _scheduler.Get(1);

        Assert.That(result.Name, Is.EqualTo("Test"));
    }

    [Test]
    public void Add_should_insert_schedule()
    {
        var schedule = new SpeedSchedule { Name = "New" };
        _repository.Insert(schedule).Returns(schedule);

        var result = _scheduler.Add(schedule);

        _repository.Received(1).Insert(schedule);
        Assert.That(result.Name, Is.EqualTo("New"));
    }

    [Test]
    public void Update_should_update_schedule()
    {
        var schedule = new SpeedSchedule { Id = 1, Name = "Updated" };
        _repository.Update(schedule).Returns(schedule);

        var result = _scheduler.Update(schedule);

        _repository.Received(1).Update(schedule);
        Assert.That(result.Name, Is.EqualTo("Updated"));
    }

    [Test]
    public void Delete_should_delete_by_id()
    {
        _scheduler.Delete(1);

        _repository.Received(1).Delete(1);
    }

    [TestCase(DayOfWeek.Monday)]
    [TestCase(DayOfWeek.Tuesday)]
    [TestCase(DayOfWeek.Wednesday)]
    [TestCase(DayOfWeek.Thursday)]
    [TestCase(DayOfWeek.Friday)]
    [TestCase(DayOfWeek.Saturday)]
    [TestCase(DayOfWeek.Sunday)]
    public void GetLimitsAt_should_match_each_day_of_week(DayOfWeek dayOfWeek)
    {
        var scheduleDays = dayOfWeek switch
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

        var schedule = new SpeedSchedule
        {
            Name = "DayTest",
            Days = scheduleDays,
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            MaxUploadSpeed = 100_000,
            MaxDownloadSpeed = 100_000,
            IsEnabled = true,
            Priority = 1
        };
        _repository.GetEnabled().Returns(new List<SpeedSchedule> { schedule });

        var daysUntil = ((int)dayOfWeek - (int)new DateTime(2026, 8, 10).DayOfWeek + 7) % 7;
        var targetDate = new DateTime(2026, 8, 10 + daysUntil, 12, 0, 0, DateTimeKind.Utc);

        var limits = _scheduler.GetLimitsAt(targetDate);

        Assert.That(limits.IsScheduleActive, Is.True);
    }

    [TestCase(DayOfWeek.Monday)]
    [TestCase(DayOfWeek.Tuesday)]
    [TestCase(DayOfWeek.Wednesday)]
    [TestCase(DayOfWeek.Thursday)]
    [TestCase(DayOfWeek.Friday)]
    [TestCase(DayOfWeek.Saturday)]
    [TestCase(DayOfWeek.Sunday)]
    public void GetLimitsAt_global_config_should_match_each_day(DayOfWeek dayOfWeek)
    {
        _repository.GetEnabled().Returns(Enumerable.Empty<SpeedSchedule>());
        _configService.SchedulerEnabled.Returns(true);
        _configService.SchedulerStartHour.Returns(0);
        _configService.SchedulerStartMinute.Returns(0);
        _configService.SchedulerEndHour.Returns(23);
        _configService.SchedulerEndMinute.Returns(59);
        _configService.AltUploadSpeedKbps.Returns(100);
        _configService.AltDownloadSpeedKbps.Returns(100);

        _configService.SchedulerMonday.Returns(dayOfWeek == DayOfWeek.Monday);
        _configService.SchedulerTuesday.Returns(dayOfWeek == DayOfWeek.Tuesday);
        _configService.SchedulerWednesday.Returns(dayOfWeek == DayOfWeek.Wednesday);
        _configService.SchedulerThursday.Returns(dayOfWeek == DayOfWeek.Thursday);
        _configService.SchedulerFriday.Returns(dayOfWeek == DayOfWeek.Friday);
        _configService.SchedulerSaturday.Returns(dayOfWeek == DayOfWeek.Saturday);
        _configService.SchedulerSunday.Returns(dayOfWeek == DayOfWeek.Sunday);

        var daysUntil = ((int)dayOfWeek - (int)new DateTime(2026, 8, 10).DayOfWeek + 7) % 7;
        var targetDate = new DateTime(2026, 8, 10 + daysUntil, 12, 0, 0, DateTimeKind.Utc);

        var limits = _scheduler.GetLimitsAt(targetDate);

        Assert.That(limits.IsScheduleActive, Is.True);
        Assert.That(limits.ActiveScheduleName, Is.EqualTo("Global Scheduler"));
    }

    [Test]
    public void GetLimitsAt_should_match_weekdays_flag()
    {
        var schedule = new SpeedSchedule
        {
            Name = "Weekdays",
            Days = ScheduleDays.Weekdays,
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            MaxUploadSpeed = 100_000,
            MaxDownloadSpeed = 100_000,
            IsEnabled = true,
            Priority = 1
        };
        _repository.GetEnabled().Returns(new List<SpeedSchedule> { schedule });

        var wednesday = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);
        var limits = _scheduler.GetLimitsAt(wednesday);
        Assert.That(limits.IsScheduleActive, Is.True);

        var saturday = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        var limitsWeekend = _scheduler.GetLimitsAt(saturday);
        Assert.That(limitsWeekend.IsScheduleActive, Is.False);
    }
}
