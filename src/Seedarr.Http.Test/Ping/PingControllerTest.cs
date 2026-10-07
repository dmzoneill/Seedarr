using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using Seedarr.Http.Ping;

namespace Seedarr.Http.Test.Ping;

[TestFixture]
public class PingControllerTest
{
    private IBasicRepository<ConfigModel> _configRepository;
    private PingController _controller;

    [SetUp]
    public void SetUp()
    {
        PingController.ResetProbeCacheForTesting();
        _configRepository = Substitute.For<IBasicRepository<ConfigModel>>();
        _controller = new PingController(_configRepository)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Test]
    public void Ping_returns_ok_when_config_store_is_readable()
    {
        _configRepository.All().Returns(new List<ConfigModel> { new() { Key = "InstanceName", Value = "Seedarr" } });

        var result = _controller.Ping();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result.Result;
        Assert.That(((PingResource)ok.Value).Status, Is.EqualTo("OK"));
    }

    [Test]
    public void Ping_returns_500_error_when_config_store_probe_fails()
    {
        _configRepository.All().Returns(_ => throw new InvalidOperationException("database unavailable"));

        var result = _controller.Ping();

        Assert.That(result.Result, Is.InstanceOf<ObjectResult>());
        var error = (ObjectResult)result.Result;
        Assert.That(error.StatusCode, Is.EqualTo(500));
        Assert.That(((PingResource)error.Value).Status, Is.EqualTo("Error"));
    }

    [Test]
    public void Ping_head_returns_500_without_json_body_when_probe_fails()
    {
        _configRepository.All().Returns(_ => throw new InvalidOperationException("database unavailable"));
        _controller.HttpContext.Request.Method = HttpMethods.Head;

        var result = _controller.Ping();

        Assert.That(result.Result, Is.InstanceOf<StatusCodeResult>());
        Assert.That(((StatusCodeResult)result.Result).StatusCode, Is.EqualTo(500));
    }

    [Test]
    public void Ping_head_returns_ok_when_config_store_is_readable()
    {
        _configRepository.All().Returns(Array.Empty<ConfigModel>());
        _controller.HttpContext.Request.Method = HttpMethods.Head;

        var result = _controller.Ping();

        Assert.That(result.Result, Is.InstanceOf<OkResult>());
    }
}
