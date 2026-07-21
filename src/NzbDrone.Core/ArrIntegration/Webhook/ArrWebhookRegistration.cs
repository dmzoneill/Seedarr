using System;
using System.Collections.Generic;
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
    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    });
    private static readonly ResiliencePipeline SharedPolicy = ResiliencePolicies.GetArrApiPolicy();

    private readonly HttpClient _client;
    private readonly ResiliencePipeline _policy;
    private readonly IConfigFileProvider _configFileProvider;
    private readonly Logger _logger;

    public ArrWebhookRegistration(IConfigFileProvider configFileProvider, HttpClient client = null, ResiliencePipeline policy = null)
    {
        _configFileProvider = configFileProvider;
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
            var seedarrUrl = GetSeedarrBaseUrl();
            var webhookUrl = $"{seedarrUrl}/api/v1/webhook/arr";

            var existingId = FindExistingWebhook(connection, apiVersion, webhookUrl);
            if (existingId.HasValue)
            {
                _logger.Debug("Seedarr webhook already registered in {0} (notification id {1})", connection.ArrType, existingId.Value);
                return true;
            }

            var fields = new List<object>
            {
                new { name = "url", value = (object)webhookUrl },
                new { name = "method", value = (object)1 }
            };

            if (!string.IsNullOrEmpty(connection.WebhookSecret))
            {
                fields.Add(new
                {
                    name = "headers",
                    value = (object)new[]
                    {
                        new { key = "X-Seedarr-Secret", value = connection.WebhookSecret }
                    }
                });
            }

            var notificationBody = new
            {
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
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    $"{connection.Url}/api/{apiVersion}/notification");
                request.Headers.Add("X-Api-Key", connection.ApiKey);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = _client.Send(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    _logger.Info("Registered Seedarr webhook in {0} at {1}", connection.ArrType, connection.Url);
                    return true;
                }

                _logger.Warn("Failed to register webhook in {0}: {1}", connection.ArrType, response.StatusCode);
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
            var seedarrUrl = GetSeedarrBaseUrl();
            var webhookUrl = $"{seedarrUrl}/api/v1/webhook/arr";

            var existingId = FindExistingWebhook(connection, apiVersion, webhookUrl);
            if (!existingId.HasValue)
            {
                return true;
            }

            return _policy.Execute(ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete,
                    $"{connection.Url}/api/{apiVersion}/notification/{existingId.Value}");
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

    private int? FindExistingWebhook(ArrConnectionDefinition connection, string apiVersion, string webhookUrl)
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
                    return (int?)null;
                }

                var json = response.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);

                foreach (var notification in doc.RootElement.EnumerateArray())
                {
                    var name = notification.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (name != "Seedarr")
                    {
                        continue;
                    }

                    if (notification.TryGetProperty("fields", out var fields))
                    {
                        foreach (var field in fields.EnumerateArray())
                        {
                            var fieldName = field.TryGetProperty("name", out var fn) ? fn.GetString() : null;
                            var fieldValue = field.TryGetProperty("value", out var fv) ? fv.GetString() : null;
                            if (fieldName == "url" && fieldValue == webhookUrl)
                            {
                                return notification.GetProperty("id").GetInt32();
                            }
                        }
                    }
                }

                return (int?)null;
            });
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to check existing webhooks in {0}", connection.ArrType);
            return null;
        }
    }

    private string GetSeedarrBaseUrl()
    {
        var bindAddress = _configFileProvider.BindAddress;
        var port = _configFileProvider.Port;
        var urlBase = _configFileProvider.UrlBase ?? "";

        if (bindAddress == "*" || bindAddress == "0.0.0.0")
        {
            bindAddress = "localhost";
        }

        var scheme = _configFileProvider.EnableSsl ? "https" : "http";
        return $"{scheme}://{bindAddress}:{port}{urlBase}";
    }
}
