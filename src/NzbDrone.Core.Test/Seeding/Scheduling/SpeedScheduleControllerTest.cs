using System;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Seeding.Scheduling;
using Seedarr.Api.V1.Seeding;

namespace NzbDrone.Core.Test.Seeding.Scheduling;

[TestFixture]
public class SpeedScheduleControllerTest
{
    private ISpeedScheduler _speedScheduler;
    private IConfigService _configService;
    private SpeedScheduleController _controller;

    [SetUp]
    public void SetUp()
    {
        _speedScheduler = Substitute.For<ISpeedScheduler>();
        _configService = Substitute.For<IConfigService>();
        _controller = new SpeedScheduleController(_speedScheduler, _configService);
    }

    [Test]
    public void Create_with_null_resource_returns_bad_request()
    {
        var result = _controller.Create(null);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_with_empty_or_whitespace_name_returns_bad_request(string name)
    {
        var resource = new SpeedScheduleResource
        {
            Name = name,
            Days = 127,
            StartTime = "08:00",
            EndTime = "17:00",
            MaxUploadSpeed = 1000,
            MaxDownloadSpeed = 1000
        };

        var result = _controller.Create(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(128)]
    [TestCase(255)]
    public void Create_with_invalid_days_returns_bad_request(int days)
    {
        var resource = new SpeedScheduleResource
        {
            Name = "Test",
            Days = days,
            StartTime = "08:00",
            EndTime = "17:00",
            MaxUploadSpeed = 1000,
            MaxDownloadSpeed = 1000
        };

        var result = _controller.Create(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase(-2, 0)]
    [TestCase(0, -2)]
    [TestCase(-500, 1000)]
    public void Create_with_invalid_speeds_returns_bad_request(long upload, long download)
    {
        var resource = new SpeedScheduleResource
        {
            Name = "Test",
            Days = 127,
            StartTime = "08:00",
            EndTime = "17:00",
            MaxUploadSpeed = upload,
            MaxDownloadSpeed = download
        };

        var result = _controller.Create(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase(null, "17:00")]
    [TestCase("08:00", null)]
    [TestCase("invalid", "17:00")]
    [TestCase("08:00", "25:00")]
    [TestCase("", "")]
    public void Create_with_invalid_times_returns_bad_request(string startTime, string endTime)
    {
        var resource = new SpeedScheduleResource
        {
            Name = "Test",
            Days = 127,
            StartTime = startTime,
            EndTime = endTime,
            MaxUploadSpeed = 1000,
            MaxDownloadSpeed = 1000
        };

        var result = _controller.Create(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void Create_with_valid_resource_returns_created()
    {
        var resource = new SpeedScheduleResource
        {
            Name = "Valid Schedule",
            Days = 127,
            StartTime = "08:00",
            EndTime = "17:00",
            MaxUploadSpeed = 1000,
            MaxDownloadSpeed = 1000,
            IsEnabled = true,
            Priority = 1
        };

        _speedScheduler.Add(Arg.Any<SpeedSchedule>()).Returns(callInfo =>
        {
            var model = callInfo.Arg<SpeedSchedule>();
            model.Id = 42;
            return model;
        });

        var result = _controller.Create(resource);

        Assert.That(result.Result, Is.InstanceOf<CreatedResult>());
        var created = (CreatedResult)result.Result;
        var value = (SpeedScheduleResource)created.Value;
        Assert.That(value.Id, Is.EqualTo(42));
        Assert.That(value.Name, Is.EqualTo("Valid Schedule"));
    }

    [Test]
    public void Update_with_null_resource_returns_bad_request()
    {
        var result = _controller.Update(1, null);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Update_with_empty_or_whitespace_name_returns_bad_request(string name)
    {
        var resource = new SpeedScheduleResource
        {
            Name = name,
            Days = 127,
            StartTime = "08:00",
            EndTime = "17:00"
        };

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(128)]
    public void Update_with_invalid_days_returns_bad_request(int days)
    {
        var resource = new SpeedScheduleResource
        {
            Name = "Test",
            Days = days,
            StartTime = "08:00",
            EndTime = "17:00"
        };

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase(-2, 0)]
    [TestCase(0, -2)]
    public void Update_with_invalid_speeds_returns_bad_request(long upload, long download)
    {
        var resource = new SpeedScheduleResource
        {
            Name = "Test",
            Days = 127,
            StartTime = "08:00",
            EndTime = "17:00",
            MaxUploadSpeed = upload,
            MaxDownloadSpeed = download
        };

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase(null, "17:00")]
    [TestCase("invalid", "17:00")]
    public void Update_with_invalid_times_returns_bad_request(string startTime, string endTime)
    {
        var existing = new SpeedSchedule { Id = 1, Name = "Existing" };
        _speedScheduler.Get(1).Returns(existing);

        var resource = new SpeedScheduleResource
        {
            Name = "Test",
            Days = 127,
            StartTime = startTime,
            EndTime = endTime,
            MaxUploadSpeed = 1000,
            MaxDownloadSpeed = 1000
        };

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void Update_non_existing_returns_not_found()
    {
        _speedScheduler.Get(99).Returns((SpeedSchedule)null);

        var resource = new SpeedScheduleResource
        {
            Name = "Valid",
            Days = 127,
            StartTime = "08:00",
            EndTime = "17:00"
        };

        var result = _controller.Update(99, resource);

        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void Update_valid_returns_updated()
    {
        var existing = new SpeedSchedule { Id = 1, Name = "Existing" };
        _speedScheduler.Get(1).Returns(existing);
        _speedScheduler.Update(Arg.Any<SpeedSchedule>()).Returns(callInfo => callInfo.Arg<SpeedSchedule>());

        var resource = new SpeedScheduleResource
        {
            Name = "Updated Name",
            Days = 127,
            StartTime = "09:00",
            EndTime = "18:00",
            MaxUploadSpeed = 500,
            MaxDownloadSpeed = 500,
            IsEnabled = true,
            Priority = 2
        };

        var result = _controller.Update(1, resource);

        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value.Name, Is.EqualTo("Updated Name"));
        Assert.That(result.Value.StartTime, Is.EqualTo("09:00"));
        Assert.That(result.Value.EndTime, Is.EqualTo("18:00"));
    }
}
