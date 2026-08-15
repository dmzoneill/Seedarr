using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NLog;
using NzbDrone.Core.Http;
using NzbDrone.Core.Validation;
using Polly;

namespace NzbDrone.Core.Notifications.Webhook;

public class WebhookNotification : INotificationService
{
    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    });
    private static readonly ResiliencePipeline Policy = ResiliencePolicies.GetWebhookPolicy();

    private readonly Logger _logger;

    public string Name => "Webhook";
    public string WebhookUrl { get; set; } = "";

    public WebhookNotification()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void OnTorrentAdded(string torrentName)
    {
        SendPayload(new { eventType = "TorrentAdded", torrentName, timestamp = DateTime.UtcNow });
    }

    public void OnSeedingStarted(string torrentName)
    {
        SendPayload(new { eventType = "SeedingStarted", torrentName, timestamp = DateTime.UtcNow });
    }

    public void OnSeedingStopped(string torrentName)
    {
        SendPayload(new { eventType = "SeedingStopped", torrentName, timestamp = DateTime.UtcNow });
    }

    public void OnHealthIssue(string source, string message)
    {
        SendPayload(new { eventType = "HealthIssue", source, message, timestamp = DateTime.UtcNow });
    }

    private void SendPayload(object payload)
    {
        if (string.IsNullOrWhiteSpace(WebhookUrl))
        {
            _logger.Warn("Webhook URL is not configured");
            return;
        }

        if (!UrlValidator.IsSafeUrl(WebhookUrl))
        {
            _logger.Warn("Webhook URL targets private network, blocked: {0}", WebhookUrl);
            return;
        }

        try
        {
            Policy.Execute(ct =>
            {
                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = HttpClient.PostAsync(WebhookUrl, content, ct).GetAwaiter().GetResult();
                _logger.Debug("Webhook sent to {0}, status: {1}", WebhookUrl, response.StatusCode);
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send webhook to {0}", WebhookUrl);
        }
    }
}
