using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Jobs;

public class ScheduledTaskCommand : Command
{
    public string TaskName { get; set; } = string.Empty;
}
