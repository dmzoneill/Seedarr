using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace NzbDrone.Common.Composition;

public static class AssemblyLoader
{
    public static List<Assembly> Load(List<string> names)
    {
        var assemblies = new List<Assembly>();
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        foreach (var name in names)
        {
            var path = Path.Combine(baseDir, $"{name}.dll");
            if (File.Exists(path))
            {
                assemblies.Add(Assembly.LoadFrom(path));
            }
            else
            {
                try
                {
                    assemblies.Add(Assembly.Load(name));
                }
                catch
                {
                    // Skip assemblies that cannot be loaded
                }
            }
        }

        return assemblies;
    }
}
