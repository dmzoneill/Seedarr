namespace NzbDrone.Core.Configuration;

public static class SpeedLimitThresholds
{
    public static long EffectiveDownloadThresholdBps(IConfigService config)
    {
        if (config == null)
        {
            return 0;
        }

        var kbps = config.AlternativeSpeedEnabled
            ? config.AltDownloadSpeedKbps
            : config.MaxDownloadSpeedKbps;

        return kbps > 0 ? kbps * 1024L : 0;
    }

    public static long EffectiveUploadThresholdBps(IConfigService config)
    {
        if (config == null)
        {
            return 0;
        }

        var kbps = config.AlternativeSpeedEnabled
            ? config.AltUploadSpeedKbps
            : config.MaxUploadSpeedKbps;

        return kbps > 0 ? kbps * 1024L : 0;
    }
}
