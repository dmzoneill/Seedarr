using System;
using System.Collections.Generic;
using System.Data;
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
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Instrumentation;
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

    [Test]
    public void GetStatus_reports_PostgreSQL_when_mainDatabase_is_PostgreSQL()
    {
        var mainDatabase = Substitute.For<IMainDatabase>();
        mainDatabase.DatabaseType.Returns(DatabaseType.PostgreSQL);

        var controller = new SystemController(
            _taskManager,
            new List<IScheduledTask>(),
            _commandQueueManager,
            _appFolderInfo,
            _lifetime,
            _configService,
            mainDatabase);

        var actionResult = controller.GetStatus();

        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var status = okResult.Value as SystemResource;
        Assert.That(status, Is.Not.Null);
        Assert.That(status.DatabaseVersion, Is.EqualTo("PostgreSQL"));
    }

    [Test]
    public void GetStatus_reports_SQLite_when_mainDatabase_is_SQLite()
    {
        var mainDatabase = Substitute.For<IMainDatabase>();
        mainDatabase.DatabaseType.Returns(DatabaseType.SQLite);

        var controller = new SystemController(
            _taskManager,
            new List<IScheduledTask>(),
            _commandQueueManager,
            _appFolderInfo,
            _lifetime,
            _configService,
            mainDatabase);

        var actionResult = controller.GetStatus();

        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var status = okResult.Value as SystemResource;
        Assert.That(status, Is.Not.Null);
        Assert.That(status.DatabaseVersion, Is.EqualTo("SQLite"));
    }

    [Test]
    public void GetStatus_reports_SQLite_when_mainDatabase_is_null()
    {
        var actionResult = _controller.GetStatus();

        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var status = okResult.Value as SystemResource;
        Assert.That(status, Is.Not.Null);
        Assert.That(status.DatabaseVersion, Is.EqualTo("SQLite"));
    }

    [Test]
    public void GetStatus_queries_applied_migration_version_dynamically()
    {
        var mainDatabase = Substitute.For<IMainDatabase>();
        var connection = Substitute.For<IDbConnection>();
        var command = Substitute.For<IDbCommand>();

        mainDatabase.OpenConnection().Returns(connection);
        connection.CreateCommand().Returns(command);
        command.ExecuteScalar().Returns(42L);

        var controller = new SystemController(
            _taskManager,
            new List<IScheduledTask>(),
            _commandQueueManager,
            _appFolderInfo,
            _lifetime,
            _configService,
            mainDatabase);

        var actionResult = controller.GetStatus();

        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var status = okResult.Value as SystemResource;
        Assert.That(status, Is.Not.Null);
        Assert.That(status.DatabaseMigration, Is.EqualTo("42"));
        Assert.That(command.CommandText, Does.Contain("VersionInfo"));
    }

    [Test]
    public void GetStatus_falls_back_to_latest_migration_when_query_fails()
    {
        var mainDatabase = Substitute.For<IMainDatabase>();
        mainDatabase.OpenConnection().Returns(_ => throw new InvalidOperationException("Connection failed"));

        var controller = new SystemController(
            _taskManager,
            new List<IScheduledTask>(),
            _commandQueueManager,
            _appFolderInfo,
            _lifetime,
            _configService,
            mainDatabase);

        var actionResult = controller.GetStatus();

        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var status = okResult.Value as SystemResource;
        Assert.That(status, Is.Not.Null);
        Assert.That(status.DatabaseMigration, Is.EqualTo(NzbDroneMigrationBase.LatestMigration.ToString()));
    }

    [Test]
    public void GetStatus_populates_valid_garbage_collection_telemetry()
    {
        var actionResult = _controller.GetStatus();

        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var status = okResult.Value as SystemResource;
        Assert.That(status, Is.Not.Null);
        Assert.That(status.GcGen0Collections, Is.GreaterThanOrEqualTo(0));
        Assert.That(status.GcGen1Collections, Is.GreaterThanOrEqualTo(0));
        Assert.That(status.GcGen2Collections, Is.GreaterThanOrEqualTo(0));
        Assert.That(status.GcTotalAllocatedBytes, Is.GreaterThan(0));
        Assert.That(status.GcHeapSizeBytes, Is.GreaterThan(0));
        Assert.That(status.GcPauseTimePercentage, Is.GreaterThanOrEqualTo(0.0));
    }

    [Test]
    public void GetStatus_populates_file_descriptor_telemetry_from_provider()
    {
        var fdProvider = Substitute.For<IFileDescriptorProvider>();
        fdProvider.GetOpenFileDescriptorCount().Returns(340);
        fdProvider.GetMaxFileDescriptors().Returns(1024);
        fdProvider.GetFileDescriptorUsagePercentage().Returns(33.2);

        var controller = new SystemController(
            _taskManager,
            new List<IScheduledTask>(),
            _commandQueueManager,
            _appFolderInfo,
            _lifetime,
            _configService,
            fileDescriptorProvider: fdProvider);

        var actionResult = controller.GetStatus();

        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var status = okResult.Value as SystemResource;
        Assert.That(status, Is.Not.Null);
        Assert.That(status.OpenFileDescriptors, Is.EqualTo(340));
        Assert.That(status.MaxFileDescriptors, Is.EqualTo(1024));
        Assert.That(status.FileDescriptorUsagePercentage, Is.EqualTo(33.2));
    }

    [Test]
    public void GetStatus_populates_OsArchitecture_and_CommitHash()
    {
        var originalCommit = BuildInfo.CommitHash;
        try
        {
            BuildInfo.CommitHash = "testcommithash123";
            var actionResult = _controller.GetStatus();

            var okResult = actionResult.Result as OkObjectResult;
            Assert.That(okResult, Is.Not.Null);
            var status = okResult.Value as SystemResource;
            Assert.That(status, Is.Not.Null);
            Assert.That(status.OsArchitecture, Is.EqualTo(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()));
            Assert.That(status.CommitHash, Is.EqualTo("testcommithash123"));
        }
        finally
        {
            BuildInfo.CommitHash = originalCommit;
        }
    }
}
