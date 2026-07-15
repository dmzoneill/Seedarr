using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Messaging.Commands;

public interface ICommandExecutor
{
    void Execute(CommandModel command);
}

public class CommandExecutor : ICommandExecutor
{
    private readonly IServiceFactory _serviceFactory;
    private readonly Logger _logger;

    public CommandExecutor(IServiceFactory serviceFactory)
    {
        _serviceFactory = serviceFactory;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute(CommandModel command)
    {
        _logger.Trace("Executing {0}", command.Name);

        try
        {
            command.Status = CommandStatus.Started;
            command.StartedAt = DateTime.UtcNow;

            var commandType = FindCommandType(command.Name);
            if (commandType == null)
            {
                _logger.Warn("No command type found for '{0}'", command.Name);
                command.Status = CommandStatus.Failed;
                command.Message = $"Unknown command: {command.Name}";
                return;
            }

            var typedCommand = DeserializeCommand(command.Body, commandType);
            var handlerType = typeof(IExecute<>).MakeGenericType(commandType);

            object handler;
            try
            {
                handler = _serviceFactory.Build(handlerType);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "No handler registered for '{0}'", command.Name);
                command.Status = CommandStatus.Failed;
                command.Message = $"No handler for command: {command.Name}";
                return;
            }

            var executeMethod = handlerType.GetMethod("Execute");
            executeMethod!.Invoke(handler, new[] { typedCommand });

            command.Status = CommandStatus.Completed;
            _logger.Debug("Completed {0}", command.Name);
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            command.Status = CommandStatus.Failed;
            command.Message = inner.Message;
            _logger.Error(inner, "Error executing {0}", command.Name);
        }
        finally
        {
            command.EndedAt = DateTime.UtcNow;
        }
    }

    private static Type FindCommandType(string name)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try
                {
                    return a.GetTypes();
                }
                catch
                {
                    return Array.Empty<Type>();
                }
            })
            .FirstOrDefault(t =>
                t.Name == name &&
                t.IsClass &&
                !t.IsAbstract &&
                typeof(Command).IsAssignableFrom(t));
    }

    private static Command DeserializeCommand(string body, Type commandType)
    {
        if (string.IsNullOrEmpty(body))
        {
            return (Command)Activator.CreateInstance(commandType)!;
        }

        return (Command)JsonSerializer.Deserialize(body, commandType, STJson.GetSerializerSettings());
    }
}
