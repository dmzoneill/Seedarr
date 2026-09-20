using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Datastore;

public class DatabaseVacuumCommand : Command
{
    public int? MaxPages { get; set; } = 5000;
}
