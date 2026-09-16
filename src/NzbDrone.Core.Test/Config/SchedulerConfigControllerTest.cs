using System.Collections.Generic;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Api.V1.Config;

namespace NzbDrone.Core.Test.Config;

[TestFixture]
public class SchedulerConfigControllerTest
{
    private IConfigService _configService;
    private SchedulerConfigController _controller;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _controller = new SchedulerConfigController(_configService);
    }

    [TestCase("Mars/Olympus")]
    [TestCase("Invalid/Timezone")]
    [TestCase("not_a_timezone")]
    public void SaveConfig_should_return_bad_request_when_TimeZone_is_invalid(string invalidTimeZone)
    {
        var resource = new SchedulerConfigResource
        {
            SchedulerStartHour = 1,
            SchedulerStartMinute = 0,
            SchedulerEndHour = 2,
            SchedulerEndMinute = 0,
            TimeZone = invalidTimeZone,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var errors = (IList<ValidationFailure>)badRequest.Value;

        Assert.That(errors, Has.Some.Matches<ValidationFailure>(e =>
            e.PropertyName == nameof(SchedulerConfigResource.TimeZone) &&
            e.ErrorMessage.Contains("Invalid TimeZone identifier.")));
    }

    [TestCase("UTC")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void SaveConfig_should_succeed_for_valid_or_empty_timezones(string validOrEmptyTimeZone)
    {
        var resource = new SchedulerConfigResource
        {
            SchedulerStartHour = 1,
            SchedulerStartMinute = 0,
            SchedulerEndHour = 2,
            SchedulerEndMinute = 0,
            TimeZone = validOrEmptyTimeZone,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.Null.Or.Not.InstanceOf<BadRequestObjectResult>());
    }
}
