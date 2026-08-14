using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NLog;

namespace NzbDrone.Core.Notifications.Discord;

public class DiscordNotification : INotificationService
{
    private static readonly HttpClient HttpClient = new();

    private readonly Logger _logger;

    public string Name => "Discord";
    public string WebhookUrl { get; set; } = "";

    public DiscordNotification()
    {
        _logger = LogManager.GetCurrentClassLogger();
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

        try
        {
            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title,
                        description,
                        color,
                        footer = new { text = "Seedarr" },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = HttpClient.PostAsync(WebhookUrl, content).GetAwaiter().GetResult();
            _logger.Debug("Discord notification sent, status: {0}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send Discord notification");
        }
    }
}
