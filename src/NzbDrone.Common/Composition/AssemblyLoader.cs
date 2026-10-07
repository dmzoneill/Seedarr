using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NLog;

namespace NzbDrone.Common.Composition;

public static class AssemblyLoader
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public static List<Assembly> Load(List<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var assemblies = new List<Assembly>();
        var failed = new List<string>();
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                var label = DescribeInvalidAssemblyName(name);
                Logger.Error("Assembly name must not be null, empty, or whitespace (got {0})", label);
                failed.Add(label);
                continue;
            }

            var path = Path.Combine(baseDir, $"{name}.dll");
            if (File.Exists(path))
            {
                try
                {
                    assemblies.Add(Assembly.LoadFrom(path));
                }
                catch (Exception ex) when (ex is FileNotFoundException or BadImageFormatException or FileLoadException)
                {
                    Logger.Warn(ex, "Could not load assembly {0}", name);
                    failed.Add(name);
                }
            }
            else
            {
                try
                {
                    assemblies.Add(Assembly.Load(name));
                }
                catch (Exception ex) when (ex is FileNotFoundException or BadImageFormatException or FileLoadException)
                {
                    Logger.Warn(ex, "Could not load assembly {0}", name);
                    failed.Add(name);
                }
            }
        }

        if (failed.Count > 0)
        {
            throw new HostStartupException(
                "Required assemblies could not be loaded: " + string.Join(", ", failed));
        }

        return assemblies;
    }

    private static string DescribeInvalidAssemblyName(string name)
    {
        if (name == null)
        {
            return "<null>";
        }

        if (name.Length == 0)
        {
            return "<empty>";
        }

        return "<whitespace>";
    }
}
