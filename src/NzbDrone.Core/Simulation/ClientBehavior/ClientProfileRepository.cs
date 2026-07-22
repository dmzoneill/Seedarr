using NzbDrone.Core.Datastore;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Simulation.ClientBehavior;

public interface IClientProfileRepository : IProviderRepository<ClientProfileDefinition>
{
}

public class ClientProfileRepository : ProviderRepository<ClientProfileDefinition>, IClientProfileRepository
{
    public ClientProfileRepository(IDatabase database)
        : base(database)
    {
    }
}
