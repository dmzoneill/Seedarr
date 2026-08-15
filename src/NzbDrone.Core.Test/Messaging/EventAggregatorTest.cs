using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;

// Event types must live at namespace scope so Castle.DynamicProxy can generate
// IHandle<T> proxies for them (private nested classes are inaccessible to the
// generated proxy assembly and cause proxy-creation failures at runtime).
namespace NzbDrone.Core.Test.Messaging;

public class EventAggregatorTestEvent : IEvent { }

public class EventAggregatorAnotherEvent : IEvent { }

[TestFixture]
public class EventAggregatorTest
{
    private IServiceProvider _serviceProvider;
    private EventAggregator _subject;

    [SetUp]
    public void SetUp()
    {
        _serviceProvider = Substitute.For<IServiceProvider>();
        _subject = new EventAggregator(_serviceProvider);
    }

    // Mirrors what Microsoft.Extensions.DependencyInjection.GetServices does internally:
    // GetServices(typeof(IHandle<T>)) → GetService(typeof(IEnumerable<IHandle<T>>))
    private void ReturnHandlers<TEvent>(params IHandle<TEvent>[] handlers)
        where TEvent : class, IEvent
    {
        _serviceProvider
            .GetService(typeof(IEnumerable<IHandle<TEvent>>))
            .Returns(handlers);
    }

    [Test]
    public void PublishEvent_should_call_handle_on_registered_handler()
    {
        var handler = Substitute.For<IHandle<EventAggregatorTestEvent>>();
        ReturnHandlers(handler);

        var @event = new EventAggregatorTestEvent();
        _subject.PublishEvent(@event);

        handler.Received(1).Handle(@event);
    }

    [Test]
    public void PublishEvent_should_call_all_registered_handlers()
    {
        var handler1 = Substitute.For<IHandle<EventAggregatorTestEvent>>();
        var handler2 = Substitute.For<IHandle<EventAggregatorTestEvent>>();
        ReturnHandlers(handler1, handler2);

        var @event = new EventAggregatorTestEvent();
        _subject.PublishEvent(@event);

        handler1.Received(1).Handle(@event);
        handler2.Received(1).Handle(@event);
    }

    [Test]
    public void PublishEvent_should_not_throw_when_no_handlers_registered()
    {
        ReturnHandlers<EventAggregatorTestEvent>();

        Assert.DoesNotThrow(() => _subject.PublishEvent(new EventAggregatorTestEvent()));
    }

    [Test]
    public void PublishEvent_should_not_throw_when_handler_throws()
    {
        var handler = Substitute.For<IHandle<EventAggregatorTestEvent>>();
        handler
            .When(h => h.Handle(Arg.Any<EventAggregatorTestEvent>()))
            .Do(_ => throw new InvalidOperationException("simulated handler error"));
        ReturnHandlers(handler);

        // The EventAggregator catches and logs exceptions per handler, so this must not propagate
        Assert.DoesNotThrow(() => _subject.PublishEvent(new EventAggregatorTestEvent()));
    }

    [Test]
    public void PublishEvent_should_continue_calling_remaining_handlers_after_one_throws()
    {
        var handler1 = Substitute.For<IHandle<EventAggregatorTestEvent>>();
        var handler2 = Substitute.For<IHandle<EventAggregatorTestEvent>>();
        handler1
            .When(h => h.Handle(Arg.Any<EventAggregatorTestEvent>()))
            .Do(_ => throw new InvalidOperationException("handler1 fails"));
        ReturnHandlers(handler1, handler2);

        _subject.PublishEvent(new EventAggregatorTestEvent());

        handler2.Received(1).Handle(Arg.Any<EventAggregatorTestEvent>());
    }

    [Test]
    public void PublishEvent_should_pass_original_event_instance_to_handler()
    {
        var handler = Substitute.For<IHandle<EventAggregatorTestEvent>>();
        ReturnHandlers(handler);

        var @event = new EventAggregatorTestEvent();
        _subject.PublishEvent(@event);

        handler.Received(1).Handle(@event);
    }

    [Test]
    public void PublishEvent_should_not_call_handler_for_different_event_type()
    {
        var testHandler = Substitute.For<IHandle<EventAggregatorTestEvent>>();
        var anotherHandler = Substitute.For<IHandle<EventAggregatorAnotherEvent>>();
        ReturnHandlers(testHandler);
        ReturnHandlers(anotherHandler);

        _subject.PublishEvent(new EventAggregatorTestEvent());

        testHandler.Received(1).Handle(Arg.Any<EventAggregatorTestEvent>());
        anotherHandler.DidNotReceive().Handle(Arg.Any<EventAggregatorAnotherEvent>());
    }
}
