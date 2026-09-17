using NzbDrone.Core.RemotePathMappings;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.RemotePathMappings;

public class RemotePathMappingResource : RestResource
{
    public string Host { get; set; }

    public string RemotePath { get; set; }

    public string LocalPath { get; set; }
}

public class RemotePathMappingTestRequest
{
    public string Host { get; set; }

    public string Path { get; set; }

    public string Direction { get; set; } = "remoteToLocal";
}

public static class RemotePathMappingResourceMapper
{
    public static RemotePathMappingResource ToResource(RemotePathMapping model)
    {
        if (model == null)
        {
            return null;
        }

        return new RemotePathMappingResource
        {
            Id = model.Id,
            Host = model.Host,
            RemotePath = model.RemotePath,
            LocalPath = model.LocalPath,
        };
    }

    public static RemotePathMapping ToModel(RemotePathMappingResource resource)
    {
        if (resource == null)
        {
            return null;
        }

        return new RemotePathMapping
        {
            Id = resource.Id,
            Host = resource.Host,
            RemotePath = resource.RemotePath,
            LocalPath = resource.LocalPath,
        };
    }
}
