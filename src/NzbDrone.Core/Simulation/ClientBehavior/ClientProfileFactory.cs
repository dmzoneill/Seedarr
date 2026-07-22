using NzbDrone.Common;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Simulation.ClientBehavior;

public interface IClientProfileFactory : IProviderFactory<IClientProfile, ClientProfileDefinition>
{
}

public class ClientProfileFactory : ProviderFactory<IClientProfile, ClientProfileDefinition>, IClientProfileFactory
{
    public ClientProfileFactory(
        IClientProfileRepository providerRepository,
        IServiceFactory serviceFactory)
        : base(providerRepository, serviceFactory)
    {
    }
}
