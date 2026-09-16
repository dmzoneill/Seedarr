using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Simulation.ClientBehavior.Profiles;

namespace NzbDrone.Core.Test.Simulation.ClientBehavior.Profiles;

[TestFixture]
public class ClientProfilePeerIdTest
{
    private static IEnumerable<TestCaseData> ProfileTestCases()
    {
        yield return new TestCaseData(new QBittorrentProfile(), "-qB4420-", "^[0-9a-zA-Z-_]{12}$", PeerIdGenerator.UrlSafeCharacterSet);
        yield return new TestCaseData(new DelugeProfile(), "-DE2030-", "^[0-9a-zA-Z-_]{12}$", PeerIdGenerator.UrlSafeCharacterSet);
        yield return new TestCaseData(new TransmissionProfile(), "-TR3000-", "^[0-9a-zA-Z]{12}$", PeerIdGenerator.Base62CharacterSet);
        yield return new TestCaseData(new UTorrentProfile(), "-UT3550-", "^[0-9a-zA-Z]{12}$", PeerIdGenerator.Base62CharacterSet);
        yield return new TestCaseData(new BiglyBTProfile(), "-BI2700-", "^[0-9a-zA-Z]{12}$", PeerIdGenerator.Base62CharacterSet);
    }

    [TestCaseSource(nameof(ProfileTestCases))]
    public void GeneratePeerId_should_have_total_length_20(
        IClientProfile profile,
        string expectedPrefix,
        string suffixPattern,
        string expectedCharSet)
    {
        var peerId = profile.GeneratePeerId();

        Assert.That(peerId, Has.Length.EqualTo(20));
    }

    [TestCaseSource(nameof(ProfileTestCases))]
    public void GeneratePeerId_should_begin_with_expected_client_prefix(
        IClientProfile profile,
        string expectedPrefix,
        string suffixPattern,
        string expectedCharSet)
    {
        var peerId = profile.GeneratePeerId();

        Assert.That(peerId, Does.StartWith(expectedPrefix));
        Assert.That(profile.PeerIdPrefix, Is.EqualTo(expectedPrefix));
    }

    [TestCaseSource(nameof(ProfileTestCases))]
    public void GeneratePeerId_should_fit_in_20_ascii_bytes(
        IClientProfile profile,
        string expectedPrefix,
        string suffixPattern,
        string expectedCharSet)
    {
        var peerId = profile.GeneratePeerId();
        var bytes = Encoding.ASCII.GetBytes(peerId);

        Assert.That(bytes, Has.Length.EqualTo(20));
    }

    [TestCaseSource(nameof(ProfileTestCases))]
    public void GeneratePeerId_suffix_should_match_client_character_set(
        IClientProfile profile,
        string expectedPrefix,
        string suffixPattern,
        string expectedCharSet)
    {
        var peerId = profile.GeneratePeerId();
        var suffix = peerId.Substring(profile.PeerIdPrefix.Length);

        Assert.That(suffix, Does.Match(suffixPattern));
        Assert.That(suffix.All(c => expectedCharSet.Contains(c)), Is.True);
    }

    [TestCaseSource(nameof(ProfileTestCases))]
    public void GeneratePeerId_suffix_should_contain_entropy_beyond_decimal_digits(
        IClientProfile profile,
        string expectedPrefix,
        string suffixPattern,
        string expectedCharSet)
    {
        var ids = Enumerable.Range(0, 50).Select(_ => profile.GeneratePeerId()).ToList();
        var anyNonDigits = ids.Any(id => id.Substring(profile.PeerIdPrefix.Length).Any(c => !char.IsDigit(c)));

        Assert.That(
            anyNonDigits,
            Is.True,
            $"Generated peer IDs for {profile.Name} should contain alphanumeric entropy and not solely decimal digits.");
    }

    [TestCaseSource(nameof(ProfileTestCases))]
    public void GeneratePeerId_should_produce_unique_ids_across_invocations(
        IClientProfile profile,
        string expectedPrefix,
        string suffixPattern,
        string expectedCharSet)
    {
        var ids = Enumerable.Range(0, 100).Select(_ => profile.GeneratePeerId()).ToList();
        var uniqueIds = new HashSet<string>(ids);

        Assert.That(uniqueIds.Count, Is.EqualTo(100));
    }

    [Test]
    public void PeerIdGenerator_should_throw_on_null_or_empty_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => PeerIdGenerator.Generate(null, PeerIdGenerator.Base62CharacterSet));
        Assert.Throws<ArgumentNullException>(() => PeerIdGenerator.Generate("-TR3000-", null));
        Assert.Throws<ArgumentException>(() => PeerIdGenerator.Generate("-TR3000-", string.Empty));
    }

    [Test]
    public void PeerIdGenerator_should_sample_uniformly_across_character_set()
    {
        const string charset = PeerIdGenerator.Base62CharacterSet;
        var counts = new Dictionary<char, int>();
        foreach (var c in charset)
        {
            counts[c] = 0;
        }

        const int iterations = 500;
        for (var i = 0; i < iterations; i++)
        {
            var id = PeerIdGenerator.Generate("-XX0000-", charset, 12);
            var suffix = id.Substring(8);
            foreach (var c in suffix)
            {
                counts[c]++;
            }
        }

        var zeroCountChars = counts.Where(kv => kv.Value == 0).ToList();
        Assert.That(zeroCountChars, Is.Empty, "All characters in the character set should be selected over a sufficient number of draws.");
    }
}
