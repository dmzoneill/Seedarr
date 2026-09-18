using System;

namespace NzbDrone.Core.Seeding.Scheduling;

public static class SpeedLimitMerger
{
    public static SpeedLimits Apply(SpeedLimits limits, long uploadBps, long downloadBps)
    {
        if (limits == null)
        {
            return null;
        }

        if (uploadBps >= 0)
        {
            limits.MaxUploadSpeed = limits.MaxUploadSpeed == SpeedLimits.Unlimited
                ? uploadBps
                : Math.Min(limits.MaxUploadSpeed, uploadBps);
        }

        if (downloadBps >= 0)
        {
            limits.MaxDownloadSpeed = limits.MaxDownloadSpeed == SpeedLimits.Unlimited
                ? downloadBps
                : Math.Min(limits.MaxDownloadSpeed, downloadBps);
        }

        return limits;
    }

    public static SpeedLimits Apply(
        SpeedLimits limits,
        long configUploadBps,
        long configDownloadBps,
        bool alternativeSpeedEnabled)
    {
        if (limits == null)
        {
            return null;
        }

        if (limits.IsScheduleActive && !alternativeSpeedEnabled)
        {
            if (limits.MaxUploadSpeed == SpeedLimits.Unlimited && configUploadBps >= 0)
            {
                limits.MaxUploadSpeed = configUploadBps;
            }

            if (limits.MaxDownloadSpeed == SpeedLimits.Unlimited && configDownloadBps >= 0)
            {
                limits.MaxDownloadSpeed = configDownloadBps;
            }

            return limits;
        }

        return Apply(limits, configUploadBps, configDownloadBps);
    }
}
