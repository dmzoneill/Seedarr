using System;

namespace NzbDrone.Common.EnvironmentInfo;

public interface ISystemClock
{
    DateTime UtcNow { get; }
    DateTimeOffset UtcNowOffset { get; }
}

public class SystemClock : ISystemClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTimeOffset UtcNowOffset => DateTimeOffset.UtcNow;
}
