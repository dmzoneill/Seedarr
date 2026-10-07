using System.Text.RegularExpressions;
using NUnit.Framework;
using NzbDrone.Host;

namespace NzbDrone.Core.Test.Host;

[TestFixture]
public class StartupSpaFallbackPathTest
{
    private static bool MatchesSpaFallback(string path) =>
        Regex.IsMatch(path, Startup.SpaFallbackExcludePathRegex, RegexOptions.CultureInvariant);

    [TestCase("dashboard")]
    [TestCase("login")]
    [TestCase("settings/general")]
    public void SpaFallback_includes_ui_routes(string path)
    {
        Assert.That(MatchesSpaFallback(path), Is.True);
    }

    [TestCase("api/v1/system/status")]
    [TestCase("signalr/messages")]
    [TestCase("swagger/index.html")]
    [TestCase("fixtures/sample.torrent")]
    [TestCase("transmission/rpc")]
    [TestCase("transmission/typo/extra")]
    [TestCase("ws/terminal")]
    public void SpaFallback_excludes_backend_roots(string path)
    {
        Assert.That(MatchesSpaFallback(path), Is.False);
    }
}
