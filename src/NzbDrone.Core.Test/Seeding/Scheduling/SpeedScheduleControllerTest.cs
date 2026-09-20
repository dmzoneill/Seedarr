using System;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Seeding.Scheduling;
using NzbDrone.SignalR;
using Seedarr.Api.V1.Seeding;

namespace NzbDrone.Core.Test.Seeding.Scheduling;

[TestFixture]
public class SpeedScheduleControllerTest
{
    private ISpeedScheduler _speedScheduler;
    private IConfigService _configService;
    private IBroadcastSignalRMessage _signalRBroadcaster;
    private SpeedScheduleController _controller;

    [SetUp]
    public void SetUp()
    {
        _speedScheduler = Substitute.For<ISpeedScheduler>();
        _configService = Substitute.For<IConfigService>();
        _signalRBroadcaster = Substitute.For<IBroadcastSignalRMessage>();
        _controller = new SpeedScheduleController(_speedScheduler, _configService, _signalRBroadcaster);
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

    [Test]
    public void GetActiveLimits_when_schedule_is_active_with_speed_boost_does_not_clamp_to_base_config()
    {
        _configService.AlternativeSpeedEnabled.Returns(false);
        _configService.MaxUploadSpeedKbps.Returns(5000);
        _configService.MaxDownloadSpeedKbps.Returns(10000);

        _speedScheduler.GetCurrentLimits().Returns(new SpeedLimits
        {
            IsScheduleActive = true,
            ActiveScheduleName = "Night Boost",
            MaxUploadSpeed = 20_000_000,
            MaxDownloadSpeed = 50_000_000
        });

        var result = _controller.GetActiveLimits();

        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value.IsScheduleActive, Is.True);
        Assert.That(result.Value.MaxUploadSpeed, Is.EqualTo(20_000_000));
        Assert.That(result.Value.MaxDownloadSpeed, Is.EqualTo(50_000_000));
    }

    [Test]
    public void GetActiveLimits_when_schedule_is_active_and_alternative_speed_enabled_clamps_to_alt_limits()
    {
        _configService.AlternativeSpeedEnabled.Returns(true);
        _configService.AltUploadSpeedKbps.Returns(50);
        _configService.AltDownloadSpeedKbps.Returns(100);

        _speedScheduler.GetCurrentLimits().Returns(new SpeedLimits
        {
            IsScheduleActive = true,
            ActiveScheduleName = "Night Boost",
            MaxUploadSpeed = 20_000_000,
            MaxDownloadSpeed = 50_000_000
        });

        var result = _controller.GetActiveLimits();

        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value.MaxUploadSpeed, Is.EqualTo(50 * 1024));
        Assert.That(result.Value.MaxDownloadSpeed, Is.EqualTo(100 * 1024));
    }

    [Test]
    public void GetActiveLimits_when_schedule_does_not_override_falls_back_to_base_config()
    {
        _configService.AlternativeSpeedEnabled.Returns(false);
        _configService.MaxUploadSpeedKbps.Returns(5000);
        _configService.MaxDownloadSpeedKbps.Returns(10000);

        _speedScheduler.GetCurrentLimits().Returns(new SpeedLimits
        {
            IsScheduleActive = true,
            ActiveScheduleName = "Active Schedule",
            MaxUploadSpeed = SpeedLimits.Unlimited,
            MaxDownloadSpeed = SpeedLimits.Unlimited
        });

        var result = _controller.GetActiveLimits();

        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value.MaxUploadSpeed, Is.EqualTo(5000 * 1024));
        Assert.That(result.Value.MaxDownloadSpeed, Is.EqualTo(10000 * 1024));
    }

    [Test]
    public void GetActiveLimits_when_schedule_is_inactive_applies_base_config()
    {
        _configService.AlternativeSpeedEnabled.Returns(false);
        _configService.MaxUploadSpeedKbps.Returns(5000);
        _configService.MaxDownloadSpeedKbps.Returns(10000);

        _speedScheduler.GetCurrentLimits().Returns(new SpeedLimits
        {
            IsScheduleActive = false,
            MaxUploadSpeed = SpeedLimits.Unlimited,
            MaxDownloadSpeed = SpeedLimits.Unlimited
        });

        var result = _controller.GetActiveLimits();

        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value.MaxUploadSpeed, Is.EqualTo(5000 * 1024));
        Assert.That(result.Value.MaxDownloadSpeed, Is.EqualTo(10000 * 1024));
    }

    [Test]
    public void Create_valid_resource_broadcasts_signalr_message()
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

        _controller.Create(resource);

        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "SpeedSchedule" &&
            m.Action == ModelAction.Created &&
            m.Body is SpeedScheduleResource &&
            ((SpeedScheduleResource)m.Body).Id == 42 &&
            ((SpeedScheduleResource)m.Body).Name == "Valid Schedule"));
    }

    [Test]
    public void Update_valid_resource_broadcasts_signalr_message()
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

        _controller.Update(1, resource);

        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "SpeedSchedule" &&
            m.Action == ModelAction.Updated &&
            m.Body is SpeedScheduleResource &&
            ((SpeedScheduleResource)m.Body).Id == 1 &&
            ((SpeedScheduleResource)m.Body).Name == "Updated Name"));
    }

    [Test]
    public void Delete_existing_schedule_returns_ok_and_broadcasts_signalr_message()
    {
        var existing = new SpeedSchedule { Id = 5, Name = "To Delete" };
        _speedScheduler.Get(5).Returns(existing);

        var result = _controller.Delete(5);

        Assert.That(result, Is.InstanceOf<OkResult>());
        _speedScheduler.Received(1).Delete(5);
        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "SpeedSchedule" &&
            m.Action == ModelAction.Deleted &&
            m.Body is SpeedScheduleResource &&
            ((SpeedScheduleResource)m.Body).Id == 5 &&
            ((SpeedScheduleResource)m.Body).Name == "To Delete"));
    }

    [Test]
    public void Delete_non_existing_returns_not_found_and_does_not_broadcast()
    {
        _speedScheduler.Get(99).Returns((SpeedSchedule)null);

        var result = _controller.Delete(99);

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
        _speedScheduler.DidNotReceive().Delete(Arg.Any<int>());
        _signalRBroadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public void Mutations_without_signalr_broadcaster_do_not_throw()
    {
        var controllerWithoutSignalR = new SpeedScheduleController(_speedScheduler, _configService, null);

        var resource = new SpeedScheduleResource
        {
            Name = "No SignalR",
            Days = 127,
            StartTime = "08:00",
            EndTime = "17:00",
            MaxUploadSpeed = 1000,
            MaxDownloadSpeed = 1000
        };

        _speedScheduler.Add(Arg.Any<SpeedSchedule>()).Returns(callInfo =>
        {
            var model = callInfo.Arg<SpeedSchedule>();
            model.Id = 10;
            return model;
        });

        Assert.DoesNotThrow(() => controllerWithoutSignalR.Create(resource));

        var existing = new SpeedSchedule { Id = 10, Name = "Existing" };
        _speedScheduler.Get(10).Returns(existing);
        _speedScheduler.Update(Arg.Any<SpeedSchedule>()).Returns(callInfo => callInfo.Arg<SpeedSchedule>());

        Assert.DoesNotThrow(() => controllerWithoutSignalR.Update(10, resource));
        Assert.DoesNotThrow(() => controllerWithoutSignalR.Delete(10));
    }
}
