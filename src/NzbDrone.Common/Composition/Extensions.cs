using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DryIoc;
using Microsoft.Extensions.Hosting;

namespace NzbDrone.Common.Composition;

public static class ContainerExtensions
{
    public static Rules WithNzbDroneRules(this Rules rules)
    {
        return rules
            .WithAutoConcreteTypeResolution(IsApplicationConcreteType)
            .WithDefaultReuse(Reuse.Singleton)
            .With(Made.Of(FactoryMethod.ConstructorWithResolvableArguments, Parameters.Of.Details(UseDeclaredDefaultForOptionalDelegate)))
            .WithoutThrowOnRegisteringDisposableTransient();
    }

    public static void AutoAddServices(this IContainer container, List<string> assemblyNames)
    {
        var assemblies = AssemblyLoader.Load(assemblyNames);
        var types = assemblies.SelectMany(AssemblyTypeLoader.GetExportedTypes).ToList();

        AutoAddServices(container, types);
    }

    public static void AutoAddServices(this IContainer container, IEnumerable<Type> types)
    {
        var typeList = types as IList<Type> ?? types.ToList();
        KnownTypes.Register(typeList);

        foreach (var type in typeList)
        {
            if (type.IsInterface || type.IsAbstract || type.IsEnum || type.IsValueType || type.IsSubclassOf(typeof(Attribute)) || type.ContainsGenericParameters || type.GetConstructors().Length == 0)
            {
                continue;
            }

            // Keep the factory from the first scan. A later scan of the same implementation must
            // not replace it or append a second one. Other implementations of the same contract
            // are still added below via AppendNewImplementation.
            if (container.IsRegistered(type))
            {
                continue;
            }

            var interfaces = type.GetInterfaces()
                .Where(i => !IsIgnoredInterface(i))
                .ToArray();

            // IHostedService is not a container contract, so a worker whose other interfaces are
            // ignored still has to be registered. The host factory resolves that singleton.
            var isHostedService = typeof(IHostedService).IsAssignableFrom(type);
            var reuse = DetermineReuse(type, interfaces.Length > 0 || isHostedService);

            if (interfaces.Length > 0)
            {
                container.RegisterMany(
                    new[] { type },
                    reuse,
                    serviceTypeCondition: IsContainerServiceContract,
                    ifAlreadyRegistered: IfAlreadyRegistered.AppendNewImplementation);
            }
            else if (isHostedService || HasExplicitReuseAttribute(type))
            {
                container.Register(type, reuse, ifAlreadyRegistered: IfAlreadyRegistered.Keep);
            }
        }
    }

    public static bool IsIgnoredInterface(Type iface)
    {
        if (iface == typeof(IDisposable) ||
            iface == typeof(IAsyncDisposable) ||
            iface == typeof(IComparable) ||
            iface == typeof(IHostedService))
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

    private static bool HasExplicitReuseAttribute(Type type)
    {
        return type.IsDefined(typeof(TransientAttribute), false)
            || type.IsDefined(typeof(ScopedAttribute), false)
            || type.IsDefined(typeof(SingletonAttribute), false);
    }

    private static IReuse DetermineReuse(Type type, bool hasInterfaces)
    {
        EnsureSingleLifetimeAttribute(type);

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

    private static void EnsureSingleLifetimeAttribute(Type type)
    {
        var attributeCount = 0;

        if (type.IsDefined(typeof(TransientAttribute), false))
        {
            attributeCount++;
        }

        if (type.IsDefined(typeof(ScopedAttribute), false))
        {
            attributeCount++;
        }

        if (type.IsDefined(typeof(SingletonAttribute), false))
        {
            attributeCount++;
        }

        if (attributeCount > 1)
        {
            throw new InvalidOperationException(
                $"Type '{type.FullName}' has multiple lifetime attributes ([Transient], [Scoped], [Singleton]). Only one may be specified.");
        }
    }

    // Optional delegate parameters are test seams (`= null`). DryIoc can always build a Func wrapper,
    // which selects those seams and hands back a task that was never started.
    private static ServiceDetails UseDeclaredDefaultForOptionalDelegate(Request request, ParameterInfo parameter)
    {
        _ = request;

        if (!parameter.IsOptional || !typeof(Delegate).IsAssignableFrom(parameter.ParameterType))
        {
            return null;
        }

        var defaultValue = parameter.DefaultValue;
        if (defaultValue is DBNull)
        {
            defaultValue = null;
        }

        return ServiceDetails.Of(defaultValue);
    }

    // BCL types such as IPAddress, Task, and HttpClient are not services. Auto-constructing them
    // picks test constructors and can throw or return a task that was never started.
    private static bool IsApplicationConcreteType(Request request)
    {
        var ns = request.ServiceType?.Namespace;
        if (string.IsNullOrEmpty(ns))
        {
            return false;
        }

        return ns.Equals("NzbDrone", StringComparison.Ordinal)
            || ns.StartsWith("NzbDrone.", StringComparison.Ordinal)
            || ns.Equals("Seedarr", StringComparison.Ordinal)
            || ns.StartsWith("Seedarr.", StringComparison.Ordinal);
    }

    private static bool IsContainerServiceContract(Type serviceType)
    {
        // BackgroundService is the shared base class. Registering every worker as that
        // contract would make a single resolve ambiguous once each implementation is kept.
        return !IsIgnoredInterface(serviceType) && serviceType != typeof(BackgroundService);
    }
}
