using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Seeding.Scheduling;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerBoost;
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
        TableMapping.Register<TrackerEntry>("TrackerEntries");
        TableMapping.Register<ClientProfileDefinition>("ClientProfileDefinitions");
        TableMapping.Register<TrackerProviderDefinition>("TrackerProviderDefinitions");
        TableMapping.Register<ArrConnectionDefinition>("ArrConnectionDefinitions");
        TableMapping.Register<DownloadClientDefinition>("DownloadClientDefinitions");
        TableMapping.Register<IndexerDefinition>("IndexerDefinitions");
        TableMapping.Register<NotificationDefinition>("NotificationDefinitions");
        TableMapping.Register<PeerConnectionLog>("PeerConnectionLogs");
        TableMapping.Register<Tag>("Tags");
        TableMapping.Register<SpeedSchedule>("SpeedSchedules");
        TableMapping.Register<DownloadHistory>("DownloadHistory");
        TableMapping.Register<TrackerBoostTracker>("TrackerBoostTrackers");
    }
}
