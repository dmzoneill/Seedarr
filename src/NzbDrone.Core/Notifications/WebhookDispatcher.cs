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
    public string ResponseBodySnippet { get; set; }
}

public interface IWebhookDispatcher
{
    Task<bool> DispatchAsync(string targetUrl, object payload, string customHeadersJson = null, CancellationToken cancellationToken = default);
    Task<bool> DispatchAsync(string targetUrl, object payload, string customHeadersJson, HttpMethod httpMethod, CancellationToken cancellationToken = default);
    Task<WebhookDispatchResult> DispatchDetailedAsync(string targetUrl, object payload, string customHeadersJson = null, CancellationToken cancellationToken = default);
    Task<WebhookDispatchResult> DispatchDetailedAsync(string targetUrl, object payload, string customHeadersJson, HttpMethod httpMethod, CancellationToken cancellationToken = default);
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

    private static readonly Regex TelegramBotPathRegex = new(
        @"(api\.telegram\.org/bot)[^/?#\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TelegramBotTokenRegex = new(
        @"\bbot\d+:[A-Za-z0-9_-]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DiscordWebhookRegex = new(
        @"/api/webhooks/(?<id>\d+)/[^/?#\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SlackWebhookRegex = new(
        @"/services/T[A-Za-z0-9]+/B[A-Za-z0-9]+/[A-Za-z0-9]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex QueryParamTokenRegex = new(
        @"((?:[?&]|^)(?:api[_-]?key|bot[_-]?token|token|passkey|secret|password|auth|access[_-]?token)=)[^&#\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BasicAuthRegex = new(
        @"(https?://[^:/@\s]+:)([^@/\s]+)(@)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string SanitizeUrlForLogging(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var sanitized = TelegramBotPathRegex.Replace(url, "$1[REDACTED]");
        sanitized = TelegramBotTokenRegex.Replace(sanitized, "bot[REDACTED]");
        sanitized = DiscordWebhookRegex.Replace(sanitized, "/api/webhooks/${id}/[REDACTED]");
        sanitized = SlackWebhookRegex.Replace(sanitized, "/services/[REDACTED]");
        sanitized = QueryParamTokenRegex.Replace(sanitized, "$1[REDACTED]");
        sanitized = BasicAuthRegex.Replace(sanitized, "$1[REDACTED]$3");

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

            if (response.Headers.TryGetValues("X-RateLimit-Reset-After", out var resetValues))
            {
                var resetStr = resetValues.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(resetStr) &&
                    double.TryParse(resetStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var resetSeconds) &&
                    resetSeconds > 0)
                {
                    return ClampRetryAfter(TimeSpan.FromSeconds(resetSeconds));
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
        return await DispatchAsync(targetUrl, payload, customHeadersJson, HttpMethod.Post, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DispatchAsync(string targetUrl, object payload, string customHeadersJson, HttpMethod httpMethod, CancellationToken cancellationToken = default)
    {
        var result = await DispatchDetailedAsync(targetUrl, payload, customHeadersJson, httpMethod ?? HttpMethod.Post, cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    public async Task<WebhookDispatchResult> DispatchDetailedAsync(string targetUrl, object payload, string customHeadersJson = null, CancellationToken cancellationToken = default)
    {
        return await DispatchDetailedAsync(targetUrl, payload, customHeadersJson, HttpMethod.Post, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WebhookDispatchResult> DispatchDetailedAsync(string targetUrl, object payload, string customHeadersJson, HttpMethod httpMethod, CancellationToken cancellationToken = default)
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
                    var request = BuildHttpRequest(targetUrl, payload, customHeadersJson, httpMethod);
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

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var responseBody = response.Content != null
                    ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
                    : string.Empty;

                if (responseBody.Contains("can't parse entities", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Warn("Telegram entity parse failure for {0}: {1}. Retrying once with plain text without parse_mode.", SanitizeUrlForLogging(targetUrl), responseBody);

                    var fallbackPayload = RemoveParseMode(payload);
                    using var fallbackResponse = await _retryPolicy.ExecuteAsync(
                        async (ct) =>
                        {
                            var fallbackRequest = BuildHttpRequest(targetUrl, fallbackPayload, customHeadersJson, httpMethod);
                            return await _httpClient.SendAsync(fallbackRequest, ct).ConfigureAwait(false);
                        },
                        cancellationToken).ConfigureAwait(false);

                    if (fallbackResponse.IsSuccessStatusCode)
                    {
                        _logger.Info("Webhook successfully dispatched to {0} on plain text retry (Status: {1})", SanitizeUrlForLogging(targetUrl), fallbackResponse.StatusCode);
                        return new WebhookDispatchResult
                        {
                            Success = true,
                            StatusCode = fallbackResponse.StatusCode,
                            Message = $"Webhook dispatched successfully (HTTP {(int)fallbackResponse.StatusCode} {fallbackResponse.StatusCode}).",
                        };
                    }

                    _logger.Warn("Webhook plain text retry to {0} returned non-success status code: {1}", SanitizeUrlForLogging(targetUrl), fallbackResponse.StatusCode);
                    var fallbackSnippet = await ExtractBodySnippetAsync(fallbackResponse, cancellationToken).ConfigureAwait(false);
                    var fallbackReason = fallbackResponse.ReasonPhrase ?? fallbackResponse.StatusCode.ToString();
                    return new WebhookDispatchResult
                    {
                        Success = false,
                        StatusCode = fallbackResponse.StatusCode,
                        ResponseBodySnippet = fallbackSnippet,
                        Message = string.IsNullOrEmpty(fallbackSnippet)
                            ? $"Webhook endpoint returned HTTP {(int)fallbackResponse.StatusCode} ({fallbackReason})."
                            : $"Webhook endpoint returned HTTP {(int)fallbackResponse.StatusCode} ({fallbackReason}): {fallbackSnippet}",
                    };
                }
            }

            var bodySnippet = await ExtractBodySnippetAsync(response, cancellationToken).ConfigureAwait(false);
            var reason = response.ReasonPhrase ?? response.StatusCode.ToString();
            if ((int)response.StatusCode >= 300 && (int)response.StatusCode <= 399 && response.Headers.Location != null)
            {
                _logger.Warn("Webhook dispatch to {0} returned redirect status code {1} to {2}", SanitizeUrlForLogging(targetUrl), response.StatusCode, SanitizeUrlForLogging(response.Headers.Location.ToString()));
            }
            else
            {
                _logger.Warn("Webhook dispatch to {0} returned non-success status code: {1}", SanitizeUrlForLogging(targetUrl), response.StatusCode);
            }
            return new WebhookDispatchResult
            {
                Success = false,
                StatusCode = response.StatusCode,
                ResponseBodySnippet = bodySnippet,
                Message = string.IsNullOrEmpty(bodySnippet)
                    ? $"Webhook endpoint returned HTTP {(int)response.StatusCode} ({reason})."
                    : $"Webhook endpoint returned HTTP {(int)response.StatusCode} ({reason}): {bodySnippet}",
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

    internal static object RemoveParseMode(object payload)
    {
        if (payload == null)
        {
            return null;
        }

        if (payload is IDictionary<string, object> dict)
        {
            var newDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in dict)
            {
                if (!string.Equals(kvp.Key, "parse_mode", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(kvp.Key, "parseMode", StringComparison.OrdinalIgnoreCase))
                {
                    newDict[kvp.Key] = kvp.Value;
                }
            }

            return newDict;
        }

        if (payload is IDictionary<string, string> stringDict)
        {
            var newDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in stringDict)
            {
                if (!string.Equals(kvp.Key, "parse_mode", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(kvp.Key, "parseMode", StringComparison.OrdinalIgnoreCase))
                {
                    newDict[kvp.Key] = kvp.Value;
                }
            }

            return newDict;
        }

        if (payload is string jsonStr && jsonStr.TrimStart().StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonStr);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var dictFromJson = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (!prop.NameEquals("parse_mode") && !prop.NameEquals("parseMode"))
                        {
                            dictFromJson[prop.Name] = prop.Value.Clone();
                        }
                    }

                    return dictFromJson;
                }
            }
            catch
            {
            }
        }

        try
        {
            var json = JsonSerializer.Serialize(payload, DefaultJsonOptions);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var dictFromJson = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!prop.NameEquals("parse_mode") && !prop.NameEquals("parseMode"))
                    {
                        dictFromJson[prop.Name] = prop.Value.Clone();
                    }
                }

                return dictFromJson;
            }
        }
        catch
        {
        }

        return payload;
    }

    private HttpRequestMessage BuildHttpRequest(string targetUrl, object payload, string customHeadersJson, HttpMethod httpMethod = null)
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
        else if (payload is string strPayload)
        {
            var trimmed = strPayload.TrimStart();
            var mediaType = (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                ? "application/json"
                : "text/plain";
            content = new StringContent(strPayload, Encoding.UTF8, mediaType);
        }
        else
        {
            var json = JsonSerializer.Serialize(payload, DefaultJsonOptions);
            content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var method = httpMethod ?? HttpMethod.Post;
        var request = new HttpRequestMessage(method, targetUrl)
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

    private static async Task<string> ExtractBodySnippetAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        if (response?.Content == null)
        {
            return string.Empty;
        }

        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var bodySnippet = raw.Trim().Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
                if (bodySnippet.Length > 256)
                {
                    bodySnippet = string.Concat(bodySnippet.AsSpan(0, 256), "...");
                }

                return bodySnippet;
            }
        }
        catch
        {
        }

        return string.Empty;
    }
}
