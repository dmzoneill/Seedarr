using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Test.ThingiProvider;

public class TestProviderDefinition : ProviderDefinition
{
}

public interface ITestProvider : IProvider
{
}

public class ConcreteTestProvider1 : ITestProvider
{
    public string Name => "ConcreteTestProvider1";
}

public class ConcreteTestProvider2 : ITestProvider
{
    public string Name => "ConcreteTestProvider2";
}

public class ConcreteProviderFactory : ProviderFactory<ITestProvider, TestProviderDefinition>
{
    public ConcreteProviderFactory(
        IProviderRepository<TestProviderDefinition> providerRepository,
        IServiceFactory serviceFactory)
        : base(providerRepository, serviceFactory)
    {
    }
}

[TestFixture]
public class ProviderFactoryTest
{
    private IProviderRepository<TestProviderDefinition> _repository;
    private IServiceFactory _serviceFactory;
    private ConcreteProviderFactory _subject;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IProviderRepository<TestProviderDefinition>>();
        _serviceFactory = Substitute.For<IServiceFactory>();
        _subject = new ConcreteProviderFactory(_repository, _serviceFactory);
    }

    [Test]
    public void All_should_delegate_to_repository()
    {
        var definitions = new List<TestProviderDefinition>
        {
            new() { Id = 1, Name = "Provider1" },
            new() { Id = 2, Name = "Provider2" }
        };
        _repository.All().Returns(definitions);

        var result = _subject.All();

        Assert.That(result, Has.Count.EqualTo(2));
        _repository.Received(1).All();
    }

    [Test]
    public void All_should_return_empty_list_when_no_definitions()
    {
        _repository.All().Returns(new List<TestProviderDefinition>());

        var result = _subject.All();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void All_should_return_list_type()
    {
        _repository.All().Returns(new List<TestProviderDefinition> { new() { Id = 1 } });

        var result = _subject.All();

        Assert.That(result, Is.InstanceOf<List<TestProviderDefinition>>());
    }

    [Test]
    public void Get_should_delegate_to_repository_with_correct_id()
    {
        var definition = new TestProviderDefinition { Id = 5, Name = "Test" };
        _repository.Get(5).Returns(definition);

        var result = _subject.Get(5);

        Assert.That(result, Is.SameAs(definition));
        _repository.Received(1).Get(5);
    }

    [Test]
    public void Get_should_not_call_repository_with_wrong_id()
    {
        var definition = new TestProviderDefinition { Id = 99 };
        _repository.Get(99).Returns(definition);

        _subject.Get(99);

        _repository.DidNotReceive().Get(Arg.Is<int>(x => x != 99));
    }

    [Test]
    public void Create_should_call_repository_insert_and_return_result()
    {
        var input = new TestProviderDefinition { Name = "New Provider" };
        var inserted = new TestProviderDefinition { Id = 1, Name = "New Provider" };
        _repository.Insert(input).Returns(inserted);

        var result = _subject.Create(input);

        Assert.That(result, Is.SameAs(inserted));
        _repository.Received(1).Insert(input);
    }

    [Test]
    public void Update_should_call_repository_update()
    {
        var definition = new TestProviderDefinition { Id = 3, Name = "Updated" };
        _repository.Update(definition).Returns(definition);

        _subject.Update(definition);

        _repository.Received(1).Update(definition);
    }

    [Test]
    public void Delete_should_call_repository_delete_with_correct_id()
    {
        _subject.Delete(7);

        _repository.Received(1).Delete(7);
    }

    [Test]
    public void Delete_should_not_call_repository_with_wrong_id()
    {
        _subject.Delete(10);

        _repository.DidNotReceive().Delete(Arg.Is<int>(x => x != 10));
    }

    [Test]
    public void GetAvailableProviders_should_delegate_to_service_factory()
    {
        var provider1 = new ConcreteTestProvider1();
        var provider2 = new ConcreteTestProvider2();
        _repository.All().Returns(new List<TestProviderDefinition>
        {
            new() { Id = 1, Name = "P1", Implementation = "ConcreteTestProvider1", Enable = true },
            new() { Id = 2, Name = "P2", Implementation = "ConcreteTestProvider2", Enable = true }
        });
        _serviceFactory.BuildAll<ITestProvider>().Returns(new List<ITestProvider> { provider1, provider2 });

        var result = _subject.GetAvailableProviders();

        Assert.That(result, Has.Count.EqualTo(2));
        _serviceFactory.Received(1).BuildAll<ITestProvider>();
    }

    [Test]
    public void GetAvailableProviders_should_return_empty_list_when_no_providers()
    {
        _repository.All().Returns(new List<TestProviderDefinition>());
        _serviceFactory.BuildAll<ITestProvider>().Returns(new List<ITestProvider>());

        var result = _subject.GetAvailableProviders();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetAvailableProviders_should_return_list_type()
    {
        _repository.All().Returns(new List<TestProviderDefinition>());
        _serviceFactory.BuildAll<ITestProvider>().Returns(new List<ITestProvider>());

        var result = _subject.GetAvailableProviders();

        Assert.That(result, Is.InstanceOf<List<ITestProvider>>());
    }

    [Test]
    public void GetAvailableProviders_should_exclude_disabled_providers()
    {
        var provider1 = new ConcreteTestProvider1();
        var provider2 = new ConcreteTestProvider2();
        _repository.All().Returns(new List<TestProviderDefinition>
        {
            new() { Id = 1, Name = "P1", Implementation = "ConcreteTestProvider1", Enable = true },
            new() { Id = 2, Name = "P2", Implementation = "ConcreteTestProvider2", Enable = false }
        });
        _serviceFactory.BuildAll<ITestProvider>().Returns(new List<ITestProvider> { provider1, provider2 });

        var result = _subject.GetAvailableProviders();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("ConcreteTestProvider1"));
    }

    [Test]
    public void All_should_reflect_all_definitions_from_repository()
    {
        var definitions = new List<TestProviderDefinition>
        {
            new() { Id = 1, Name = "A" },
            new() { Id = 2, Name = "B" },
            new() { Id = 3, Name = "C" }
        };
        _repository.All().Returns(definitions);

        var result = _subject.All();

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].Name, Is.EqualTo("A"));
        Assert.That(result[1].Name, Is.EqualTo("B"));
        Assert.That(result[2].Name, Is.EqualTo("C"));
    }
}
