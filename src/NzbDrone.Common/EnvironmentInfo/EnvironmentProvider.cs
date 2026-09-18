using System;
using System.IO;

namespace NzbDrone.Common.EnvironmentInfo;

public class EnvironmentProvider : IEnvironmentProvider, IPlatformInfo
{
    public static bool IsDocker => CheckIsDocker();

    bool IEnvironmentProvider.IsDocker => IsDocker;

    bool IPlatformInfo.IsDocker => IsDocker;

    public string Platform => OsInfo.Os;

    public string GetEnvironmentVariable(string variable)
    {
        return Environment.GetEnvironmentVariable(variable);
    }

    public static bool CheckIsDocker()
    {
        try
        {
            if (File.Exists("/.dockerenv"))
            {
                return true;
            }

            if (Environment.GetEnvironmentVariable("SEEDARR_IN_DOCKER") != null)
            {
                return true;
            }

            if (string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch
        {
            // Ignore access errors
        }

        return false;
    }
}
