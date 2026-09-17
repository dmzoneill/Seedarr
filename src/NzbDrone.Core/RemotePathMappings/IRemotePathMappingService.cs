using System.Collections.Generic;

namespace NzbDrone.Core.RemotePathMappings;

public interface IRemotePathMappingService
{
    List<RemotePathMapping> All();
    RemotePathMapping Get(int id);
    RemotePathMapping Add(RemotePathMapping mapping);
    void Update(RemotePathMapping mapping);
    void Delete(int id);
    string Remap(string host, string remotePath);
    RemotePathMappingTestResult TestMapping(string host, string path, string direction = "remoteToLocal");
}
