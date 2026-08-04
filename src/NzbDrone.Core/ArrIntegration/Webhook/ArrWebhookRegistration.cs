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
        if (!connection.WebhookEnabled)
        {
            return true;
        }

        try
        {
            var apiVersion = connection.ArrType == "Lidarr" ? "v1" : "v3";
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
                onDownload = false,
                onUpgrade = false,
                onRename = false,
                onHealthIssue = false,
                includeHealthWarnings = false,
                fields
            };

            var json = JsonSerializer.Serialize(notificationBody);

            return _policy.Execute(ct =>
            {
                var url = isUpdate
                    ? $"{connection.Url}/api/{apiVersion}/notification/{existing.Id}"
                    : $"{connection.Url}/api/{apiVersion}/notification";
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
        try
        {
            var apiVersion = connection.ArrType == "Lidarr" ? "v1" : "v3";
            var existing = FindExistingWebhook(connection, apiVersion);
            if (existing == null)
            {
                return true;
            }

            return _policy.Execute(ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete,
                    $"{connection.Url}/api/{apiVersion}/notification/{existing.Id}");
                request.Headers.Add("X-Api-Key", connection.ApiKey);

                using var response = _client.Send(request, ct);
                if (response.IsSuccessStatusCode)
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
            _logger.Error(ex, "Failed to unregister webhook from {0}", connection.ArrType);
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
                    $"{connection.Url}/api/{apiVersion}/notification");
                request.Headers.Add("X-Api-Key", connection.ApiKey);

                using var response = _client.Send(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    return (ExistingWebhookInfo)null;
                }

                var json = response.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);

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
                                    (url != null && url.Contains("/api/v1/webhook/arr", StringComparison.OrdinalIgnoreCase));

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
        var envUrl = Environment.GetEnvironmentVariable("SEEDARR_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            return envUrl.TrimEnd('/');
        }

        var connectionHost = connection?.WebhookHost;
        if (!string.IsNullOrWhiteSpace(connectionHost))
        {
            if (connectionHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                connectionHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return connectionHost.TrimEnd('/');
            }

            var scheme = _configFileProvider.EnableSsl ? "https" : "http";
            return $"{scheme}://{connectionHost}:{_configFileProvider.Port}{_configFileProvider.UrlBase ?? ""}";
        }

        var envHost = Environment.GetEnvironmentVariable("SEEDARR_HOST");
        if (!string.IsNullOrWhiteSpace(envHost))
        {
            var scheme = _configFileProvider.EnableSsl ? "https" : "http";
            return $"{scheme}://{envHost}:{_configFileProvider.Port}{_configFileProvider.UrlBase ?? ""}";
        }

        var bindAddress = _configFileProvider.BindAddress;
        var port = _configFileProvider.Port;
        var urlBase = _configFileProvider.UrlBase ?? "";

        if (string.IsNullOrWhiteSpace(bindAddress) ||
            bindAddress == "*" ||
            bindAddress == "0.0.0.0" ||
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

        var schemeDefault = _configFileProvider.EnableSsl ? "https" : "http";
        return $"{schemeDefault}://{bindAddress}:{port}{urlBase}";
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
