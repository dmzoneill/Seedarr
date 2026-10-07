using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Torrents;
using NzbDrone.SignalR;
using Seedarr.Api.V1.Automation;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class AutomationControllerTest
{
    private IAutomationService _automationService;
    private IAutomationMarketplaceService _marketplaceService;
    private ITorrentService _torrentService;
    private IBroadcastSignalRMessage _signalRBroadcaster;
    private AutomationController _controller;

    [SetUp]
    public void SetUp()
    {
        _automationService = Substitute.For<IAutomationService>();
        _marketplaceService = Substitute.For<IAutomationMarketplaceService>();
        _torrentService = Substitute.For<ITorrentService>();
        _signalRBroadcaster = Substitute.For<IBroadcastSignalRMessage>();
        _controller = new AutomationController(_automationService, _marketplaceService, _torrentService, _signalRBroadcaster);
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
    public void Controller_should_have_Authorize_Reader_attribute()
    {
        var type = typeof(AutomationController);
        var attr = type.GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault() as AuthorizeAttribute;

        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Policy, Is.EqualTo(Policies.Reader));
    }

    [TestCase(nameof(AutomationController.Create))]
    [TestCase(nameof(AutomationController.Delete))]
    [TestCase(nameof(AutomationController.Run))]
    [TestCase(nameof(AutomationController.Test))]
    [TestCase(nameof(AutomationController.InstallMarketplaceTemplate))]
    public void Admin_endpoints_should_have_Authorize_AdminOnly_attribute(string methodName)
    {
        var method = typeof(AutomationController).GetMethods().FirstOrDefault(m => m.Name == methodName);
        Assert.That(method, Is.Not.Null);

        var attr = method.GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault() as AuthorizeAttribute;
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Policy, Is.EqualTo(Policies.AdminOnly));
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

    [Test]
    public void Run_with_missing_script_id_returns_not_found()
    {
        _automationService.Get(999999).Returns((AutomationScript)null);

        var result = _controller.Run(999999);

        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
        _automationService.DidNotReceive().ExecuteScript(Arg.Any<int>(), Arg.Any<int?>());
        _signalRBroadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public void Run_with_missing_torrent_id_returns_not_found()
    {
        _automationService.Get(1).Returns(new AutomationScript { Id = 1, Name = "Script" });
        _torrentService.Get(99999).Returns((Torrent)null);

        var result = _controller.Run(1, 99999);

        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
        _automationService.DidNotReceive().ExecuteScript(Arg.Any<int>(), Arg.Any<int?>());
    }

    [Test]
    public void Test_with_missing_torrent_id_returns_not_found()
    {
        _torrentService.Get(99999).Returns((Torrent)null);

        var result = _controller.Test(new AutomationTestRequestResource
        {
            Script = new AutomationScriptResource { Name = "Test", Code = "console.log(1);" },
            TorrentId = 99999,
        });

        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
        _automationService.DidNotReceive().TestScript(
            Arg.Any<AutomationScript>(),
            Arg.Any<int?>(),
            Arg.Any<Dictionary<string, object>>());
    }
}
