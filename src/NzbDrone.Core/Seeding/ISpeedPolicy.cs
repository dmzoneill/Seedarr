using System;
using System.Collections.Generic;
using NzbDrone.Core.Seeding.Scheduling;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Seeding;

public interface ISpeedPolicy
{
    SpeedLimits GetEffectiveLimits();
    int GetUploadLimit(Torrent torrent);
    int GetDownloadLimit(Torrent torrent);
    void ProcessDownloading(List<Torrent> torrents, SpeedLimits limits, TimeSpan tickInterval);
    void ProcessSeeding(List<Torrent> torrents, SpeedLimits limits, TimeSpan tickInterval);

    /// <summary>
    /// Opens one download budget for the following download and seeding passes.
    /// Residual seeding download then spends only what the downloading pass left.
    /// </summary>
    void BeginSharedDownloadBudget(SpeedLimits limits);

    /// <summary>
    /// Closes the shared download budget so the next tick cannot reuse it.
    /// </summary>
    void EndSharedDownloadBudget();
    long CalculateEffectiveUploadSpeed(Torrent torrent, long targetSpeed, DateTime? now = null);
    long ComputeUploadSpeed(Torrent torrent, long targetSpeed, DateTime? now = null);
    void SetSeedingStartTime(int torrentId, DateTime startTime);
    void ResetSeedingStartTime(int torrentId);
}
