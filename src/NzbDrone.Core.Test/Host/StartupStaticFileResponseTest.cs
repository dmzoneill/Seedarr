using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using NSubstitute;
using NUnit.Framework;
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
}
