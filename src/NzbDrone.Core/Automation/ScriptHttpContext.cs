#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Automation;

public class ScriptHttpContext
{
    private static readonly Lazy<HttpClient> InsecureClient = new(() => CreatePooledClient(allowInsecure: true));
    private static readonly Lazy<HttpClient> SecureClient = new(() => CreatePooledClient(allowInsecure: false));

    public static HttpClient DefaultInsecureClient => InsecureClient.Value;
    public static HttpClient DefaultSecureClient => SecureClient.Value;

    private readonly HttpClient? _customClient;

    public ScriptHttpContext(HttpClient? client = null)
    {
        _customClient = client;
    }

    private static HttpClient CreatePooledClient(bool allowInsecure)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
        };

        if (allowInsecure)
        {
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
        }

        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        return client;
    }

    public HttpClient ResolveClient(bool allowInsecure)
    {
        return _customClient ?? (allowInsecure ? InsecureClient.Value : SecureClient.Value);
    }

    public object get(string url, IDictionary<string, object>? options = null)
    {
        return SendAsync(HttpMethod.Get, url, null, options).GetAwaiter().GetResult();
    }

    public object post(string url, object? body = null, IDictionary<string, object>? options = null)
    {
        return SendAsync(HttpMethod.Post, url, body, options).GetAwaiter().GetResult();
    }

    public object put(string url, object? body = null, IDictionary<string, object>? options = null)
    {
        return SendAsync(HttpMethod.Put, url, body, options).GetAwaiter().GetResult();
    }

    public object delete(string url, IDictionary<string, object>? options = null)
    {
        return SendAsync(HttpMethod.Delete, url, null, options).GetAwaiter().GetResult();
    }

    private async Task<Dictionary<string, object?>> SendAsync(HttpMethod method, string url, object? body, IDictionary<string, object>? options)
    {
        using var request = new HttpRequestMessage(method, url);

        if (options != null)
        {
            if (options.TryGetValue("headers", out var rawHeaders) && rawHeaders is IDictionary<string, object> headersDict)
            {
                foreach (var kvp in headersDict)
                {
                    request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value?.ToString());
                }
            }

            if (options.TryGetValue("cookies", out var rawCookies))
            {
                if (rawCookies is IDictionary<string, object> cookiesDict)
                {
                    var cookieHeader = new StringBuilder();
                    foreach (var kvp in cookiesDict)
                    {
                        if (cookieHeader.Length > 0)
                        {
                            cookieHeader.Append("; ");
                        }

                        cookieHeader.Append($"{kvp.Key}={kvp.Value}");
                    }

                    request.Headers.TryAddWithoutValidation("Cookie", cookieHeader.ToString());
                }
                else if (rawCookies is string cookieStr)
                {
                    request.Headers.TryAddWithoutValidation("Cookie", cookieStr);
                }
            }
        }

        if (body != null)
        {
            if (body is string strBody)
            {
                var contentType = "text/plain";
                if (options != null && options.TryGetValue("contentType", out var ct) && ct != null)
                {
                    contentType = ct.ToString()!;
                }
                else if (strBody.TrimStart().StartsWith('{') || strBody.TrimStart().StartsWith('['))
                {
                    contentType = "application/json";
                }

                request.Content = new StringContent(strBody, Encoding.UTF8, contentType);
            }
            else if (body is IDictionary<string, object> formDict)
            {
                var isJson = options != null && options.TryGetValue("json", out var jsonOpt) && jsonOpt is true;
                if (isJson)
                {
                    var jsonStr = JsonSerializer.Serialize(formDict);
                    request.Content = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                }
                else
                {
                    var formList = new List<KeyValuePair<string, string>>();
                    foreach (var kvp in formDict)
                    {
                        formList.Add(new KeyValuePair<string, string>(kvp.Key, kvp.Value?.ToString() ?? string.Empty));
                    }

                    request.Content = new FormUrlEncodedContent(formList);
                }
            }
        }

        var timeoutSeconds = 15;
        var allowInsecure = true;
        if (options != null)
        {
            if (options.TryGetValue("timeoutSeconds", out var toVal) && int.TryParse(toVal?.ToString(), out var toParsed))
            {
                timeoutSeconds = toParsed;
            }

            if (options.TryGetValue("allowInsecure", out var aiVal) && bool.TryParse(aiVal?.ToString(), out var aiParsed))
            {
                allowInsecure = aiParsed;
            }
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 15));
        var client = ResolveClient(allowInsecure);
        using var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);

        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var statusCode = (int)response.StatusCode;
        var isOk = response.IsSuccessStatusCode;

        object? parsedJson = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(responseBody) &&
                (responseBody.TrimStart().StartsWith('{') || responseBody.TrimStart().StartsWith('[')))
            {
                using var doc = JsonDocument.Parse(responseBody);
                parsedJson = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);
            }
        }
        catch
        {
            // not valid JSON
        }

        var resHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in response.Headers)
        {
            resHeaders[h.Key] = string.Join(", ", h.Value);
        }

        return new Dictionary<string, object?>
        {
            ["status"] = statusCode,
            ["ok"] = isOk,
            ["body"] = responseBody,
            ["json"] = parsedJson,
            ["headers"] = resHeaders,
        };
    }
}
