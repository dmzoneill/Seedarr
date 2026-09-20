namespace NzbDrone.Core.Datastore;

public enum WalCheckpointMode
{
    Passive,
    Full,
    Restart,
    Truncate
}
