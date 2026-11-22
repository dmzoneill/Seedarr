using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Composition;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.ArrIntegration.Webhook;
using NzbDrone.Core.Dht;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Notifications.Telegram;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Encryption;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Transport;
using NzbDrone.Host;
using NzbDrone.SignalR;
using Seedarr.Api.V1.Network;

namespace NzbDrone.Core.Test.Composition;

[TestFixture]
[NonParallelizable]
public class ServiceRegistrationCoverageTest
{
    private static readonly List<string> Assemblies = new()
    {
        "Seedarr.Host",
        "Seedarr.Core",
        "Seedarr.Common",
        "Seedarr.SignalR",
        "Seedarr.Http",
        "Seedarr.Api.V1",
    };

    [Test]
    public void container_resolves_every_handler_health_check_scheduled_task_and_one_hosted_instance()
    {
        KnownTypes.Clear();
        var tempDir = Path.Combine(Path.GetTempPath(), "seedarr-di-coverage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        IContainer adapted = null;

        try
        {
            var container = new Container(rules => rules.WithNzbDroneRules());
            container.RegisterInstance(new StartupContext("--data=" + tempDir));
            container.AutoAddServices(Assemblies);
            container.Register(
                typeof(IBasicRepository<>),
                typeof(BasicRepository<>),
                Reuse.Singleton,
                ifAlreadyRegistered: IfAlreadyRegistered.Keep);
            container.RegisterDelegate<IDatabase>(resolver => resolver.Resolve<IMainDatabase>(), Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.RegisterInstance(Substitute.For<IHubContext<MessageHub>>());

            var types = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => Assemblies.Contains(assembly.GetName().Name, StringComparer.OrdinalIgnoreCase))
                .SelectMany(AssemblyTypeLoader.GetExportedTypes)
                .ToList();

            AssertRegistrationsSurviveRescan(container, typeof(IHealthCheck));
            AssertRegistrationsSurviveRescan(container, typeof(IScheduledTask));

            var services = new ServiceCollection();
            Startup.RegisterApplicationHostedServices(services);

            var factory = new DryIocServiceProviderFactory(container);
            adapted = factory.CreateBuilder(services);

            AssertEveryImplementation(adapted, types, typeof(IHealthCheck));
            AssertEveryImplementation(adapted, types, typeof(IScheduledTask));
            AssertEveryEventHandler(adapted, types);

            Assert.That(adapted.ResolveMany<IHandle<VpnKillSwitchTriggeredEvent>>().Count(), Is.GreaterThan(1));
            Assert.That(adapted.ResolveMany<IHandle<ApplicationStartedEvent>>().Count(), Is.GreaterThan(1));
            Assert.That(adapted.ResolveMany<IHandle<TorrentPausedEvent>>().Count(), Is.GreaterThan(1));

            var hosted = adapted.ResolveMany<IHostedService>().ToList();
            AssertSingleHostedInstance<IChokeManager>(adapted, hosted);
            AssertSingleHostedInstance<IUtpManager>(adapted, hosted);
            AssertSingleHostedInstance<IPeerServer>(adapted, hosted);
            AssertSingleHostedInstance<IDhtService>(adapted, hosted);
            AssertSingleHostedInstance<IUpnpService>(adapted, hosted);
            AssertSingleHostedInstance<IDhKeyPool>(adapted, hosted);
            AssertSingleHostedInstance<IExternalIpService>(adapted, hosted);

            foreach (var backgroundType in KnownTypes.GetImplementations(typeof(BackgroundService)))
            {
                var matches = hosted.Where(service => service.GetType() == backgroundType).ToList();
                Assert.That(matches, Has.Count.EqualTo(1), backgroundType.Name);
                Assert.That(adapted.Resolve(backgroundType), Is.SameAs(matches[0]), backgroundType.Name);
            }

            var appLifetimes = hosted.OfType<AppLifetime>().ToList();
            Assert.That(appLifetimes, Has.Count.EqualTo(1));
            Assert.That(adapted.Resolve<AppLifetime>(), Is.SameAs(appLifetimes[0]));

            var vpnUtp = adapted.ResolveMany<IHandle<VpnKillSwitchTriggeredEvent>>().OfType<UtpManager>().Single();
            Assert.That(vpnUtp, Is.SameAs(adapted.Resolve<IUtpManager>()));

            var addedPeer = adapted.ResolveMany<IHandle<TorrentAddedEvent>>().OfType<PeerServer>().Single();
            Assert.That(addedPeer, Is.SameAs(adapted.Resolve<IPeerServer>()));

            var startedTasks = adapted.ResolveMany<IHandle<ApplicationStartedEvent>>().OfType<TaskManager>().Single();
            Assert.That(startedTasks, Is.SameAs(adapted.Resolve<ITaskManager>()));

            AssertProductionDelegate(Field<Delegate>(adapted.Resolve<UpnpService>(), "_deviceDiscoverer"), "UpnpService");
            var engine = adapted.Resolve<PortMappingEngine>();
            AssertProductionDelegate(Field<Delegate>(engine, "_udpProber"), "PortMappingEngine");
            AssertProductionDelegate(Field<Delegate>(engine, "_upnpProber"), "PortMappingEngine");
            Assert.That(engine.NatPmpClient, Is.Null);
            AssertProductionDelegate(Field<Delegate>(adapted.Resolve<FFprobeMediaInspector>(), "_processExecutor"), "FFprobeMediaInspector");

            var webhook = adapted.Resolve<ArrWebhookService>();
            Assert.That(
                Field<HttpClient>(webhook, "_client"),
                Is.SameAs(StaticField<HttpClient>(typeof(ArrWebhookService), "SharedClient")));

            var telegram = adapted.Resolve<TelegramNotification>();
            Assert.That(
                Field<HttpClient>(telegram, "_httpClient"),
                Is.SameAs(StaticField<HttpClient>(typeof(TelegramNotification), "SharedHttpClient")));

            Assert.That(Field<HttpClient>(adapted.Resolve<ExternalIpService>(), "_injectedClient"), Is.Null);
            Assert.That(adapted.Resolve<PortMappingController>(), Is.Not.Null);

            var toDispose = adapted;
            adapted = null;
            Assert.DoesNotThrow(() => toDispose.Dispose());
        }
        finally
        {
            adapted?.Dispose();
            KnownTypes.Clear();
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (IOException)
                {
                    // SQLite may still be releasing the temp database.
                }
            }
        }
    }

