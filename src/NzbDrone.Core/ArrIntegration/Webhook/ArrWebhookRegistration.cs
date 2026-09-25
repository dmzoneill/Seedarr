using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using Polly;

namespace NzbDrone.Core.ArrIntegration.Webhook;

public interface IArrWebhookRegistration
{
    bool RegisterWebhook(ArrConnectionDefinition connection);
    bool UnregisterWebhook(ArrConnectionDefinition connection);
}

public class ArrWebhookRegistration : IArrWebhookRegistration
{
    private record ExistingWebhookInfo(int Id, string Url, string ApiKey);

    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    });
    private static readonly ResiliencePipeline SharedPolicy = ResiliencePolicies.GetArrApiPolicy();

    private readonly HttpClient _client;
    private readonly ResiliencePipeline _policy;
    private readonly IConfigFileProvider _configFileProvider;
    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public ArrWebhookRegistration(IConfigFileProvider configFileProvider, IConfigService configService, HttpClient client = null, ResiliencePipeline policy = null)
    {
        _configFileProvider = configFileProvider;
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
        _client = client ?? SharedClient;
        _policy = policy ?? SharedPolicy;
    }

    public bool RegisterWebhook(ArrConnectionDefinition connection)
    {
        if (connection == null || !connection.Enable || !connection.WebhookEnabled)
        {
            return true;
        }

        if (string.Equals(connection.ArrType, "Prowlarr", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var apiVersion = string.Equals(connection.ArrType, "Lidarr", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(connection.ArrType, "Readarr", StringComparison.OrdinalIgnoreCase) ? "v1" : "v3";
            var seedarrUrl = GetSeedarrBaseUrl(connection);
            var webhookUrl = $"{seedarrUrl}/api/v1/webhook/arr";
            var currentApiKey = _configFileProvider.ApiKey ?? string.Empty;

            var existing = FindExistingWebhook(connection, apiVersion);
            var existingKey = existing?.ApiKey ?? string.Empty;
            if (existing != null &&
                string.Equals(existing.Url, webhookUrl, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(currentApiKey) || string.Equals(existingKey, currentApiKey, StringComparison.Ordinal)))
            {
                _logger.Debug("Seedarr webhook already registered in {0} (notification id {1})", connection.ArrType, existing.Id);
                return true;
            }

            var fields = new List<object>
            {
                new { name = "url", value = (object)webhookUrl },
                new { name = "method", value = (object)1 },
                new
                {
                    name = "headers",
                    value = (object)new[]
                    {
                        new { key = "X-Api-Key", value = currentApiKey }
                    }
                }
            };

            // NOTE: Only one "Seedarr" notification is supported per arr app.
            // FindExistingWebhook matches by this name + URL, so a second connection to the
            // same arr app won't register a separate webhook — the first one is reused/updated.
            var isUpdate = existing != null && existing.Id > 0;
            var notificationBody = new
            {
                id = isUpdate ? existing.Id : 0,
                name = "Seedarr",
                implementation = "Webhook",
                configContract = "WebhookSettings",
                onGrab = true,
                onDownload = true,
                onUpgrade = false,
                onRename = true,
                onHealthIssue = false,
                includeHealthWarnings = false,
                fields
            };

            var json = JsonSerializer.Serialize(notificationBody);

            return _policy.Execute(ct =>
            {
                var url = isUpdate
                    ? $"{connection.Url?.TrimEnd('/')}/api/{apiVersion}/notification/{existing.Id}"
                    : $"{connection.Url?.TrimEnd('/')}/api/{apiVersion}/notification";
                var method = isUpdate ? HttpMethod.Put : HttpMethod.Post;

                using var request = new HttpRequestMessage(method, url);
                request.Headers.Add("X-Api-Key", connection.ApiKey);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = _client.Send(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    _logger.Info(
                        "{0} Seedarr webhook in {1} at {2} (target: {3})",
                        isUpdate ? "Updated" : "Registered",
                        connection.ArrType,
                        connection.Url,
                        webhookUrl);
                    return true;
                }

                _logger.Warn(
                    "Failed to {0} webhook in {1}: {2}",
                    isUpdate ? "update" : "register",
                    connection.ArrType,
                    response.StatusCode);
                return false;
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to register webhook in {0}", connection.ArrType);
            return false;
        }
    }

    public bool UnregisterWebhook(ArrConnectionDefinition connection)
    {
        if (connection == null || string.IsNullOrWhiteSpace(connection.Url) || string.Equals(connection.ArrType, "Prowlarr", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var apiVersion = string.Equals(connection.ArrType, "Lidarr", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(connection.ArrType, "Readarr", StringComparison.OrdinalIgnoreCase) ? "v1" : "v3";
            var existing = FindExistingWebhook(connection, apiVersion);
            if (existing == null)
            {
                return true;
            }

            return _policy.Execute(ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete,
                    $"{connection.Url?.TrimEnd('/')}/api/{apiVersion}/notification/{existing.Id}");
                request.Headers.Add("X-Api-Key", connection.ApiKey);

                using var response = _client.Send(request, ct);
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.Info("Unregistered Seedarr webhook from {0}", connection.ArrType);
                    return true;
                }

                _logger.Warn("Failed to unregister webhook from {0}: {1}", connection.ArrType, response.StatusCode);
                return false;
            });
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to unregister webhook from {0}", connection.ArrType);
            return false;
        }
    }

    private ExistingWebhookInfo FindExistingWebhook(ArrConnectionDefinition connection, string apiVersion)
    {
        try
        {
            return _policy.Execute(ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{connection.Url?.TrimEnd('/')}/api/{apiVersion}/notification");
                request.Headers.Add("X-Api-Key", connection.ApiKey);

                using var response = _client.Send(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    return (ExistingWebhookInfo)null;
                }

                using var stream = response.Content.ReadAsStream(ct);
                using var doc = JsonDocument.Parse(stream);

                foreach (var notification in doc.RootElement.EnumerateArray())
                {
                    var name = notification.TryGetProperty("name", out var n) ? n.GetString() : null;

                    string url = null;
                    string apiKey = null;

                    if (notification.TryGetProperty("fields", out var fields))
                    {
                        foreach (var field in fields.EnumerateArray())
                        {
                            var fieldName = field.TryGetProperty("name", out var fn) ? fn.GetString() : null;
                            if (fieldName == "url")
                            {
                                url = field.TryGetProperty("value", out var fv) ? fv.GetString() : null;
                            }
                            else if (fieldName == "headers" && field.TryGetProperty("value", out var headers))
                            {
                                foreach (var h in headers.EnumerateArray())
                                {
                                    var hKey = h.TryGetProperty("key", out var hk) ? hk.GetString() : null;
                                    if (string.Equals(hKey, "X-Api-Key", StringComparison.OrdinalIgnoreCase))
                                    {
                                        apiKey = h.TryGetProperty("value", out var hv) ? hv.GetString() : null;
                                    }
                                }
                            }
                        }
                    }

                    var isSeedarr = string.Equals(name, "Seedarr", StringComparison.OrdinalIgnoreCase) ||
                                    (url != null && url.Contains("seedarr", StringComparison.OrdinalIgnoreCase) &&
                                     (url.Contains("/api/v1/webhook/arr", StringComparison.OrdinalIgnoreCase) ||
                                      url.Contains("/api/v1/webhooks/arr", StringComparison.OrdinalIgnoreCase)));

                    if (isSeedarr && notification.TryGetProperty("id", out var idProp))
                    {
                        return new ExistingWebhookInfo(idProp.GetInt32(), url, apiKey);
                    }
                }

                return (ExistingWebhookInfo)null;
            });
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to check existing webhooks in {0}", connection.ArrType);
            return null;
        }
    }

    private string GetSeedarrBaseUrl(ArrConnectionDefinition connection)
    {
        var urlBase = NormalizeUrlBase(_configFileProvider.UrlBase);

        var envUrl = Environment.GetEnvironmentVariable("SEEDARR_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            return AppendUrlBase(envUrl, urlBase);
        }

        var scheme = _configFileProvider.EnableSsl ? "https" : "http";
        var port = _configFileProvider.EnableSsl ? _configFileProvider.SslPort : _configFileProvider.Port;

        var hostCandidate = connection?.WebhookHost;
        if (string.IsNullOrWhiteSpace(hostCandidate))
        {
            hostCandidate = _configService?.WebhookBaseUrl;
        }

        if (!string.IsNullOrWhiteSpace(hostCandidate))
        {
            if (hostCandidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                hostCandidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return AppendUrlBase(hostCandidate, urlBase);
            }

            return AppendUrlBase($"{scheme}://{FormatHostWithPort(hostCandidate, port)}", urlBase);
        }

        var envHost = Environment.GetEnvironmentVariable("SEEDARR_HOST");
        if (!string.IsNullOrWhiteSpace(envHost))
        {
            if (envHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                envHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return AppendUrlBase(envHost, urlBase);
            }

            return AppendUrlBase($"{scheme}://{FormatHostWithPort(envHost, port)}", urlBase);
        }

        var bindAddress = _configFileProvider.BindAddress;

        if (string.IsNullOrWhiteSpace(bindAddress) ||
            bindAddress == "*" ||
            bindAddress == "0.0.0.0" ||
            bindAddress == "::" ||
            IsHexContainerId(bindAddress))
        {
            if (IsLoopbackOrLocalhost(connection?.Url))
            {
                bindAddress = "127.0.0.1";
            }
            else
            {
                var hostname = Dns.GetHostName();
                bindAddress = IsHexContainerId(hostname) ? "seedarr" : hostname;
            }
        }

        return AppendUrlBase($"{scheme}://{FormatHostWithPort(bindAddress, port)}", urlBase);
    }

    private static string AppendUrlBase(string baseUrl, string urlBase)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }

        var trimmedBase = baseUrl.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(urlBase))
        {
            return trimmedBase;
        }

        if (trimmedBase.EndsWith(urlBase, StringComparison.OrdinalIgnoreCase))
        {
            return trimmedBase;
        }

        return $"{trimmedBase}{urlBase}";
    }

    private static string FormatHostWithPort(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        var trimmedHost = host.Trim().TrimEnd('/');

        bool hasPort;
        if (trimmedHost.StartsWith('[') && trimmedHost.Contains(']'))
        {
            hasPort = trimmedHost.Substring(trimmedHost.IndexOf(']') + 1).Contains(':');
        }
        else
        {
            hasPort = trimmedHost.Contains(':');
        }

        if (hasPort)
        {
            return trimmedHost;
        }

        if (port == 80 || port == 443)
        {
            return trimmedHost;
        }

        var hostWithoutBrackets = trimmedHost.Trim('[', ']');
        if (trimmedHost.Contains('.') && !IPAddress.TryParse(hostWithoutBrackets, out _))
        {
            return trimmedHost;
        }

        return $"{trimmedHost}:{port}";
    }

    private static string NormalizeUrlBase(string urlBase)
    {
        if (string.IsNullOrWhiteSpace(urlBase))
        {
            return string.Empty;
        }

        var trimmed = urlBase.Trim().Trim('/');
        return string.IsNullOrEmpty(trimmed) ? string.Empty : "/" + trimmed;
    }

    private static bool IsLoopbackOrLocalhost(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return url.Contains("://localhost", StringComparison.OrdinalIgnoreCase)
            || url.Contains("://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || url.Contains("://[::1]", StringComparison.OrdinalIgnoreCase)
            || url.Contains("://0.0.0.0", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHexContainerId(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        if (s.Length != 12 && s.Length != 64)
        {
            return false;
        }

        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }

        return true;
    }
}
