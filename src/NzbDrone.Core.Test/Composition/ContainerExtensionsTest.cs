using System;
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

    [Transient]
    public class TransientCustomService : ICustomService
    {
    }

    [Scoped]
    public class ScopedCustomService : ICustomService
    {
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
    public void AutoAddServices_registers_class_only_implementing_IDisposable_by_concrete_type()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());

        container.AutoAddServices(new[] { typeof(OnlyDisposableService) });

        Assert.That(container.IsRegistered<IDisposable>(), Is.False);
        Assert.That(container.IsRegistered<OnlyDisposableService>(), Is.True);
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
}
