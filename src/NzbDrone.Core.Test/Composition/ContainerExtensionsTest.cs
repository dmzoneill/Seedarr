using System;
using System.Linq;
using System.Threading.Tasks;
using DryIoc;
using NUnit.Framework;
using NzbDrone.Common.Composition;

namespace NzbDrone.Core.Test.Composition;

[TestFixture]
public class ContainerExtensionsTest
{
    public interface ICustomService
    {
    }

    public class DisposableService : ICustomService, IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    public class AsyncDisposableService : ICustomService, IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public class ComparableService : ICustomService, IComparable, IComparable<ComparableService>, IEquatable<ComparableService>
    {
        public int CompareTo(object obj) => 0;
        public int CompareTo(ComparableService other) => 0;
        public bool Equals(ComparableService other) => true;
    }

    public class OnlyDisposableService : IDisposable
    {
        public void Dispose()
        {
        }
    }

    public class PlainPocoService
    {
    }

    [Singleton]
    public class SingletonConcreteService
    {
    }

    [Transient]
    public class TransientCustomService : ICustomService
    {
    }

    [Scoped]
    public class ScopedCustomService : ICustomService
    {
    }

    [Singleton]
    [Transient]
    public class ConflictingLifetimeService : ICustomService
    {
    }

    public interface IGenericCustomService<T>
    {
    }

    public class OpenGenericCustomService<T> : IGenericCustomService<T>
    {
    }

    [Test]
    public void AutoAddServices_second_pass_does_not_duplicate_interface_registrations()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());

        container.AutoAddServices(new[] { typeof(DisposableService) });
        container.AutoAddServices(new[] { typeof(DisposableService) });

        var services = container.Resolve<IEnumerable<ICustomService>>();
        Assert.That(services.Count(), Is.EqualTo(1));
    }

    [Test]
    public void AutoAddServices_does_not_register_open_generic_type_definitions()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());

        container.AutoAddServices(new[] { typeof(OpenGenericCustomService<>) });

        Assert.That(container.IsRegistered(typeof(OpenGenericCustomService<>)), Is.False);
        Assert.That(container.IsRegistered(typeof(IGenericCustomService<>)), Is.False);
        Assert.That(container.IsRegistered<IGenericCustomService<string>>(), Is.False);
    }

    [Test]
    public void AutoAddServices_does_not_register_IDisposable_as_service_contract()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());

        container.AutoAddServices(new[] { typeof(DisposableService) });

        Assert.That(container.IsRegistered<IDisposable>(), Is.False);
        Assert.That(container.IsRegistered<ICustomService>(), Is.True);
        Assert.That(container.IsRegistered<DisposableService>(), Is.True);

        var resolved = container.Resolve<ICustomService>();
        Assert.That(resolved, Is.InstanceOf<DisposableService>());
    }

    [Test]
    public void AutoAddServices_does_not_register_IAsyncDisposable_as_service_contract()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());

        container.AutoAddServices(new[] { typeof(AsyncDisposableService) });

        Assert.That(container.IsRegistered<IAsyncDisposable>(), Is.False);
        Assert.That(container.IsRegistered<ICustomService>(), Is.True);
    }

    [Test]
    public void AutoAddServices_does_not_register_IComparable_or_IEquatable()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());

        container.AutoAddServices(new[] { typeof(ComparableService) });

        Assert.That(container.IsRegistered<IComparable>(), Is.False);
        Assert.That(container.IsRegistered<IComparable<ComparableService>>(), Is.False);
        Assert.That(container.IsRegistered<IEquatable<ComparableService>>(), Is.False);
        Assert.That(container.IsRegistered<ICustomService>(), Is.True);
    }

    [Test]
    public void AutoAddServices_does_not_register_interface_less_types_without_explicit_reuse_attribute()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());

        container.AutoAddServices(new[] { typeof(OnlyDisposableService), typeof(PlainPocoService) });

        Assert.That(container.IsRegistered<IDisposable>(), Is.False);
        Assert.That(container.IsRegistered<OnlyDisposableService>(), Is.False);
        Assert.That(container.IsRegistered<PlainPocoService>(), Is.False);
    }

    [Test]
    public void AutoAddServices_registers_interface_less_types_with_explicit_reuse_attribute()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());

        container.AutoAddServices(new[] { typeof(SingletonConcreteService) });

        Assert.That(container.IsRegistered<SingletonConcreteService>(), Is.True);

        var instance1 = container.Resolve<SingletonConcreteService>();
        var instance2 = container.Resolve<SingletonConcreteService>();
        Assert.That(instance1, Is.SameAs(instance2));
    }

    [Test]
    public void AutoAddServices_respects_Transient_attribute()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());

        container.AutoAddServices(new[] { typeof(TransientCustomService) });

        var instance1 = container.Resolve<ICustomService>();
        var instance2 = container.Resolve<ICustomService>();

        Assert.That(instance1, Is.Not.SameAs(instance2));
    }

    [Test]
    public void AutoAddServices_respects_Scoped_attribute()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());

        container.AutoAddServices(new[] { typeof(ScopedCustomService) });

        using var scope1 = container.OpenScope();
        var instance1 = scope1.Resolve<ICustomService>();
        var instance1b = scope1.Resolve<ICustomService>();
        Assert.That(instance1, Is.SameAs(instance1b));

        using var scope2 = container.OpenScope();
        var instance2 = scope2.Resolve<ICustomService>();
        Assert.That(instance1, Is.Not.SameAs(instance2));
    }

    [Test]
    public void AutoAddServices_throws_when_multiple_lifetime_attributes_are_present()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            container.AutoAddServices(new[] { typeof(ConflictingLifetimeService) }));

        Assert.That(ex.Message, Does.Contain(nameof(ConflictingLifetimeService)));
        Assert.That(ex.Message, Does.Contain("multiple lifetime attributes"));
    }
}
