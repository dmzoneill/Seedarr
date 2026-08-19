using System;
using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Test.Messaging;

internal class SampleCommand : Command
{
    public bool WasExecuted { get; set; }
}

internal class SampleCommandExecutor : IExecute<SampleCommand>
{
    public void Execute(SampleCommand command)
    {
        command.WasExecuted = true;
    }
}

internal class ThrowingCommand : Command { }

internal class ThrowingCommandExecutor : IExecute<ThrowingCommand>
{
    public void Execute(ThrowingCommand command)
    {
        throw new InvalidOperationException("Handler error");
    }
}

internal class StubCommandRepository : IBasicRepository<CommandModel>
{
    public CommandModel LastUpdated { get; private set; }

    public CommandModel Get(int id) => null;

    public IEnumerable<CommandModel> All() => [];

    public CommandModel Insert(CommandModel model) => model;

    public CommandModel Update(CommandModel model)
    {
        LastUpdated = model;
        return model;
    }

    public void Delete(int id)
    {
    }

    public void Delete(CommandModel model)
    {
    }

    public IEnumerable<CommandModel> InsertMany(IEnumerable<CommandModel> models) => models;

    public IEnumerable<CommandModel> UpdateMany(IEnumerable<CommandModel> models) => models;

    public void DeleteMany(IEnumerable<int> ids)
    {
    }

    public void Purge(bool vacuum = false)
    {
    }

    public bool HasItems() => false;

    public CommandModel Upsert(CommandModel model) => model;

    public void SetFields(CommandModel model, params System.Linq.Expressions.Expression<Func<CommandModel, object>>[] properties)
    {
    }
}

internal class StubServiceFactory : IServiceFactory
{
    private readonly SampleCommandExecutor _sampleHandler = new();
    private readonly ThrowingCommandExecutor _throwingHandler = new();

    public T Build<T>()
        where T : class
    {
        return (T)Build(typeof(T));
    }

    public object Build(Type type)
    {
        if (type == typeof(IExecute<SampleCommand>))
        {
            return _sampleHandler;
        }

        if (type == typeof(IExecute<ThrowingCommand>))
        {
            return _throwingHandler;
        }

        throw new InvalidOperationException($"No handler for {type.Name}");
    }

    public IEnumerable<T> BuildAll<T>()
        where T : class
    {
        return [];
    }
}

[TestFixture]
public class CommandExecutorTest
{
    private CommandExecutor _subject;
    private StubServiceFactory _serviceFactory;
    private StubCommandRepository _repository;

    [SetUp]
    public void SetUp()
    {
        _serviceFactory = new StubServiceFactory();
        _repository = new StubCommandRepository();
        _subject = new CommandExecutor(_serviceFactory, _repository);
    }

    [Test]
    public void Execute_should_dispatch_to_handler_and_complete()
    {
        var command = new CommandModel { Name = "SampleCommand", Body = "{}" };

        _subject.Execute(command);

        Assert.That(command.Status, Is.EqualTo(CommandStatus.Completed));
    }

    [Test]
    public void Execute_should_set_started_at()
    {
        var command = new CommandModel { Name = "SampleCommand", Body = "{}" };
        var before = DateTime.UtcNow;

        _subject.Execute(command);

        Assert.That(command.StartedAt, Is.Not.Null);
        Assert.That(command.StartedAt, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void Execute_should_set_ended_at()
    {
        var command = new CommandModel { Name = "SampleCommand", Body = "{}" };
        var before = DateTime.UtcNow;

        _subject.Execute(command);

        Assert.That(command.EndedAt, Is.Not.Null);
        Assert.That(command.EndedAt, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void Execute_ended_at_should_be_after_or_equal_to_started_at()
    {
        var command = new CommandModel { Name = "SampleCommand", Body = "{}" };

        _subject.Execute(command);

        Assert.That(command.EndedAt, Is.GreaterThanOrEqualTo(command.StartedAt));
    }

    [Test]
    public void Execute_should_set_failed_for_unknown_command_type()
    {
        var command = new CommandModel { Name = "UnknownCommand" };

        _subject.Execute(command);

        Assert.That(command.Status, Is.EqualTo(CommandStatus.Failed));
    }

    [Test]
    public void Execute_should_set_failed_when_handler_throws()
    {
        var command = new CommandModel { Name = "ThrowingCommand", Body = "{}" };

        _subject.Execute(command);

        Assert.That(command.Status, Is.EqualTo(CommandStatus.Failed));
        Assert.That(command.Message, Is.EqualTo("Handler error"));
    }

    [Test]
    public void Execute_should_not_throw_for_unknown_command()
    {
        var command = new CommandModel { Name = "NotAReal Command" };

        Assert.DoesNotThrow(() => _subject.Execute(command));
    }
}
