using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.SignalR;
using Seedarr.Api.V1.Automation;

namespace Seedarr.Api.V1.Test.Automation;

[TestFixture]
public class AutomationControllerTest
{
    private IAutomationService _automationService;
    private IAutomationMarketplaceService _marketplaceService;
    private IBroadcastSignalRMessage _signalRBroadcaster;
    private AutomationController _controller;

    [SetUp]
    public void SetUp()
    {
        _automationService = Substitute.For<IAutomationService>();
        _marketplaceService = Substitute.For<IAutomationMarketplaceService>();
        _signalRBroadcaster = Substitute.For<IBroadcastSignalRMessage>();
        _controller = new AutomationController(_automationService, _marketplaceService, _signalRBroadcaster);
    }

    [Test]
    public void Update_with_id_route_param_and_valid_existing_script_returns_ok()
    {
        var existing = new AutomationScript
        {
            Id = 1,
            Name = "Existing Script",
            Language = AutomationLanguage.Yaml,
            Code = "name: Test"
        };
        _automationService.Get(1).Returns(existing);
        _automationService.Update(Arg.Any<AutomationScript>()).Returns(callInfo => callInfo.Arg<AutomationScript>());

        var resource = new AutomationScriptResource
        {
            Id = 1,
            Name = "Updated Script",
            Language = AutomationLanguage.Yaml,
            Code = "name: Updated"
        };

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var returned = okResult.Value as AutomationScriptResource;
        Assert.That(returned, Is.Not.Null);
        Assert.That(returned.Id, Is.EqualTo(1));
        Assert.That(returned.Name, Is.EqualTo("Updated Script"));
        _automationService.Received(1).Update(Arg.Is<AutomationScript>(s => s.Id == 1 && s.Name == "Updated Script"));
    }

    [Test]
    public void Update_with_id_route_param_when_resource_id_is_zero_assigns_id_and_returns_ok()
    {
        var existing = new AutomationScript
        {
            Id = 10,
            Name = "Existing Script",
            Language = AutomationLanguage.Yaml,
            Code = "name: Test"
        };
        _automationService.Get(10).Returns(existing);
        _automationService.Update(Arg.Any<AutomationScript>()).Returns(callInfo => callInfo.Arg<AutomationScript>());

        var resource = new AutomationScriptResource
        {
            Id = 0,
            Name = "Updated Script",
            Language = AutomationLanguage.Yaml,
            Code = "name: Updated"
        };

        var result = _controller.Update(10, resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var returned = okResult.Value as AutomationScriptResource;
        Assert.That(returned, Is.Not.Null);
        Assert.That(returned.Id, Is.EqualTo(10));
        _automationService.Received(1).Update(Arg.Is<AutomationScript>(s => s.Id == 10 && s.Name == "Updated Script"));
    }

    [Test]
    public void Update_with_id_route_param_when_script_does_not_exist_returns_not_found()
    {
        _automationService.Get(999).Returns((AutomationScript)null);

        var resource = new AutomationScriptResource
        {
            Id = 999,
            Name = "NonExistent Script"
        };

        var result = _controller.Update(999, resource);

        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
        var notFoundResult = (NotFoundObjectResult)result.Result;
        Assert.That(notFoundResult.StatusCode, Is.EqualTo(404));
        Assert.That(notFoundResult.Value, Is.EqualTo("Automation script not found"));
        _automationService.DidNotReceive().Update(Arg.Any<AutomationScript>());
    }

    [Test]
    public void Update_with_id_mismatch_returns_bad_request()
    {
        var resource = new AutomationScriptResource
        {
            Id = 2,
            Name = "Mismatched Script"
        };

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.StatusCode, Is.EqualTo(400));
        Assert.That(badRequest.Value, Does.Contain("ID mismatch"));
        _automationService.DidNotReceive().Update(Arg.Any<AutomationScript>());
    }

