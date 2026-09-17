using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NLog;

namespace NzbDrone.Core.Notifications.Telegram;

public class TelegramNotification : INotificationService
{
    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        AllowAutoRedirect = false,
    });

    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

    public string Name => "Telegram";
    public TelegramSettings Settings { get; set; } = new();

    public TelegramNotification()
    {
        _logger = LogManager.GetCurrentClassLogger();
        _httpClient = SharedHttpClient;
    }

    public TelegramNotification(HttpClient httpClient)
    {
        _logger = LogManager.GetCurrentClassLogger();
        _httpClient = httpClient;
    }

    public void OnTorrentAdded(string torrentName) => SendMessage($"[Seedarr] Torrent Added\n{torrentName}");
    public void OnSeedingStarted(string torrentName) => SendMessage($"[Seedarr] Seeding Started\n{torrentName}");
    public void OnSeedingStopped(string torrentName) => SendMessage($"[Seedarr] Seeding Stopped\n{torrentName}");
    public void OnHealthIssue(string source, string message) => SendMessage($"[Seedarr] Health Issue: {source}\n{message}");

    private void SendMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(Settings.BotToken) || string.IsNullOrWhiteSpace(Settings.ChatId))
        {
            _logger.Warn("Telegram bot token or chat ID is not configured");
            return;
        }

        try
        {
            var url = $"https://api.telegram.org/bot{Settings.BotToken}/sendMessage";
            var payload = new
            {
                chat_id = Settings.ChatId,
                text = message,
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            _httpClient.PostAsync(url, content).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send Telegram message to chat {0}", Settings.ChatId);
        }
    }
}
