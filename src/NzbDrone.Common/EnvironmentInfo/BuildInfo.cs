using System;
using System.IO;
using System.Reflection;

namespace NzbDrone.Common.EnvironmentInfo;

public static class BuildInfo
{
    static BuildInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyVersion = assembly.GetName().Version ?? new Version(0, 1, 0);

        var versionFromFile = ReadVersionFile();
        Version = versionFromFile ?? assemblyVersion;
    }

    public static Version Version { get; }
    public static string AppName => "Seedarr";
    public static string Branch => "main";

    private static Version ReadVersionFile()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "version"),
            Path.Combine(Directory.GetCurrentDirectory(), "version"),
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var content = File.ReadAllText(path).Trim();

                foreach (var line in content.Split('\n'))
                {
                    var trimmed = line.Trim();

                    if (trimmed.StartsWith("version=", StringComparison.OrdinalIgnoreCase))
                    {
                        var versionString = trimmed.Substring("version=".Length).Trim();
                        if (Version.TryParse(versionString, out var parsed))
                        {
                            return parsed;
                        }
                    }

                    if (Version.TryParse(trimmed, out var direct))
                    {
                        return direct;
                    }
                }
            }
            catch
            {
                // Ignore read errors, fall through to assembly version
            }
        }

        return null;
    }
}
