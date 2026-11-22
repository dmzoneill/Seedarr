using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.WebSeeds;

public interface IWebSeedRedirectHandler
{
    IReadOnlyDictionary<string, string> PermanentRedirects { get; }

    Task<HttpResponseMessage> SendWithRedirectsAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseHeadersRead,
        CancellationToken cancellationToken = default);

    string GetResolvedUrl(string url);

    void RecordPermanentRedirect(string originalUrl, string targetUrl);

    void ClearCache();
}

public class WebSeedRedirectHandler : IWebSeedRedirectHandler
{
    public const int MaxRedirects = 5;

    private readonly ConcurrentDictionary<string, string> _permanentRedirects = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> PermanentRedirects => _permanentRedirects;

    public static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    public string GetResolvedUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var current = url;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (visited.Add(current) && _permanentRedirects.TryGetValue(current, out var target))
        {
            current = target;
        }

        return current;
    }

    public void RecordPermanentRedirect(string originalUrl, string targetUrl)
    {
        if (string.IsNullOrWhiteSpace(originalUrl) || string.IsNullOrWhiteSpace(targetUrl))
        {
            return;
        }

        _permanentRedirects[originalUrl] = targetUrl;
    }

    public void ClearCache()
    {
        _permanentRedirects.Clear();
    }

    public async Task<HttpResponseMessage> SendWithRedirectsAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseHeadersRead,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(request);

        var currentRequest = request;
        var initialUri = request.RequestUri;
        EnsureSafeWebSeedTarget(initialUri);

        var resolvedUrlString = GetResolvedUrl(initialUri.AbsoluteUri);
        if (!string.Equals(resolvedUrlString, initialUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
        {
            var resolvedUri = new Uri(resolvedUrlString);
            EnsureSafeWebSeedTarget(resolvedUri);
            currentRequest = CreateRedirectRequest(request, resolvedUri);
        }

        var visitedUris = new HashSet<Uri> { currentRequest.RequestUri };
        var redirectCount = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await httpClient.SendAsync(currentRequest, completionOption, cancellationToken);

            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            var location = response.Headers.Location;
            if (location == null)
            {
                if (response.Headers.TryGetValues("Location", out var values))
                {
                    var rawLocation = System.Linq.Enumerable.FirstOrDefault(values);
                    if (!string.IsNullOrWhiteSpace(rawLocation) && Uri.TryCreate(rawLocation, UriKind.RelativeOrAbsolute, out var parsed))
                    {
                        location = parsed;
                    }
                }
            }

            if (location == null)
            {
                throw new InvalidOperationException($"Redirect status code {response.StatusCode} returned without valid Location header.");
            }

            var targetUri = location.IsAbsoluteUri ? location : new Uri(currentRequest.RequestUri, location);

            try
            {
                EnsureSafeWebSeedTarget(targetUri);
            }
            catch (InvalidOperationException)
            {
                response.Dispose();
                if (currentRequest != request)
                {
                    currentRequest.Dispose();
                }

                throw;
            }

            if (!visitedUris.Add(targetUri))
            {
                response.Dispose();
                if (currentRequest != request)
                {
                    currentRequest.Dispose();
                }

                throw new InvalidOperationException($"Circular redirect detected for URI: {targetUri}");
            }

            redirectCount++;
            if (redirectCount > MaxRedirects)
            {
                response.Dispose();
                if (currentRequest != request)
                {
                    currentRequest.Dispose();
                }

                throw new InvalidOperationException($"Maximum redirect limit of {MaxRedirects} exceeded.");
            }

            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.PermanentRedirect)
            {
                RecordPermanentRedirect(initialUri.AbsoluteUri, targetUri.AbsoluteUri);
                RecordPermanentRedirect(currentRequest.RequestUri.AbsoluteUri, targetUri.AbsoluteUri);
            }

            var nextRequest = CreateRedirectRequest(currentRequest, targetUri);

            response.Dispose();
            if (currentRequest != request)
            {
                currentRequest.Dispose();
            }

            currentRequest = nextRequest;
        }
    }

    private static HttpRequestMessage CreateRedirectRequest(HttpRequestMessage originalRequest, Uri targetUri)
    {
        var newRequest = new HttpRequestMessage(originalRequest.Method, targetUri)
        {
            Version = originalRequest.Version,
            VersionPolicy = originalRequest.VersionPolicy
        };

        foreach (var header in originalRequest.Headers)
        {
            newRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (originalRequest.Headers.Range != null)
        {
            newRequest.Headers.Range = originalRequest.Headers.Range;
        }

        return newRequest;
    }

    internal static void EnsureSafeWebSeedTarget(Uri uri)
    {
        if (!IsAllowedWebSeedTarget(uri))
        {
            throw new InvalidOperationException(
                $"SSRF guard: web seed target '{uri}' is prohibited (restricted IP, loopback, link-local, or metadata host).");
        }
    }

    internal static bool IsAllowedWebSeedTarget(Uri uri)
    {
        if (uri == null || !uri.IsAbsoluteUri)
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var host = uri.IdnHost?.Trim().Trim('[', ']');
        if (string.IsNullOrWhiteSpace(host) || IsDeniedWebSeedHostname(host))
        {
            return false;
        }

        if (IPAddress.TryParse(host, out var literal))
        {
            return !UrlValidator.IsPrivateIp(literal);
        }

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            if (addresses.Length == 0)
            {
                return false;
            }

            return addresses.All(address => !UrlValidator.IsPrivateIp(address));
        }
        catch (SocketException)
        {
            // No resolver. A dotted public name cannot be a literal private address.
            // Names that resolve to private space are rejected when DNS works.
            return host.Contains('.');
        }
    }

    private static bool IsDeniedWebSeedHostname(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "metadata.google.internal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "instance-data", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase);
    }
}
