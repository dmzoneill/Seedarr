using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.SignalR;
using Seedarr.Http.REST;

namespace Seedarr.Http.Test.REST;

public class TestModel : ModelBase
{
    public string Name { get; set; }
    public int Value { get; set; }
}

public class TestResource : RestResource
{
    public string Name { get; set; }
    public int Value { get; set; }
}

public class TestControllerWithSignalR : RestControllerWithSignalR<TestResource, TestModel>
{
    public int GetResourceByIdCallCount { get; private set; }

    public HashSet<int> NullResourceIds { get; } = new();

    public ManualResetEventSlim BlockGetResourceById { get; set; }

    public TestControllerWithSignalR(IBroadcastSignalRMessage broadcaster, TimeSpan? coalesceWindow = null)
        : base(broadcaster, null, coalesceWindow)
    {
    }

    protected override TestResource GetResourceById(TestModel model)
    {
        if (BlockGetResourceById != null && model.Value == 2)
        {
            BlockGetResourceById.Wait();
        }

        GetResourceByIdCallCount++;
        if (NullResourceIds.Contains(model.Id))
        {
            return null;
        }

        return new TestResource
        {
            Id = model.Id,
            Name = model.Name,
            Value = model.Value,
        };
    }
}

[TestFixture]
public class RestControllerWithSignalRTest
{
    private IBroadcastSignalRMessage _broadcaster;
    private TestControllerWithSignalR _controller;

    [SetUp]
    public void SetUp()
    {
        MessageHub.ResetForTesting();
        _broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        _broadcaster.IsConnected.Returns(true);
        _controller = new TestControllerWithSignalR(_broadcaster, TimeSpan.FromMilliseconds(200));
    }

    [TearDown]
    public void TearDown()
    {
        _controller?.Dispose();
    }

