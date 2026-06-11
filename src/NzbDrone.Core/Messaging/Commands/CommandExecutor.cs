using System;
using NLog;

namespace NzbDrone.Core.Messaging.Commands;

public interface ICommandExecutor
{
    void Execute(CommandModel command);
}

public class CommandExecutor : ICommandExecutor
{
    private readonly Logger _logger;

    public CommandExecutor()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute(CommandModel command)
    {
        _logger.Trace("Executing {0}", command.Name);

        try
        {
            command.Status = CommandStatus.Started;
            command.StartedAt = DateTime.UtcNow;

            command.Status = CommandStatus.Completed;
            _logger.Debug("Completed {0}", command.Name);
        }
        catch (Exception ex)
        {
            command.Status = CommandStatus.Failed;
            command.Message = ex.Message;
            _logger.Error(ex, "Error executing {0}", command.Name);
        }
        finally
        {
            command.EndedAt = DateTime.UtcNow;
        }
    }
}
