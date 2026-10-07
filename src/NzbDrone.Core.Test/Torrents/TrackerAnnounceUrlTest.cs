using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TrackerAnnounceUrlTest
{
    [Test]
    public void IsAnnounceUrl_rejects_placeholders()
    {
        Assert.That(TrackerAnnounceUrl.IsAnnounceUrl("*"), Is.False);
        Assert.That(TrackerAnnounceUrl.IsAnnounceUrl("[DHT]"), Is.False);
        Assert.That(TrackerAnnounceUrl.IsAnnounceUrl(""), Is.False);
        Assert.That(TrackerAnnounceUrl.IsAnnounceUrl("   "), Is.False);
    }

    [Test]
    public void IsAnnounceUrl_accepts_standard_schemes()
    {
        Assert.That(TrackerAnnounceUrl.IsAnnounceUrl("http://tracker.example/announce"), Is.True);
        Assert.That(TrackerAnnounceUrl.IsAnnounceUrl("https://tracker.example/announce"), Is.True);
        Assert.That(TrackerAnnounceUrl.IsAnnounceUrl("udp://tracker.example:1337/announce"), Is.True);
    }

    [Test]
    public void FirstAnnounceUrl_skips_invalid_prefix_rows()
    {
        var url = TrackerAnnounceUrl.FirstAnnounceUrl(new[] { "*", "http://tracker.example/announce" });
        Assert.That(url, Is.EqualTo("http://tracker.example/announce"));
    }
}
