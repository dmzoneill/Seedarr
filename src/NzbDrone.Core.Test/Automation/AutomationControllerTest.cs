using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.SignalR;
using Seedarr.Api.V1.Automation;

namespace NzbDrone.Core.Test.Automation;

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
}
