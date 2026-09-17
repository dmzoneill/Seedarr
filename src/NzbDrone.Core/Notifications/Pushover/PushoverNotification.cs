using System;
using System.Collections.Generic;
using System.Net.Http;
using NLog;

namespace NzbDrone.Core.Notifications.Pushover;

public class PushoverNotification : INotificationService
{
    private const string PushoverApiUrl = "https://api.pushover.net/1/messages.json";

    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        AllowAutoRedirect = false,
    });

    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

    public string Name => "Pushover";
    public string ApiToken { get; set; } = "";
    public string UserKey { get; set; } = "";
    public int Priority { get; set; }
    public int RetrySeconds { get; set; } = 60;
    public int ExpireSeconds { get; set; } = 3600;
    public string Device { get; set; }
    public string Sound { get; set; }
    public PushoverSettings Settings { get; set; }

    public PushoverNotification()
    {
        _logger = LogManager.GetCurrentClassLogger();
        _httpClient = SharedHttpClient;
    }

    public PushoverNotification(HttpClient httpClient)
    {
        _logger = LogManager.GetCurrentClassLogger();
        _httpClient = httpClient;
    }

    public PushoverNotification(PushoverSettings settings)
        : this()
    {
        ApplySettings(settings);
    }

    public PushoverNotification(HttpClient httpClient, PushoverSettings settings)
        : this(httpClient)
    {
        ApplySettings(settings);
    }

    public void ApplySettings(PushoverSettings settings)
    {
        if (settings == null)
        {
            return;
        }

        Settings = settings;
        if (!string.IsNullOrEmpty(settings.ApiKey))
        {
            ApiToken = settings.ApiKey;
        }
        else if (!string.IsNullOrEmpty(settings.ApiToken))
        {
            ApiToken = settings.ApiToken;
        }

        if (!string.IsNullOrEmpty(settings.UserKey))
        {
            UserKey = settings.UserKey;
        }

        Priority = settings.Priority;
        RetrySeconds = settings.RetrySeconds;
        ExpireSeconds = settings.ExpireSeconds;
        Device = settings.Device;
        Sound = settings.Sound;
    }

    public void OnTorrentAdded(string torrentName) => SendMessage("Torrent Added", torrentName);
    public void OnSeedingStarted(string torrentName) => SendMessage("Seeding Started", torrentName);
    public void OnSeedingStopped(string torrentName) => SendMessage("Seeding Stopped", torrentName);
    public void OnHealthIssue(string source, string message) => SendMessage($"Health: {source}", message);

    public void SendMessage(string title, string message)
    {
        SendMessage(title, message, null, null, null, null, null);
    }

    public void SendMessage(
        string title,
        string message,
        int? priority = null,
        int? retry = null,
        int? expire = null,
        string device = null,
        string sound = null)
    {
        var token = !string.IsNullOrWhiteSpace(ApiToken) ? ApiToken : Settings?.ApiKey ?? Settings?.ApiToken;
        var user = !string.IsNullOrWhiteSpace(UserKey) ? UserKey : Settings?.UserKey;

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(user))
        {
            _logger.Warn("Pushover API token or user key is not configured");
            return;
        }

        var effectivePriority = Math.Clamp(priority ?? Settings?.Priority ?? Priority, -2, 2);
        var effectiveRetry = retry ?? Settings?.RetrySeconds ?? RetrySeconds;
        var effectiveExpire = expire ?? Settings?.ExpireSeconds ?? ExpireSeconds;
        var effectiveDevice = device ?? Settings?.Device ?? Device;
        var effectiveSound = sound ?? Settings?.Sound ?? Sound;

        var truncatedTitle = NotificationPayloadBuilder.Truncate(title, 250);
        var truncatedMessage = NotificationPayloadBuilder.Truncate(message, 1024);

        try
        {
            var formPairs = new List<KeyValuePair<string, string>>
            {
                new("token", token),
                new("user", user),
                new("title", truncatedTitle),
                new("message", truncatedMessage),
                new("timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
                new("priority", effectivePriority.ToString()),
            };

            if (effectivePriority == 2)
            {
                var clampedRetry = Math.Max(30, effectiveRetry > 0 ? effectiveRetry : 60);
                var clampedExpire = Math.Min(10800, effectiveExpire > 0 ? effectiveExpire : 3600);
                formPairs.Add(new("retry", clampedRetry.ToString()));
                formPairs.Add(new("expire", clampedExpire.ToString()));
            }

            if (!string.IsNullOrWhiteSpace(effectiveDevice))
            {
                formPairs.Add(new("device", effectiveDevice));
            }

            if (!string.IsNullOrWhiteSpace(effectiveSound))
            {
                formPairs.Add(new("sound", effectiveSound));
            }

            using var formData = new FormUrlEncodedContent(formPairs);
            using var request = new HttpRequestMessage(HttpMethod.Post, PushoverApiUrl) { Content = formData };
            using var response = _httpClient.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Pushover notification failed with HTTP {(int)response.StatusCode} {response.StatusCode}", null, response.StatusCode);
            }

            _logger.Debug("Pushover notification sent, status: {0}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send Pushover notification");
        }
    }

    public void SendMessage(IDictionary<string, object> payload)
    {
        if (payload == null)
        {
            return;
        }

        var title = payload.TryGetValue("title", out var t) ? t?.ToString() : "Seedarr Notification";
        var message = payload.TryGetValue("message", out var m) ? m?.ToString() : "";
        int? priority = null;
        if (payload.TryGetValue("priority", out var p) && p != null && int.TryParse(p.ToString(), out var pVal))
        {
            priority = pVal;
        }

        int? retry = null;
        if (payload.TryGetValue("retry", out var r) && r != null && int.TryParse(r.ToString(), out var rVal))
        {
            retry = rVal;
        }

        int? expire = null;
        if (payload.TryGetValue("expire", out var e) && e != null && int.TryParse(e.ToString(), out var eVal))
        {
            expire = eVal;
        }

        var device = payload.TryGetValue("device", out var d) ? d?.ToString() : null;
        var sound = payload.TryGetValue("sound", out var s) ? s?.ToString() : null;

        SendMessage(title, message, priority, retry, expire, device, sound);
    }
}
