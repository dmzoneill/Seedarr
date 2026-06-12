using NzbDrone.Core.Datastore;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Trackers;

public interface ITrackerProviderRepository : IProviderRepository<TrackerProviderDefinition>
{
}

public class TrackerProviderRepository : ProviderRepository<TrackerProviderDefinition>, ITrackerProviderRepository
{
    public TrackerProviderRepository(IDatabase database)
        : base(database)
    {
    }
}
