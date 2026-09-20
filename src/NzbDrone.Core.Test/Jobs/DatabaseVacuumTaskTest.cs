using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Jobs;

namespace NzbDrone.Core.Test.Jobs;

[TestFixture]
public class DatabaseVacuumTaskTest
{
    private IDatabaseMaintenanceService _maintenanceService;
    private DatabaseVacuumTask _subject;

    [SetUp]
    public void SetUp()
    {
        _maintenanceService = Substitute.For<IDatabaseMaintenanceService>();
        _subject = new DatabaseVacuumTask(_maintenanceService);
    }

    [Test]
    public void DefaultInterval_should_be_24_hours()
    {
        Assert.That(_subject.DefaultInterval, Is.EqualTo(1440));
    }

    [Test]
    public void Execute_should_perform_maintenance_when_freelist_count_is_positive()
    {
        _maintenanceService.GetFreelistCount().Returns(100L);
        _maintenanceService.IsIncrementalAutoVacuumEnabled().Returns(true);

        _subject.Execute();

        _maintenanceService.Received(1).PerformMaintenance(5000);
    }

    [Test]
    public void Execute_should_perform_maintenance_when_auto_vacuum_is_not_enabled()
    {
        _maintenanceService.GetFreelistCount().Returns(0L);
        _maintenanceService.IsIncrementalAutoVacuumEnabled().Returns(false);

        _subject.Execute();

        _maintenanceService.Received(1).PerformMaintenance(5000);
    }

    [Test]
    public void Execute_should_skip_maintenance_when_freelist_is_zero_and_auto_vacuum_enabled()
    {
        _maintenanceService.GetFreelistCount().Returns(0L);
        _maintenanceService.IsIncrementalAutoVacuumEnabled().Returns(true);

        _subject.Execute();

        _maintenanceService.DidNotReceive().PerformMaintenance(Arg.Any<int?>());
    }

    [Test]
    public void Execute_with_command_should_pass_max_pages()
    {
        var command = new DatabaseVacuumCommand { MaxPages = 2000 };

        _subject.Execute(command);

        _maintenanceService.Received(1).PerformMaintenance(2000);
    }
}
