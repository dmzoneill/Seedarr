using System;

namespace NzbDrone.Common.Composition;

public class HostStartupException : Exception
{
    public HostStartupException(string message)
        : base(message)
    {
    }

    public HostStartupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
