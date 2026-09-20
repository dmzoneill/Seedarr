using System;

namespace NzbDrone.Core.Blocklist;

public class BlocklistQuotaExceededException : Exception
{
    public BlocklistQuotaExceededException(string message)
        : base(message)
    {
    }

    public BlocklistQuotaExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
