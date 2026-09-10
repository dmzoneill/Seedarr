using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;

namespace NzbDrone.Core.Test.Authentication;

[TestFixture]
public class IdentityProviderServiceTest
{
    private IIdentityProviderRepository _repository;
    private IdentityProviderService _service;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IIdentityProviderRepository>();
        _service = new IdentityProviderService(_repository);
    }

    [Test]
    public void GetAll_ReturnsAllProviders()
    {
        var providers = new List<IdentityProviderDefinition>
        {
            new() { Id = 1, ProviderId = "oidc1", Name = "OIDC 1" },
            new() { Id = 2, ProviderId = "oidc2", Name = "OIDC 2" },
        };

        _repository.All().Returns(providers);

        var result = _service.GetAll();

        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0].ProviderId, Is.EqualTo("oidc1"));
    }

    [Test]
    public void GetEnabled_ReturnsEnabledOnly()
    {
        var providers = new List<IdentityProviderDefinition>
        {
            new() { Id = 1, ProviderId = "oidc1", Name = "OIDC 1", IsEnabled = true },
        };

        _repository.GetEnabled().Returns(providers);

        var result = _service.GetEnabled();

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].IsEnabled, Is.True);
    }

    [Test]
    public void GetById_ReturnsProvider()
    {
        var provider = new IdentityProviderDefinition { Id = 1, ProviderId = "test", Name = "Test" };
        _repository.Get(1).Returns(provider);

        var result = _service.GetById(1);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("Test"));
    }

    [Test]
    public void Add_SetsTimestampsAndInserts()
    {
        var provider = new IdentityProviderDefinition { ProviderId = "new", Name = "New" };
        _repository.Insert(provider).Returns(provider);

        var result = _service.Add(provider);

        Assert.That(result, Is.Not.Null);
        _repository.Received(1).Insert(Arg.Is<IdentityProviderDefinition>(p => p.ProviderId == "new"));
    }

    [Test]
    public void Delete_CallsRepositoryDelete()
    {
        _service.Delete(5);

        _repository.Received(1).Delete(5);
    }

    [Test]
    public async Task TestConnectionAsync_WhenNoIssuerOrMetadata_ReturnsTrue()
    {
        var provider = new IdentityProviderDefinition
        {
            ProviderId = "test",
            Name = "Test",
            IssuerUrl = null,
            MetadataUrl = null,
        };

        var result = await _service.TestConnectionAsync(provider);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task TestConnectionAsync_WhenProviderNull_ReturnsFalse()
    {
        var result = await _service.TestConnectionAsync(null);

        Assert.That(result, Is.False);
    }
}