    private static void AssertRegistrationsSurviveRescan(IContainer container, Type serviceType)
    {
        var before = ImplementationTypes(container, serviceType);
        Assert.That(before.Count, Is.GreaterThan(1), serviceType.Name);
        container.AutoAddServices(Assemblies);
        Assert.That(ImplementationTypes(container, serviceType), Is.EquivalentTo(before));
    }

    private static void AssertEveryImplementation(IResolver container, IEnumerable<Type> types, Type serviceType)
    {
        var expected = ConcreteImplementations(types, serviceType);
        var resolved = container.ResolveMany(serviceType).Select(service => service.GetType()).ToList();
        Assert.That(resolved, Is.Unique, serviceType.Name);
        Assert.That(resolved, Is.EquivalentTo(expected), serviceType.Name);
    }

    private static void AssertEveryEventHandler(IResolver container, IEnumerable<Type> types)
    {
        var typeList = types as IList<Type> ?? types.ToList();
        var contracts = typeList
            .SelectMany(type => type.GetInterfaces())
            .Where(iface => iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IHandle<>))
            .Distinct()
            .ToList();

        Assert.That(contracts.Count, Is.GreaterThan(1));
        foreach (var contract in contracts)
        {
            AssertEveryImplementation(container, typeList, contract);
        }
    }

    private static void AssertSingleHostedInstance<TService>(IResolver container, IEnumerable<IHostedService> hosted)
        where TService : class
    {
        var fromContract = container.Resolve<TService>();
        var fromHost = hosted.OfType<TService>().ToList();
        Assert.That(fromHost, Has.Count.EqualTo(1), typeof(TService).Name);
        Assert.That(fromHost[0], Is.SameAs(fromContract), typeof(TService).Name);
    }

    private static List<Type> ConcreteImplementations(IEnumerable<Type> types, Type serviceType)
    {
        return types
            .Where(type => serviceType.IsAssignableFrom(type)
                && type.IsClass
                && !type.IsAbstract
                && !type.ContainsGenericParameters
                && type.GetConstructors().Length > 0)
            .Distinct()
            .ToList();
    }

    private static List<Type> ImplementationTypes(IContainer container, Type serviceType)
    {
        return container.GetServiceRegistrations()
            .Where(registration => registration.ServiceType == serviceType)
            .Select(registration => registration.ImplementationType)
            .Where(type => type != null)
            .Distinct()
            .ToList();
    }

    private static void AssertProductionDelegate(Delegate callback, string ownerName)
    {
        Assert.That(callback, Is.Not.Null);
        var owner = callback.Method.DeclaringType?.FullName ?? string.Empty;
        Assert.That(owner, Does.Not.Contain("DryIoc"), owner);
        Assert.That(owner, Does.Contain(ownerName));
    }

    private static T Field<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, name);
        return (T)field.GetValue(instance);
    }

    private static T StaticField<T>(Type type, string name)
    {
        var field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, name);
        return (T)field.GetValue(null);
    }
}
