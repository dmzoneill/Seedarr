// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Developer.GitHub;
using NzbDrone.Core.Developer.Quality;
using NzbDrone.Core.Developer.Uml;
using Seedarr.Api.V1.System;

namespace NzbDrone.Core.Test.Developer;

[TestFixture]
public class SystemDeveloperControllerTest
{
    private IDeveloperUmlService _umlService;
    private IDeveloperGitHubService _gitHubService;
    private IDeveloperQualityService _qualityService;
    private SystemDeveloperController _controller;

    [SetUp]
    public void SetUp()
    {
        _umlService = Substitute.For<IDeveloperUmlService>();
        _gitHubService = Substitute.For<IDeveloperGitHubService>();
        _qualityService = Substitute.For<IDeveloperQualityService>();

        _controller = new SystemDeveloperController(
            umlService: _umlService,
            gitHubService: _gitHubService,
            qualityService: _qualityService);
    }

    [Test]
    public void GetUmlDiagram_should_invoke_service_and_return_ok()
    {
        var expectedResult = new DeveloperUmlDiagramResult
        {
            DiagramType = "di",
            Subsystem = "torrents",
            Title = "Test UML",
            MermaidCode = "graph TD\n  A --> B",
        };

        _umlService.GenerateDiagram(Arg.Any<DeveloperUmlOptions>()).Returns(expectedResult);

        var response = _controller.GetUmlDiagram("di", "torrents", true, true);
        var okResult = response.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult.Value, Is.SameAs(expectedResult));
    }

    [Test]
    public void GetUmlSubsystems_should_return_list_of_subsystems()
    {
        var subsystems = new List<string> { "all", "torrents", "network" };
        _umlService.GetAvailableSubsystems().Returns(subsystems);

        var response = _controller.GetUmlSubsystems();
        var okResult = response.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult.Value, Is.EqualTo(subsystems));
    }

    [Test]
    public void GetUmlDiagramTypes_should_return_list_of_diagram_types()
    {
        var types = new List<string> { "class", "di", "state" };
        _umlService.GetAvailableDiagramTypes().Returns(types);

        var response = _controller.GetUmlDiagramTypes();
        var okResult = response.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult.Value, Is.EqualTo(types));
    }

    [Test]
    public async Task GetPullRequests_should_invoke_github_service()
    {
        var expectedList = new DeveloperGitHubListResult<DeveloperPullRequestItem>
        {
            Items = new List<DeveloperPullRequestItem>
            {
                new() { Number = 42, Title = "Feature PR" },
            },
        };

        _gitHubService.GetPullRequestsAsync("open", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedList));

        var response = await _controller.GetPullRequests("open", CancellationToken.None);
        var okResult = response.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult.Value, Is.SameAs(expectedList));
    }

    [Test]
    public async Task GetIssues_should_invoke_github_service()
    {
        var expectedList = new DeveloperGitHubListResult<DeveloperIssueItem>
        {
            Items = new List<DeveloperIssueItem>
            {
                new() { Number = 101, Title = "Bug Report" },
            },
        };

        _gitHubService.GetIssuesAsync("all", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedList));

        var response = await _controller.GetIssues("all", CancellationToken.None);
        var okResult = response.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult.Value, Is.SameAs(expectedList));
    }

    [Test]
    public async Task GetQualityReport_should_invoke_quality_service()
    {
        var expectedReport = new DeveloperQualityReport
        {
            ProjectName = "Seedarr",
            QualityGate = new QualityGateStatus { Status = "OK" },
        };

        _qualityService.GetQualityReportAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedReport));

        var response = await _controller.GetQualityReport(CancellationToken.None);
        var okResult = response.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult.Value, Is.SameAs(expectedReport));
    }

    [Test]
    public void Endpoints_should_handle_null_services_gracefully_with_defaults()
    {
        var defaultController = new SystemDeveloperController();

        var uml = defaultController.GetUmlDiagram("class", "all", false, false);
        Assert.That(uml.Result, Is.TypeOf<OkObjectResult>());

        var subs = defaultController.GetUmlSubsystems();
        Assert.That(subs.Result, Is.TypeOf<OkObjectResult>());

        var types = defaultController.GetUmlDiagramTypes();
        Assert.That(types.Result, Is.TypeOf<OkObjectResult>());
    }
}
