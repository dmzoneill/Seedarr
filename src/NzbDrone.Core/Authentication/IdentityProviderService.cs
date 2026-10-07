using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using NLog;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Authentication;

public class IdentityProviderService : IIdentityProviderService
{
    public const string DataProtectionPurpose = "Seedarr.IdentityProvider.ClientSecret";
    private const string MaskedClientSecret = "********";
    private const string AlternateMaskedClientSecret = "******";

    public static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AllowAutoRedirect = false,
    };

    private static readonly HttpClient DefaultHttpClient = new(SharedHandler, disposeHandler: false);
    private readonly IIdentityProviderRepository _repository;
    private readonly HttpClient _httpClient;
    private readonly IDataProtector _protector;
    private readonly Logger _logger;

    public IdentityProviderService(
        IIdentityProviderRepository repository,
        HttpClient httpClient = null,
        IDataProtectionProvider dataProtectionProvider = null)
    {
        _repository = repository;
        _httpClient = httpClient ?? DefaultHttpClient;
        _protector = dataProtectionProvider?.CreateProtector(DataProtectionPurpose);
        _logger = LogManager.GetCurrentClassLogger();
    }

    public List<IdentityProviderDefinition> GetAll()
    {
        return _repository.All().ToList();
    }

    public List<IdentityProviderDefinition> GetEnabled()
    {
        return _repository.GetEnabled().ToList();
    }

    public IdentityProviderDefinition GetById(int id)
    {
        return _repository.Get(id);
    }

    public IdentityProviderDefinition GetByProviderId(string providerId)
    {
        return _repository.FindByProviderId(providerId);
    }

    public IdentityProviderDefinition Add(IdentityProviderDefinition provider)
    {
        provider.CreatedAt = DateTime.UtcNow;
        provider.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(provider.ClientSecretEncrypted))
        {
            provider.ClientSecretEncrypted = EncryptClientSecret(provider.ClientSecretEncrypted);
        }

        return _repository.Insert(provider);
    }

    public IdentityProviderDefinition Update(IdentityProviderDefinition provider)
    {
        provider.UpdatedAt = DateTime.UtcNow;

        var existing = _repository.Get(provider.Id);
        if (existing != null && ShouldPreserveClientSecret(provider.ClientSecretEncrypted))
        {
            provider.ClientSecretEncrypted = existing.ClientSecretEncrypted;
        }
        else if (!string.IsNullOrEmpty(provider.ClientSecretEncrypted))
        {
            provider.ClientSecretEncrypted = EncryptClientSecret(provider.ClientSecretEncrypted);
        }

        _repository.Update(provider);
        return provider;
    }

    public void Delete(int id)
    {
        _repository.Delete(id);
    }

    public string EncryptClientSecret(string secret)
    {
        if (string.IsNullOrEmpty(secret) || _protector == null)
        {
            return secret;
        }

        if (IsEncrypted(secret))
        {
            return secret;
        }

        try
        {
            return _protector.Protect(secret);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to encrypt identity provider client secret");
            return secret;
        }
    }

    public string DecryptClientSecret(string encryptedSecret)
    {
        if (string.IsNullOrEmpty(encryptedSecret) || _protector == null)
        {
            return encryptedSecret;
        }

        try
        {
            return _protector.Unprotect(encryptedSecret);
        }
        catch (Exception ex)
        {
            _logger.Trace(ex, "Failed to unprotect client secret, falling back to plaintext");
            return encryptedSecret;
        }
    }

    private static bool ShouldPreserveClientSecret(string clientSecret)
    {
        return string.IsNullOrWhiteSpace(clientSecret)
            || clientSecret == MaskedClientSecret
            || clientSecret == AlternateMaskedClientSecret;
    }

    private bool IsEncrypted(string value)
    {
        if (string.IsNullOrEmpty(value) || _protector == null)
        {
            return false;
        }

        try
        {
            _protector.Unprotect(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TestConnectionAsync(IdentityProviderDefinition provider)
    {
        if (provider == null)
        {
            return false;
        }

        try
        {
            if (provider.ProviderType == IdentityProviderType.ForwardAuth)
            {
                return true;
            }

            var rawUrl = provider.ProviderType switch
            {
                IdentityProviderType.Oidc => !string.IsNullOrWhiteSpace(provider.IssuerUrl)
                    ? (provider.IssuerUrl.Trim().TrimEnd('/').EndsWith(".well-known/openid-configuration", StringComparison.OrdinalIgnoreCase)
                        ? provider.IssuerUrl.Trim()
                        : (provider.IssuerUrl.Trim().EndsWith('/') ? provider.IssuerUrl.Trim() + ".well-known/openid-configuration" : provider.IssuerUrl.Trim() + "/.well-known/openid-configuration"))
                    : provider.MetadataUrl,
                IdentityProviderType.Saml => provider.MetadataUrl,
                _ => provider.IssuerUrl,
            };

            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                return false;
            }

            var targetUrl = rawUrl.Trim();

            if (!UrlValidator.IsSafeUrl(targetUrl))
            {
                _logger.Warn("Blocked unsafe target URL for identity provider {0}: {1}", provider.Name, targetUrl);
                return false;
            }

            if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                _logger.Warn("Blocked non-HTTPS target URL for identity provider {0}: {1}", provider.Name, targetUrl);
                return false;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await _httpClient.GetAsync(targetUrl, cts.Token); // NOSONAR
            return await IsTestConnectionSuccessfulAsync(response, targetUrl, provider.Name, cts.Token);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to test connection to identity provider {0}", provider.Name);
            return false;
        }
    }

    private async Task<bool> IsTestConnectionSuccessfulAsync(
        HttpResponseMessage response,
        string requestUrl,
        string providerName,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        if (!IsRedirectStatusCode(response.StatusCode))
        {
            return false;
        }

        var redirectUrl = ResolveRedirectLocation(response, requestUrl);
        if (redirectUrl == null)
        {
            _logger.Warn("Redirect from identity provider {0} missing Location header for {1}", providerName, requestUrl);
            return false;
        }

        if (!UrlValidator.IsSafeUrl(redirectUrl))
        {
            _logger.Warn("Blocked unsafe redirect URL for identity provider {0}: {1}", providerName, redirectUrl);
            return false;
        }

        if (!Uri.TryCreate(redirectUrl, UriKind.Absolute, out var redirectUri) || redirectUri.Scheme != Uri.UriSchemeHttps)
        {
            _logger.Warn("Blocked non-HTTPS redirect URL for identity provider {0}: {1}", providerName, redirectUrl);
            return false;
        }

        using var redirectResponse = await _httpClient.GetAsync(redirectUrl, cancellationToken); // NOSONAR
        return redirectResponse.IsSuccessStatusCode;
    }

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private static string ResolveRedirectLocation(HttpResponseMessage response, string requestUrl)
    {
        var location = response.Headers.Location;
        if (location == null && response.Headers.TryGetValues("Location", out var values))
        {
            var rawLocation = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(rawLocation)
                && Uri.TryCreate(rawLocation, UriKind.RelativeOrAbsolute, out var parsed))
            {
                location = parsed;
            }
        }

        if (location == null)
        {
            return null;
        }

        var targetUri = location.IsAbsoluteUri ? location : new Uri(new Uri(requestUrl), location);
        return targetUri.AbsoluteUri;
    }
}
