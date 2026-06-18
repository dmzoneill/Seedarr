using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Simulation.ClientBehavior.Profiles;

namespace NzbDrone.Core.Test.Simulation.ClientBehavior;

[TestFixture]
public class ClientProfileTest
{
    private static IEnumerable<IClientProfile> AllProfiles()
    {
        yield return new QBittorrentProfile();
        yield return new DelugeProfile();
        yield return new TransmissionProfile();
    }

    [Test]
    public void QBittorrent_peer_id_should_start_with_correct_prefix()
    {
        var profile = new QBittorrentProfile();

        var peerId = profile.GeneratePeerId();

        Assert.That(peerId, Does.StartWith("-qB4420-"));
    }

    [Test]
    public void Deluge_peer_id_should_start_with_correct_prefix()
    {
        var profile = new DelugeProfile();

        var peerId = profile.GeneratePeerId();

        Assert.That(peerId, Does.StartWith("-DE2030-"));
    }

    [Test]
    public void Transmission_peer_id_should_start_with_correct_prefix()
    {
        var profile = new TransmissionProfile();

        var peerId = profile.GeneratePeerId();

        Assert.That(peerId, Does.StartWith("-TR3000-"));
    }

    [TestCaseSource(nameof(AllProfiles))]
    public void GeneratePeerId_should_return_20_characters(IClientProfile profile)
    {
        var peerId = profile.GeneratePeerId();

        Assert.That(
            peerId,
            Has.Length.EqualTo(20),
            $"Peer ID for {profile.Name} should be exactly 20 characters (BitTorrent spec)");
    }

    [TestCaseSource(nameof(AllProfiles))]
    public void GeneratePeerId_should_fit_in_20_bytes(IClientProfile profile)
    {
        var peerId = profile.GeneratePeerId();
        var bytes = Encoding.ASCII.GetBytes(peerId);

        Assert.That(
            bytes,
            Has.Length.EqualTo(20),
            $"Peer ID for {profile.Name} must be exactly 20 bytes per BitTorrent protocol");
    }

    [TestCaseSource(nameof(AllProfiles))]
    public void GeneratePeerId_should_start_with_PeerIdPrefix(IClientProfile profile)
    {
        var peerId = profile.GeneratePeerId();

        Assert.That(peerId, Does.StartWith(profile.PeerIdPrefix));
    }

    [TestCaseSource(nameof(AllProfiles))]
    public void GeneratePeerId_prefix_should_be_8_characters(IClientProfile profile)
    {
        Assert.That(
            profile.PeerIdPrefix,
            Has.Length.EqualTo(8),
            $"Azureus-style peer ID prefix for {profile.Name} must be 8 characters");
    }

    [TestCaseSource(nameof(AllProfiles))]
    public void GeneratePeerId_suffix_should_be_numeric_digits(IClientProfile profile)
    {
        var peerId = profile.GeneratePeerId();
        var suffix = peerId.Substring(profile.PeerIdPrefix.Length);

        Assert.That(
            suffix,
            Does.Match("^[0-9]+$"),
            $"Suffix for {profile.Name} should be numeric digits only");
    }

    [TestCaseSource(nameof(AllProfiles))]
    public void GeneratePeerId_should_produce_unique_ids(IClientProfile profile)
    {
        var ids = Enumerable.Range(0, 100).Select(_ => profile.GeneratePeerId()).ToList();
        var uniqueIds = new HashSet<string>(ids);

        // With 12 random decimal digits, collisions in 100 samples are astronomically unlikely
        Assert.That(
            uniqueIds.Count,
            Is.EqualTo(100),
            $"100 generated peer IDs for {profile.Name} should all be unique");
    }

    [TestCaseSource(nameof(AllProfiles))]
    public void PeerIdPrefix_should_follow_azureus_style_format(IClientProfile profile)
    {
        // Azureus-style: -XXYYYY- where XX is client id and YYYY is version
        Assert.That(
            profile.PeerIdPrefix,
            Does.Match("^-[A-Za-z]{2}[0-9]{4}-$"),
            $"Peer ID prefix for {profile.Name} should follow Azureus-style: -XXYYYY-");
    }

    [Test]
    public void QBittorrent_properties_should_match_expected_values()
    {
        var profile = new QBittorrentProfile();

        Assert.Multiple(() =>
        {
            Assert.That(profile.Name, Is.EqualTo("qBittorrent 4.4.2"));
            Assert.That(profile.UserAgent, Is.EqualTo("qBittorrent/4.4.2"));
            Assert.That(profile.ClientVersion, Is.EqualTo("4.4.2"));
            Assert.That(profile.DefaultPort, Is.EqualTo(6881));
            Assert.That(profile.SupportsEncryption, Is.True);
            Assert.That(profile.SupportsDht, Is.True);
            Assert.That(profile.SupportsPex, Is.True);
        });
    }

    [Test]
    public void Deluge_properties_should_match_expected_values()
    {
        var profile = new DelugeProfile();

        Assert.Multiple(() =>
        {
            Assert.That(profile.Name, Is.EqualTo("Deluge 2.0.3"));
            Assert.That(profile.UserAgent, Is.EqualTo("Deluge/2.0.3"));
            Assert.That(profile.ClientVersion, Is.EqualTo("2.0.3"));
            Assert.That(profile.DefaultPort, Is.EqualTo(6881));
            Assert.That(profile.SupportsEncryption, Is.True);
            Assert.That(profile.SupportsDht, Is.True);
            Assert.That(profile.SupportsPex, Is.True);
        });
    }

    [Test]
    public void Transmission_properties_should_match_expected_values()
    {
        var profile = new TransmissionProfile();

        Assert.Multiple(() =>
        {
            Assert.That(profile.Name, Is.EqualTo("Transmission 3.00"));
            Assert.That(profile.UserAgent, Is.EqualTo("Transmission/3.00"));
            Assert.That(profile.ClientVersion, Is.EqualTo("3.00"));
            Assert.That(profile.DefaultPort, Is.EqualTo(51413));
            Assert.That(profile.SupportsEncryption, Is.True);
            Assert.That(profile.SupportsDht, Is.True);
            Assert.That(profile.SupportsPex, Is.True);
        });
    }
}
