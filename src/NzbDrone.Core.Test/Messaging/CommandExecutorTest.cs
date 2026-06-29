using System;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Test.Messaging;

[TestFixture]
public class CommandExecutorTest
{
    private CommandExecutor _subject;

    [SetUp]
    public void SetUp()
    {
        _subject = new CommandExecutor();
    }

    [Test]
    public void Execute_should_set_status_to_completed()
    {
        var command = new CommandModel { Name = "TestCommand" };

        _subject.Execute(command);

        Assert.That(command.Status, Is.EqualTo(CommandStatus.Completed));
    }

    [Test]
    public void Execute_should_set_started_at_to_approximately_now()
    {
        var command = new CommandModel { Name = "TestCommand" };
        var before = DateTime.UtcNow;

        _subject.Execute(command);

        Assert.That(command.StartedAt, Is.Not.Null);
        Assert.That(command.StartedAt, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void Execute_should_set_ended_at_in_finally_block()
    {
        var command = new CommandModel { Name = "TestCommand" };
        var before = DateTime.UtcNow;

        _subject.Execute(command);

        Assert.That(command.EndedAt, Is.Not.Null);
        Assert.That(command.EndedAt, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void Execute_ended_at_should_be_after_or_equal_to_started_at()
    {
        var command = new CommandModel { Name = "TestCommand" };

        _subject.Execute(command);

        Assert.That(command.EndedAt, Is.GreaterThanOrEqualTo(command.StartedAt));
    }

    [Test]
    public void Execute_should_not_throw_for_a_valid_command()
    {
        var command = new CommandModel { Name = "TestCommand" };

        Assert.DoesNotThrow(() => _subject.Execute(command));
    }

    [Test]
    public void Execute_should_set_started_at_before_ended_at()
    {
        var command = new CommandModel { Name = "TestCommand" };

        _subject.Execute(command);

        // The finally block sets EndedAt after the try body, so it can never precede StartedAt
        Assert.That(command.StartedAt, Is.LessThanOrEqualTo(command.EndedAt));
    }

    [Test]
    public void Execute_should_work_with_empty_command_name()
    {
        var command = new CommandModel { Name = "" };

        Assert.DoesNotThrow(() => _subject.Execute(command));
        Assert.That(command.Status, Is.EqualTo(CommandStatus.Completed));
    }
}
