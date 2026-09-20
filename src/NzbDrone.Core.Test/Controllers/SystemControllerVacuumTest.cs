using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;
using Seedarr.Api.V1.System;

namespace NzbDrone.Core.Test.Controllers;

[TestFixture]
public class SystemControllerVacuumTest
{
    private ITaskManager _taskManager;
    private IManageCommandQueue _commandQueueManager;
    private IAppFolderInfo _appFolderInfo;
    private IHostApplicationLifetime _lifetime;
    private IConfigService _configService;
    private IMainDatabase _mainDatabase;
    private IDatabaseMaintenanceService _maintenanceService;
    private SystemController _controller;

    [SetUp]
    public void SetUp()
    {
        _taskManager = Substitute.For<ITaskManager>();
        _commandQueueManager = Substitute.For<IManageCommandQueue>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _lifetime = Substitute.For<IHostApplicationLifetime>();
        _configService = Substitute.For<IConfigService>();
        _mainDatabase = Substitute.For<IMainDatabase>();
        _maintenanceService = Substitute.For<IDatabaseMaintenanceService>();

        _controller = new SystemController(
            _taskManager,
            new List<IScheduledTask>(),
            _commandQueueManager,
            _appFolderInfo,
            _lifetime,
            _configService,
            _mainDatabase,
            null,
            null,
            _maintenanceService);
    }

    [Test]
    public void VacuumDatabase_should_return_ok_with_reclaimed_statistics()
    {
        _maintenanceService.PerformMaintenance(5000).Returns(new DatabaseMaintenanceResult
        {
            Success = true,
            DatabaseType = "SQLite",
            InitialFreelistPages = 1500,
            FinalFreelistPages = 500,
            ReclaimedPages = 1000,
            PageSize = 4096,
            ReclaimedBytes = 1000 * 4096,
            Message = "Reclaimed 1000 pages (4,096,000 bytes)."
        });

        var result = _controller.VacuumDatabase(null, 5000);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var resource = (DatabaseVacuumResource)okResult.Value;

        Assert.That(resource.Success, Is.True);
        Assert.That(resource.DatabaseType, Is.EqualTo("SQLite"));
        Assert.That(resource.InitialFreelistPages, Is.EqualTo(1500));
        Assert.That(resource.FinalFreelistPages, Is.EqualTo(500));
        Assert.That(resource.ReclaimedPages, Is.EqualTo(1000));
        Assert.That(resource.PageSize, Is.EqualTo(4096));
        Assert.That(resource.ReclaimedBytes, Is.EqualTo(4096000));
    }

    [Test]
    public void VacuumDatabase_should_use_max_pages_from_request_body_when_query_param_omitted()
    {
        _maintenanceService.PerformMaintenance(2500).Returns(new DatabaseMaintenanceResult
        {
            Success = true,
            DatabaseType = "SQLite",
            ReclaimedPages = 500
        });

        var request = new DatabaseVacuumRequest { MaxPages = 2500 };
        var result = _controller.VacuumDatabase(request, null);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _maintenanceService.Received(1).PerformMaintenance(2500);
    }

    [Test]
    public void VacuumDatabase_should_return_500_when_maintenance_service_is_null()
    {
        var controllerWithoutService = new SystemController(
            _taskManager,
            new List<IScheduledTask>(),
            _commandQueueManager,
            _appFolderInfo,
            _lifetime,
            _configService,
            _mainDatabase);

        var result = controllerWithoutService.VacuumDatabase();

        Assert.That(result.Result, Is.InstanceOf<ObjectResult>());
        var objectResult = (ObjectResult)result.Result;
        Assert.That(objectResult.StatusCode, Is.EqualTo(500));
        var resource = (DatabaseVacuumResource)objectResult.Value;
        Assert.That(resource.Success, Is.False);
    }
}
