using System;
using System.Reflection;

namespace NzbDrone.SignalR;

public static class SignalRBroadcastFanout
{
    public static void Broadcast(IBroadcastSignalRMessage broadcaster, SignalRMessage message, string channel = null, int? torrentId = null)
    {
        if (broadcaster == null || message == null)
        {
            return;
        }

        broadcaster.BroadcastMessage(message);

        if (!string.IsNullOrWhiteSpace(channel))
        {
            broadcaster.BroadcastToChannel(channel, message);
        }

        if (torrentId.HasValue && torrentId.Value > 0)
        {
            broadcaster.BroadcastToTorrent(torrentId.Value, message);
        }
    }

    public static bool TryResolveScope(string resourceName, object body, out string channel, out int? torrentId)
    {
        channel = null;
        torrentId = null;

        if (string.IsNullOrWhiteSpace(resourceName))
        {
            return false;
        }

        switch (resourceName.Trim().ToLowerInvariant())
        {
            case "torrent":
            case "torrents":
                channel = "torrents";
                torrentId = TryGetPositiveInt(body, "Id");
                return true;
            case "tracker":
            case "trackers":
                channel = "trackers";
                torrentId = TryGetPositiveInt(body, "TorrentId") ?? TryGetPositiveInt(body, "torrentId");
                return true;
            case "health":
                channel = "health";
                return true;
            case "command":
            case "system":
                channel = "system";
                return true;
            case "task":
                channel = "tasks";
                return true;
            case "seeding":
            case "speedschedule":
                channel = "speeds";
                return true;
            case "automation":
                channel = "activity";
                return true;
            case "category":
            case "tag":
                channel = "activity";
                return true;
            default:
                return false;
        }
    }

    public static int? TryGetPositiveInt(object body, string propertyName)
    {
        if (body == null || string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        var value = body.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
            ?.GetValue(body);

        if (value is int intValue && intValue > 0)
        {
            return intValue;
        }

        if (value is long longValue && longValue > 0 && longValue <= int.MaxValue)
        {
            return (int)longValue;
        }

        return null;
    }
}
