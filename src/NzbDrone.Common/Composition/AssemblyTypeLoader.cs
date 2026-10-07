using System;
using System.Linq;
using System.Reflection;
using NLog;

namespace NzbDrone.Common.Composition;

public static class AssemblyTypeLoader
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static Type[] GetExportedTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            foreach (var loaderEx in ex.LoaderExceptions)
            {
                if (loaderEx != null)
                {
                    Logger.Warn(loaderEx, "Could not load exported type from assembly {0}", assembly.GetName().Name);
                }
            }

            return ex.Types.Where(t => t != null).ToArray();
        }
    }
}
