using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.ThingiProvider;

public class ProviderRepository<TProviderDefinition> : BasicRepository<TProviderDefinition>, IProviderRepository<TProviderDefinition>
    where TProviderDefinition : ProviderDefinition, new()
{
    public ProviderRepository(IDatabase database)
        : base(database)
    {
    }
}
