using System;
using System.Collections.Generic;
using System.IO;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.HealthCheck.Checks;

public class AppFolderPermissionsCheck : IProvideHealthCheck
{
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly Func<string, bool> _isWritableCheck;

    public AppFolderPermissionsCheck(
        IAppFolderInfo appFolderInfo,
        Func<string, bool> isWritableCheck = null)
    {
        _appFolderInfo = appFolderInfo;
        _isWritableCheck = isWritableCheck;
    }

    public HealthCheckResult Check()
    {
        if (_appFolderInfo == null || string.IsNullOrWhiteSpace(_appFolderInfo.AppDataFolder))
        {
            return new HealthCheckResult(
                GetType(),
                HealthCheckResultType.Error,
                "AppData folder is not configured or path is empty.");
        }

        var appData = _appFolderInfo.AppDataFolder;
        if (!CheckFolderWritable(appData, out var appDataError))
        {
            return new HealthCheckResult(
                GetType(),
                HealthCheckResultType.Error,
                $"AppData folder '{appData}' is not writable: {appDataError}");
        }

        var unwritableSubfolders = new List<string>();

        var logsFolder = Path.Combine(appData, "logs");
        if (!CheckFolderWritable(logsFolder, out var logsError))
        {
            unwritableSubfolders.Add($"logs ('{logsFolder}': {logsError})");
        }

        var backupsFolder = Directory.Exists(Path.Combine(appData, "backups"))
            ? Path.Combine(appData, "backups")
            : Path.Combine(appData, "Backups");

        if (!CheckFolderWritable(backupsFolder, out var backupsError))
        {
            unwritableSubfolders.Add($"backups ('{backupsFolder}': {backupsError})");
        }

        if (unwritableSubfolders.Count > 0)
        {
            return new HealthCheckResult(
                GetType(),
                HealthCheckResultType.Warning,
                $"Application subfolders are not writable: {string.Join(", ", unwritableSubfolders)}");
        }

        return new HealthCheckResult(GetType(), HealthCheckResultType.Ok);
    }

    private bool CheckFolderWritable(string folderPath, out string error)
    {
        error = null;

        if (_isWritableCheck != null)
        {
            try
            {
                var writable = _isWritableCheck(folderPath);
                if (!writable)
                {
                    error = "Permission denied";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        try
        {
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            var testFile = Path.Combine(folderPath, $".perm_test_{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(testFile, Array.Empty<byte>());
                return true;
            }
            finally
            {
                if (File.Exists(testFile))
                {
                    try
                    {
                        File.Delete(testFile);
                    }
                    catch
                    {
                        // Best-effort cleanup
                    }
                }
            }
        }
        catch (UnauthorizedAccessException uex)
        {
            error = uex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
