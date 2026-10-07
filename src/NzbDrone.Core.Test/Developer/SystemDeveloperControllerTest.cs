// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Developer;
using NzbDrone.Core.Developer.GitHub;
using NzbDrone.Core.Developer.Quality;
using NzbDrone.Core.Developer.Uml;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Telemetry;
using Seedarr.Api.V1.System;

namespace NzbDrone.Core.Test.Developer;

[TestFixture]
public class SystemDeveloperControllerTest
{
    private IDeveloperUmlService _umlService;
    private IDeveloperGitHubService _gitHubService;
    private IDeveloperQualityService _qualityService;
    private IDeveloperEventStore _eventStore;
    private IDeveloperHttpTrafficStore _httpTrafficStore;
    private IDeveloperWebhookStore _webhookStore;
    private IManageCommandQueue _commandQueue;
    private IMainDatabase _mainDatabase;
    private ISystemResourceService _resourceService;
    private SystemDeveloperController _controller;

    [SetUp]
    public void SetUp()
    {
        _umlService = Substitute.For<IDeveloperUmlService>();
        _gitHubService = Substitute.For<IDeveloperGitHubService>();
        _qualityService = Substitute.For<IDeveloperQualityService>();
        _eventStore = Substitute.For<IDeveloperEventStore>();
        _httpTrafficStore = Substitute.For<IDeveloperHttpTrafficStore>();
        _webhookStore = Substitute.For<IDeveloperWebhookStore>();
        _commandQueue = Substitute.For<IManageCommandQueue>();
        _mainDatabase = Substitute.For<IMainDatabase>();
        _resourceService = Substitute.For<ISystemResourceService>();
        _resourceService.GetTorrentEngineMetrics().Returns(new TorrentEngineMetrics
        {
            IsRunning = true,
            ActiveTorrents = 0,
            TotalDataUploaded = 0,
            TotalDataDownloaded = 0,
            TotalUploadSpeed = 0,
            TotalDownloadSpeed = 0,
        });

        _controller = new SystemDeveloperController(
            eventStore: _eventStore,
            httpTrafficStore: _httpTrafficStore,
            webhookStore: _webhookStore,
            commandQueue: _commandQueue,
            mainDatabase: _mainDatabase,
            umlService: _umlService,
            gitHubService: _gitHubService,
            qualityService: _qualityService,
            resourceService: _resourceService);
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
    public void GetEvents_should_return_recent_events_from_store()
    {
        var entries = new List<DeveloperEventEntry>
        {
            new() { Id = "1", EventName = "TorrentAdded", EventType = "TorrentAddedEvent", SourceNamespace = "Core", PayloadJson = "{}" },
        };
        _eventStore.GetRecentEvents(10, null).Returns(entries);

        var response = _controller.GetEvents(10, null);
        Assert.That(response.Value, Is.Not.Null);
        Assert.That(response.Value.Events.Count, Is.EqualTo(1));
        Assert.That(response.Value.TotalRecorded, Is.EqualTo(1));
    }

    [TestCase(0, 1)]
    [TestCase(-100, 1)]
    [TestCase(5001, 5000)]
    [TestCase(int.MaxValue, 5000)]
    public void GetEvents_should_clamp_limit_before_querying_store(int requested, int expected)
    {
        _eventStore.GetRecentEvents(expected, null).Returns(new List<DeveloperEventEntry>());

        _controller.GetEvents(requested, null);

        _eventStore.Received(1).GetRecentEvents(expected, null);
    }

    [TestCase(0, 1)]
    [TestCase(int.MaxValue, 5000)]
    public void GetHttpTraffic_should_clamp_limit_before_querying_store(int requested, int expected)
    {
        _httpTrafficStore.GetRecent(expected, null).Returns(new List<DeveloperHttpTrafficEntry>());

        _controller.GetHttpTraffic(requested, null);

        _httpTrafficStore.Received(1).GetRecent(expected, null);
    }

    [TestCase(0, 1)]
    [TestCase(int.MaxValue, 5000)]
    public void GetWebhookHistory_should_clamp_limit_before_querying_store(int requested, int expected)
    {
        _webhookStore.GetRecent(expected).Returns(new List<DeveloperWebhookEntry>());

        _controller.GetWebhookHistory(requested);

        _webhookStore.Received(1).GetRecent(expected);
    }

    [Test]
    public void PublishSyntheticEvent_should_record_and_return_created_event()
    {
        var request = new DeveloperPublishEventRequest
        {
            EventName = "CustomTestEvent",
            PayloadJson = "{\"test\": true}",
        };

        var response = _controller.PublishSyntheticEvent(request);
        var okResult = response.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var badResp = _controller.PublishSyntheticEvent(new DeveloperPublishEventRequest { EventName = "" });
        Assert.That(badResp.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void ClearEvents_should_invoke_store_clear()
    {
        var result = _controller.ClearEvents();
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _eventStore.Received().Clear();
    }

    [Test]
    public void GetCommands_should_return_command_descriptors()
    {
        var response = _controller.GetCommands();
        Assert.That(response.Value, Is.Not.Null);
        Assert.That(response.Value.Commands, Is.Not.Null);
    }

    [Test]
    public void ExecuteCommand_should_validate_command_name()
    {
        var badResp = _controller.ExecuteCommand(new DeveloperCommandExecuteRequest { CommandName = "" });
        Assert.That(badResp.Result, Is.InstanceOf<BadRequestObjectResult>());

        var missingResp = _controller.ExecuteCommand(new DeveloperCommandExecuteRequest { CommandName = "NonExistentCommandXYZ" });
        Assert.That(missingResp.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void GetHttpTraffic_should_return_traffic_items()
    {
        var items = new List<DeveloperHttpTrafficEntry>
        {
            new() { Id = "1", Method = "GET", Url = "https://example.com", StatusCode = 200, DurationMs = 45, IsSuccess = true },
        };
        _httpTrafficStore.GetRecent(50, null).Returns(items);

        var response = _controller.GetHttpTraffic(50, null);
        Assert.That(response.Value, Is.Not.Null);
        Assert.That(response.Value.Items.Count, Is.EqualTo(1));
    }

    [Test]
    public void ClearHttpTraffic_should_invoke_traffic_store_clear()
    {
        var result = _controller.ClearHttpTraffic();
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _httpTrafficStore.Received().Clear();
    }

    [Test]
    public void Webhook_endpoints_should_return_templates_history_and_simulation()
    {
        _webhookStore.GetRecent(Arg.Any<int>()).Returns(new List<DeveloperWebhookEntry>());

        var templates = _controller.GetWebhookTemplates();
        Assert.That(templates.Value, Is.Not.Null);
        Assert.That(templates.Value.Count, Is.GreaterThanOrEqualTo(2));

        var history = _controller.GetWebhookHistory(10);
        Assert.That(history.Value, Is.Not.Null);

        var simBad = _controller.SimulateWebhook(new DeveloperWebhookSimulateRequest { PayloadJson = "" });
        Assert.That(simBad.Result, Is.InstanceOf<BadRequestObjectResult>());

        var simInvalidJson = _controller.SimulateWebhook(new DeveloperWebhookSimulateRequest { PayloadJson = "invalid-json" });
        Assert.That(simInvalidJson.Result, Is.InstanceOf<BadRequestObjectResult>());

        var simOk = _controller.SimulateWebhook(new DeveloperWebhookSimulateRequest
        {
            EventType = "Grab",
            PayloadJson = "{\"eventType\": \"Grab\", \"series\": {\"title\": \"Show\"}}",
        });
        Assert.That(simOk.Result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public void GetConfiguration_should_return_configuration_matrix()
    {
        var response = _controller.GetConfiguration(unmask: false);
        Assert.That(response.Value, Is.Not.Null);
        Assert.That(response.Value.Environment.EnvironmentVariables, Is.Not.Null);

        var unmasked = _controller.GetConfiguration(unmask: true);
        Assert.That(unmasked.Value, Is.Not.Null);
    }

    [Test]
    public void Simulation_endpoint_should_return_live_engine_metrics()
    {
        _resourceService.GetTorrentEngineMetrics().Returns(new TorrentEngineMetrics
        {
            IsRunning = true,
            ActiveTorrents = 2,
            TotalDataUploaded = 4096,
            TotalDataDownloaded = 2048,
            TotalUploadSpeed = 512,
            TotalDownloadSpeed = 128,
        });

        var sim = _controller.GetSimulation();
        Assert.That(sim.Value, Is.Not.Null);
        Assert.That(sim.Value.IsRunning, Is.True);
        Assert.That(sim.Value.ActiveSimulatedTorrents, Is.EqualTo(2));
        Assert.That(sim.Value.TotalUploadedBytes, Is.EqualTo(4096));
        Assert.That(sim.Value.TotalDownloadedBytes, Is.EqualTo(2048));
        Assert.That(sim.Value.CurrentUploadRateBytesPerSec, Is.EqualTo(512));
        Assert.That(sim.Value.CurrentDownloadRateBytesPerSec, Is.EqualTo(128));
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

        var ev = defaultController.GetEvents();
        Assert.That(ev.Value, Is.Not.Null);

        var tr = defaultController.GetHttpTraffic();
        Assert.That(tr.Value, Is.Not.Null);

        var wh = defaultController.GetWebhookHistory();
        Assert.That(wh.Value, Is.Not.Null);

        var sim = defaultController.GetSimulation();
        Assert.That(sim.Value, Is.Not.Null);
        Assert.That(sim.Value.IsRunning, Is.False);
        Assert.That(sim.Value.ActiveSimulatedTorrents, Is.EqualTo(0));
    }
}
