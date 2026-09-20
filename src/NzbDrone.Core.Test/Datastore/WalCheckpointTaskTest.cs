using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Datastore;

[TestFixture]
public class WalCheckpointTaskTest
{
    private IDatabaseMaintenanceService _maintenanceService;
    private WalCheckpointTask _subject;

    [SetUp]
    public void SetUp()
    {
        _maintenanceService = Substitute.For<IDatabaseMaintenanceService>();
        _subject = new WalCheckpointTask(_maintenanceService);
    }

    [Test]
    public void DefaultInterval_should_be_15_minutes()
    {
        Assert.That(_subject.DefaultInterval, Is.EqualTo(15));
    }

    [Test]
    public void Execute_should_run_passive_checkpoint_and_escalate_to_restart_when_not_busy()
    {
        _maintenanceService.CheckpointWal(WalCheckpointMode.Passive).Returns(new DatabaseMaintenanceResult
        {
            Success = true,
            Busy = 0,
            WalLogPages = 100,
            WalCheckpointedPages = 100,
            CheckpointMode = WalCheckpointMode.Passive
        });

        _subject.Execute();

        _maintenanceService.Received(1).CheckpointWal(WalCheckpointMode.Passive);
        _maintenanceService.Received(1).CheckpointWal(WalCheckpointMode.Restart);
    }

    [Test]
    public void Execute_should_not_escalate_to_restart_when_passive_checkpoint_is_busy()
    {
        _maintenanceService.CheckpointWal(WalCheckpointMode.Passive).Returns(new DatabaseMaintenanceResult
        {
            Success = true,
            Busy = 1,
            WalLogPages = 100,
            WalCheckpointedPages = 50,
            CheckpointMode = WalCheckpointMode.Passive
        });

        _subject.Execute();

        _maintenanceService.Received(1).CheckpointWal(WalCheckpointMode.Passive);
        _maintenanceService.DidNotReceive().CheckpointWal(WalCheckpointMode.Restart);
    }

    [Test]
    public void Execute_should_not_escalate_to_restart_when_passive_checkpoint_fails()
    {
        _maintenanceService.CheckpointWal(WalCheckpointMode.Passive).Returns(new DatabaseMaintenanceResult
        {
            Success = false,
            Busy = 0,
            Message = "Disk I/O error",
            CheckpointMode = WalCheckpointMode.Passive
        });

        _subject.Execute();

        _maintenanceService.Received(1).CheckpointWal(WalCheckpointMode.Passive);
        _maintenanceService.DidNotReceive().CheckpointWal(WalCheckpointMode.Restart);
    }

    [Test]
    public void Execute_with_command_should_run_specified_mode()
    {
        var command = new WalCheckpointCommand { Mode = WalCheckpointMode.Truncate };

        _subject.Execute(command);

        _maintenanceService.Received(1).CheckpointWal(WalCheckpointMode.Truncate);
    }
}
