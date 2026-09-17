using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NLog;

namespace NzbDrone.Common.EnvironmentInfo;

public static class BuildInfo
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
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
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "version"),
            Path.Combine(Directory.GetCurrentDirectory(), "version"),
        };

        try
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                candidates.Add(Path.Combine(dir.FullName, "version"));
            }
        }
        catch
        {
        }

        try
        {
            for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
            {
                candidates.Add(Path.Combine(dir.FullName, "version"));
            }
        }
        catch
        {
        }

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
                        if (TryParseVersionString(versionString, out var parsed))
                        {
                            return parsed;
                        }
                    }

                    if (TryParseVersionString(trimmed, out var direct))
                    {
                        return direct;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or FormatException)
            {
                Logger.Warn(ex, "Could not read version file at {0}", path);
            }
        }

        return null;
    }

    public static bool TryParseVersionString(string raw, out Version parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var clean = raw.Trim().TrimStart('v', 'V');
        if (Version.TryParse(clean, out parsed))
        {
            return true;
        }

        // If direct Version.TryParse fails, strip +build metadata and -prerelease suffix
        var plusIndex = clean.IndexOf('+');
        if (plusIndex >= 0)
        {
            clean = clean[..plusIndex];
        }

        var dashIndex = clean.IndexOf('-');
        if (dashIndex >= 0)
        {
            clean = clean[..dashIndex];
        }

        if (string.IsNullOrWhiteSpace(clean))
        {
            return false;
        }

        if (!clean.Contains('.'))
        {
            clean += ".0";
        }

        return Version.TryParse(clean, out parsed);
    }
}
