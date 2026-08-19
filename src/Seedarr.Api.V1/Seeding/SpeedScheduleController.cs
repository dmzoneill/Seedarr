using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Seeding.Scheduling;
using Seedarr.Http;

namespace Seedarr.Api.V1.Seeding;

[V1ApiController("speedschedule")]
public class SpeedScheduleController : Controller
{
    private readonly ISpeedScheduler _speedScheduler;

    public SpeedScheduleController(ISpeedScheduler speedScheduler)
    {
        _speedScheduler = speedScheduler;
    }

    [HttpGet]
    public List<SpeedScheduleResource> GetAll()
    {
        return _speedScheduler.GetAll().Select(ToResource).ToList();
    }

    [HttpGet("{id:int}")]
    public ActionResult<SpeedScheduleResource> GetById(int id)
    {
        var schedule = _speedScheduler.Get(id);
        if (schedule == null)
        {
            return NotFound();
        }

        return ToResource(schedule);
    }

    [HttpGet("active")]
    public ActionResult<SpeedLimits> GetActiveLimits()
    {
        return _speedScheduler.GetCurrentLimits();
    }

    [HttpPost]
    public ActionResult<SpeedScheduleResource> Create([FromBody] SpeedScheduleResource resource)
    {
        SpeedSchedule schedule;
        try
        {
            schedule = ToModel(resource);
        }
        catch (FormatException)
        {
            return BadRequest("Invalid time format. Expected HH:mm.");
        }

        var added = _speedScheduler.Add(schedule);
        return Created($"/api/v1/speedschedule/{added.Id}", ToResource(added));
    }

    [HttpPut("{id:int}")]
    public ActionResult<SpeedScheduleResource> Update(int id, [FromBody] SpeedScheduleResource resource)
    {
        var existing = _speedScheduler.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        SpeedSchedule schedule;
        try
        {
            schedule = ToModel(resource);
        }
        catch (FormatException)
        {
            return BadRequest("Invalid time format. Expected HH:mm.");
        }

        schedule.Id = id;
        var updated = _speedScheduler.Update(schedule);
        return ToResource(updated);
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        var existing = _speedScheduler.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        _speedScheduler.Delete(id);
        return Ok();
    }

    private static SpeedScheduleResource ToResource(SpeedSchedule model)
    {
        return new SpeedScheduleResource
        {
            Id = model.Id,
            Name = model.Name,
            Days = (int)model.Days,
            StartTime = model.StartTime.ToString("HH:mm"),
            EndTime = model.EndTime.ToString("HH:mm"),
            MaxUploadSpeed = model.MaxUploadSpeed,
            MaxDownloadSpeed = model.MaxDownloadSpeed,
            IsEnabled = model.IsEnabled,
            Priority = model.Priority
        };
    }

    private static SpeedSchedule ToModel(SpeedScheduleResource resource)
    {
        return new SpeedSchedule
        {
            Id = resource.Id,
            Name = resource.Name,
            Days = (ScheduleDays)resource.Days,
            StartTime = TimeOnly.Parse(resource.StartTime),
            EndTime = TimeOnly.Parse(resource.EndTime),
            MaxUploadSpeed = resource.MaxUploadSpeed,
            MaxDownloadSpeed = resource.MaxDownloadSpeed,
            IsEnabled = resource.IsEnabled,
            Priority = resource.Priority
        };
    }
}
