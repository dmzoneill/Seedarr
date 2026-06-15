using NzbDrone.Core.Datastore;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ArrIntegration;

public interface IArrConnectionRepository : IProviderRepository<ArrConnectionDefinition>
{
}

public class ArrConnectionRepository : ProviderRepository<ArrConnectionDefinition>, IArrConnectionRepository
{
    public ArrConnectionRepository(IDatabase database)
        : base(database)
    {
    }
}
