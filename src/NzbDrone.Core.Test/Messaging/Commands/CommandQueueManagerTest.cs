using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Test.Messaging.Commands;

[TestFixture]
public class CommandQueueManagerTest
{
    private ICommandRepository _repository;
    private CommandQueueManager _subject;

    private class TestCommand : Command
    {
    }

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<ICommandRepository>();
        _repository.All().Returns(new List<CommandModel>());
        _repository.GetByStatus(Arg.Any<CommandStatus>()).Returns(new List<CommandModel>());
        _subject = new CommandQueueManager(_repository);
    }

    [TearDown]
    public void TearDown()
    {
        _subject?.Dispose();
    }

    [Test]
    public void Push_should_set_queued_at_on_command()
    {
        var command = new TestCommand();
        var before = DateTime.UtcNow;

        _subject.Push(command);

        Assert.That(command.QueuedAt, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void Push_should_set_trigger_on_command()
    {
        var command = new TestCommand();

        _subject.Push(command, CommandTrigger.Manual);

        Assert.That(command.Trigger, Is.EqualTo(CommandTrigger.Manual));
    }

    [Test]
    public void Push_should_create_model_with_queued_status()
    {
        var command = new TestCommand();

        _subject.Push(command);

        _repository.Received(1).Insert(Arg.Is<CommandModel>(m => m.Status == CommandStatus.Queued));
    }

    [Test]
    public void Push_should_call_repository_insert()
    {
        var command = new TestCommand();

        _subject.Push(command);

        _repository.Received(1).Insert(Arg.Any<CommandModel>());
    }

    [Test]
    public void Push_should_return_model_with_correct_name()
    {
        var command = new TestCommand();

        var result = _subject.Push(command);

        Assert.That(result.Name, Is.EqualTo("TestCommand"));
    }

    [Test]
    public void Push_should_set_trigger_on_model()
    {
        var command = new TestCommand();

        var result = _subject.Push(command, CommandTrigger.Scheduled);

        Assert.That(result.Trigger, Is.EqualTo(CommandTrigger.Scheduled));
    }

    [Test]
    public void Push_should_default_trigger_to_unspecified()
    {
        var command = new TestCommand();

        var result = _subject.Push(command);

        Assert.That(result.Trigger, Is.EqualTo(CommandTrigger.Unspecified));
    }

    [Test]
    public void GetAll_should_return_commands_ordered_by_queued_at_descending()
    {
        var now = DateTime.UtcNow;
        var commands = new List<CommandModel>
        {
            new() { Id = 1, Name = "First", QueuedAt = now.AddMinutes(-10) },
            new() { Id = 2, Name = "Second", QueuedAt = now.AddMinutes(-5) },
            new() { Id = 3, Name = "Third", QueuedAt = now }
        };
        _repository.All().Returns(commands);

        var result = _subject.GetAll().ToList();

        Assert.That(result[0].Name, Is.EqualTo("Third"));
        Assert.That(result[2].Name, Is.EqualTo("First"));
    }

    [Test]
    public void GetAll_should_return_at_most_50()
    {
        var commands = Enumerable.Range(0, 60)
            .Select(i => new CommandModel { Id = i, Name = $"Cmd{i}", QueuedAt = DateTime.UtcNow.AddMinutes(-i) })
            .ToList();
        _repository.All().Returns(commands);

        var result = _subject.GetAll().ToList();

        Assert.That(result, Has.Count.EqualTo(50));
    }

    [Test]
    public void GetStarted_should_query_repository_by_started_status()
    {
        var startedCommands = new List<CommandModel>
        {
            new() { Id = 2, Name = "B", Status = CommandStatus.Started }
        };
        _repository.GetByStatus(CommandStatus.Started).Returns(startedCommands);

        var result = _subject.GetStarted().ToList();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("B"));
        _repository.Received(1).GetByStatus(CommandStatus.Started);
    }

    [Test]
    public void GetQueued_should_query_repository_by_queued_status()
    {
        var queuedCommands = new List<CommandModel>
        {
            new() { Id = 1, Name = "A", Status = CommandStatus.Queued },
            new() { Id = 3, Name = "C", Status = CommandStatus.Queued }
        };
        _repository.GetByStatus(CommandStatus.Queued).Returns(queuedCommands);

        var result = _subject.GetQueued().ToList();

        Assert.That(result, Has.Count.EqualTo(2));
        _repository.Received(1).GetByStatus(CommandStatus.Queued);
    }

    [Test]
    public void Dispose_should_not_throw()
    {
        Assert.DoesNotThrow(() => _subject.Dispose());
    }

    [Test]
    public void Push_should_set_body_on_model()
    {
        var command = new TestCommand();

        var result = _subject.Push(command);

        Assert.That(result.Body, Is.Not.Null.And.Not.Empty);
    }

    // --- CleanupOldCommands tests (private method exercised via reflection) ---

    private void InvokeCleanupOldCommands()
    {
        var method = typeof(CommandQueueManager)
            .GetMethod("CleanupOldCommands", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, null);
    }

    [Test]
    public void CleanupOldCommands_should_call_repository_delete_once()
    {
        InvokeCleanupOldCommands();

        _repository.Received(1).DeleteOldTerminalCommands(Arg.Any<DateTime>());
    }

    [Test]
    public void CleanupOldCommands_should_pass_cutoff_approximately_7_days_ago()
    {
        DateTime capturedCutoff = default;
        _repository.When(r => r.DeleteOldTerminalCommands(Arg.Any<DateTime>()))
            .Do(ci => capturedCutoff = ci.Arg<DateTime>());

        var before = DateTime.UtcNow.AddDays(-7);
        InvokeCleanupOldCommands();
        var after = DateTime.UtcNow.AddDays(-7);

        Assert.That(capturedCutoff, Is.InRange(before.AddSeconds(-1), after.AddSeconds(1)));
    }

    [Test]
    public void CleanupOldCommands_should_not_throw_when_no_commands()
    {
        Assert.DoesNotThrow(() => InvokeCleanupOldCommands());
    }
}
