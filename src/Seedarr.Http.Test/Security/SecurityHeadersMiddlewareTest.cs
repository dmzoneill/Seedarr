using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Seedarr.Http.Security;

namespace Seedarr.Http.Test.Security;

[TestFixture]
public class SecurityHeadersMiddlewareTest
{
    [Test]
    public async Task InvokeAsync_AddsExpectedSecurityHeaders_WhenStarting()
    {
        var context = new DefaultHttpContext();
        var nextCalled = false;
        var middleware = new SecurityHeadersMiddleware(next: ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        Assert.That(nextCalled, Is.True);
        Assert.That(context.Response.Headers["X-Frame-Options"].ToString(), Is.EqualTo("SAMEORIGIN"));
        Assert.That(context.Response.Headers["X-Content-Type-Options"].ToString(), Is.EqualTo("nosniff"));
        Assert.That(context.Response.Headers["Referrer-Policy"].ToString(), Is.EqualTo("strict-origin-when-cross-origin"));
        Assert.That(context.Response.Headers["Permissions-Policy"].ToString(), Is.EqualTo("geolocation=(), camera=(), microphone=()"));
        Assert.That(context.Response.Headers["X-XSS-Protection"].ToString(), Is.EqualTo("0"));
        Assert.That(context.Response.Headers["Content-Security-Policy"].ToString(), Is.EqualTo("frame-ancestors 'self';"));
    }

    [Test]
    public async Task InvokeAsync_DoesNotOverwriteExistingHeaders()
    {
        var context = new DefaultHttpContext();
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";

        var middleware = new SecurityHeadersMiddleware(next: ctx => Task.CompletedTask);

        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        Assert.That(context.Response.Headers["X-Frame-Options"].ToString(), Is.EqualTo("DENY"));
        Assert.That(context.Response.Headers["Content-Security-Policy"].ToString(), Is.EqualTo("default-src 'none'"));
        Assert.That(context.Response.Headers["X-Content-Type-Options"].ToString(), Is.EqualTo("nosniff"));
        Assert.That(context.Response.Headers["Referrer-Policy"].ToString(), Is.EqualTo("strict-origin-when-cross-origin"));
        Assert.That(context.Response.Headers["Permissions-Policy"].ToString(), Is.EqualTo("geolocation=(), camera=(), microphone=()"));
        Assert.That(context.Response.Headers["X-XSS-Protection"].ToString(), Is.EqualTo("0"));
    }
}
