using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore;
using NzbDrone.SignalR;

namespace NzbDrone.Core.Messaging.Commands;

public interface ICommandExecutor
{
    void Execute(CommandModel command);
}

public class CommandExecutor : ICommandExecutor
{
    private static readonly ConcurrentDictionary<string, Type> CommandTypeCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly IServiceFactory _serviceFactory;
    private readonly IBasicRepository<CommandModel> _repository;
    private readonly IBroadcastSignalRMessage _signalRBroadcaster;
    private readonly Logger _logger;

    public CommandExecutor(
        IServiceFactory serviceFactory,
        IBasicRepository<CommandModel> repository,
        IBroadcastSignalRMessage signalRBroadcaster = null)
    {
        _serviceFactory = serviceFactory;
        _repository = repository;
        _signalRBroadcaster = signalRBroadcaster;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute(CommandModel command)
    {
        _logger.Trace("Executing {0}", command.Name);

        try
        {
            var current = _repository.Get(command.Id);
            if (current != null && current.Status == CommandStatus.Cancelled)
            {
                _logger.Debug("Command {0} (id: {1}) was cancelled before execution started", command.Name, command.Id);
                return;
            }

            command.Status = CommandStatus.Started;
            command.StartedAt = DateTime.UtcNow;
            _repository.Update(command);
            BroadcastCommand("CommandStarted", ModelAction.Created, command);

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

            current = _repository.Get(command.Id);
            if (current != null && current.Status == CommandStatus.Cancelled)
            {
                _logger.Debug("Command {0} (id: {1}) was cancelled during execution", command.Name, command.Id);
                return;
            }

            command.Status = CommandStatus.Completed;
            _logger.Debug("Completed {0}", command.Name);
        }
        catch (Exception ex)
        {
            var current = _repository.Get(command.Id);
            if (current != null && current.Status == CommandStatus.Cancelled)
            {
                _logger.Debug("Command {0} (id: {1}) was cancelled", command.Name, command.Id);
                return;
            }

            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            command.Status = CommandStatus.Failed;
            command.Message = inner.Message;
            _logger.Error(inner, "Error executing {0}", command.Name);
        }
        finally
        {
            var current = _repository.Get(command.Id);
            if (current == null || current.Status != CommandStatus.Cancelled)
            {
                command.EndedAt = DateTime.UtcNow;
                _repository.Update(command);
            }

            BroadcastCommand("CommandCompleted", ModelAction.Updated, command);
        }
    }

    private void BroadcastCommand(string eventName, ModelAction action, CommandModel command)
    {
        if (_signalRBroadcaster == null)
        {
            return;
        }

        try
        {
            _signalRBroadcaster.BroadcastMessage(new SignalRMessage
            {
                Name = eventName,
                Action = action,
                Body = command
            });
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to broadcast {0} for command {1}", eventName, command?.Name);
        }
    }

    /// <summary>
    /// Resolves a command type by name, case-insensitively and tolerating missing or redundant 'Command' suffix.
    /// </summary>
    /// <param name="name">The command name to look up.</param>
    /// <returns>The resolved Type or null if not found.</returns>
    public static Type FindCommandType(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return CommandTypeCache.GetOrAdd(name, static n =>
        {
            var trimmed = n.Trim();
            var nameWithoutSuffix = trimmed.EndsWith("Command", StringComparison.OrdinalIgnoreCase)
                ? trimmed[..^7]
                : trimmed;

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
                {
                    if (!t.IsClass || t.IsAbstract || !typeof(Command).IsAssignableFrom(t))
                    {
                        return false;
                    }

                    var typeName = t.Name;
                    var typeNameWithoutSuffix = typeName.EndsWith("Command", StringComparison.OrdinalIgnoreCase)
                        ? typeName[..^7]
                        : typeName;

                    return string.Equals(typeName, trimmed, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(typeName, trimmed + "Command", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(typeNameWithoutSuffix, trimmed, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(typeNameWithoutSuffix, nameWithoutSuffix, StringComparison.OrdinalIgnoreCase);
                });
        });
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
