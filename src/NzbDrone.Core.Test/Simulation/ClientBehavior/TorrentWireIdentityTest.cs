using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Simulation.ClientBehavior.Profiles;

namespace NzbDrone.Core.Test.Simulation.ClientBehavior;

[TestFixture]
public class TorrentWireIdentityTest
{
    private const string InfoHash = "0123456789abcdef0123456789abcdef01234567";

    [TestCase("Transmission", "-TR3000-", "Transmission")]
    [TestCase("Deluge", "-DE2030-", "Deluge")]
    [TestCase("uTorrent", "-UT3550-", "uTorrent")]
    [TestCase("µTorrent", "-UT3550-", "uTorrent")]
    [TestCase("BiglyBT", "-BI2700-", "BiglyBT")]
    [TestCase("qBittorrent", "-qB4420-", "qBittorrent")]
    public void MatchProfile_resolves_each_built_in_client(string name, string prefix, string expectedName)
    {
        var profile = TorrentWireIdentityResolver.MatchProfile(name, factory: null);

        Assert.That(profile, Is.Not.Null);
        Assert.That(profile.PeerIdPrefix, Is.EqualTo(prefix));
        Assert.That(profile.Name, Does.Contain(expectedName));
    }

    [Test]
    public void MatchProfile_returns_null_for_blank_or_unknown_names_and_survives_a_factory_failure()
    {
        Assert.That(TorrentWireIdentityResolver.MatchProfile(null, null), Is.Null);
        Assert.That(TorrentWireIdentityResolver.MatchProfile("   ", null), Is.Null);
        Assert.That(TorrentWireIdentityResolver.MatchProfile("NotAClient", null), Is.Null);

        var factory = Substitute.For<IClientProfileFactory>();
        factory.GetAvailableProviders().Returns((List<IClientProfile>)null);
        Assert.That(TorrentWireIdentityResolver.MatchProfile("Transmission", factory).Name, Does.Contain("Transmission"));

        factory.GetAvailableProviders().Returns(_ => throw new InvalidOperationException("unavailable"));
        Assert.That(TorrentWireIdentityResolver.MatchProfile("Deluge", factory).Name, Does.Contain("Deluge"));
    }

    [Test]
    public void MatchProfile_prefers_a_factory_profile_by_name_type_or_contained_text()
    {
        var byEquals = Substitute.For<IClientProfile>();
        byEquals.Name.Returns("qBittorrent 4.4.2");
        var byType = new TransmissionProfile();
        var nameless = Substitute.For<IClientProfile>();
        nameless.Name.Returns((string)null);

        var factory = Substitute.For<IClientProfileFactory>();
        factory.GetAvailableProviders().Returns(new List<IClientProfile> { nameless, byEquals, byType });

        Assert.That(TorrentWireIdentityResolver.MatchProfile("qBittorrent 4.4.2", factory), Is.SameAs(byEquals));
        Assert.That(TorrentWireIdentityResolver.MatchProfile("TransmissionProf", factory), Is.SameAs(byType));
        Assert.That(TorrentWireIdentityResolver.MatchProfile("4.4.2", factory), Is.SameAs(byEquals));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("UnknownClient/9.9")]
    public void MatchUserAgent_returns_null_when_the_agent_is_blank_or_unknown(string userAgent)
    {
        Assert.That(TorrentWireIdentityResolver.MatchUserAgent(userAgent, null), Is.Null);
    }

    [TestCase("Transmission/3.00", "Transmission")]
    [TestCase("Deluge/2.0.3", "Deluge")]
    [TestCase("uTorrent/3.5.5", "uTorrent")]
    [TestCase("µTorrent/3.5.5", "uTorrent")]
    [TestCase("BiglyBT/2.7.0.0", "BiglyBT")]
    [TestCase("qBittorrent/4.4.2", "qBittorrent")]
    public void MatchUserAgent_resolves_each_built_in_client(string userAgent, string expectedName)
    {
        var profile = TorrentWireIdentityResolver.MatchUserAgent(userAgent, factory: null);

        Assert.That(profile.Name, Does.Contain(expectedName));
    }

    [Test]
    public void Resolve_without_a_simulator_keeps_an_ephemeral_identity_for_an_unknown_primary_client()
    {
        var config = Substitute.For<IConfigService>();
        config.PrimaryClient.Returns("NotARealClient");
        config.ClientBehaviorEngineEnabled.Returns(false);
        config.AnonymousMode.Returns(false);
        var cache = new ConcurrentDictionary<string, (string PeerId, string AnnounceKey)>();

        var first = TorrentWireIdentityResolver.Resolve(InfoHash, false, null, config, null, null, cache);
        var second = TorrentWireIdentityResolver.Resolve(InfoHash, false, null, config, null, null, cache);

        Assert.That(first.Profile.Name, Does.Contain("qBittorrent"));
        Assert.That(first.PeerId, Does.StartWith("-qB4420-"));
        Assert.That(first.PeerId, Is.EqualTo(second.PeerId));
        Assert.That(first.AnnounceKey, Is.EqualTo(second.AnnounceKey));
        Assert.That(first.AnnounceKey, Has.Length.EqualTo(8));
        Assert.That(first.UserAgent, Does.Contain("qBittorrent"));
        Assert.That(first.Session, Is.Null);
    }

