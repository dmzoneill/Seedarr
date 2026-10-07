using System;

namespace NzbDrone.Core.Exceptions;

public class DuplicateRemotePathMappingException : InvalidOperationException
{
    public string Host { get; }
    public string RemotePath { get; }

    public DuplicateRemotePathMappingException(string host, string remotePath)
        : base($"Remote path mapping for host '{host}' and remote path '{remotePath}' already exists.")
    {
        Host = host;
        RemotePath = remotePath;
    }

    public DuplicateRemotePathMappingException(string host, string remotePath, string message)
        : base(message)
    {
        Host = host;
        RemotePath = remotePath;
    }

    public DuplicateRemotePathMappingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
