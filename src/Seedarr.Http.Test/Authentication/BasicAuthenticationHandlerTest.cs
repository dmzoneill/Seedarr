using System;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Http.Authentication;

namespace Seedarr.Http.Test.Authentication;

[TestFixture]
public class BasicAuthenticationHandlerTest
{
    private IConfigFileProvider _configFileProvider;
    private IOptionsMonitor<BasicAuthenticationOptions> _optionsMonitor;
    private ILoggerFactory _loggerFactory;
    private UrlEncoder _encoder;

    [SetUp]
    public void SetUp()
    {
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _configFileProvider.AuthenticationEnabled.Returns(true);

        _optionsMonitor = Substitute.For<IOptionsMonitor<BasicAuthenticationOptions>>();
        var options = new BasicAuthenticationOptions();
        _optionsMonitor.Get(Arg.Any<string>()).Returns(options);
        _optionsMonitor.CurrentValue.Returns(options);

        _loggerFactory = Substitute.For<ILoggerFactory>();
        _encoder = UrlEncoder.Default;
    }

    private async Task<AuthenticateResult> AuthenticateAsync(HttpContext context)
    {
        var handler = new BasicAuthenticationHandler(_optionsMonitor, _loggerFactory, _encoder, _configFileProvider);
        var scheme = new AuthenticationScheme(BasicAuthenticationOptions.DefaultScheme, null, typeof(BasicAuthenticationHandler));
        await handler.InitializeAsync(scheme, context);
        return await handler.AuthenticateAsync();
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenAuthorizationHeaderMissing_ReturnsNoResult()
    {
        var context = new DefaultHttpContext();

        var result = await AuthenticateAsync(context);

        Assert.That(result.None, Is.True);
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenSchemeIsNotBasic_ReturnsNoResult()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = "Bearer some-token";

        var result = await AuthenticateAsync(context);

        Assert.That(result.None, Is.True);
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenApiKeyInUsernameAndInvalidPassword_FailsAuthentication()
    {
        _configFileProvider.ApiKey.Returns("master-api-key");

        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("master-api-key:wrong-password"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Failure, Is.Not.Null);
        Assert.That(result.Failure.Message, Is.EqualTo("Invalid Basic authentication credentials."));
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenValidApiKeyInPassword_Succeeds()
    {
        _configFileProvider.ApiKey.Returns("master-api-key");

        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:master-api-key"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Principal?.FindFirst(ClaimTypes.Name)?.Value, Is.EqualTo("admin"));
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenValidApiKeyInUsernameAndPassword_SanitizesUsernameClaimToAdmin()
    {
        _configFileProvider.ApiKey.Returns("master-api-key");

        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("master-api-key:master-api-key"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Principal?.FindFirst(ClaimTypes.Name)?.Value, Is.EqualTo("Admin"));
        Assert.That(result.Principal?.Claims.Any(c => c.Value.Contains("master-api-key")), Is.False);
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenApiKeyIsNull_FailsWithoutThrowing()
    {
        _configFileProvider.ApiKey.Returns((string)null);

        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:password"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Failure?.Message, Is.EqualTo("Invalid Basic authentication credentials."));
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenPasswordDifferentLength_FailsGracefully()
    {
        _configFileProvider.ApiKey.Returns("master-api-key-configured");

        // Shorter password
        var shortContext = new DefaultHttpContext();
        var shortCreds = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:short"));
        shortContext.Request.Headers["Authorization"] = $"Basic {shortCreds}";
        var shortResult = await AuthenticateAsync(shortContext);
        Assert.That(shortResult.Succeeded, Is.False);

        // Longer password
        var longContext = new DefaultHttpContext();
        var longCreds = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:master-api-key-configured-extra-long-suffix"));
        longContext.Request.Headers["Authorization"] = $"Basic {longCreds}";
        var longResult = await AuthenticateAsync(longContext);
        Assert.That(longResult.Succeeded, Is.False);
    }
}
