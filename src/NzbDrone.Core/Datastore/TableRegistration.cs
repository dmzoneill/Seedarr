using NzbDrone.Core.Configuration;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Datastore;

public static class TableRegistration
{
    public static void RegisterTables()
    {
        TableMapping.Register<CommandModel>("Commands");
        TableMapping.Register<ConfigModel>("Config");
        TableMapping.Register<ScheduledTask>("ScheduledTasks");
    }
}
