// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Developer.Testing;
using Seedarr.Api.V1.System;

namespace NzbDrone.Core.Test.Developer;

[TestFixture]
public class DeveloperTestingControllerTest
{
    private IDeveloperTestRunner _testRunner;
    private SystemDeveloperTestingController _controllerWithRunner;
    private SystemDeveloperTestingController _controllerWithoutRunner;

    [SetUp]
    public void SetUp()
    {
        _testRunner = Substitute.For<IDeveloperTestRunner>();
        _controllerWithRunner = new SystemDeveloperTestingController(_testRunner);
        _controllerWithoutRunner = new SystemDeveloperTestingController(null);
    }

    [Test]
    public void GetTests_should_return_tests_from_mock_runner()
    {
        var expectedTests = new List<DeveloperTestItem>
        {
            new()
            {
                Id = "test-1",
                Name = "Test One",
                Category = "Unit",
                Description = "First test",
                TargetComponent = "Torrents",
            },
            new()
            {
                Id = "test-2",
                Name = "Test Two",
                Category = "Integration",
                Description = "Second test",
                TargetComponent = "Network",
            },
        };

        _testRunner.DiscoverTests().Returns(expectedTests);

        var actionResult = _controllerWithRunner.GetTests();
        var okResult = actionResult.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult.Value, Is.SameAs(expectedTests));
    }

    [Test]
    public void GetTests_should_return_empty_list_when_runner_is_null()
    {
        var actionResult = _controllerWithoutRunner.GetTests();

        Assert.That(actionResult.Value, Is.Not.Null);
        Assert.That(actionResult.Value, Is.Empty);
    }

    [Test]
    public async Task RunTests_should_run_tests_request_from_mock_runner()
    {
        var request = new TestExecutionRequest
        {
            TestIds = new List<string> { "test-1", "test-2" },
            Category = "Unit",
            RunAll = false,
        };

        var expectedResponse = new TestExecutionResponse
        {
            TotalTests = 2,
            Passed = 2,
            Failed = 0,
            Skipped = 0,
            TotalDurationMs = 120,
            Results = new List<DeveloperTestResult>
            {
                new() { TestId = "test-1", Status = "Passed", DurationMs = 60 },
                new() { TestId = "test-2", Status = "Passed", DurationMs = 60 },
            },
        };

        _testRunner.RunTestsAsync(request).Returns(Task.FromResult(expectedResponse));

        var actionResult = await _controllerWithRunner.RunTests(request);
        var okResult = actionResult.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult.Value, Is.SameAs(expectedResponse));
    }

    [Test]
    public async Task RunTests_should_pass_default_request_when_request_is_null()
    {
        var expectedResponse = new TestExecutionResponse
        {
            TotalTests = 5,
            Passed = 5,
            Failed = 0,
            Skipped = 0,
            TotalDurationMs = 300,
        };

        _testRunner.RunTestsAsync(Arg.Is<TestExecutionRequest>(r => r.RunAll)).Returns(Task.FromResult(expectedResponse));

        var actionResult = await _controllerWithRunner.RunTests(null);
        var okResult = actionResult.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult.Value, Is.SameAs(expectedResponse));
    }

    [Test]
    public async Task RunTests_should_return_503_when_runner_is_null()
    {
        var actionResult = await _controllerWithoutRunner.RunTests(new TestExecutionRequest());

        var objResult = actionResult.Result as ObjectResult;
        Assert.That(objResult, Is.Not.Null);
        Assert.That(objResult.StatusCode, Is.EqualTo(503));
        Assert.That(objResult.Value, Is.EqualTo("Developer test runner is not available."));
    }

    [Test]
    public async Task RunSingleTest_should_return_test_result_json_from_mock_runner()
    {
        var expectedResult = new DeveloperTestResult
        {
            TestId = "torrent-service-check",
            Name = "Torrent Service Check",
            Category = "Services",
            Status = "Passed",
            DurationMs = 45,
            Output = "All checks passed",
        };

        _testRunner.RunTestAsync("torrent-service-check").Returns(Task.FromResult(expectedResult));

        var actionResult = await _controllerWithRunner.RunSingleTest("torrent-service-check");
        var contentResult = actionResult as ContentResult;

        Assert.That(contentResult, Is.Not.Null);
        Assert.That(contentResult.ContentType, Is.EqualTo("application/json"));

        var deserialized = JsonSerializer.Deserialize<DeveloperTestResult>(contentResult.Content);
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized.TestId, Is.EqualTo("torrent-service-check"));
        Assert.That(deserialized.Status, Is.EqualTo("Passed"));
        Assert.That(deserialized.Output, Is.EqualTo("All checks passed"));
    }

    [Test]
    public async Task RunSingleTest_should_return_skipped_json_when_runner_is_null()
    {
        var actionResult = await _controllerWithoutRunner.RunSingleTest("unregistered-test");
        var contentResult = actionResult as ContentResult;

        Assert.That(contentResult, Is.Not.Null);
        Assert.That(contentResult.ContentType, Is.EqualTo("application/json"));

        var deserialized = JsonSerializer.Deserialize<DeveloperTestResult>(contentResult.Content);
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized.TestId, Is.EqualTo("unregistered-test"));
        Assert.That(deserialized.Status, Is.EqualTo("Skipped"));
    }

    [Test]
    public void GetHistory_should_return_recent_results_from_mock_runner()
    {
        var expectedHistory = new List<DeveloperTestResult>
        {
            new() { TestId = "test-1", Status = "Passed", DurationMs = 20 },
            new() { TestId = "test-2", Status = "Failed", DurationMs = 30, ErrorMessage = "Failed asserting" },
        };

        _testRunner.GetRecentResults(25).Returns(expectedHistory);

        var actionResult = _controllerWithRunner.GetHistory(25);
        var okResult = actionResult.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult.Value, Is.SameAs(expectedHistory));
    }

    [Test]
    public void GetHistory_should_return_empty_list_when_runner_is_null()
    {
        var actionResult = _controllerWithoutRunner.GetHistory(50);

        Assert.That(actionResult.Value, Is.Not.Null);
        Assert.That(actionResult.Value, Is.Empty);
    }

    [Test]
    public void ClearHistory_should_call_ClearHistory_on_mock_runner()
    {
        var actionResult = _controllerWithRunner.ClearHistory();

        Assert.That(actionResult, Is.InstanceOf<NoContentResult>());
        _testRunner.Received(1).ClearHistory();
    }

    [Test]
    public void ClearHistory_should_handle_null_runner_safely()
    {
        var actionResult = _controllerWithoutRunner.ClearHistory();

        Assert.That(actionResult, Is.InstanceOf<NoContentResult>());
    }
}
