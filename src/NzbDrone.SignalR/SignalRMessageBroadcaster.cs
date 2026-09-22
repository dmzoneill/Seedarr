using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        var eventNames = GetNamedEvents(message);
        foreach (var eventName in eventNames)
        {
            _logger.Trace("Broadcasting direct SignalR event: {0}", eventName);
            _hubContext?.Clients?.All?.SendAsync(eventName, message.Body)
                ?.ContinueWith(t => _logger.Warn(t.Exception, "SignalR named event broadcast failed"), TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    public void BroadcastToGroup(string groupName, SignalRMessage message)
    {
        if (string.IsNullOrWhiteSpace(groupName) || message == null)
        {
            return;
        }

        if (_deduplicationWindow > TimeSpan.Zero && IsDuplicate(message, groupName))
        {
            _logger.Trace("Suppressing duplicate SignalR broadcast for group {0}, message: {1}", groupName, message.Name);
            return;
        }

        var group = _hubContext?.Clients?.Group(groupName);
        if (group == null)
        {
            return;
        }

        _logger.Trace("Broadcasting SignalR message to group {0}: {1}", groupName, message.Name);
        group.SendAsync("receiveMessage", message)
            ?.ContinueWith(t => _logger.Warn(t.Exception, "SignalR group broadcast failed"), TaskContinuationOptions.OnlyOnFaulted);

        var eventNames = GetNamedEvents(message);
        foreach (var eventName in eventNames)
        {
            _logger.Trace("Broadcasting direct SignalR event to group {0}: {1}", groupName, eventName);
            group.SendAsync(eventName, message.Body)
                ?.ContinueWith(t => _logger.Warn(t.Exception, "SignalR group named event broadcast failed"), TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    public void BroadcastToTorrent(int torrentId, SignalRMessage message)
    {
        BroadcastToGroup($"torrent-{torrentId}", message);
    }

    public void BroadcastToChannel(string channel, SignalRMessage message)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        BroadcastToGroup($"channel-{channel.ToLowerInvariant()}", message);
    }

    private static List<string> GetNamedEvents(SignalRMessage message)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(message?.Name))
        {
            return list;
        }

        if (string.Equals(message.Name, "Torrent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message.Name, "Torrents", StringComparison.OrdinalIgnoreCase))
        {
            switch (message.Action)
            {
                case ModelAction.Created:
                    list.Add("TorrentAdded");
                    list.Add("torrent_added");
                    list.Add("torrentAdded");
                    break;
                case ModelAction.Updated:
                    list.Add("TorrentUpdated");
                    list.Add("torrent_updated");
                    list.Add("torrentUpdated");
                    break;
                case ModelAction.Deleted:
                    list.Add("TorrentDeleted");
                    list.Add("torrent_deleted");
                    list.Add("torrentDeleted");
                    break;
            }
        }
        else if (string.Equals(message.Name, "TorrentAdded", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "torrentAdded", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "torrent_added", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("TorrentAdded");
            list.Add("torrent_added");
            list.Add("torrentAdded");
        }
        else if (string.Equals(message.Name, "TorrentUpdated", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "torrentUpdated", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "torrent_updated", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("TorrentUpdated");
            list.Add("torrent_updated");
            list.Add("torrentUpdated");
        }
        else if (string.Equals(message.Name, "TorrentDeleted", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "torrentDeleted", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "torrent_deleted", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("TorrentDeleted");
            list.Add("torrent_deleted");
            list.Add("torrentDeleted");
        }
        else if (string.Equals(message.Name, "Seeding", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "SeedingStatsUpdated", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "speedPulse", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "speed_update", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "speedUpdate", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("SeedingStatsUpdated");
            list.Add("speed_update");
            list.Add("speedPulse");
            list.Add("speedUpdate");
        }
        else if (string.Equals(message.Name, "Health", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "HealthCheckCompleted", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "health_warning", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "healthWarning", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("HealthCheckCompleted");
            list.Add("health_warning");
            list.Add("healthWarning");
        }
        else if (string.Equals(message.Name, "Command", StringComparison.OrdinalIgnoreCase))
        {
            var cmdEvent = message.Action == ModelAction.Created ? "CommandStarted" : "CommandCompleted";
            list.Add(cmdEvent);
            list.Add("task_progress");
            list.Add("taskProgress");
        }
        else if (string.Equals(message.Name, "System", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("CommandCompleted");
            list.Add("task_progress");
            list.Add("taskProgress");
        }
        else if (string.Equals(message.Name, "Task", StringComparison.OrdinalIgnoreCase))
        {
            var taskEvent = message.Action == ModelAction.Created ? "TaskStarted" : "TaskCompleted";
            list.Add(taskEvent);
            list.Add("task_progress");
            list.Add("taskProgress");
        }
        else if (string.Equals(message.Name, "CommandStarted", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "CommandCompleted", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "TaskStarted", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "TaskCompleted", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "task_progress", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "taskProgress", StringComparison.OrdinalIgnoreCase))
        {
            list.Add(message.Name);
            list.Add("task_progress");
            list.Add("taskProgress");
        }
        else if (string.Equals(message.Name, "Automation", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "AutomationExecution", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "AutomationScript", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "AutomationExecuted", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("AutomationExecuted");
        }
        else if (string.Equals(message.Name, "AutomationTriggerEvaluation", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "AutomationTriggerEvaluated", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("AutomationTriggerEvaluated");
        }
        else if (string.Equals(message.Name, "PieceCompleted", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "PieceBatchCompleted", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "pieceMapUpdated", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "piece_map_updated", StringComparison.OrdinalIgnoreCase))
        {
            list.Add(message.Name);
            list.Add("pieceMapUpdated");
            list.Add("piece_map_updated");
        }
        else if (string.Equals(message.Name, "Tracker", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "Trackers", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "TrackerUpdated", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "trackerUpdated", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "tracker_updated", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("trackerUpdated");
            list.Add("tracker_updated");
        }
        else if (string.Equals(message.Name, "TrackerAnnounced", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "trackerAnnounced", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "TrackerAnnounceEvent", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "tracker_announced", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("trackerAnnounced");
            list.Add("tracker_announced");
        }
        else if (string.Equals(message.Name, "TorrentRecheckProgress", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Name, "torrent_recheck_progress", StringComparison.OrdinalIgnoreCase))
        {
            list.Add("TorrentRecheckProgress");
            list.Add("torrent_recheck_progress");
        }
        else
        {
            list.Add(message.Name);
        }

        return list;
    }


    private bool IsDuplicate(SignalRMessage message, string groupName = null)
    {
        var key = GetDeduplicationKey(message, groupName);
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

    private static string GetDeduplicationKey(SignalRMessage message, string groupName = null)
    {
        var prefix = string.IsNullOrEmpty(groupName) ? string.Empty : $"[{groupName}]";
        var name = message.Name ?? string.Empty;
        var action = message.Action.ToString();

        if (message is PieceCompletedMessage pcm)
        {
            return $"{prefix}{name}:{pcm.InfoHash}:{pcm.PieceIndex}";
        }

        if (message is PieceBatchCompletedMessage pbcm)
        {
            return $"{prefix}{name}:{pbcm.InfoHash}:{string.Join(",", pbcm.PieceIndexes)}";
        }

        if (message.Body != null)
        {
            var idProp = message.Body.GetType().GetProperty("Id");
            if (idProp != null)
            {
                var id = idProp.GetValue(message.Body);
                if (id != null)
                {
                    return $"{prefix}{name}:{action}:{id}";
                }
            }
        }

        return $"{prefix}{name}:{action}";
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
