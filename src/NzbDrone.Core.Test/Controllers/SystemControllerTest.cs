using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;
using Seedarr.Api.V1.System;

namespace NzbDrone.Core.Test.Controllers;

[TestFixture]
public class SystemControllerTest
{
    private ITaskManager _taskManager;
    private IManageCommandQueue _commandQueueManager;
    private IAppFolderInfo _appFolderInfo;
    private IHostApplicationLifetime _lifetime;
    private IConfigService _configService;
    private SystemController _controller;

    [SetUp]
    public void SetUp()
    {
        _taskManager = Substitute.For<ITaskManager>();
        _commandQueueManager = Substitute.For<IManageCommandQueue>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _lifetime = Substitute.For<IHostApplicationLifetime>();
        _configService = Substitute.For<IConfigService>();

        _controller = new SystemController(
            _taskManager,
            new List<IScheduledTask>(),
            _commandQueueManager,
            _appFolderInfo,
            _lifetime,
            _configService);
    }

    [Test]
    public void Restart_has_Authorize_Admin_attribute()
    {
        var method = typeof(SystemController).GetMethod(nameof(SystemController.Restart));
        Assert.That(method, Is.Not.Null);

        var attr = method.GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault() as AuthorizeAttribute;
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Roles, Is.EqualTo("Admin"));
    }

    [Test]
    public void Shutdown_has_Authorize_Admin_attribute()
    {
        var method = typeof(SystemController).GetMethod(nameof(SystemController.Shutdown));
        Assert.That(method, Is.Not.Null);

        var attr = method.GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault() as AuthorizeAttribute;
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Roles, Is.EqualTo("Admin"));
    }

    [Test]
    public void Restart_returns_forbid_when_authenticated_user_not_in_admin_role()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "regularUser"),
            new Claim(ClaimTypes.Role, "User")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        var result = _controller.Restart();

        Assert.That(result, Is.InstanceOf<ForbidResult>());
    }

    [Test]
    public void Shutdown_returns_forbid_when_authenticated_user_not_in_admin_role()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "regularUser"),
            new Claim(ClaimTypes.Role, "User")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        var result = _controller.Shutdown();

        Assert.That(result, Is.InstanceOf<ForbidResult>());
    }

    [Test]
    public void Restart_returns_ok_when_user_is_admin()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "adminUser"),
            new Claim(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        var result = _controller.Restart();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public void Shutdown_returns_ok_when_user_is_admin()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "adminUser"),
            new Claim(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        var result = _controller.Shutdown();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }
}
