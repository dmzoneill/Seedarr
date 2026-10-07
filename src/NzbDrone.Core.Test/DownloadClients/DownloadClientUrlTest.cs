using NUnit.Framework;
using NzbDrone.Core.DownloadClients;

namespace NzbDrone.Core.Test.DownloadClients;

[TestFixture]
public class DownloadClientUrlTest
{
    [TestCase(null, "")]
    [TestCase("", "")]
    [TestCase("   ", "")]
    [TestCase("/", "")]
    [TestCase("qbittorrent", "/qbittorrent")]
    [TestCase("/qbittorrent/", "/qbittorrent")]
    [TestCase("/qbittorrent", "/qbittorrent")]
    public void NormalizeUrlBase_normalizes_path_prefix(string input, string expected)
    {
        Assert.That(DownloadClientUrl.NormalizeUrlBase(input), Is.EqualTo(expected));
    }

    [Test]
    public void BuildRootUrl_includes_normalized_url_base()
    {
        Assert.That(
            DownloadClientUrl.BuildRootUrl(false, "host", 8080, "/qbittorrent/"),
            Is.EqualTo("http://host:8080/qbittorrent"));
    }
}
