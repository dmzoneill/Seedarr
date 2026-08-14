using System;
using System.Collections.Generic;
using System.Net.Http;
using NLog;

namespace NzbDrone.Core.Notifications.Pushover;

public class PushoverNotification : INotificationService
{
    private const string PushoverApiUrl = "https://api.pushover.net/1/messages.json";

    private static readonly HttpClient HttpClient = new();

    private readonly Logger _logger;

    public string Name => "Pushover";
    public string ApiToken { get; set; } = "";
    public string UserKey { get; set; } = "";

    public PushoverNotification()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void OnTorrentAdded(string torrentName) => SendMessage("Torrent Added", torrentName);
    public void OnSeedingStarted(string torrentName) => SendMessage("Seeding Started", torrentName);
    public void OnSeedingStopped(string torrentName) => SendMessage("Seeding Stopped", torrentName);
    public void OnHealthIssue(string source, string message) => SendMessage($"Health: {source}", message);

    private void SendMessage(string title, string message)
    {
        if (string.IsNullOrWhiteSpace(ApiToken) || string.IsNullOrWhiteSpace(UserKey))
        {
            _logger.Warn("Pushover API token or user key is not configured");
            return;
        }

        try
        {
            var formData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("token", ApiToken),
                new KeyValuePair<string, string>("user", UserKey),
                new KeyValuePair<string, string>("title", title),
                new KeyValuePair<string, string>("message", message),
                new KeyValuePair<string, string>("timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
            });

            var response = HttpClient.PostAsync(PushoverApiUrl, formData).GetAwaiter().GetResult();
            _logger.Debug("Pushover notification sent, status: {0}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send Pushover notification");
        }
    }
}
