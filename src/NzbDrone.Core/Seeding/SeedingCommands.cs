using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Seeding;

public class StartSeedingCommand : Command
{
    public int TorrentId { get; set; }
}

public class StopSeedingCommand : Command
{
    public int TorrentId { get; set; }
}

public class StartAllSeedingCommand : Command
{
}

public class StopAllSeedingCommand : Command
{
}
