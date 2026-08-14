using NzbDrone.Common;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.DownloadClients;

public interface IDownloadClientFactory : IProviderFactory<IDownloadClient, DownloadClientDefinition>
{
}

public class DownloadClientFactory : ProviderFactory<IDownloadClient, DownloadClientDefinition>, IDownloadClientFactory
{
    public DownloadClientFactory(
        IDownloadClientRepository providerRepository,
        IServiceFactory serviceFactory)
        : base(providerRepository, serviceFactory)
    {
    }
}
