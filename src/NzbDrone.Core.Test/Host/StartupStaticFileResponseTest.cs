using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Host;

namespace NzbDrone.Core.Test.Host;

[TestFixture]
public class StartupStaticFileResponseTest
{
    [Test]
    public void PrepareWwwrootStaticFileResponse_index_html_sets_no_cache_headers()
    {
        var context = new DefaultHttpContext();
        var file = Substitute.For<IFileInfo>();
        file.Name.Returns("index.html");
        var ctx = new StaticFileResponseContext(context, file);

        Startup.PrepareWwwrootStaticFileResponse(ctx);

        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-cache, no-store, must-revalidate"));
        Assert.That(context.Response.Headers.Pragma.ToString(), Is.EqualTo("no-cache"));
        Assert.That(context.Response.Headers.Expires.ToString(), Is.EqualTo("0"));
    }

    [Test]
    public void PrepareWwwrootStaticFileResponse_other_assets_set_long_term_cache_headers()
    {
        var context = new DefaultHttpContext();
        var file = Substitute.For<IFileInfo>();
        file.Name.Returns("app.bundle.js");
        var ctx = new StaticFileResponseContext(context, file);

        Startup.PrepareWwwrootStaticFileResponse(ctx);

        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("public, max-age=31536000, immutable"));
    }

    [Test]
    public async Task TryEnsureAuthenticatedWhenRequiredAsync_skips_check_when_authentication_disabled()
    {
        var config = Substitute.For<IConfigFileProvider>();
        config.AuthenticationEnabled.Returns(false);
        var context = new DefaultHttpContext();

        var result = await Startup.TryEnsureAuthenticatedWhenRequiredAsync(context, config);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task TryEnsureAuthenticatedWhenRequiredAsync_succeeds_when_user_already_authenticated()
    {
        var config = Substitute.For<IConfigFileProvider>();
        config.AuthenticationEnabled.Returns(true);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "tester") },
                authenticationType: "Cookies",
                nameType: ClaimTypes.Name,
                roleType: ClaimTypes.Role)),
        };

        var result = await Startup.TryEnsureAuthenticatedWhenRequiredAsync(context, config);

        Assert.That(result, Is.True);
    }
}
