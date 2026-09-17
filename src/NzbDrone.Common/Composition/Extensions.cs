using System;
using System.Collections.Generic;
using System.Linq;
using DryIoc;

namespace NzbDrone.Common.Composition;

public static class ContainerExtensions
{
    public static Rules WithNzbDroneRules(this Rules rules)
    {
        return rules
            .WithAutoConcreteTypeResolution()
            .WithDefaultReuse(Reuse.Singleton)
            .With(Made.Of(FactoryMethod.ConstructorWithResolvableArguments))
            .WithoutThrowOnRegisteringDisposableTransient();
    }

    public static void AutoAddServices(this IContainer container, List<string> assemblyNames)
    {
        var assemblies = AssemblyLoader.Load(assemblyNames);
        var types = assemblies.SelectMany(a => a.GetExportedTypes()).ToList();

        AutoAddServices(container, types);
    }

    public static void AutoAddServices(this IContainer container, IEnumerable<Type> types)
    {
        var typeList = types as IList<Type> ?? types.ToList();
        KnownTypes.Register(typeList);

        foreach (var type in typeList)
        {
            if (type.IsInterface || type.IsAbstract || type.IsEnum || type.IsValueType || type.IsSubclassOf(typeof(Attribute)) || type.GetConstructors().Length == 0)
            {
                continue;
            }

            var interfaces = type.GetInterfaces()
                .Where(i => !IsIgnoredInterface(i))
                .ToArray();

            var reuse = DetermineReuse(type, interfaces.Length > 0);

            if (interfaces.Length > 0)
            {
                container.RegisterMany(
                    new[] { type },
                    reuse,
                    serviceTypeCondition: t => !IsIgnoredInterface(t),
                    ifAlreadyRegistered: IfAlreadyRegistered.AppendNotKeyed);
            }
            else
            {
                container.Register(type, reuse, ifAlreadyRegistered: IfAlreadyRegistered.Keep);
            }
        }
    }

    public static bool IsIgnoredInterface(Type iface)
    {
        if (iface == typeof(IDisposable) ||
            iface == typeof(IAsyncDisposable) ||
            iface == typeof(IComparable))
        {
            return true;
        }

        if (iface.IsGenericType)
        {
            var genericDef = iface.GetGenericTypeDefinition();
            if (genericDef == typeof(IComparable<>) ||
                genericDef == typeof(IEquatable<>))
            {
                return true;
            }
        }

        return false;
    }

    private static IReuse DetermineReuse(Type type, bool hasInterfaces)
    {
        if (type.IsDefined(typeof(TransientAttribute), false))
        {
            return Reuse.Transient;
        }

        if (type.IsDefined(typeof(ScopedAttribute), false))
        {
            return Reuse.Scoped;
        }

        if (type.IsDefined(typeof(SingletonAttribute), false))
        {
            return Reuse.Singleton;
        }

        return hasInterfaces ? Reuse.Singleton : Reuse.Transient;
    }
}
