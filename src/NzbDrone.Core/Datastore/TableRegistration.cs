using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Datastore;

public static class TableRegistration
{
    public static void RegisterTables()
    {
        TableMapping.Register<CommandModel>("Commands");
        TableMapping.Register<ConfigModel>("Config");
        TableMapping.Register<ScheduledTask>("ScheduledTasks");
        TableMapping.Register<Torrent>("Torrents");
        TableMapping.Register<TorrentFile>("TorrentFiles");
        TableMapping.Register<ClientProfileDefinition>("ClientProfileDefinitions");
        TableMapping.Register<TrackerProviderDefinition>("TrackerProviderDefinitions");
        TableMapping.Register<ArrConnectionDefinition>("ArrConnectionDefinitions");
        TableMapping.Register<NotificationDefinition>("NotificationDefinitions");
    }
}
