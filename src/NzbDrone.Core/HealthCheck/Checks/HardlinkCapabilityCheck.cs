using System;
using System.IO;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.HealthCheck.Checks;

public class HardlinkCapabilityCheck : IHealthCheck
{
    private readonly IConfigService _configService;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly IHardlinkProvider _hardlinkProvider;
    private readonly Logger _logger;

    public HardlinkCapabilityCheck(
        IConfigService configService,
        IAppFolderInfo appFolderInfo = null,
        IHardlinkProvider hardlinkProvider = null)
    {
        _configService = configService;
        _appFolderInfo = appFolderInfo;
        _hardlinkProvider = hardlinkProvider ?? new HardlinkProvider();
        _logger = LogManager.GetCurrentClassLogger();
    }

    public HealthCheckResult Check()
    {
        try
        {
            var targetDir = _configService?.TorrentSaveDirectory;
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                targetDir = _configService?.WatchFolderPath;
            }

            if (string.IsNullOrWhiteSpace(targetDir))
            {
                return HealthCheckResult.Ok("HardlinkCapability");
            }

            if (!Directory.Exists(targetDir))
            {
                return HealthCheckResult.Notice(
                    "HardlinkCapability",
                    $"Configured download directory does not exist: '{targetDir}'");
            }

            var testFile = Path.Combine(targetDir, $".seedarr_hl_test_{Guid.NewGuid():N}.tmp");
            var testLink = Path.Combine(targetDir, $".seedarr_hl_test_{Guid.NewGuid():N}.link");

            try
            {
                File.WriteAllBytes(testFile, new byte[] { 0x01 });

                var success = _hardlinkProvider.TryCreateHardLink(testFile, testLink, out var error);
                if (!success)
                {
                    _logger.Warn("Hardlink creation failed in download directory {0}: {1}", targetDir, error);
                    return HealthCheckResult.Warning(
                        "HardlinkCapability",
                        $"Hardlinks cannot be created in download directory '{targetDir}'. Filesystem or cross-device boundaries prevent hardlinking ({error}). Atomic imports will fail, causing slow copies and duplicate disk usage.");
                }

                return HealthCheckResult.Ok("HardlinkCapability");
            }
            finally
            {
                try
                {
                    if (File.Exists(testLink))
                    {
                        File.Delete(testLink);
                    }
                }
                catch
                {
                    // Ignore cleanup error
                }

                try
                {
                    if (File.Exists(testFile))
                    {
                        File.Delete(testFile);
                    }
                }
                catch
                {
                    // Ignore cleanup error
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to verify hardlink capability in download directory");
            return HealthCheckResult.Warning(
                "HardlinkCapability",
                $"Unable to verify hardlink capability: {ex.Message}");
        }
    }
}
