using System;
using System.Net.Http;
using System.Net.Security;
using NzbDrone.Core.Http;
using Polly;

namespace NzbDrone.Core.ArrIntegration;

public static class ArrConnectionResources
{
    public static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    });

    public static readonly HttpClient SharedInsecureClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, _, _, _) => true
        }
    });

    public static readonly ResiliencePipeline SharedPolicy = ResiliencePolicies.GetArrApiPolicy();

    public static HttpClient GetClient(bool acceptInvalidCertificates)
    {
        return acceptInvalidCertificates ? SharedInsecureClient : SharedClient;
    }

    public static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var candidate = url.Trim();
        if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            candidate = "http://" + candidate;
        }

        return candidate.TrimEnd('/');
    }

    public static bool TryNormalizeUrl(string url, out string normalizedUrl, out string errorMessage)
    {
        normalizedUrl = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            errorMessage = "URL cannot be empty";
            return false;
        }

        var candidate = url.Trim();
        if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            candidate = "http://" + candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsedUri) ||
            (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(parsedUri.Host))
        {
            errorMessage = $"Invalid URL format: '{url}'";
            return false;
        }

        normalizedUrl = candidate.TrimEnd('/');
        return true;
    }
}
