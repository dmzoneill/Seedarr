using System;

namespace NzbDrone.Core.WebSeeds;

public class WebSeedException : InvalidOperationException
{
    public WebSeedException(string message)
        : base(message)
    {
    }

    public WebSeedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
