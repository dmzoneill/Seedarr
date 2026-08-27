using NUnit.Framework;
using NzbDrone.Core.TrackerBoost;

namespace NzbDrone.Core.Test.TrackerBoost;

[TestFixture]
public class TrackerBoostValidationTest
{
    [TestCase("http://127.0.0.1.stackoverflow.tech/44817e2f66221a38b0029e8e098b9aff/announce", true)]
    [TestCase("udp://tracker.opentrackr.org:1337/announce", true)]
    [TestCase("http://tracker.files.fm:6969/announce", true)]
    [TestCase("https://tracker.tamersunion.org:443/announce", true)]
    [TestCase("udp://open.stealth.si:80/announce", true)]
    [TestCase("http://routing.bgp.technology/announce", true)]
    [TestCase("http://localhost:6969/announce", false)]
    [TestCase("http://127.0.0.1:6969/announce", false)]
    [TestCase("http://127.0.0.2:6969/announce", false)]
    [TestCase("http://[::1]:6969/announce", false)]
    [TestCase("http://0.0.0.0:6969/announce", false)]
    [TestCase("http://test.localhost/announce", false)]
    [TestCase("http://tracker.local/announce", false)]
    [TestCase("http://tracker.internal/announce", false)]
    [TestCase("dht://router.bittorrent.com:6881", false)]
    [TestCase("pex://somepeer", false)]
    [TestCase("lsd://somelocal", false)]
    [TestCase("http://tracker.example.com/announce?passkey=secret123", false)]
    [TestCase("http://tracker.example.com/announce?authkey=secret123", false)]
    [TestCase("http://tracker.example.com/announce?torrentpass=secret123", false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("not a url", false)]
    public void IsValidPublicTrackerUrl_validates_correctly(string url, bool expected)
    {
        var result = TrackerBoostService.IsValidPublicTrackerUrl(url);
        Assert.That(result, Is.EqualTo(expected), $"Failed for URL: {url}");
    }
}
