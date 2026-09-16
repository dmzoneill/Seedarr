using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Common.Composition;

namespace NzbDrone.Core.Test.Composition;

[TestFixture]
public class KnownTypesTest
{
    private interface ITestContract
    {
    }

    private class TestImplA : ITestContract
    {
    }

    private class TestImplB : ITestContract
    {
    }

    private abstract class AbstractTestImpl : ITestContract
    {
    }

    [SetUp]
    public void SetUp()
    {
        KnownTypes.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        KnownTypes.Clear();
    }

    [Test]
    public void Register_deduplicates_types_when_called_multiple_times()
    {
        var types = new List<Type> { typeof(TestImplA), typeof(TestImplB) };

        KnownTypes.Register(types);
        KnownTypes.Register(types);

        var implementations = KnownTypes.GetImplementations(typeof(ITestContract));

        Assert.That(implementations, Has.Count.EqualTo(2));
        Assert.That(implementations, Contains.Item(typeof(TestImplA)));
        Assert.That(implementations, Contains.Item(typeof(TestImplB)));
    }

    [Test]
    public void Register_ignores_null_collection_and_null_elements()
    {
        KnownTypes.Register((IEnumerable<Type>)null);
        KnownTypes.Register(new List<Type> { null, typeof(TestImplA), null });

        var implementations = KnownTypes.GetImplementations(typeof(ITestContract));

        Assert.That(implementations, Has.Count.EqualTo(1));
        Assert.That(implementations, Contains.Item(typeof(TestImplA)));
    }

    [Test]
    public void Clear_removes_all_registered_types()
    {
        KnownTypes.Register(new[] { typeof(TestImplA) });
        Assert.That(KnownTypes.Count, Is.EqualTo(1));

        KnownTypes.Clear();

        Assert.That(KnownTypes.Count, Is.EqualTo(0));
        Assert.That(KnownTypes.GetImplementations(typeof(ITestContract)), Is.Empty);
    }

    [Test]
    public void GetImplementations_excludes_interfaces_and_abstract_classes()
    {
        KnownTypes.Register(new[] { typeof(ITestContract), typeof(AbstractTestImpl), typeof(TestImplA) });

        var implementations = KnownTypes.GetImplementations(typeof(ITestContract));

        Assert.That(implementations, Has.Count.EqualTo(1));
        Assert.That(implementations, Contains.Item(typeof(TestImplA)));
    }

    [Test]
    public void GetImplementations_returns_empty_for_null_contract()
    {
        KnownTypes.Register(new[] { typeof(TestImplA) });

        var implementations = KnownTypes.GetImplementations(null);

        Assert.That(implementations, Is.Empty);
    }

    [Test]
    public void Concurrent_Register_and_GetImplementations_is_thread_safe()
    {
        var tasks = new List<Task>();
        const int iterations = 100;

        for (var i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (var j = 0; j < iterations; j++)
                {
                    KnownTypes.Register(new[] { typeof(TestImplA), typeof(TestImplB) });
                    var impls = KnownTypes.GetImplementations(typeof(ITestContract));
                    Assert.That(impls.Count, Is.InRange(0, 2));
                }
            }));
        }

        Assert.DoesNotThrowAsync(async () => await Task.WhenAll(tasks));

        var finalImpls = KnownTypes.GetImplementations(typeof(ITestContract));
        Assert.That(finalImpls, Has.Count.EqualTo(2));
    }
}
