using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Api.V1.System;

namespace Seedarr.Api.V1.Test.System;

[TestFixture]
public class SetupControllerTest
{
    private IConfigService _configService;
    private IConfigFileProvider _configFileProvider;
    private SetupController _controller;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _controller = new SetupController(_configService, _configFileProvider);
    }

    [Test]
    public void GetStatus_should_return_correct_setup_and_auth_flags_when_not_completed()
    {
        _configService.IsSetupCompleted.Returns(false);
        _configService.AuthenticationEnabled.Returns(false);
        _configService.GetValue("AdminUsername", string.Empty).Returns(string.Empty);
        _configService.GetValue("AdminPassword", string.Empty).Returns(string.Empty);

        var actionResult = _controller.GetStatus();
        var okResult = actionResult.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        var status = okResult.Value as SetupStatusResource;
        Assert.That(status, Is.Not.Null);
        Assert.That(status.IsSetupCompleted, Is.False);
        Assert.That(status.IsAuthEnabled, Is.False);
        Assert.That(status.HasAdminUser, Is.False);
    }

    [Test]
    public void GetStatus_should_return_correct_setup_and_auth_flags_when_completed()
    {
        _configService.IsSetupCompleted.Returns(true);
        _configService.AuthenticationEnabled.Returns(true);

        var actionResult = _controller.GetStatus();
        var okResult = actionResult.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        var status = okResult.Value as SetupStatusResource;
        Assert.That(status, Is.Not.Null);
        Assert.That(status.IsSetupCompleted, Is.True);
        Assert.That(status.IsAuthEnabled, Is.True);
        Assert.That(status.HasAdminUser, Is.True);
    }

    [Test]
    public void Complete_should_mark_setup_completed_and_enable_auth()
    {
        _configService.IsSetupCompleted.Returns(false);

        var request = new SetupCompleteRequest
        {
            Username = "admin",
            Password = "StrongPassword123!",
        };

        var result = _controller.Complete(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _configService.Received(1).AuthenticationEnabled = true;
        _configService.Received(1).IsSetupCompleted = true;
        _configFileProvider.Received(1).SetIsSetupCompleted(true);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("Short1!")]
    [TestCase("lowercase12345")]
    [TestCase("UPPERCASE12345")]
    [TestCase("LettersOnlyLongEnough")]
    public void Complete_should_reject_weak_passwords(string weakPassword)
    {
        _configService.IsSetupCompleted.Returns(false);

        var request = new SetupCompleteRequest
        {
            Username = "admin",
            Password = weakPassword,
        };

        var result = _controller.Complete(request);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase("StrongPassword1")]
    [TestCase("StrongPassword!")]
    [TestCase("SuperSecret99#")]
    public void Complete_should_accept_strong_passwords(string strongPassword)
    {
        _configService.IsSetupCompleted.Returns(false);

        var request = new SetupCompleteRequest
        {
            Username = "admin",
            Password = strongPassword,
        };

        var result = _controller.Complete(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public void Complete_should_lockdown_endpoint_when_setup_is_already_completed()
    {
        _configService.IsSetupCompleted.Returns(true);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };

        var request = new SetupCompleteRequest
        {
            Username = "admin",
            Password = "StrongPassword123!",
        };

        var result = _controller.Complete(request);

        Assert.That(result, Is.InstanceOf<UnauthorizedObjectResult>());
    }

    [Test]
    public void Complete_should_allow_authenticated_admin_when_setup_is_already_completed()
    {
        _configService.IsSetupCompleted.Returns(true);

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, "admin"),
                new Claim(ClaimTypes.Role, "Admin"),
            },
            "TestAuth");

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
        };

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };

        var request = new SetupCompleteRequest
        {
            Username = "admin",
            Password = "NewStrongPassword123!",
        };

        var result = _controller.Complete(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }
}
