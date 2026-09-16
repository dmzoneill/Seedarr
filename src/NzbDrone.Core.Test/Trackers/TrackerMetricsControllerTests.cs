using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Trackers.Metrics;
using Seedarr.Api.V1.Trackers;

namespace NzbDrone.Core.Test.Trackers;

[TestFixture]
public class TrackerMetricsControllerTests
{
    private ITrackerMetricService _trackerMetricService;
    private TrackerMetricsController _controller;

    [SetUp]
    public void SetUp()
    {
        _trackerMetricService = Substitute.For<ITrackerMetricService>();
        _controller = new TrackerMetricsController(_trackerMetricService);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(-24)]
    public void GetHistory_returns_BadRequest_when_hours_is_zero_or_negative(int hours)
    {
        var result = _controller.GetHistory(1, hours: hours);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Hours must be greater than 0."));
        _trackerMetricService.DidNotReceive().GetHistory(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [TestCase(169)]
    [TestCase(200)]
    [TestCase(1000)]
    public void GetHistory_returns_BadRequest_when_hours_exceeds_168(int hours)
    {
        var result = _controller.GetHistory(1, hours: hours);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Hours cannot exceed 168 (7 days)."));
        _trackerMetricService.DidNotReceive().GetHistory(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(-500)]
    public void GetHistory_returns_BadRequest_when_limit_is_zero_or_negative(int limit)
    {
        var result = _controller.GetHistory(1, hours: 24, limit: limit);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Limit must be between 1 and 2000."));
        _trackerMetricService.DidNotReceive().GetHistory(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [TestCase(2001)]
    [TestCase(5000)]
    public void GetHistory_returns_BadRequest_when_limit_exceeds_2000(int limit)
    {
        var result = _controller.GetHistory(1, hours: 24, limit: limit);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Limit must be between 1 and 2000."));
        _trackerMetricService.DidNotReceive().GetHistory(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Test]
    public void GetHistory_returns_Ok_and_passes_arguments_when_valid()
    {
        var expectedSnapshots = new List<TrackerMetricSnapshot>
        {
            new() { Id = 1, TrackerMetricId = 5, ResponseTimeMs = 45 }
        };

        _trackerMetricService.GetHistory(5, 48, 1000).Returns(expectedSnapshots);

        var result = _controller.GetHistory(5, hours: 48, limit: 1000);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        Assert.That(okResult.Value, Is.SameAs(expectedSnapshots));
        _trackerMetricService.Received(1).GetHistory(5, 48, 1000);
    }

    [Test]
    public void GetHistory_uses_defaults_when_not_specified()
    {
        var expectedSnapshots = new List<TrackerMetricSnapshot>();
        _trackerMetricService.GetHistory(1, 24, 500).Returns(expectedSnapshots);

        var result = _controller.GetHistory(1);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _trackerMetricService.Received(1).GetHistory(1, 24, 500);
    }

    [Test]
    public void GetAll_returns_mapped_resources()
    {
        var metrics = new List<TrackerMetric>
        {
            new() { Id = 1, TrackerUrl = "http://tracker1.test/announce" }
        };
        _trackerMetricService.GetAllMetrics().Returns(metrics);

        var result = _controller.GetAll();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var list = okResult.Value as List<TrackerMetricResource>;
        Assert.That(list, Is.Not.Null);
        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0].Id, Is.EqualTo(1));
    }

    [Test]
    public void Get_returns_NotFound_when_metric_does_not_exist()
    {
        _trackerMetricService.GetMetric(99).Returns((TrackerMetric)null);

        var result = _controller.Get(99);

        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void Get_returns_Ok_when_metric_exists()
    {
        var metric = new TrackerMetric { Id = 42, TrackerUrl = "http://tracker.test/announce" };
        _trackerMetricService.GetMetric(42).Returns(metric);

        var result = _controller.Get(42);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var resource = okResult.Value as TrackerMetricResource;
        Assert.That(resource, Is.Not.Null);
        Assert.That(resource.Id, Is.EqualTo(42));
    }

    [Test]
    public void Reset_calls_service_and_returns_Ok()
    {
        var result = _controller.Reset(10);

        _trackerMetricService.Received(1).ResetMetrics(10);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public void Delete_calls_service_and_returns_Ok()
    {
        var result = _controller.Delete(10);

        _trackerMetricService.Received(1).DeleteMetric(10);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }
}