    [Test]
    public void Update_without_route_id_with_valid_existing_script_returns_ok()
    {
        var existing = new AutomationScript
        {
            Id = 5,
            Name = "Existing Script"
        };
        _automationService.Get(5).Returns(existing);
        _automationService.Update(Arg.Any<AutomationScript>()).Returns(callInfo => callInfo.Arg<AutomationScript>());

        var resource = new AutomationScriptResource
        {
            Id = 5,
            Name = "Updated Without Route ID"
        };

        var result = _controller.Update(resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _automationService.Received(1).Update(Arg.Is<AutomationScript>(s => s.Id == 5));
    }

    [Test]
    public void Update_without_route_id_when_script_does_not_exist_returns_not_found()
    {
        _automationService.Get(999).Returns((AutomationScript)null);

        var resource = new AutomationScriptResource
        {
            Id = 999,
            Name = "NonExistent Script"
        };

        var result = _controller.Update(resource);

        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
        var notFoundResult = (NotFoundObjectResult)result.Result;
        Assert.That(notFoundResult.StatusCode, Is.EqualTo(404));
        Assert.That(notFoundResult.Value, Is.EqualTo("Automation script not found"));
        _automationService.DidNotReceive().Update(Arg.Any<AutomationScript>());
    }

    [Test]
    public void Update_with_null_resource_returns_bad_request()
    {
        var result = _controller.Update(1, null!);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        _automationService.DidNotReceive().Update(Arg.Any<AutomationScript>());
    }

    [Test]
    public void Update_with_empty_name_returns_bad_request()
    {
        var existing = new AutomationScript
        {
            Id = 1,
            Name = "Existing Script"
        };
        _automationService.Get(1).Returns(existing);

        var resource = new AutomationScriptResource
        {
            Id = 1,
            Name = "   "
        };

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Script name is required."));
        _automationService.DidNotReceive().Update(Arg.Any<AutomationScript>());
    }

    [Test]
    public void Update_with_partial_json_body_preserves_omitted_fields()
    {
        var createdAt = new DateTime(2020, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var existing = new AutomationScript
        {
            Id = 1,
            Name = "Original",
            Trigger = AutomationTrigger.TorrentCompleted,
            Language = AutomationLanguage.JavaScript,
            Code = "console.log('keep');",
            IsEnabled = true,
            TargetCategories = new List<string> { "movies" },
            TargetTagIds = new List<int> { 42 },
            CreatedAt = createdAt,
            LastExecutionStatus = "Success",
        };
        _automationService.Get(1).Returns(existing);
        _automationService.Update(Arg.Any<AutomationScript>()).Returns(callInfo => callInfo.Arg<AutomationScript>());

        using var document = JsonDocument.Parse("{\"id\":1,\"name\":\"Renamed only\"}");
        var result = _controller.Update(document.RootElement, 1);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _automationService.Received(1).Update(Arg.Is<AutomationScript>(script =>
            script.Id == 1
            && script.Name == "Renamed only"
            && script.Code == "console.log('keep');"
            && script.Trigger == AutomationTrigger.TorrentCompleted
            && script.Language == AutomationLanguage.JavaScript
            && script.IsEnabled
            && script.TargetCategories.Count == 1
            && script.TargetCategories[0] == "movies"
            && script.TargetTagIds.Count == 1
            && script.TargetTagIds[0] == 42
            && script.CreatedAt == createdAt
            && script.LastExecutionStatus == "Success"));
    }

    [Test]
    public void Update_with_list_preview_lastExecutionLog_does_not_persist_truncation()
    {
        var fullLog = new string('x', AutomationController.MaxListLogPreviewCharacters + 200);
        var truncatedPreview = string.Concat(fullLog.AsSpan(0, AutomationController.MaxListLogPreviewCharacters), "...");
        var existing = new AutomationScript
        {
            Id = 1,
            Name = "Script",
            Language = AutomationLanguage.Yaml,
            Code = "name: Test",
            LastExecutionLog = fullLog,
            LastExecutionStatus = "Success",
        };
        _automationService.Get(1).Returns(existing);
        _automationService.Update(Arg.Any<AutomationScript>()).Returns(callInfo => callInfo.Arg<AutomationScript>());

        var json = JsonSerializer.Serialize(new
        {
            id = 1,
            name = "Renamed",
            lastExecutionLog = truncatedPreview,
            lastExecutionStatus = "Failed",
        });
        using var document = JsonDocument.Parse(json);
        var result = _controller.Update(document.RootElement, 1);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _automationService.Received(1).Update(Arg.Is<AutomationScript>(script =>
            script.Name == "Renamed"
            && script.LastExecutionLog == fullLog
            && script.LastExecutionStatus == "Success"));
    }

    [Test]
    public void Update_with_full_resource_body_preserves_existing_lastExecutionLog()
    {
        var fullLog = new string('y', AutomationController.MaxListLogPreviewCharacters + 50);
        var truncatedPreview = string.Concat(fullLog.AsSpan(0, AutomationController.MaxListLogPreviewCharacters), "...");
        var existing = new AutomationScript
        {
            Id = 2,
            Name = "Script",
            Language = AutomationLanguage.Yaml,
            Code = "name: Test",
            LastExecutionLog = fullLog,
        };
        _automationService.Get(2).Returns(existing);
        _automationService.Update(Arg.Any<AutomationScript>()).Returns(callInfo => callInfo.Arg<AutomationScript>());

        var resource = new AutomationScriptResource
        {
            Id = 2,
            Name = "Updated",
            Language = AutomationLanguage.Yaml,
            Code = "name: Updated",
            LastExecutionLog = truncatedPreview,
        };

        var result = _controller.Update(2, resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _automationService.Received(1).Update(Arg.Is<AutomationScript>(script =>
            script.LastExecutionLog == fullLog && script.Name == "Updated"));
    }

    [Test]
    public void Delete_returns_NotFound_when_script_does_not_exist()
    {
        _automationService.Get(999).Returns((AutomationScript)null);

        var result = _controller.Delete(999);

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
        _automationService.DidNotReceive().Delete(Arg.Any<int>());
    }

    [Test]
    public void Delete_returns_ok_and_deletes_when_script_exists()
    {
        _automationService.Get(1).Returns(new AutomationScript { Id = 1, Name = "Script" });

        var result = _controller.Delete(1);

        Assert.That(result, Is.InstanceOf<OkResult>());
        _automationService.Received(1).Delete(1);
    }
}
