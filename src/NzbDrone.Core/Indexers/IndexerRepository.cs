using NzbDrone.Core.Datastore;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers;

public interface IIndexerRepository : IProviderRepository<IndexerDefinition>
{
}

public class IndexerRepository : ProviderRepository<IndexerDefinition>, IIndexerRepository
{
    public IndexerRepository(IDatabase database)
        : base(database)
    {
    }
}
