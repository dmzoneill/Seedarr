using System;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Datastore;

[TestFixture]
public class DatabaseMaintenanceServiceTest
{
    private IMainDatabase _mainDatabase;
    private DatabaseMaintenanceService _subject;

    [SetUp]
    public void SetUp()
    {
        _mainDatabase = Substitute.For<IMainDatabase>();
        _subject = new DatabaseMaintenanceService(_mainDatabase);
    }

    [Test]
    public void PerformMaintenance_should_reclaim_pages_when_freelist_count_is_positive()
    {
        _mainDatabase.DatabaseType.Returns(DatabaseType.SQLite);
        _mainDatabase.GetPageSize().Returns(4096L);
        _mainDatabase.GetAutoVacuumStatus().Returns(2);
        _mainDatabase.GetFreelistCount().Returns(1000L, 200L);

        var result = _subject.PerformMaintenance(500);

        Assert.That(result.Success, Is.True);
        Assert.That(result.DatabaseType, Is.EqualTo("SQLite"));
        Assert.That(result.InitialFreelistPages, Is.EqualTo(1000L));
        Assert.That(result.FinalFreelistPages, Is.EqualTo(200L));
        Assert.That(result.ReclaimedPages, Is.EqualTo(800L));
        Assert.That(result.ReclaimedBytes, Is.EqualTo(800L * 4096L));
        _mainDatabase.Received(1).IncrementalVacuum(500);
        _mainDatabase.Received(1).Checkpoint();
    }

    [Test]
    public void PerformMaintenance_should_return_early_when_freelist_is_zero_and_auto_vacuum_is_incremental()
    {
        _mainDatabase.DatabaseType.Returns(DatabaseType.SQLite);
        _mainDatabase.GetPageSize().Returns(4096L);
        _mainDatabase.GetAutoVacuumStatus().Returns(2);
        _mainDatabase.GetFreelistCount().Returns(0L);

        var result = _subject.PerformMaintenance();

        Assert.That(result.Success, Is.True);
        Assert.That(result.ReclaimedPages, Is.EqualTo(0L));
        Assert.That(result.Message, Does.Contain("No reclaimable"));
        _mainDatabase.DidNotReceive().IncrementalVacuum(Arg.Any<int>());
        _mainDatabase.Received(1).Checkpoint();
    }

    [Test]
    public void PerformMaintenance_should_execute_vacuum_when_auto_vacuum_is_not_incremental_even_if_freelist_is_zero()
    {
        _mainDatabase.DatabaseType.Returns(DatabaseType.SQLite);
        _mainDatabase.GetPageSize().Returns(4096L);
        _mainDatabase.GetAutoVacuumStatus().Returns(0);
        _mainDatabase.GetFreelistCount().Returns(0L, 0L);

        var result = _subject.PerformMaintenance();

        Assert.That(result.Success, Is.True);
        _mainDatabase.Received(1).IncrementalVacuum(5000);
    }

    [Test]
    public void PerformMaintenance_should_handle_postgres_database()
    {
        _mainDatabase.DatabaseType.Returns(DatabaseType.PostgreSQL);

        var result = _subject.PerformMaintenance();

        Assert.That(result.Success, Is.True);
        Assert.That(result.DatabaseType, Is.EqualTo("PostgreSQL"));
        _mainDatabase.Received(1).Optimize();
        _mainDatabase.Received(1).Vacuum();
    }

    [Test]
    public void PerformMaintenance_should_return_failure_when_exception_is_thrown()
    {
        _mainDatabase.DatabaseType.Returns(DatabaseType.SQLite);
        _mainDatabase.GetPageSize().Returns(4096L);
        _mainDatabase.GetAutoVacuumStatus().Returns(2);
        _mainDatabase.GetFreelistCount().Returns(500L);
        _mainDatabase.When(x => x.IncrementalVacuum(Arg.Any<int>())).Do(_ => throw new InvalidOperationException("database disk image is malformed"));

        var result = _subject.PerformMaintenance();

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("database disk image is malformed"));
    }

    [Test]
    public void GetFreelistBytes_should_multiply_count_by_page_size()
    {
        _mainDatabase.GetFreelistCount().Returns(50L);
        _mainDatabase.GetPageSize().Returns(4096L);

        var bytes = _subject.GetFreelistBytes();

        Assert.That(bytes, Is.EqualTo(50L * 4096L));
    }

    [Test]
    public void IsIncrementalAutoVacuumEnabled_should_return_true_only_when_auto_vacuum_is_2()
    {
        _mainDatabase.DatabaseType.Returns(DatabaseType.SQLite);
        _mainDatabase.GetAutoVacuumStatus().Returns(2);
        Assert.That(_subject.IsIncrementalAutoVacuumEnabled(), Is.True);

        _mainDatabase.GetAutoVacuumStatus().Returns(0);
        Assert.That(_subject.IsIncrementalAutoVacuumEnabled(), Is.False);

        _mainDatabase.GetAutoVacuumStatus().Returns(1);
        Assert.That(_subject.IsIncrementalAutoVacuumEnabled(), Is.False);
    }
}
