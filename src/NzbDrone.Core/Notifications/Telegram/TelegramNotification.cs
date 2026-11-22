using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Notifications.Telegram;

public class TelegramNotification : INotificationService
{
    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        AllowAutoRedirect = false,
    })
    {
        Timeout = NotificationHttp.Timeout
    };

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

    public void OnTorrentAdded(string torrentName) => SendMessageAsync($"[Seedarr] Torrent Added\n{torrentName}").GetAwaiter().GetResult();
    public void OnSeedingStarted(string torrentName) => SendMessageAsync($"[Seedarr] Seeding Started\n{torrentName}").GetAwaiter().GetResult();
    public void OnSeedingStopped(string torrentName) => SendMessageAsync($"[Seedarr] Seeding Stopped\n{torrentName}").GetAwaiter().GetResult();
    public void OnHealthIssue(string source, string message) => SendMessageAsync($"[Seedarr] Health Issue: {source}\n{message}").GetAwaiter().GetResult();

    private async Task SendMessageAsync(string message)
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
            using var response = await _httpClient.PostAsync(url, content, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send Telegram message to chat {0}", Settings.ChatId);
        }
    }
}
