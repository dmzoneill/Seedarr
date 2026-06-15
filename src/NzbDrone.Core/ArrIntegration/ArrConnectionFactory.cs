using NzbDrone.Common;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ArrIntegration;

public interface IArrConnectionFactory : IProviderFactory<IArrConnection, ArrConnectionDefinition>
{
}

public class ArrConnectionFactory : ProviderFactory<IArrConnection, ArrConnectionDefinition>, IArrConnectionFactory
{
    public ArrConnectionFactory(
        IArrConnectionRepository providerRepository,
        IServiceFactory serviceFactory)
        : base(providerRepository, serviceFactory)
    {
    }
}
