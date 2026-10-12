using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Api.V1.Config;

namespace NzbDrone.Core.Test.Config;

[TestFixture]
public class AdvancedConfigControllerTest
{
    private IConfigService _configService;
    private AdvancedConfigController _controller;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.UiRefreshRateSec.Returns(5);
        _configService.LogToFile.Returns(true);
        _configService.FileLogLevel.Returns("Info");
        _configService.DebugMode.Returns(false);
        _controller = new AdvancedConfigController(_configService);
    }

    [Test]
    public void GetConfig_returns_resource_with_id_one()
    {
        var resource = _controller.GetConfig();

        Assert.That(resource.Id, Is.EqualTo(1));
        Assert.That(resource.UiRefreshRateSec, Is.EqualTo(5));
    }

    [Test]
    public void GetConfigById_returns_not_found_for_invalid_id()
    {
        var result = _controller.GetConfigById(99);

        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void SaveConfig_rejects_invalid_resource_id()
    {
        var resource = new AdvancedConfigResource { Id = 42, UiRefreshRateSec = 5 };

        var result = _controller.SaveConfig(null, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        _configService.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public void SaveConfig_rejects_route_id_mismatch()
    {
        var resource = new AdvancedConfigResource { Id = 1, UiRefreshRateSec = 5 };

        var result = _controller.SaveConfig(2, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void SaveConfig_validation_failure_returns_bad_request()
    {
        var resource = new AdvancedConfigResource { UiRefreshRateSec = 0 };

        var result = _controller.SaveConfig(null, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void SaveConfig_argument_exception_returns_bad_request_message()
    {
        _configService
            .When(s => s.SaveConfigDictionary(Arg.Any<Dictionary<string, object>>()))
            .Do(_ => throw new ArgumentException("bad key"));

        var resource = new AdvancedConfigResource { UiRefreshRateSec = 10 };
        var result = _controller.SaveConfig(null, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void SaveConfig_unexpected_exception_returns_problem()
    {
        _configService
            .When(s => s.SaveConfigDictionary(Arg.Any<Dictionary<string, object>>()))
            .Do(_ => throw new InvalidOperationException("disk full"));

        var resource = new AdvancedConfigResource { UiRefreshRateSec = 10 };
        var result = _controller.SaveConfig(null, resource);

        Assert.That(result.Result, Is.InstanceOf<ObjectResult>());
        var objectResult = (ObjectResult)result.Result;
        Assert.That(objectResult.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public void SaveConfig_success_returns_accepted()
    {
        var resource = new AdvancedConfigResource { UiRefreshRateSec = 15, DebugMode = true };

        var result = _controller.SaveConfig(null, resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        _configService.Received(1).SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }
}
