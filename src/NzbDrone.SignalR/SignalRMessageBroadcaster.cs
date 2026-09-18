using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using NLog;
using NzbDrone.Core.Datastore;

namespace NzbDrone.SignalR;

public class SignalRMessageBroadcaster : IBroadcastSignalRMessage
{
    public static readonly TimeSpan DefaultDeduplicationWindow = TimeSpan.FromMilliseconds(1000);

    private readonly IHubContext<MessageHub> _hubContext;
    private readonly Logger _logger;
    private readonly TimeSpan _deduplicationWindow;
    private readonly ConcurrentDictionary<string, (string PayloadFingerprint, DateTime Timestamp)> _recentPayloads = new();

    public SignalRMessageBroadcaster(IHubContext<MessageHub> hubContext)
        : this(hubContext, DefaultDeduplicationWindow)
    {
    }

    public SignalRMessageBroadcaster(IHubContext<MessageHub> hubContext, TimeSpan deduplicationWindow)
    {
        _hubContext = hubContext;
        _logger = LogManager.GetCurrentClassLogger();
        _deduplicationWindow = deduplicationWindow;
    }

    public bool IsConnected => MessageHub.IsConnected;

    public void ResetDeduplicationCache()
    {
        _recentPayloads.Clear();
    }

    public void BroadcastMessage(SignalRMessage message)
    {
        if (message == null)
        {
            return;
        }

        if (_deduplicationWindow > TimeSpan.Zero && IsDuplicate(message))
        {
            _logger.Trace("Suppressing duplicate SignalR broadcast for message: {0}", message.Name);
            return;
        }

        _logger.Trace("Broadcasting SignalR message: {0}", message.Name);
        _hubContext?.Clients?.All?.SendAsync("receiveMessage", message)
            ?.ContinueWith(t => _logger.Warn(t.Exception, "SignalR broadcast failed"), TaskContinuationOptions.OnlyOnFaulted);

        var eventName = GetNamedEvent(message);
        if (!string.IsNullOrEmpty(eventName))
        {
            _logger.Trace("Broadcasting direct SignalR event: {0}", eventName);
            _hubContext?.Clients?.All?.SendAsync(eventName, message.Body)
                ?.ContinueWith(t => _logger.Warn(t.Exception, "SignalR named event broadcast failed"), TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private static string GetNamedEvent(SignalRMessage message)
    {
        if (string.IsNullOrEmpty(message?.Name))
        {
            return null;
        }

        if (string.Equals(message.Name, "Torrent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message.Name, "Torrents", StringComparison.OrdinalIgnoreCase))
        {
            return message.Action switch
            {
                ModelAction.Created => "TorrentAdded",
                ModelAction.Updated => "TorrentUpdated",
                ModelAction.Deleted => "TorrentDeleted",
                _ => null
            };
        }

        if (string.Equals(message.Name, "Seeding", StringComparison.OrdinalIgnoreCase))
        {
            return "SeedingStatsUpdated";
        }

        if (string.Equals(message.Name, "Health", StringComparison.OrdinalIgnoreCase))
        {
            return "HealthCheckCompleted";
        }

        if (string.Equals(message.Name, "Command", StringComparison.OrdinalIgnoreCase))
        {
            return message.Action switch
            {
                ModelAction.Created => "CommandStarted",
                _ => "CommandCompleted"
            };
        }

        if (string.Equals(message.Name, "System", StringComparison.OrdinalIgnoreCase))
        {
            return "CommandCompleted";
        }

        if (string.Equals(message.Name, "Task", StringComparison.OrdinalIgnoreCase))
        {
            return message.Action switch
            {
                ModelAction.Created => "TaskStarted",
                _ => "TaskCompleted"
            };
        }

        if (string.Equals(message.Name, "CommandStarted", StringComparison.OrdinalIgnoreCase))
        {
            return "CommandStarted";
        }

        if (string.Equals(message.Name, "CommandCompleted", StringComparison.OrdinalIgnoreCase))
        {
            return "CommandCompleted";
        }

        if (string.Equals(message.Name, "TaskStarted", StringComparison.OrdinalIgnoreCase))
        {
            return "TaskStarted";
        }

        if (string.Equals(message.Name, "TaskCompleted", StringComparison.OrdinalIgnoreCase))
        {
            return "TaskCompleted";
        }

        if (string.Equals(message.Name, "Automation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message.Name, "AutomationExecution", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message.Name, "AutomationScript", StringComparison.OrdinalIgnoreCase))
        {
            return "AutomationExecuted";
        }

        if (string.Equals(message.Name, "AutomationTriggerEvaluation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message.Name, "AutomationTriggerEvaluated", StringComparison.OrdinalIgnoreCase))
        {
            return "AutomationTriggerEvaluated";
        }

        if (string.Equals(message.Name, "PieceCompleted", StringComparison.OrdinalIgnoreCase))
        {
            return "PieceCompleted";
        }

        if (string.Equals(message.Name, "PieceBatchCompleted", StringComparison.OrdinalIgnoreCase))
        {
            return "PieceBatchCompleted";
        }

        if (message.Name is "TorrentAdded" or "TorrentUpdated" or "TorrentDeleted" or "SeedingStatsUpdated" or "HealthCheckCompleted" or "CommandStarted" or "CommandCompleted" or "TaskStarted" or "TaskCompleted" or "AutomationExecuted" or "AutomationTriggerEvaluated" or "PieceCompleted" or "PieceBatchCompleted")
        {
            return message.Name;
        }

        return null;
    }

    private bool IsDuplicate(SignalRMessage message)
    {
        var key = GetDeduplicationKey(message);
        var fingerprint = GetPayloadFingerprint(message.Body);
        var now = DateTime.UtcNow;

        if (_recentPayloads.TryGetValue(key, out var lastEntry))
        {
            if (string.Equals(lastEntry.PayloadFingerprint, fingerprint, StringComparison.Ordinal) &&
                (now - lastEntry.Timestamp) < _deduplicationWindow)
            {
                return true;
            }
        }

        PruneStalePayloads(now);
        _recentPayloads[key] = (fingerprint, now);
        return false;
    }

    private static string GetDeduplicationKey(SignalRMessage message)
    {
        var name = message.Name ?? string.Empty;
        var action = message.Action.ToString();

        if (message is PieceCompletedMessage pcm)
        {
            return $"{name}:{pcm.InfoHash}:{pcm.PieceIndex}";
        }

        if (message is PieceBatchCompletedMessage pbcm)
        {
            return $"{name}:{pbcm.InfoHash}:{string.Join(",", pbcm.PieceIndexes)}";
        }

        if (message.Body != null)
        {
            var idProp = message.Body.GetType().GetProperty("Id");
            if (idProp != null)
            {
                var id = idProp.GetValue(message.Body);
                if (id != null)
                {
                    return $"{name}:{action}:{id}";
                }
            }
        }

        return $"{name}:{action}";
    }

    private static string GetPayloadFingerprint(object body)
    {
        if (body == null)
        {
            return "null";
        }

        try
        {
            return JsonSerializer.Serialize(body);
        }
        catch
        {
            return body.ToString() ?? string.Empty;
        }
    }

    private void PruneStalePayloads(DateTime now)
    {
        if (_recentPayloads.Count > 500)
        {
            var staleThreshold = now - TimeSpan.FromMinutes(2);
            foreach (var kvp in _recentPayloads)
            {
                if (kvp.Value.Timestamp < staleThreshold)
                {
                    _recentPayloads.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
