using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Messaging.Commands;

public class CommandQueueManager : IManageCommandQueue, IDisposable
{
    private readonly IBasicRepository<CommandModel> _repository;
    private readonly Logger _logger;
    private readonly Timer _cleanupTimer;

    public CommandQueueManager(IBasicRepository<CommandModel> repository)
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
        return _repository.All().Where(c => c.Status == CommandStatus.Started);
    }

    public IEnumerable<CommandModel> GetQueued()
    {
        return _repository.All().Where(c => c.Status == CommandStatus.Queued);
    }

    private void CleanupOldCommands()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var old = _repository.All()
            .Where(c => c.Status is CommandStatus.Completed or CommandStatus.Failed or CommandStatus.Cancelled)
            .Where(c => c.EndedAt.HasValue && c.EndedAt.Value < cutoff)
            .ToList();

        foreach (var command in old)
        {
            _repository.Delete(command);
        }

        if (old.Count > 0)
        {
            _logger.Info("Cleaned up {0} old command records", old.Count);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}