    [Test]
    public void Resolve_uses_the_configured_user_agent_when_the_profile_does_not_advertise_one()
    {
        var profile = Substitute.For<IClientProfile>();
        profile.Name.Returns("CustomOnly");
        profile.UserAgent.Returns((string)null);
        profile.GeneratePeerId().Returns("-XX0000-aaaaaaaaaaaa");
        var factory = Substitute.For<IClientProfileFactory>();
        factory.GetAvailableProviders().Returns(new List<IClientProfile> { profile });

        var config = Substitute.For<IConfigService>();
        config.AnonymousMode.Returns(true);
        config.ClientBehaviorEngineEnabled.Returns(false);
        config.BitTorrentUserAgent.Returns("   ");
        config.PrimaryClient.Returns("CustomOnly");

        var identity = TorrentWireIdentityResolver.Resolve(InfoHash, false, null, config, null, factory, ephemeralIdentities: null);

        Assert.That(identity.Profile, Is.SameAs(profile));
        Assert.That(identity.UserAgent, Is.EqualTo("qBittorrent/4.4.2"));
        Assert.That(identity.PeerId, Is.EqualTo("-XX0000-aaaaaaaaaaaa"));
        Assert.That(identity.AnnounceKey, Has.Length.EqualTo(8));
    }

    [Test]
    public void Resolve_fills_a_missing_announce_key_and_falls_back_when_the_session_has_no_profile()
    {
        var config = Substitute.For<IConfigService>();
        config.ClientBehaviorEngineEnabled.Returns(true);
        config.AnonymousMode.Returns(false);
        config.BitTorrentUserAgent.Returns((string)null);

        var fallback = new QBittorrentProfile();
        var session = new TorrentClientSession
        {
            PeerId = "-qB4420-eeeeeeeeeeee",
            AnnounceKey = "",
            Profile = null
        };
        var simulator = Substitute.For<IClientBehaviorSimulator>();
        simulator.GetOrCreateSession(InfoHash, false).Returns(session);
        simulator.GetProfileForTorrent(InfoHash, false).Returns(fallback);

        var identity = TorrentWireIdentityResolver.Resolve(InfoHash, false, null, config, simulator, null, new ConcurrentDictionary<string, (string PeerId, string AnnounceKey)>());

        Assert.That(identity.Profile, Is.SameAs(fallback));
        Assert.That(identity.PeerId, Is.EqualTo("-qB4420-eeeeeeeeeeee"));
        Assert.That(identity.AnnounceKey, Has.Length.EqualTo(8));
        Assert.That(session.AnnounceKey, Is.EqualTo(identity.AnnounceKey));
        Assert.That(identity.UserAgent, Does.Contain("qBittorrent"));
    }

    [Test]
    public void Resolve_uses_the_engine_disabled_session_when_the_user_agent_is_unknown()
    {
        var config = Substitute.For<IConfigService>();
        config.ClientBehaviorEngineEnabled.Returns(false);
        config.AnonymousMode.Returns(false);
        config.BitTorrentUserAgent.Returns("SomeUnknown/1.2");

        var session = new TorrentClientSession
        {
            PeerId = "",
            AnnounceKey = "ABCD1234",
            Profile = new DelugeProfile(),
            ProfileName = "Deluge 2.0.3"
        };
        var simulator = Substitute.For<IClientBehaviorSimulator>();
        simulator.GetOrCreateSession(InfoHash, true).Returns(session);

        var cache = new ConcurrentDictionary<string, (string PeerId, string AnnounceKey)>();
        var first = TorrentWireIdentityResolver.Resolve(InfoHash, true, "   ", config, simulator, null, cache);
        var second = TorrentWireIdentityResolver.Resolve(InfoHash, true, "   ", config, simulator, null, cache);

        Assert.That(first.Profile.Name, Does.Contain("Deluge"));
        Assert.That(first.PeerId, Does.StartWith("-DE2030-"));
        Assert.That(first.PeerId, Is.EqualTo(second.PeerId));
        Assert.That(first.AnnounceKey, Is.EqualTo(second.AnnounceKey));
        Assert.That(first.AnnounceKey, Has.Length.EqualTo(8));
    }

    [Test]
    public void Resolve_keeps_the_selected_profile_when_a_pinned_session_has_no_profile()
    {
        var config = Substitute.For<IConfigService>();
        config.AnonymousMode.Returns(true);
        var session = new TorrentClientSession
        {
            PeerId = "-TR3000-ffffffffffff",
            AnnounceKey = "FEEDBEEF",
            Profile = null
        };
        var simulator = Substitute.For<IClientBehaviorSimulator>();
        simulator.GetOrCreateSession(InfoHash, false, Arg.Any<IClientProfile>()).Returns(session);

        var identity = TorrentWireIdentityResolver.Resolve(InfoHash, false, "Transmission", config, simulator, null, null);

        Assert.That(identity.Profile.Name, Does.Contain("Transmission"));
        Assert.That(identity.PeerId, Is.EqualTo("-TR3000-ffffffffffff"));
        Assert.That(identity.AnnounceKey, Is.EqualTo("FEEDBEEF"));
        Assert.That(identity.UserAgent, Does.Contain("Transmission"));
    }

    [Test]
    public void Resolve_with_no_config_uses_qBittorrent_and_a_stable_cache_for_a_blank_info_hash()
    {
        var cache = new ConcurrentDictionary<string, (string PeerId, string AnnounceKey)>();
        var first = TorrentWireIdentityResolver.Resolve("  ", false, null, null, null, null, cache);
        var second = TorrentWireIdentityResolver.Resolve("  ", false, null, null, null, null, cache);
        var blank = TorrentWireIdentityResolver.Resolve(null, false, null, null, null, null, cache);

        Assert.That(first.Profile.Name, Does.Contain("qBittorrent"));
        Assert.That(first.PeerId, Is.EqualTo(second.PeerId));
        Assert.That(first.UserAgent, Does.Contain("qBittorrent"));
        Assert.That(blank.PeerId, Does.StartWith("-qB4420-"));
        Assert.That(blank.PeerId, Has.Length.EqualTo(20));
    }
}
