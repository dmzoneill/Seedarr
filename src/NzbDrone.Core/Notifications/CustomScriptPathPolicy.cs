using System;
using System.Collections.Generic;
using System.IO;

namespace NzbDrone.Core.Notifications;

public static class CustomScriptPathPolicy
{
    public static readonly string[] AllowedScriptDirectories = OperatingSystem.IsWindows()
        ? new[] { @"C:\Program Files\Seedarr\Scripts", @"C:\ProgramData\Seedarr\Scripts", @"C:\scripts" }
        : new[] { "/usr/local/bin", "/usr/bin", "/opt/seedarr/scripts", "/var/lib/seedarr/scripts", "/etc/seedarr/scripts", "/scripts" };

    public static bool IsInAllowedDirectory(string fullPath, string customScriptsDir = null)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        var allowed = new List<string>(AllowedScriptDirectories);
        if (!string.IsNullOrWhiteSpace(customScriptsDir))
        {
            allowed.Add(customScriptsDir);
        }

        foreach (var dir in allowed)
        {
            var fullDir = Path.GetFullPath(dir);
            if (!fullDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                fullDir += Path.DirectorySeparatorChar;
            }

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (fullPath.StartsWith(fullDir, comparison) || string.Equals(fullPath, Path.GetFullPath(dir), comparison))
            {
                return true;
            }
        }

        return false;
    }

    public static string AllowedDirectoriesDescription => string.Join(", ", AllowedScriptDirectories);

    public static string ValidateAbsoluteScriptPath(string scriptPath, string customScriptsDir)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            return "Script path is required.";
        }

        if (scriptPath.Contains('\0') || scriptPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return "Script path contains invalid characters.";
        }

        if (!Path.IsPathRooted(scriptPath))
        {
            return "Script path must be an absolute path.";
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(scriptPath);
        }
        catch (Exception ex)
        {
            return $"Invalid script path: {ex.Message}";
        }

        if (!IsInAllowedDirectory(fullPath, customScriptsDir))
        {
            return $"Custom script path '{fullPath}' is not permitted. Scripts must be located within authorized directories ({AllowedDirectoriesDescription}).";
        }

        return null;
    }
}
