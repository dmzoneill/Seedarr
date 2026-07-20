using System;

namespace NzbDrone.Core.Exceptions;

public class SeedarrStartupException : Exception
{
    public SeedarrStartupException(string message)
        : base(message)
    {
    }

    public SeedarrStartupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
