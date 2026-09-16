using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Messaging.Commands;

public class CommandQueueManager : IManageCommandQueue, IDisposable
{
    private readonly ICommandRepository _repository;
    private readonly Logger _logger;
    private readonly Timer _cleanupTimer;

    public CommandQueueManager(ICommandRepository repository)
    {
        _repository = repository;
        _logger = LogManager.GetCurrentClassLogger();
        _cleanupTimer = new Timer(
            _ =>
            {
                try
                {
                    CleanupOldCommands();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during command history cleanup");
                }
            },
            null,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(24));
    }

    public CommandModel Push<TCommand>(TCommand command, CommandTrigger trigger = CommandTrigger.Unspecified)
        where TCommand : Command
    {
        command.QueuedAt = DateTime.UtcNow;
        command.Trigger = trigger;

        _logger.Trace("Publishing {0}", command.Name);

        var model = new CommandModel
        {
            Name = command.Name,
            Body = command.ToJson(),
            Status = CommandStatus.Queued,
            QueuedAt = command.QueuedAt,
            Trigger = trigger
        };

        _repository.Insert(model);

        return model;
    }

    public CommandModel PushRaw(string name, string body, CommandTrigger trigger = CommandTrigger.Manual)
    {
        _logger.Trace("Publishing raw command {0}", name);

        var model = new CommandModel
        {
            Name = name,
            Body = body,
            Status = CommandStatus.Queued,
            QueuedAt = DateTime.UtcNow,
            Trigger = trigger
        };

        _repository.Insert(model);
        return model;
    }

    public IEnumerable<CommandModel> GetAll()
    {
        return _repository.All()
            .OrderByDescending(c => c.QueuedAt)
            .Take(50);
    }

    public IEnumerable<CommandModel> GetStarted()
    {
        return _repository.GetByStatus(CommandStatus.Started);
    }

    public IEnumerable<CommandModel> GetQueued()
    {
        return _repository.GetByStatus(CommandStatus.Queued);
    }

    public CommandModel Get(int id)
    {
        return _repository.Get(id);
    }

    public bool Cancel(int id)
    {
        var command = _repository.Get(id);
        if (command == null)
        {
            return false;
        }

        if (command.Status == CommandStatus.Queued || command.Status == CommandStatus.Started)
        {
            command.Status = CommandStatus.Cancelled;
            command.EndedAt = DateTime.UtcNow;
            command.Message = "Cancelled by user";
            _repository.Update(command);
            _logger.Info("Cancelled command {0} (id: {1})", command.Name, id);
            return true;
        }

        return false;
    }

    private void CleanupOldCommands()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        _repository.DeleteOldTerminalCommands(cutoff);
        _logger.Debug("Cleaned up terminal command records older than {0:yyyy-MM-dd}", cutoff);
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}
