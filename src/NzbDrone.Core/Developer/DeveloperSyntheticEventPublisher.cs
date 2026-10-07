using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Developer;

public static class DeveloperSyntheticEventPublisher
{
    private static readonly HashSet<string> BlockedEventTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(ApplicationStartedEvent),
        nameof(ApplicationShutdownRequested),
    };

    private static readonly Lazy<Dictionary<string, Type>> EventTypesByName = new(BuildEventTypeIndex);

    public static bool TryCreateEvent(string eventName, string payloadJson, out IEvent @event, out string error)
    {
        @event = null;
        error = null;

        if (string.IsNullOrWhiteSpace(eventName))
        {
            error = "EventName cannot be empty.";
            return false;
        }

        var payload = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;

        if (!TryResolveEventType(eventName.Trim(), out var eventType))
        {
            error = $"Unknown event type '{eventName}'. Use a concrete IEvent type name (for example TorrentImportedEvent).";
            return false;
        }

        if (BlockedEventTypeNames.Contains(eventType.Name))
        {
            error = $"Publishing '{eventType.Name}' via the developer sandbox is not allowed.";
            return false;
        }

        try
        {
            var instance = JsonSerializer.Deserialize(payload, eventType, STJson.GetSerializerSettings());
            if (instance is not IEvent resolved)
            {
                error = $"Type '{eventType.Name}' does not implement IEvent.";
                return false;
            }

            @event = resolved;
            return true;
        }
        catch (JsonException jex)
        {
            error = $"Invalid payload JSON for '{eventType.Name}': {jex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Failed to deserialize payload for '{eventType.Name}': {ex.Message}";
            return false;
        }
    }

    private static bool TryResolveEventType(string eventName, out Type eventType)
    {
        eventType = null;
        var index = EventTypesByName.Value;

        if (index.TryGetValue(eventName, out eventType))
        {
            return true;
        }

        if (!eventName.EndsWith("Event", StringComparison.OrdinalIgnoreCase) &&
            index.TryGetValue(eventName + "Event", out eventType))
        {
            return true;
        }

        if (eventName.EndsWith("Event", StringComparison.OrdinalIgnoreCase))
        {
            var withoutSuffix = eventName.Substring(0, eventName.Length - 5);
            if (index.TryGetValue(withoutSuffix, out eventType))
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, Type> BuildEventTypeIndex()
    {
        var index = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            var name = assembly.GetName().Name;
            if (name == null || (!name.Contains("NzbDrone", StringComparison.OrdinalIgnoreCase) &&
                                  !name.Contains("Seedarr", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch
            {
                continue;
            }

            foreach (var type in types.Where(t => t.IsClass && !t.IsAbstract && typeof(IEvent).IsAssignableFrom(t)))
            {
                index.TryAdd(type.Name, type);
                var shortName = type.Name.EndsWith("Event", StringComparison.Ordinal)
                    ? type.Name.Substring(0, type.Name.Length - 5)
                    : type.Name;
                index.TryAdd(shortName, type);
            }
        }

        return index;
    }
}
