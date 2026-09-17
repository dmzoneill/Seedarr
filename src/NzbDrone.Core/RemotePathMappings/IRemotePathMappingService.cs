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
    string RemapRemoteToLocal(string host, string remotePath);
    string RemapLocalToRemote(string host, string localPath);
    RemotePathMappingTestResult TestMapping(string host, string path, string direction = "remoteToLocal");
}
