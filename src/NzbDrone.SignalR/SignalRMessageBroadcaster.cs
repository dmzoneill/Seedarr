using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using NLog;
using NzbDrone.Core.Datastore;

namespace NzbDrone.SignalR;

public class SignalRMessageBroadcaster : IBroadcastSignalRMessage
{
    private readonly IHubContext<MessageHub> _hubContext;
    private readonly Logger _logger;

    public SignalRMessageBroadcaster(IHubContext<MessageHub> hubContext)
    {
        _hubContext = hubContext;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool IsConnected => MessageHub.IsConnected;

    public void BroadcastMessage(SignalRMessage message)
    {
        _logger.Trace("Broadcasting SignalR message: {0}", message?.Name);
        _hubContext.Clients.All.SendAsync("receiveMessage", message)
            .ContinueWith(t => _logger.Warn(t.Exception, "SignalR broadcast failed"), TaskContinuationOptions.OnlyOnFaulted);

        var eventName = GetNamedEvent(message);
        if (!string.IsNullOrEmpty(eventName))
        {
            _logger.Trace("Broadcasting direct SignalR event: {0}", eventName);
            _hubContext.Clients.All.SendAsync(eventName, message.Body)
                .ContinueWith(t => _logger.Warn(t.Exception, "SignalR named event broadcast failed"), TaskContinuationOptions.OnlyOnFaulted);
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

        if (string.Equals(message.Name, "Command", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message.Name, "System", StringComparison.OrdinalIgnoreCase))
        {
            return "CommandCompleted";
        }

        if (message.Name is "TorrentAdded" or "TorrentUpdated" or "TorrentDeleted" or "SeedingStatsUpdated" or "HealthCheckCompleted" or "CommandCompleted")
        {
            return message.Name;
        }

        return null;
    }
}

