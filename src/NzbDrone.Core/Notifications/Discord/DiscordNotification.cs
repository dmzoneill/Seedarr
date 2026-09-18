using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NLog;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.Discord;

public class DiscordNotification : INotificationService
{
    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        AllowAutoRedirect = false,
    });

    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

    public string Name => "Discord";
    public string WebhookUrl { get; set; } = "";

    public DiscordNotification()
    {
        _logger = LogManager.GetCurrentClassLogger();
        _httpClient = SharedHttpClient;
    }

    public DiscordNotification(HttpClient httpClient)
    {
        _logger = LogManager.GetCurrentClassLogger();
        _httpClient = httpClient;
    }

    public void OnTorrentAdded(string torrentName) => SendEmbed("Torrent Added", torrentName, 0x35C5F4);
    public void OnSeedingStarted(string torrentName) => SendEmbed("Seeding Started", torrentName, 0x4CAF50);
    public void OnSeedingStopped(string torrentName) => SendEmbed("Seeding Stopped", torrentName, 0xFF9800);
    public void OnHealthIssue(string source, string message) => SendEmbed($"Health: {source}", message, 0xF44336);

    private void SendEmbed(string title, string description, int color)
    {
        if (string.IsNullOrWhiteSpace(WebhookUrl))
        {
            _logger.Warn("Discord webhook URL is not configured");
            return;
        }

        if (!UrlValidator.IsSafeUrl(WebhookUrl))
        {
            _logger.Warn("Webhook URL targets private network, blocked: {0}", WebhookUrl);
            return;
        }

        try
        {
            var truncatedTitle = title != null ? NotificationPayloadBuilder.Truncate(title, 256) : null;
            var maxDescLength = Math.Min(4096, 6000 - (truncatedTitle?.Length ?? 0) - "Seedarr".Length);
            if (maxDescLength < 0)
            {
                maxDescLength = 0;
            }

            var truncatedDescription = description != null ? NotificationPayloadBuilder.Truncate(description, maxDescLength) : null;

            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = truncatedTitle,
                        description = truncatedDescription,
                        color,
                        footer = new { text = "Seedarr" },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, WebhookUrl) { Content = content };
            using var response = _httpClient.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Discord notification failed with HTTP {(int)response.StatusCode} {response.StatusCode}", null, response.StatusCode);
            }

            _logger.Debug("Discord notification sent, status: {0}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send Discord notification");
        }
    }
}
