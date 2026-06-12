using NzbDrone.Common;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Trackers;

public interface ITrackerProviderFactory : IProviderFactory<ITrackerProvider, TrackerProviderDefinition>
{
}

public class TrackerProviderFactory : ProviderFactory<ITrackerProvider, TrackerProviderDefinition>, ITrackerProviderFactory
{
    public TrackerProviderFactory(
        ITrackerProviderRepository providerRepository,
        IServiceFactory serviceFactory)
        : base(providerRepository, serviceFactory)
    {
    }
}
