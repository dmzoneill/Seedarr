// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net.Http.Headers;
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

public class BasicAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "Basic";
}

public class BasicAuthenticationHandler : AuthenticationHandler<BasicAuthenticationOptions>
{
    private readonly IConfigFileProvider _configFileProvider;

    public BasicAuthenticationHandler(
        IOptionsMonitor<BasicAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfigFileProvider configFileProvider)
        : base(options, logger, encoder)
    {
        _configFileProvider = configFileProvider;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        try
        {
            var authHeader = AuthenticationHeaderValue.Parse(Request.Headers["Authorization"]);
            if (!string.Equals(authHeader.Scheme, "Basic", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var credentialBytes = Convert.FromBase64String(authHeader.Parameter ?? string.Empty);
            var credentials = Encoding.UTF8.GetString(credentialBytes).Split(':', 2);
            var username = credentials.Length > 0 ? credentials[0] : string.Empty;
            var password = credentials.Length > 1 ? credentials[1] : string.Empty;

            var configuredApiKey = _configFileProvider.ApiKey;

            if (!_configFileProvider.AuthenticationEnabled ||
                (!string.IsNullOrWhiteSpace(configuredApiKey) &&
                 FixedTimeEquals(password, configuredApiKey)))
            {
                var safeUsername = string.IsNullOrWhiteSpace(username) ||
                                   (!string.IsNullOrWhiteSpace(configuredApiKey) && FixedTimeEquals(username, configuredApiKey))
                                   ? "Admin"
                                   : username;

                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "1"),
                    new Claim(ClaimTypes.Name, safeUsername),
                    new Claim(ClaimTypes.Role, "Admin"),
                };
                var identity = new ClaimsIdentity(claims, BasicAuthenticationOptions.DefaultScheme);
                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), BasicAuthenticationOptions.DefaultScheme)));
            }

            return Task.FromResult(AuthenticateResult.Fail("Invalid Basic authentication credentials."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AuthenticateResult.Fail($"Failed to parse Basic authentication header: {ex.Message}"));
        }
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        Span<byte> hashA = stackalloc byte[32];
        Span<byte> hashB = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(a), hashA);
        SHA256.HashData(Encoding.UTF8.GetBytes(b), hashB);
        return CryptographicOperations.FixedTimeEquals(hashA, hashB);
    }
}
