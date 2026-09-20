using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.RemotePathMappings;

public class RemotePathMappingRepository : BasicRepository<RemotePathMapping>, IRemotePathMappingRepository
{
    public RemotePathMappingRepository(IDatabase database)
        : base(database)
    {
    }
}
