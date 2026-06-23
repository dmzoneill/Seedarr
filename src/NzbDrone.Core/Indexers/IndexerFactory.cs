using NzbDrone.Common;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers;

public interface IIndexerFactory : IProviderFactory<IIndexer, IndexerDefinition>
{
}

public class IndexerFactory : ProviderFactory<IIndexer, IndexerDefinition>, IIndexerFactory
{
    public IndexerFactory(
        IIndexerRepository providerRepository,
        IServiceFactory serviceFactory)
        : base(providerRepository, serviceFactory)
    {
    }
}
