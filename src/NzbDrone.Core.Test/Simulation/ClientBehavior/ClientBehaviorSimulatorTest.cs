using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Simulation.ClientBehavior;

namespace NzbDrone.Core.Test.Simulation.ClientBehavior;

[TestFixture]
public class ClientBehaviorSimulatorTest
{
    private IConfigService _configService;
    private IClientProfileFactory _profileFactory;
    private ClientBehaviorSimulator _simulator;

    private IClientProfile _qbitProfile;
    private IClientProfile _delugeProfile;
    private IClientProfile _transmissionProfile;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _profileFactory = Substitute.For<IClientProfileFactory>();

        _qbitProfile = Substitute.For<IClientProfile>();
        _qbitProfile.Name.Returns("qBittorrent 4.4.2");

        _delugeProfile = Substitute.For<IClientProfile>();
        _delugeProfile.Name.Returns("Deluge 2.0.3");

        _transmissionProfile = Substitute.For<IClientProfile>();
        _transmissionProfile.Name.Returns("Transmission 3.00");

        _profileFactory.GetAvailableProviders().Returns(
            new List<IClientProfile> { _qbitProfile, _delugeProfile, _transmissionProfile });

        _configService.ClientBehaviorEngineEnabled.Returns(true);
        _configService.PrimaryClient.Returns("qBittorrent");
        _configService.ClientProfileSwitching.Returns(false);
        _configService.SwitchClientProbability.Returns(0.0);

        _simulator = new ClientBehaviorSimulator(_configService, _profileFactory);
    }

    [Test]
    public void IsEnabled_should_return_true_when_engine_enabled()
    {
        _configService.ClientBehaviorEngineEnabled.Returns(true);

        Assert.That(_simulator.IsEnabled, Is.True);
    }

    [Test]
    public void IsEnabled_should_return_false_when_engine_disabled()
    {
        _configService.ClientBehaviorEngineEnabled.Returns(false);

        Assert.That(_simulator.IsEnabled, Is.False);
    }

    [Test]
    public void GetActiveProfile_should_return_default_when_disabled()
    {
        _configService.ClientBehaviorEngineEnabled.Returns(false);

        var profile = _simulator.GetActiveProfile();

        Assert.That(profile.Name, Is.EqualTo("qBittorrent 4.4.2"));
    }

    [Test]
    public void GetActiveProfile_should_match_primary_client()
    {
        _configService.PrimaryClient.Returns("Deluge");

        _simulator = new ClientBehaviorSimulator(_configService, _profileFactory);
        var profile = _simulator.GetActiveProfile();

        Assert.That(profile.Name, Is.EqualTo("Deluge 2.0.3"));
    }

    [Test]
    public void GetActiveProfile_should_match_by_prefix()
    {
        _configService.PrimaryClient.Returns("Trans");

        _simulator = new ClientBehaviorSimulator(_configService, _profileFactory);
        var profile = _simulator.GetActiveProfile();

        Assert.That(profile.Name, Is.EqualTo("Transmission 3.00"));
    }

    [Test]
    public void GetActiveProfile_should_return_first_available_when_no_match()
    {
        _configService.PrimaryClient.Returns("NonExistent");

        _simulator = new ClientBehaviorSimulator(_configService, _profileFactory);
        var profile = _simulator.GetActiveProfile();

        Assert.That(profile.Name, Is.EqualTo("qBittorrent 4.4.2"));
    }

    [Test]
    public void GetActiveProfile_should_throw_when_no_profiles_available()
    {
        _profileFactory.GetAvailableProviders().Returns(new List<IClientProfile>());
        _configService.PrimaryClient.Returns("qBittorrent");

        _simulator = new ClientBehaviorSimulator(_configService, _profileFactory);

        Assert.That(() => _simulator.GetActiveProfile(), Throws.TypeOf<System.NullReferenceException>());
    }

    [Test]
    public void GetActiveProfile_should_cache_profile_between_calls()
    {
        var profile1 = _simulator.GetActiveProfile();
        var profile2 = _simulator.GetActiveProfile();

        Assert.That(profile1, Is.SameAs(profile2));
    }

    [Test]
    public void GetActiveProfile_should_not_switch_when_switching_disabled()
    {
        _configService.ClientProfileSwitching.Returns(false);

        var profile1 = _simulator.GetActiveProfile();
        var profile2 = _simulator.GetActiveProfile();

        Assert.That(profile1.Name, Is.EqualTo(profile2.Name));
    }

    [Test]
    public void GetActiveProfile_should_keep_same_when_switch_probability_zero()
    {
        _configService.ClientProfileSwitching.Returns(true);
        _configService.SwitchClientProbability.Returns(0.0);

        var profile1 = _simulator.GetActiveProfile();

        for (var i = 0; i < 100; i++)
        {
            var profile = _simulator.GetActiveProfile();
            Assert.That(profile.Name, Is.EqualTo(profile1.Name));
        }
    }

    [Test]
    public void GetActiveProfile_should_eventually_switch_when_probability_is_1()
    {
        _configService.ClientProfileSwitching.Returns(true);
        _configService.SwitchClientProbability.Returns(1.0);

        var profile1 = _simulator.GetActiveProfile();
        var profile2 = _simulator.GetActiveProfile();

        Assert.That(
            profile1.Name == profile2.Name,
            Is.False,
            "With probability 1.0 the profile should switch on each call");
    }

    [Test]
    public void GetActiveProfile_should_not_switch_when_only_one_profile()
    {
        _profileFactory.GetAvailableProviders().Returns(new List<IClientProfile> { _qbitProfile });
        _configService.ClientProfileSwitching.Returns(true);
        _configService.SwitchClientProbability.Returns(1.0);

        _simulator = new ClientBehaviorSimulator(_configService, _profileFactory);
        var profile1 = _simulator.GetActiveProfile();
        var profile2 = _simulator.GetActiveProfile();

        Assert.That(profile2.Name, Is.EqualTo(profile1.Name));
    }

    [Test]
    public void GetActiveProfile_should_select_alternate_excluding_current()
    {
        _configService.ClientProfileSwitching.Returns(true);
        _configService.SwitchClientProbability.Returns(1.0);

        var profile1 = _simulator.GetActiveProfile();
        var profile2 = _simulator.GetActiveProfile();

        Assert.That(profile2.Name, Is.Not.EqualTo(profile1.Name));
        Assert.That(profile2.Name, Is.AnyOf("qBittorrent 4.4.2", "Deluge 2.0.3", "Transmission 3.00"));
    }
}
