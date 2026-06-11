using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.ThingiProvider;

public interface IProviderRepository<TProviderDefinition> : IBasicRepository<TProviderDefinition>
    where TProviderDefinition : ProviderDefinition, new()
{
}
