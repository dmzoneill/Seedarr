using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;

namespace NzbDrone.Core.Test.ArrIntegration;

[TestFixture]
public class ArrSyncScheduledTaskTest
{
    private IArrConnectionFactory _connectionFactory;
    private IArrSyncService _arrSyncService;
    private ArrSyncScheduledTask _task;

    [SetUp]
    public void Setup()
    {
        _connectionFactory = Substitute.For<IArrConnectionFactory>();
        _arrSyncService = Substitute.For<IArrSyncService>();
        _task = new ArrSyncScheduledTask(_connectionFactory, _arrSyncService);
    }

    [Test]
    public void DefaultInterval_should_be_60_minutes()
    {
        Assert.That(_task.DefaultInterval, Is.EqualTo(60));
    }

    [Test]
    public void Execute_should_early_exit_when_no_connections()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>());

        _task.Execute();

        _arrSyncService.DidNotReceive().Sync();
    }

    [Test]
    public void Execute_should_early_exit_when_connections_are_disabled()
    {
        var connections = new List<ArrConnectionDefinition>
        {
            new() { Enable = false, SyncEnabled = true }
        };
        _connectionFactory.All().Returns(connections);

        _task.Execute();

        _arrSyncService.DidNotReceive().Sync();
    }

    [Test]
    public void Execute_should_early_exit_when_sync_is_disabled()
    {
        var connections = new List<ArrConnectionDefinition>
        {
            new() { Enable = true, SyncEnabled = false }
        };
        _connectionFactory.All().Returns(connections);

        _task.Execute();

        _arrSyncService.DidNotReceive().Sync();
    }

    [Test]
    public void Execute_should_call_sync_when_active_connections_exist()
    {
        var connections = new List<ArrConnectionDefinition>
        {
            new() { Enable = false, SyncEnabled = true },
            new() { Enable = true, SyncEnabled = true }
        };
        _connectionFactory.All().Returns(connections);
        _arrSyncService.Sync().Returns(new SyncResult { Added = 2, Skipped = 1, Failed = 0 });

        _task.Execute();

        _arrSyncService.Received(1).Sync();
    }

    [Test]
    public void Execute_should_not_throw_when_sync_service_throws()
    {
        var connections = new List<ArrConnectionDefinition>
        {
            new() { Enable = true, SyncEnabled = true }
        };
        _connectionFactory.All().Returns(connections);
        _arrSyncService.When(s => s.Sync()).Do(_ => throw new Exception("Sync exploded"));

        Assert.DoesNotThrow(() => _task.Execute());
    }

    [Test]
    public void Execute_handles_null_connection_list_gracefully()
    {
        _connectionFactory.All().Returns((List<ArrConnectionDefinition>)null);

        Assert.DoesNotThrow(() => _task.Execute());
        _arrSyncService.DidNotReceive().Sync();
    }
}
