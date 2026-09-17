using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Seeding.Scheduling;
using Seedarr.Http;

namespace Seedarr.Api.V1.Seeding;

[V1ApiController("speedschedule")]
public class SpeedScheduleController : Controller
{
    private readonly ISpeedScheduler _speedScheduler;
    private readonly IConfigService _configService;

    public SpeedScheduleController(ISpeedScheduler speedScheduler, IConfigService configService)
    {
        _speedScheduler = speedScheduler;
        _configService = configService;
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
        var limits = _speedScheduler.GetCurrentLimits();

        var uploadKbps = _configService.AlternativeSpeedEnabled
            ? _configService.AltUploadSpeedKbps
            : _configService.MaxUploadSpeedKbps;
        var downloadKbps = _configService.AlternativeSpeedEnabled
            ? _configService.AltDownloadSpeedKbps
            : _configService.MaxDownloadSpeedKbps;

        var uploadLimitBps = uploadKbps > 0 ? (long)uploadKbps * 1024 : SpeedLimits.Unlimited;
        var downloadLimitBps = downloadKbps > 0 ? (long)downloadKbps * 1024 : SpeedLimits.Unlimited;

        return SpeedLimitMerger.Apply(limits, uploadLimitBps, downloadLimitBps);
    }

    [HttpPost]
    public ActionResult<SpeedScheduleResource> Create([FromBody] SpeedScheduleResource resource)
    {
        var validation = ValidateResource(resource);
        if (validation != null)
        {
            return validation;
        }

        SpeedSchedule schedule;
        try
        {
            schedule = ToModel(resource);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return BadRequest("Invalid time format. Expected HH:mm.");
        }

        var added = _speedScheduler.Add(schedule);
        return Created($"/api/v1/speedschedule/{added.Id}", ToResource(added));
    }

    [HttpPut("{id:int}")]
    public ActionResult<SpeedScheduleResource> Update(int id, [FromBody] SpeedScheduleResource resource)
    {
        var validation = ValidateResource(resource);
        if (validation != null)
        {
            return validation;
        }

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
        catch (Exception ex) when (ex is FormatException or ArgumentException)
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

    private ActionResult ValidateResource(SpeedScheduleResource resource)
    {
        if (resource == null)
        {
            return BadRequest("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return BadRequest("Schedule name cannot be empty.");
        }

        if (resource.Days <= 0 || resource.Days > 127)
        {
            return BadRequest("Days must be between 1 and 127.");
        }

        if (resource.MaxUploadSpeed < -1 || resource.MaxDownloadSpeed < -1)
        {
            return BadRequest("Speed limit cannot be less than -1.");
        }

        return null;
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
        if (string.IsNullOrWhiteSpace(resource.StartTime))
        {
            throw new ArgumentException("StartTime cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(resource.EndTime))
        {
            throw new ArgumentException("EndTime cannot be null or empty.");
        }

        return new SpeedSchedule
        {
            Id = resource.Id,
            Name = resource.Name?.Trim() ?? string.Empty,
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
