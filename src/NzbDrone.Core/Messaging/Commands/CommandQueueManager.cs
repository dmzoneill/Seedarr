using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Messaging.Commands;

public class CommandQueueManager : IManageCommandQueue
{
    private readonly IBasicRepository<CommandModel> _repository;
    private readonly Logger _logger;

    public CommandQueueManager(IBasicRepository<CommandModel> repository)
    {
        _repository = repository;
        _logger = LogManager.GetCurrentClassLogger();
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
}
