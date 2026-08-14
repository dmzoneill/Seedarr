using NzbDrone.Core.Datastore;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.DownloadClients;

public interface IDownloadClientRepository : IProviderRepository<DownloadClientDefinition>
{
}

public class DownloadClientRepository : ProviderRepository<DownloadClientDefinition>, IDownloadClientRepository
{
    public DownloadClientRepository(IDatabase database)
        : base(database)
    {
    }
}
