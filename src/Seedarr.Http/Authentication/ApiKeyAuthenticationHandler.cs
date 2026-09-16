using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NzbDrone.Core.Configuration;

namespace Seedarr.Http.Authentication;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "ApiKey";
    public string HeaderName { get; set; } = "X-Api-Key";
}

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IConfigFileProvider _configFileProvider;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfigFileProvider configFileProvider)
        : base(options, logger, encoder)
    {
        _configFileProvider = configFileProvider;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_configFileProvider.AuthenticationEnabled)
        {
            var noAuthClaims = new[]
            {
                new Claim(ClaimTypes.Name, "Anonymous"),
                new Claim(ClaimTypes.Role, "Admin"),
            };
            var noAuthIdentity = new ClaimsIdentity(noAuthClaims, "NoAuth");
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(noAuthIdentity),
                    ApiKeyAuthenticationOptions.DefaultScheme)));
        }

        var apiKey = Request.Headers[Options.HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(apiKey) && Request.Headers.TryGetValue("ApiKey", out var altHeader))
        {
            apiKey = altHeader.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(apiKey) && Request.Query.TryGetValue("apikey", out var qKey))
        {
            apiKey = qKey.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(apiKey) && Request.Query.TryGetValue("api_key", out var qKey2))
        {
            apiKey = qKey2.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(apiKey) && Request.Query.TryGetValue("access_token", out var qToken))
        {
            apiKey = qToken.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(apiKey) &&
            Request.Headers.TryGetValue("Authorization", out var authHeader) &&
            authHeader.ToString().StartsWith("Bearer ", System.StringComparison.OrdinalIgnoreCase))
        {
            apiKey = authHeader.ToString()["Bearer ".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var configuredApiKey = _configFileProvider.ApiKey;
        if (string.IsNullOrWhiteSpace(configuredApiKey) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(apiKey),
                Encoding.UTF8.GetBytes(configuredApiKey)))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "API"),
            new Claim(ClaimTypes.Role, "Admin"),
        };
        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationOptions.DefaultScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationOptions.DefaultScheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
