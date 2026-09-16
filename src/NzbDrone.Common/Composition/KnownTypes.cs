using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Common.Composition;

public static class KnownTypes
{
    private static readonly HashSet<Type> _types = new();
    private static readonly object _lock = new();

    public static int Count
    {
        get
        {
            lock (_lock)
            {
                return _types.Count;
            }
        }
    }

    public static void Register(List<Type> types) => Register((IEnumerable<Type>)types);

    public static void Register(IEnumerable<Type> types)
    {
        if (types == null)
        {
            return;
        }

        lock (_lock)
        {
            foreach (var t in types)
            {
                if (t != null)
                {
                    _types.Add(t);
                }
            }
        }
    }

    public static void Clear()
    {
        lock (_lock)
        {
            _types.Clear();
        }
    }

    public static List<Type> GetImplementations(Type contractType)
    {
        if (contractType == null)
        {
            return new List<Type>();
        }

        lock (_lock)
        {
            return _types
                .Where(t => contractType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToList();
        }
    }
}