    [Test]
    public void Rapid_consecutive_updates_for_same_id_coalesce_into_throttled_broadcasts()
    {
        for (var i = 0; i < 10; i++)
        {
            var model = new TestModel { Id = 1, Name = "Item", Value = i };
            _controller.Handle(new ModelEvent<TestModel>(model, ModelAction.Updated));
        }

        // Only the leading-edge update should have broadcasted immediately; the subsequent 9 are throttled
        Assert.That(_controller.PendingUpdatesCount, Is.EqualTo(1));
        _broadcaster.Received(1).BroadcastMessage(Arg.Any<SignalRMessage>());

        // Flush remaining coalesced update
        _controller.Flush();

        Assert.That(_controller.PendingUpdatesCount, Is.EqualTo(0));
        // Exactly 2 broadcasts total (leading edge + final coalesced state) rather than 10
        _broadcaster.Received(2).BroadcastMessage(Arg.Any<SignalRMessage>());

        // The final broadcast payload should have the latest value (9)
        _broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Updated &&
            ((TestResource)m.Body).Value == 9));
    }

    [Test]
    public void Window_expiry_broadcasts_pending_coalesce_instead_of_stale_incoming_update()
    {
        const int entityId = 7;
        var leading = new TestModel { Id = entityId, Name = "Item", Value = 1 };
        _controller.Handle(new ModelEvent<TestModel>(leading, ModelAction.Updated));

        var newerPending = new TestModel { Id = entityId, Name = "Item", Value = 2 };
        _controller.Handle(new ModelEvent<TestModel>(newerPending, ModelAction.Updated));
        Assert.That(_controller.PendingUpdatesCount, Is.EqualTo(1));

        ForceCoalesceWindowExpired(entityId);

        var stale = new TestModel { Id = entityId, Name = "Item", Value = 1 };
        _controller.Handle(new ModelEvent<TestModel>(stale, ModelAction.Updated));

        Assert.That(_controller.PendingUpdatesCount, Is.EqualTo(0));
        _broadcaster.Received(2).BroadcastMessage(Arg.Any<SignalRMessage>());
        _broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Updated &&
            ((TestResource)m.Body).Value == 2));
    }

    private void ForceCoalesceWindowExpired(int entityId)
    {
        var controllerType = typeof(RestControllerWithSignalR<TestResource, TestModel>);
        var syncLock = controllerType.GetField("_syncLock", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(_controller);
        var lastBroadcastTimes = (Dictionary<int, DateTime>)controllerType
            .GetField("_lastBroadcastTimes", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(_controller);

        lock (syncLock)
        {
            lastBroadcastTimes[entityId] = DateTime.UtcNow - TimeSpan.FromHours(1);
        }
    }

    [Test]
    public void Distinct_entity_ids_are_all_broadcasted()
    {
        var model1 = new TestModel { Id = 101, Name = "First", Value = 1 };
        var model2 = new TestModel { Id = 102, Name = "Second", Value = 2 };
        var model3 = new TestModel { Id = 103, Name = "Third", Value = 3 };

        _controller.Handle(new ModelEvent<TestModel>(model1, ModelAction.Updated));
        _controller.Handle(new ModelEvent<TestModel>(model2, ModelAction.Updated));
        _controller.Handle(new ModelEvent<TestModel>(model3, ModelAction.Updated));

        _broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m => ((TestResource)m.Body).Id == 101));
        _broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m => ((TestResource)m.Body).Id == 102));
        _broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m => ((TestResource)m.Body).Id == 103));
        _broadcaster.Received(3).BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public void Create_and_Delete_events_are_processed_cleanly()
    {
        var model = new TestModel { Id = 50, Name = "CreatedItem", Value = 100 };

        // 1. Create dispatched promptly
        _controller.Handle(new ModelEvent<TestModel>(model, ModelAction.Created));
        _broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Created &&
            ((TestResource)m.Body).Id == 50));

        // 2. First update after create broadcasts immediately (Created does not start coalesce window)
        var updateModel = new TestModel { Id = 50, Name = "UpdatedItem", Value = 101 };
        _controller.Handle(new ModelEvent<TestModel>(updateModel, ModelAction.Updated));
        Assert.That(_controller.PendingUpdatesCount, Is.EqualTo(0));
        _broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Updated &&
            ((TestResource)m.Body).Value == 101));

        // 3. Delete dispatched promptly and cancels pending update
        var deleteModel = new TestModel { Id = 50, Name = "DeletedItem", Value = 101 };
        _controller.Handle(new ModelEvent<TestModel>(deleteModel, ModelAction.Deleted));

        _broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Deleted &&
            ((TestResource)m.Body).Id == 50));

        Assert.That(_controller.PendingUpdatesCount, Is.EqualTo(0));

        // 4. Flushing after delete should not broadcast any stale update
        _controller.Flush();
        _broadcaster.DidNotReceive().BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Updated &&
            ((TestResource)m.Body).Id == 50));
    }

    [Test]
    public void First_updated_after_created_broadcasts_immediately_within_coalesce_window()
    {
        const int entityId = 99;
        var created = new TestModel { Id = entityId, Name = "New", Value = 0 };
        _controller.Handle(new ModelEvent<TestModel>(created, ModelAction.Created));

        Thread.Sleep(50);

        var updated = new TestModel { Id = entityId, Name = "New", Value = 50 };
        _controller.Handle(new ModelEvent<TestModel>(updated, ModelAction.Updated));

        Assert.That(_controller.PendingUpdatesCount, Is.EqualTo(0));
        _broadcaster.Received(2).BroadcastMessage(Arg.Any<SignalRMessage>());
        _broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Updated &&
            ((TestResource)m.Body).Value == 50));
    }

    [Test]
    public void Coalesce_timer_does_not_emit_updated_after_deleted_while_broadcast_is_in_flight()
    {
        using var blockBroadcast = new ManualResetEventSlim(false);
        _controller.BlockGetResourceById = blockBroadcast;

        var first = new TestModel { Id = 50, Name = "Item", Value = 1 };
        var second = new TestModel { Id = 50, Name = "Item", Value = 2 };
        _controller.Handle(new ModelEvent<TestModel>(first, ModelAction.Updated));
        _controller.Handle(new ModelEvent<TestModel>(second, ModelAction.Updated));
        Assert.That(_controller.PendingUpdatesCount, Is.EqualTo(1));

        Thread.Sleep(250);

        var deleteModel = new TestModel { Id = 50, Name = "DeletedItem", Value = 2 };
        _controller.Handle(new ModelEvent<TestModel>(deleteModel, ModelAction.Deleted));

        blockBroadcast.Set();
        Thread.Sleep(100);

        _broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Updated &&
            ((TestResource)m.Body).Value == 1));
        _broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Deleted &&
            ((TestResource)m.Body).Id == 50));
        _broadcaster.DidNotReceive().BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Updated &&
            ((TestResource)m.Body).Value == 2));
    }

    [Test]
    public void First_update_after_hub_reconnect_broadcasts_immediately_despite_recent_pre_disconnect_coalesce()
    {
        const int entityId = 42;
        var leading = new TestModel { Id = entityId, Name = "Item", Value = 1 };
        _controller.Handle(new ModelEvent<TestModel>(leading, ModelAction.Updated));

        var coalesced = new TestModel { Id = entityId, Name = "Item", Value = 2 };
        _controller.Handle(new ModelEvent<TestModel>(coalesced, ModelAction.Updated));
        Assert.That(_controller.PendingUpdatesCount, Is.EqualTo(1));

        _broadcaster.IsConnected.Returns(false);
        MessageHub.BumpConnectionEpochForTesting();
        _controller.Handle(new ModelEvent<TestModel>(coalesced, ModelAction.Updated));

        _broadcaster.IsConnected.Returns(true);
        MessageHub.BumpConnectionEpochForTesting();
        var afterReconnect = new TestModel { Id = entityId, Name = "Item", Value = 99 };
        _controller.Handle(new ModelEvent<TestModel>(afterReconnect, ModelAction.Updated));

        Assert.That(_controller.PendingUpdatesCount, Is.EqualTo(0));
        _broadcaster.Received(2).BroadcastMessage(Arg.Any<SignalRMessage>());
        _broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Updated &&
            ((TestResource)m.Body).Value == 99));
    }

    [Test]
    public void Handle_does_not_broadcast_when_broadcaster_is_not_connected()
    {
        _broadcaster.IsConnected.Returns(false);
        var model = new TestModel { Id = 1, Name = "OfflineItem", Value = 1 };

        _controller.Handle(new ModelEvent<TestModel>(model, ModelAction.Updated));

        Assert.That(_controller.GetResourceByIdCallCount, Is.EqualTo(0));
        _broadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public void Failed_leading_edge_mapping_does_not_throttle_next_successful_update()
    {
        const int entityId = 88;
        _controller.NullResourceIds.Add(entityId);

        var failed = new TestModel { Id = entityId, Name = "Item", Value = 1 };
        _controller.Handle(new ModelEvent<TestModel>(failed, ModelAction.Updated));
        _broadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());

        _controller.NullResourceIds.Remove(entityId);
        var success = new TestModel { Id = entityId, Name = "Item", Value = 2 };
        _controller.Handle(new ModelEvent<TestModel>(success, ModelAction.Updated));

        Assert.That(_controller.PendingUpdatesCount, Is.EqualTo(0));
        _broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Updated &&
            ((TestResource)m.Body).Value == 2));
    }

    [Test]
    public void Handle_with_zero_coalesce_window_broadcasts_all_updates_immediately()
    {
        using var unthrottled = new TestControllerWithSignalR(_broadcaster, TimeSpan.Zero);

        for (var i = 0; i < 5; i++)
        {
            var model = new TestModel { Id = 10, Name = "Immediate", Value = i };
            unthrottled.Handle(new ModelEvent<TestModel>(model, ModelAction.Updated));
        }

        Assert.That(unthrottled.PendingUpdatesCount, Is.EqualTo(0));
        _broadcaster.Received(5).BroadcastMessage(Arg.Any<SignalRMessage>());
    }
}
