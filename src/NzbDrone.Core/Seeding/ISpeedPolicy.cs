using System;
using System.Collections.Generic;
using NzbDrone.Core.Seeding.Scheduling;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Seeding;

public interface ISpeedPolicy
{
    SpeedLimits GetEffectiveLimits();
    void ProcessDownloading(List<Torrent> torrents, SpeedLimits limits, TimeSpan tickInterval);
    void ProcessSeeding(List<Torrent> torrents, SpeedLimits limits, TimeSpan tickInterval);
}
