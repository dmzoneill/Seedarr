using System;
using System.Collections.Generic;
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

    public TestControllerWithSignalR(IBroadcastSignalRMessage broadcaster, TimeSpan? coalesceWindow = null)
        : base(broadcaster, null, coalesceWindow)
    {
    }

    protected override TestResource GetResourceById(TestModel model)
    {
        GetResourceByIdCallCount++;
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

        // 2. Throttled update queued
        var updateModel = new TestModel { Id = 50, Name = "UpdatedItem", Value = 101 };
        _controller.Handle(new ModelEvent<TestModel>(updateModel, ModelAction.Updated));
        Assert.That(_controller.PendingUpdatesCount, Is.EqualTo(1));

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
    public void Handle_does_not_broadcast_when_broadcaster_is_not_connected()
    {
        _broadcaster.IsConnected.Returns(false);
        var model = new TestModel { Id = 1, Name = "OfflineItem", Value = 1 };

        _controller.Handle(new ModelEvent<TestModel>(model, ModelAction.Updated));

        Assert.That(_controller.GetResourceByIdCallCount, Is.EqualTo(0));
        _broadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
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
