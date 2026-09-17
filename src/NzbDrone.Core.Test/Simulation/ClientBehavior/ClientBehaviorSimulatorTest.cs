using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Torrents;

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
    public void GetActiveProfile_should_return_fallback_when_no_profiles_available()
    {
        _profileFactory.GetAvailableProviders().Returns(new List<IClientProfile>());
        _configService.PrimaryClient.Returns("qBittorrent");

        _simulator = new ClientBehaviorSimulator(_configService, _profileFactory);

        var profile = _simulator.GetActiveProfile();
        Assert.That(profile, Is.Not.Null);
        Assert.That(profile.Name, Does.Contain("qBittorrent"));
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

    [Test]
    public void GetEffectiveDropoutProbability_should_return_base_when_disabled()
    {
        _configService.ClientBehaviorEngineEnabled.Returns(false);

        var result = _simulator.GetEffectiveDropoutProbability(0.1);

        Assert.That(result, Is.EqualTo(0.1));
    }

    [Test]
    public void GetEffectiveDropoutProbability_should_modulate_by_profile()
    {
        _configService.ClientBehaviorEngineEnabled.Returns(true);
        _configService.PrimaryClient.Returns("Transmission");
        _configService.BehaviorVariation.Returns(0.0);

        var transmissionSimulator = new ClientBehaviorSimulator(_configService, _profileFactory);
        var result = transmissionSimulator.GetEffectiveDropoutProbability(0.1);

        Assert.That(result, Is.EqualTo(0.08).Within(0.001));
    }

    [Test]
    public void GetEffectiveRotationPercentage_should_modulate_by_profile()
    {
        _configService.ClientBehaviorEngineEnabled.Returns(true);
        _configService.PrimaryClient.Returns("Deluge");
        _configService.BehaviorVariation.Returns(0.0);

        var delugeSimulator = new ClientBehaviorSimulator(_configService, _profileFactory);
        var result = delugeSimulator.GetEffectiveRotationPercentage(0.1);

        Assert.That(result, Is.EqualTo(0.12).Within(0.001));
    }

    [Test]
    public void GetEffectiveIdleChance_should_modulate_by_profile()
    {
        _configService.ClientBehaviorEngineEnabled.Returns(true);
        _configService.PrimaryClient.Returns("Transmission");
        _configService.BehaviorVariation.Returns(0.0);

        var transmissionSimulator = new ClientBehaviorSimulator(_configService, _profileFactory);
        var result = transmissionSimulator.GetEffectiveIdleChance(0.3);

        Assert.That(result, Is.EqualTo(0.33).Within(0.001));
    }

    [Test]
    public void GetActiveProfile_should_lock_to_primary_client_and_suppress_switching_when_isPrivateTorrent_is_true()
    {
        _configService.ClientBehaviorEngineEnabled.Returns(true);
        _configService.PrimaryClient.Returns("qBittorrent");
        _configService.ClientProfileSwitching.Returns(true);
        _configService.SwitchClientProbability.Returns(1.0);

        _simulator = new ClientBehaviorSimulator(_configService, _profileFactory);

        for (var i = 0; i < 10; i++)
        {
            var profile = _simulator.GetActiveProfile(isPrivateTorrent: true);
            Assert.That(profile.Name, Is.EqualTo("qBittorrent 4.4.2"), "Private torrent must lock to primary client without rotating");
        }
    }

    [Test]
    public void GetActiveProfile_should_allow_switching_when_isPrivateTorrent_is_false()
    {
        _configService.ClientBehaviorEngineEnabled.Returns(true);
        _configService.PrimaryClient.Returns("qBittorrent");
        _configService.ClientProfileSwitching.Returns(true);
        _configService.SwitchClientProbability.Returns(1.0);

        _simulator = new ClientBehaviorSimulator(_configService, _profileFactory);

        var profile1 = _simulator.GetActiveProfile(isPrivateTorrent: false);
        var profile2 = _simulator.GetActiveProfile(isPrivateTorrent: false);

        Assert.That(profile2.Name, Is.Not.EqualTo(profile1.Name), "Public torrent should switch when probability is 1.0");
    }

    [Test]
    public void GetOrCreateSession_should_return_valid_session_with_expected_fields()
    {
        _qbitProfile.GeneratePeerId().Returns("-qB4420-123456789012");

        var session = _simulator.GetOrCreateSession("0123456789ABCDEF0123456789ABCDEF01234567");

        Assert.That(session, Is.Not.Null);
        Assert.That(session.ProfileName, Is.EqualTo("qBittorrent 4.4.2"));
        Assert.That(session.PeerId, Is.EqualTo("-qB4420-123456789012"));
        Assert.That(session.AnnounceKey, Is.Not.Null.And.Not.Empty);
        Assert.That(session.AnnounceKey.Length, Is.EqualTo(8));
        Assert.That(session.CreatedAt, Is.GreaterThan(DateTime.UtcNow.AddMinutes(-1)));
        Assert.That(session.Profile, Is.EqualTo(_qbitProfile));
    }

    [Test]
    public void GetOrCreateSession_should_be_immutable_for_active_torrent_despite_switching_enabled()
    {
        _configService.ClientProfileSwitching.Returns(true);
        _configService.SwitchClientProbability.Returns(1.0);
        _qbitProfile.GeneratePeerId().Returns("-qB4420-AAAAAAAAAAAAAAAA");
        _delugeProfile.GeneratePeerId().Returns("-DE2030-BBBBBBBBBBBBBBBB");

        _simulator = new ClientBehaviorSimulator(_configService, _profileFactory);

        var session1 = _simulator.GetOrCreateSession("0123456789ABCDEF0123456789ABCDEF01234567");

        for (var i = 0; i < 50; i++)
        {
            var nextSession = _simulator.GetOrCreateSession("0123456789ABCDEF0123456789ABCDEF01234567");
            Assert.That(nextSession, Is.SameAs(session1));
            Assert.That(nextSession.ProfileName, Is.EqualTo(session1.ProfileName));
            Assert.That(nextSession.PeerId, Is.EqualTo(session1.PeerId));
            Assert.That(nextSession.AnnounceKey, Is.EqualTo(session1.AnnounceKey));
        }
    }

    [Test]
    public void GetProfileForTorrent_should_remain_immutable_for_active_torrent_when_switching_enabled()
    {
        _configService.ClientProfileSwitching.Returns(true);
        _configService.SwitchClientProbability.Returns(1.0);

        _simulator = new ClientBehaviorSimulator(_configService, _profileFactory);

        var profile1 = _simulator.GetProfileForTorrent("0123456789ABCDEF0123456789ABCDEF01234567");

        for (var i = 0; i < 50; i++)
        {
            var nextProfile = _simulator.GetProfileForTorrent("0123456789ABCDEF0123456789ABCDEF01234567");
            Assert.That(nextProfile.Name, Is.EqualTo(profile1.Name));
        }
    }

    [Test]
    public void GetOrCreateSession_should_lock_private_torrent_to_default_profile_without_switching()
    {
        _configService.ClientProfileSwitching.Returns(true);
        _configService.SwitchClientProbability.Returns(1.0);
        _configService.PrimaryClient.Returns("qBittorrent");

        _simulator = new ClientBehaviorSimulator(_configService, _profileFactory);

        for (var i = 0; i < 20; i++)
        {
            var session = _simulator.GetOrCreateSession($"private_hash_{i}", isPrivateTorrent: true);
            Assert.That(session.ProfileName, Is.EqualTo("qBittorrent 4.4.2"), "Private torrent session must lock to primary client");
        }
    }

    [Test]
    public void ReleaseSession_should_remove_session_and_allow_new_session_to_be_created()
    {
        var infoHash = "0123456789ABCDEF0123456789ABCDEF01234567";
        var session1 = _simulator.GetOrCreateSession(infoHash);

        Assert.That(_simulator.GetSession(infoHash), Is.SameAs(session1));

        var released = _simulator.ReleaseSession(infoHash);
        Assert.That(released, Is.True);
        Assert.That(_simulator.GetSession(infoHash), Is.Null);

        var releasedAgain = _simulator.ReleaseSession(infoHash);
        Assert.That(releasedAgain, Is.False);

        var session2 = _simulator.GetOrCreateSession(infoHash);
        Assert.That(session2, Is.Not.Null);
        Assert.That(session2, Is.Not.SameAs(session1));
    }

    [Test]
    public void ReleaseAllSessions_should_clear_all_active_sessions()
    {
        _simulator.GetOrCreateSession("hash1");
        _simulator.GetOrCreateSession("hash2");

        Assert.That(_simulator.GetSession("hash1"), Is.Not.Null);
        Assert.That(_simulator.GetSession("hash2"), Is.Not.Null);

        _simulator.ReleaseAllSessions();

        Assert.That(_simulator.GetSession("hash1"), Is.Null);
        Assert.That(_simulator.GetSession("hash2"), Is.Null);
    }

    [Test]
    public void GetOrCreateSession_should_isolate_sessions_between_different_torrents()
    {
        var session1 = _simulator.GetOrCreateSession("hash1");
        var session2 = _simulator.GetOrCreateSession("hash2");

        Assert.That(session1, Is.Not.SameAs(session2));
    }

    [Test]
    public void GetOrCreateSession_should_be_case_insensitive_for_info_hash()
    {
        var lowerHash = "abcdef1234567890abcdef1234567890abcdef12";
        var upperHash = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";

        var session1 = _simulator.GetOrCreateSession(lowerHash);
        var session2 = _simulator.GetOrCreateSession(upperHash);

        Assert.That(session2, Is.SameAs(session1));
    }

    [Test]
    public void Handle_TorrentPausedEvent_should_release_session()
    {
        var infoHash = "0123456789ABCDEF0123456789ABCDEF01234567";
        _simulator.GetOrCreateSession(infoHash);
        Assert.That(_simulator.GetSession(infoHash), Is.Not.Null);

        _simulator.Handle(new TorrentPausedEvent(new Torrent { InfoHash = infoHash }));

        Assert.That(_simulator.GetSession(infoHash), Is.Null);
    }

    [Test]
    public void Handle_TorrentDeletedEvent_should_release_session()
    {
        var infoHash = "0123456789ABCDEF0123456789ABCDEF01234567";
        _simulator.GetOrCreateSession(infoHash);
        Assert.That(_simulator.GetSession(infoHash), Is.Not.Null);

        _simulator.Handle(new TorrentDeletedEvent(1, new Torrent { InfoHash = infoHash }));

        Assert.That(_simulator.GetSession(infoHash), Is.Null);
    }

    [Test]
    public void Handle_TorrentStatusChangedEvent_should_release_session_when_stopped_or_paused()
    {
        var infoHash = "0123456789ABCDEF0123456789ABCDEF01234567";
        _simulator.GetOrCreateSession(infoHash);
        Assert.That(_simulator.GetSession(infoHash), Is.Not.Null);

        _simulator.Handle(new TorrentStatusChangedEvent(
            new Torrent { InfoHash = infoHash },
            TorrentStatus.Seeding,
            TorrentStatus.Stopped));

        Assert.That(_simulator.GetSession(infoHash), Is.Null);
    }
}
