using System;

namespace NzbDrone.Core.Seeding.Scheduling;

public static class SpeedLimitMerger
{
    public static SpeedLimits Apply(SpeedLimits limits, long uploadBps, long downloadBps)
    {
        if (uploadBps > 0)
        {
            limits.MaxUploadSpeed = limits.MaxUploadSpeed == SpeedLimits.Unlimited
                ? uploadBps
                : Math.Min(limits.MaxUploadSpeed, uploadBps);
        }

        if (downloadBps > 0)
        {
            limits.MaxDownloadSpeed = limits.MaxDownloadSpeed == SpeedLimits.Unlimited
                ? downloadBps
                : Math.Min(limits.MaxDownloadSpeed, downloadBps);
        }

        return limits;
    }
}
