using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Validation;
using Polly;
using Polly.Retry;

namespace NzbDrone.Core.Notifications;

public class WebhookDispatchResult
{
    public bool Success { get; set; }
    public HttpStatusCode? StatusCode { get; set; }
    public string Message { get; set; }
}

public interface IWebhookDispatcher
{
    Task<bool> DispatchAsync(string targetUrl, object payload, string customHeadersJson = null, CancellationToken cancellationToken = default);
    Task<WebhookDispatchResult> DispatchDetailedAsync(string targetUrl, object payload, string customHeadersJson = null, CancellationToken cancellationToken = default);
}

public class WebhookDispatcher : IWebhookDispatcher
{
    public static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = false,
    };

    private static readonly HttpClient SharedDefaultClient = new(SharedHandler, disposeHandler: false)
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly HttpClient _httpClient;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private readonly Logger _logger;
    private readonly TimeSpan _timeout;
    private readonly bool _allowLoopback;

    public TimeSpan Timeout => _timeout;

    public WebhookDispatcher(HttpClient httpClient = null, TimeSpan? timeout = null, bool allowLoopback = false)
        : this(httpClient, null, timeout, allowLoopback)
    {
    }

    internal WebhookDispatcher(HttpClient httpClient, AsyncRetryPolicy<HttpResponseMessage> retryPolicy, TimeSpan? timeout = null, bool allowLoopback = false)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        _allowLoopback = allowLoopback;
        _httpClient = httpClient ?? (timeout == null || timeout == TimeSpan.FromSeconds(10)
            ? SharedDefaultClient
            : new HttpClient(SharedHandler, disposeHandler: false) { Timeout = _timeout });
        _logger = LogManager.GetCurrentClassLogger();
        _retryPolicy = retryPolicy ?? CreateRetryPolicy();
    }

    public static bool IsValidTargetUrl(string targetUrl, bool allowLoopback = false)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return UrlValidator.IsSafeHost(uri.Host, allowLoopback);
    }

    public static string SanitizeUrlForLogging(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var sanitized = Regex.Replace(url, @"bot\d+:[A-Za-z0-9_-]+", "bot[REDACTED]", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"/api/webhooks/(?<id>\d+)/[A-Za-z0-9_-]+", "/api/webhooks/${id}/[REDACTED]", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"/services/T[A-Za-z0-9]+/B[A-Za-z0-9]+/[A-Za-z0-9]+", "/services/[REDACTED]", RegexOptions.IgnoreCase);

        return sanitized;
    }

    internal static AsyncRetryPolicy<HttpResponseMessage> CreateRetryPolicy(
        int retryCount = 3,
        Func<int, TimeSpan> sleepDurationProvider = null,
        Action<DelegateResult<HttpResponseMessage>, TimeSpan, int, Context> onRetry = null)
    {
        return Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
            .OrResult(r => (int)r.StatusCode >= 500 || r.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount,
                sleepDurationProvider: (retryAttempt, outcome, context) =>
                {
                    if (outcome.Result != null)
                    {
                        var retryAfter = ExtractRetryAfter(outcome.Result);
                        if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
                        {
                            return retryAfter.Value;
                        }
                    }

                    return sleepDurationProvider != null
                        ? sleepDurationProvider(retryAttempt)
                        : TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                },
                onRetryAsync: (outcome, timespan, retryAttempt, context) =>
                {
                    outcome.Result?.Dispose();

                    if (onRetry != null)
                    {
                        onRetry(outcome, timespan, retryAttempt, context);
                    }
                    else
                    {
                        LogManager.GetCurrentClassLogger().Warn("Webhook dispatch failed. Retrying in {0}s (Attempt {1}/{2})...", timespan.TotalSeconds, retryAttempt, retryCount);
                    }

                    return Task.CompletedTask;
                });
    }

    public static readonly TimeSpan MaxRetryAfterDelay = TimeSpan.FromSeconds(60);

    public static TimeSpan ClampRetryAfter(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return delay > MaxRetryAfterDelay ? MaxRetryAfterDelay : delay;
    }

    public static TimeSpan? ExtractRetryAfter(HttpResponseMessage response)
    {
        if (response == null)
        {
            return null;
        }

        try
        {
            if (response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Delta.HasValue)
                {
                    return ClampRetryAfter(response.Headers.RetryAfter.Delta.Value);
                }

                if (response.Headers.RetryAfter.Date.HasValue)
                {
                    var delta = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                    if (delta > TimeSpan.Zero)
                    {
                        return ClampRetryAfter(delta);
                    }
                }
            }

            if (response.Headers.TryGetValues("Retry-After", out var retryValues))
            {
                var val = retryValues.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                    {
                        return ClampRetryAfter(TimeSpan.FromSeconds(seconds));
                    }

                    if (DateTimeOffset.TryParse(val, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
                    {
                        var delta = date - DateTimeOffset.UtcNow;
                        if (delta > TimeSpan.Zero)
                        {
                            return ClampRetryAfter(delta);
                        }
                    }
                }
            }

            if (response.Content != null)
            {
                // Inspect buffered content in memory without synchronous blocking on network content streams
                var stream = response.Content.ReadAsStream();
                if (stream is MemoryStream ms)
                {
                    var originalPosition = ms.Position;
                    string rawBody;
                    using (var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
                    {
                        rawBody = reader.ReadToEnd();
                    }

                    if (ms.CanSeek)
                    {
                        ms.Position = originalPosition;
                    }

                    if (!string.IsNullOrWhiteSpace(rawBody) && rawBody.TrimStart().StartsWith("{"))
                    {
                        using var doc = JsonDocument.Parse(rawBody);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("parameters", out var paramsElem) &&
                            paramsElem.ValueKind == JsonValueKind.Object &&
                            paramsElem.TryGetProperty("retry_after", out var tgRetry))
                        {
                            if (tgRetry.TryGetDouble(out var s))
                            {
                                return ClampRetryAfter(TimeSpan.FromSeconds(s));
                            }
                        }

                        var retryProps = new[] { "retry_after", "retryAfter", "retry_after_seconds", "retryAfterSeconds" };
                        foreach (var prop in retryProps)
                        {
                            if (root.TryGetProperty(prop, out var elem))
                            {
                                if (elem.ValueKind == JsonValueKind.Number && elem.TryGetDouble(out var s))
                                {
                                    return ClampRetryAfter(TimeSpan.FromSeconds(s));
                                }

                                if (elem.ValueKind == JsonValueKind.String && double.TryParse(elem.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var sParsed))
                                {
                                    return ClampRetryAfter(TimeSpan.FromSeconds(sParsed));
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    public async Task<bool> DispatchAsync(string targetUrl, object payload, string customHeadersJson = null, CancellationToken cancellationToken = default)
    {
        var result = await DispatchDetailedAsync(targetUrl, payload, customHeadersJson, cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    public async Task<WebhookDispatchResult> DispatchDetailedAsync(string targetUrl, object payload, string customHeadersJson = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return new WebhookDispatchResult
            {
                Success = false,
                Message = "Target webhook URL is required.",
            };
        }

        if (!IsValidTargetUrl(targetUrl, _allowLoopback))
        {
            _logger.Warn("Webhook dispatch blocked: Invalid or prohibited target URL (SSRF protection): {0}", SanitizeUrlForLogging(targetUrl));
            return new WebhookDispatchResult
            {
                Success = false,
                Message = $"Target URL '{SanitizeUrlForLogging(targetUrl)}' is prohibited (SSRF protection: loopback, link-local, and cloud metadata addresses are not permitted).",
            };
        }

        try
        {
            using var response = await _retryPolicy.ExecuteAsync(
                async (ct) =>
                {
                    var request = BuildHttpRequest(targetUrl, payload, customHeadersJson);
                    return await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _logger.Info("Webhook successfully dispatched to {0} (Status: {1})", SanitizeUrlForLogging(targetUrl), response.StatusCode);
                return new WebhookDispatchResult
                {
                    Success = true,
                    StatusCode = response.StatusCode,
                    Message = $"Webhook dispatched successfully (HTTP {(int)response.StatusCode} {response.StatusCode}).",
                };
            }

            _logger.Warn("Webhook dispatch to {0} returned non-success status code: {1}", SanitizeUrlForLogging(targetUrl), response.StatusCode);
            return new WebhookDispatchResult
            {
                Success = false,
                StatusCode = response.StatusCode,
                Message = $"Webhook endpoint returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? response.StatusCode.ToString()}).",
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "HTTP error while dispatching webhook to {0}", SanitizeUrlForLogging(targetUrl));
            return new WebhookDispatchResult
            {
                Success = false,
                StatusCode = ex.StatusCode,
                Message = $"HTTP request failed: {ex.Message}",
            };
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            _logger.Error(ex, "Webhook dispatch to {0} timed out", SanitizeUrlForLogging(targetUrl));
            return new WebhookDispatchResult
            {
                Success = false,
                Message = "Webhook request timed out.",
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to dispatch webhook to {0}", SanitizeUrlForLogging(targetUrl));
            return new WebhookDispatchResult
            {
                Success = false,
                Message = $"Webhook dispatch failed: {ex.Message}",
            };
        }
    }

    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private HttpRequestMessage BuildHttpRequest(string targetUrl, object payload, string customHeadersJson)
    {
        HttpContent content;

        if (targetUrl.Contains("pushover.net", StringComparison.OrdinalIgnoreCase) &&
            payload is IDictionary<string, object> dict)
        {
            var formPairs = dict.Select(kvp =>
                new KeyValuePair<string, string>(kvp.Key, kvp.Value?.ToString() ?? string.Empty));
            content = new FormUrlEncodedContent(formPairs);
        }
        else if (targetUrl.Contains("pushover.net", StringComparison.OrdinalIgnoreCase) &&
            payload is IDictionary<string, string> stringDict)
        {
            content = new FormUrlEncodedContent(stringDict);
        }
        else
        {
            var json = JsonSerializer.Serialize(payload, DefaultJsonOptions);
            content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, targetUrl)
        {
            Content = content,
        };

        AttachCustomHeaders(request, customHeadersJson);

        return request;
    }

    private void AttachCustomHeaders(HttpRequestMessage request, string customHeadersJson)
    {
        if (string.IsNullOrWhiteSpace(customHeadersJson))
        {
            return;
        }

        var trimmed = customHeadersJson.Trim();

        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var key = prop.Name?.Trim();
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            continue;
                        }

                        var val = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString()?.Trim()
                            : prop.Value.GetRawText().Trim();

                        AddHeader(request, key, val);
                    }

                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to parse custom headers JSON. Attempting fallback key-value parsing.");
            }
        }

        try
        {
            var lines = trimmed.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var addedAny = false;
            foreach (var line in lines)
            {
                var cleanLine = line.Trim();
                if (string.IsNullOrWhiteSpace(cleanLine) || cleanLine.StartsWith("#") || cleanLine.StartsWith("//"))
                {
                    continue;
                }

                var separatorIndex = cleanLine.IndexOfAny(new[] { ':', '=' });
                if (separatorIndex > 0)
                {
                    var key = cleanLine.Substring(0, separatorIndex).Trim();
                    var val = cleanLine.Substring(separatorIndex + 1).Trim();
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        AddHeader(request, key, val);
                        addedAny = true;
                    }
                }
            }

            if (!addedAny && !trimmed.StartsWith("{"))
            {
                _logger.Warn("Could not parse custom headers from input (header keys: {0})", RedactHeadersForLogging(customHeadersJson));
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to parse custom headers (header keys: {0})", RedactHeadersForLogging(customHeadersJson));
        }
    }

    internal static string RedactHeadersForLogging(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        try
        {
            var lines = input.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var keys = new List<string>();
            foreach (var line in lines)
            {
                var clean = line.Trim().Trim('{', '}', '"');
                var sep = clean.IndexOfAny(new[] { ':', '=' });
                if (sep > 0)
                {
                    keys.Add(clean.Substring(0, sep).Trim().Trim('"'));
                }
            }

            return keys.Count > 0 ? string.Join(", ", keys) : "[unparseable headers]";
        }
        catch
        {
            return "[redacted]";
        }
    }

    private void AddHeader(HttpRequestMessage request, string key, string value)
    {
        var added = request.Headers.TryAddWithoutValidation(key, value ?? string.Empty);
        if (!added && request.Content != null)
        {
            added = request.Content.Headers.TryAddWithoutValidation(key, value ?? string.Empty);
        }

        if (added)
        {
            _logger.Debug("Attached custom header '{0}' to webhook request", key);
        }
        else
        {
            _logger.Warn("Failed to add custom header '{0}' to outgoing request", key);
        }
    }
}
