using System;
using System.Reflection;

namespace NzbDrone.Common.EnvironmentInfo;

public static class BuildInfo
{
    static BuildInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        Version = assembly.GetName().Version ?? new Version(0, 1, 0);
    }

    public static Version Version { get; }
    public static string AppName => "Seedarr";
    public static string Branch => "main";
}
