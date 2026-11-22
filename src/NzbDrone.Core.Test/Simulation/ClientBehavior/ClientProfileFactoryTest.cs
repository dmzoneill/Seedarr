using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Simulation.ClientBehavior.Profiles;

namespace NzbDrone.Core.Test.Simulation.ClientBehavior;

[TestFixture]
public class ClientProfileFactoryTest
{
    private IClientProfileRepository _repository;
    private IServiceFactory _serviceFactory;
    private ClientProfileFactory _subject;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IClientProfileRepository>();
        _serviceFactory = Substitute.For<IServiceFactory>();
        _subject = new ClientProfileFactory(_repository, _serviceFactory);
    }

    [Test]
    public void GetAvailableProviders_seeds_every_built_in_profile_when_the_table_is_empty()
    {
        var stored = new List<ClientProfileDefinition>();
        _repository.All().Returns(stored);
        _repository.Insert(Arg.Any<ClientProfileDefinition>()).Returns(call =>
        {
            var definition = call.Arg<ClientProfileDefinition>();
            stored.Add(definition);
            return definition;
        });
        _serviceFactory.BuildAll<IClientProfile>().Returns(new List<IClientProfile> { new QBittorrentProfile() });

        var first = _subject.GetAvailableProviders();
        var second = _subject.GetAvailableProviders();

        Assert.That(stored.Select(d => d.Implementation), Is.EquivalentTo(new[]
        {
            "QBittorrentProfile",
            "DelugeProfile",
            "TransmissionProfile",
            "UTorrentProfile",
            "BiglyBTProfile"
        }));
        Assert.That(stored.All(d => d.Enable), Is.True);
        Assert.That(first.Select(p => p.GetType().Name), Is.EquivalentTo(stored.Select(d => d.Implementation)));
        Assert.That(second.Select(p => p.GetType().Name), Is.EquivalentTo(first.Select(p => p.GetType().Name)));
        _repository.Received(5).Insert(Arg.Any<ClientProfileDefinition>());
    }

    [Test]
    public void GetAvailableProviders_uses_built_ins_when_the_container_only_returns_one_implementation()
    {
        _repository.All().Returns(new List<ClientProfileDefinition>
        {
            new() { Name = "Transmission 3.00", Implementation = "TransmissionProfile", Enable = true },
            new() { Name = "qBittorrent 4.4.2", Implementation = "QBittorrentProfile", Enable = false },
            new() { Name = "Deluge 2.0.3", Implementation = "DelugeProfile", Enable = true },
            new() { Name = "uTorrent 3.5.5", Implementation = "UTorrentProfile", Enable = true },
            new() { Name = "BiglyBT 2.7.0.0", Implementation = "BiglyBTProfile", Enable = true }
        });
        _serviceFactory.BuildAll<IClientProfile>().Returns(new List<IClientProfile> { new QBittorrentProfile() });

        var result = _subject.GetAvailableProviders();

        Assert.That(result.Select(p => p.GetType().Name), Is.EquivalentTo(new[]
        {
            "TransmissionProfile",
            "DelugeProfile",
            "UTorrentProfile",
            "BiglyBTProfile"
        }));
        Assert.That(result.Select(p => p.GetType().Name), Does.Not.Contain("QBittorrentProfile"));
    }

    [Test]
    public void GetDefaultDefinitions_lists_every_built_in_profile()
    {
        var definitions = _subject.GetDefaultDefinitions();

        Assert.That(definitions.Select(d => d.Implementation), Is.EquivalentTo(new[]
        {
            "QBittorrentProfile",
            "DelugeProfile",
            "TransmissionProfile",
            "UTorrentProfile",
            "BiglyBTProfile"
        }));
        Assert.That(definitions.All(d => d.Enable), Is.True);
    }
}
