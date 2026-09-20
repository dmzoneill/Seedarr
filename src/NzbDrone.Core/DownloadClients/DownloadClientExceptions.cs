using System;

namespace NzbDrone.Core.DownloadClients;

public class DownloadClientException : Exception
{
    public DownloadClientException(string message)
        : base(message)
    {
    }

    public DownloadClientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public class DownloadClientAuthenticationException : DownloadClientException
{
    public DownloadClientAuthenticationException(string message)
        : base(message)
    {
    }

    public DownloadClientAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public class DownloadClientUnavailableException : DownloadClientException
{
    public DownloadClientUnavailableException(string message)
        : base(message)
    {
    }

    public DownloadClientUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
