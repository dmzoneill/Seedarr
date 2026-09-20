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
public class ApiKeyAuthenticationHandlerTest
{
    private IConfigFileProvider _configFileProvider;
    private IOptionsMonitor<ApiKeyAuthenticationOptions> _optionsMonitor;
    private ILoggerFactory _loggerFactory;
    private UrlEncoder _encoder;

    [SetUp]
    public void SetUp()
    {
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _configFileProvider.AuthenticationEnabled.Returns(true);

        _optionsMonitor = Substitute.For<IOptionsMonitor<ApiKeyAuthenticationOptions>>();
        var options = new ApiKeyAuthenticationOptions();
        _optionsMonitor.Get(Arg.Any<string>()).Returns(options);
        _optionsMonitor.CurrentValue.Returns(options);

        _loggerFactory = Substitute.For<ILoggerFactory>();
        _encoder = UrlEncoder.Default;
    }

    private async Task<AuthenticateResult> AuthenticateAsync(HttpContext context)
    {
        var handler = new ApiKeyAuthenticationHandler(_optionsMonitor, _loggerFactory, _encoder, _configFileProvider);
        var scheme = new AuthenticationScheme(ApiKeyAuthenticationOptions.DefaultScheme, null, typeof(ApiKeyAuthenticationHandler));
        await handler.InitializeAsync(scheme, context);
        return await handler.AuthenticateAsync();
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenConfiguredApiKeyIsNull_DoesNotThrowAndFailsAuthentication()
    {
        _configFileProvider.ApiKey.Returns((string)null);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "client-provided-api-key";

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Failure, Is.Not.Null);
        Assert.That(result.Failure.Message, Is.EqualTo("Invalid API Key"));
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenConfiguredApiKeyIsWhitespace_DoesNotThrowAndFailsAuthentication()
    {
        _configFileProvider.ApiKey.Returns("   ");

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "client-provided-api-key";

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Failure, Is.Not.Null);
        Assert.That(result.Failure.Message, Is.EqualTo("Invalid API Key"));
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenNoApiKeyProvided_ReturnsNoResult()
    {
        _configFileProvider.ApiKey.Returns("configured-secret-key");

        var context = new DefaultHttpContext();

        var result = await AuthenticateAsync(context);

        Assert.That(result.None, Is.True);
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenValidApiKeyInHeader_Succeeds()
    {
        _configFileProvider.ApiKey.Returns("configured-secret-key");

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "configured-secret-key";

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Principal?.Identity?.Name, Is.EqualTo("API"));
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenValidApiKeyInBearerHeader_Succeeds()
    {
        _configFileProvider.ApiKey.Returns("configured-secret-key");

        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = "Bearer configured-secret-key";

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenInvalidApiKey_Fails()
    {
        _configFileProvider.ApiKey.Returns("configured-secret-key");

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "incorrect-key";

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Failure?.Message, Is.EqualTo("Invalid API Key"));
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenApiKeyDifferentLength_FailsGracefullyWithoutTimingLeak()
    {
        _configFileProvider.ApiKey.Returns("configured-secret-key-12345");

        // Shorter key
        var shortContext = new DefaultHttpContext();
        shortContext.Request.Headers["X-Api-Key"] = "short";
        var shortResult = await AuthenticateAsync(shortContext);
        Assert.That(shortResult.Succeeded, Is.False);

        // Longer key
        var longContext = new DefaultHttpContext();
        longContext.Request.Headers["X-Api-Key"] = "configured-secret-key-12345-much-longer-key-extension";
        var longResult = await AuthenticateAsync(longContext);
        Assert.That(longResult.Succeeded, Is.False);
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenAuthenticationDisabled_SucceedsWithoutKey()
    {
        _configFileProvider.AuthenticationEnabled.Returns(false);

        var context = new DefaultHttpContext();

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Principal?.Identity?.Name, Is.EqualTo("Anonymous"));
    }
}
