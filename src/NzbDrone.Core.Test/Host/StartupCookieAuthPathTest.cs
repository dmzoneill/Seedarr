using NUnit.Framework;
using NzbDrone.Host;

namespace NzbDrone.Core.Test.Host;

[TestFixture]
public class StartupCookieAuthPathTest
{
    [TestCase(null, "/")]
    [TestCase("", "/")]
    [TestCase("/", "/")]
    [TestCase("/seedarr", "/seedarr")]
    [TestCase("seedarr", "/seedarr")]
    [TestCase("/seedarr/", "/seedarr/")]
    public void GetCookiePathFromUrlBase_matches_cookie_path_semantics(string urlBase, string expected)
    {
        Assert.That(Startup.GetCookiePathFromUrlBase(urlBase), Is.EqualTo(expected));
    }

    [TestCase(null, "/login")]
    [TestCase("", "/login")]
    [TestCase("/", "/login")]
    [TestCase("/seedarr", "/seedarr/login")]
    [TestCase("seedarr", "/seedarr/login")]
    [TestCase("/seedarr/", "/seedarr/login")]
    public void GetAuthRedirectPathFromUrlBase_prefixes_login_path(string urlBase, string expected)
    {
        Assert.That(Startup.GetAuthRedirectPathFromUrlBase(urlBase, "/login"), Is.EqualTo(expected));
    }

    [TestCase("/seedarr", "/seedarr/login?accessDenied=true")]
    [TestCase("seedarr", "/seedarr/login?accessDenied=true")]
    public void GetAuthRedirectPathFromUrlBase_prefixes_access_denied_path(string urlBase, string expected)
    {
        Assert.That(
            Startup.GetAuthRedirectPathFromUrlBase(urlBase, "/login?accessDenied=true"),
            Is.EqualTo(expected));
    }
}
