using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using NzbDrone.Core.Simulation.ClientBehavior.Profiles;

namespace NzbDrone.Core.Test.Simulation.ClientBehavior;

[TestFixture]
public class BiglyBTProfileTest
{
    private BiglyBTProfile _profile;

    [SetUp]
    public void SetUp()
    {
        _profile = new BiglyBTProfile();
    }

    [Test]
    public void Name_should_be_BiglyBT_2_7_0_0()
    {
        Assert.That(_profile.Name, Is.EqualTo("BiglyBT 2.7.0.0"));
    }

    [Test]
    public void PeerIdPrefix_should_be_BG2700()
    {
        Assert.That(_profile.PeerIdPrefix, Is.EqualTo("-BG2700-"));
    }

    [Test]
    public void UserAgent_should_be_correct()
    {
        Assert.That(_profile.UserAgent, Is.EqualTo("BiglyBT/2.7.0.0"));
    }

    [Test]
    public void ClientVersion_should_be_correct()
    {
        Assert.That(_profile.ClientVersion, Is.EqualTo("2.7.0.0"));
    }

    [Test]
    public void DefaultPort_should_be_6881()
    {
        Assert.That(_profile.DefaultPort, Is.EqualTo(6881));
    }

    [Test]
    public void SupportsEncryption_should_be_true()
    {
        Assert.That(_profile.SupportsEncryption, Is.True);
    }

    [Test]
    public void SupportsDht_should_be_true()
    {
        Assert.That(_profile.SupportsDht, Is.True);
    }

    [Test]
    public void SupportsPex_should_be_true()
    {
        Assert.That(_profile.SupportsPex, Is.True);
    }

    [Test]
    public void GeneratePeerId_should_return_20_characters()
    {
        var peerId = _profile.GeneratePeerId();

        Assert.That(peerId, Has.Length.EqualTo(20));
    }

    [Test]
    public void GeneratePeerId_should_fit_in_20_bytes()
    {
        var peerId = _profile.GeneratePeerId();
        var bytes = Encoding.ASCII.GetBytes(peerId);

        Assert.That(bytes, Has.Length.EqualTo(20));
    }

    [Test]
    public void GeneratePeerId_should_start_with_PeerIdPrefix()
    {
        var peerId = _profile.GeneratePeerId();

        Assert.That(peerId, Does.StartWith(_profile.PeerIdPrefix));
    }

    [Test]
    public void PeerIdPrefix_should_be_8_characters()
    {
        Assert.That(_profile.PeerIdPrefix, Has.Length.EqualTo(8));
    }

    [Test]
    public void GeneratePeerId_suffix_should_be_numeric_digits()
    {
        var peerId = _profile.GeneratePeerId();
        var suffix = peerId.Substring(_profile.PeerIdPrefix.Length);

        Assert.That(suffix, Does.Match("^[0-9]+$"));
    }

    [Test]
    public void GeneratePeerId_should_produce_unique_ids()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => _profile.GeneratePeerId()).ToList();
        var uniqueIds = new HashSet<string>(ids);

        Assert.That(uniqueIds.Count, Is.EqualTo(100));
    }

    [Test]
    public void PeerIdPrefix_should_follow_azureus_style_format()
    {
        Assert.That(_profile.PeerIdPrefix, Does.Match("^-[A-Za-z]{2}[0-9]{4}-$"));
    }

    [Test]
    public void Profile_should_have_non_empty_name_and_version()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_profile.Name, Is.Not.Empty);
            Assert.That(_profile.UserAgent, Is.Not.Empty);
            Assert.That(_profile.ClientVersion, Is.Not.Empty);
            Assert.That(_profile.DefaultPort, Is.GreaterThan(0));
            Assert.That(_profile.SupportsEncryption, Is.True);
            Assert.That(_profile.SupportsDht, Is.True);
            Assert.That(_profile.SupportsPex, Is.True);
        });
    }
}
