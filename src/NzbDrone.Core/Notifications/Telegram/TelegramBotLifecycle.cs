using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Notifications.Telegram;

public class TelegramWebhookInfo
{
    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("has_custom_certificate")]
    public bool HasCustomCertificate { get; set; }

    [JsonPropertyName("pending_update_count")]
    public int PendingUpdateCount { get; set; }

    [JsonPropertyName("last_error_date")]
    public long? LastErrorDate { get; set; }

    [JsonPropertyName("last_error_message")]
    public string LastErrorMessage { get; set; }
}

public class TelegramApiResponse<T>
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("result")]
    public T Result { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; set; }
}

public interface ITelegramBotLifecycle
{
    Task<bool> SetWebhookAsync(string webhookUrl = null, string secretToken = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteWebhookAsync(bool dropPendingUpdates = false, CancellationToken cancellationToken = default);
    Task<TelegramWebhookInfo> GetWebhookInfoAsync(CancellationToken cancellationToken = default);
}

public class TelegramBotLifecycle : ITelegramBotLifecycle
{
    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        AllowAutoRedirect = false,
    });

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IConfigService _configService;
    private readonly TelegramSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

    public TelegramBotLifecycle(
        IConfigService configService,
        HttpClient httpClient = null,
        TelegramSettings settings = null)
    {
        _configService = configService;
        _httpClient = httpClient ?? SharedHttpClient;
        _settings = settings ?? new TelegramSettings();
        _logger = LogManager.GetCurrentClassLogger();
    }

    private string GetBotToken()
    {
        return !string.IsNullOrWhiteSpace(_settings?.BotToken) ? _settings.BotToken : _configService?.TelegramBotToken;
    }

    public async Task<bool> SetWebhookAsync(string webhookUrl = null, string secretToken = null, CancellationToken cancellationToken = default)
    {
        var botToken = GetBotToken();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.Warn("Cannot set Telegram webhook: Bot token is not configured");
            return false;
        }

        var url = webhookUrl ?? _configService?.TelegramWebhookUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.Warn("Cannot set Telegram webhook: Webhook URL is not configured");
            return false;
        }

        var token = secretToken ?? _configService?.TelegramSecretToken;
        var apiUrl = $"https://api.telegram.org/bot{botToken}/setWebhook";

        var payload = new Dictionary<string, object>
        {
            ["url"] = url
        };

        if (!string.IsNullOrWhiteSpace(token))
        {
            payload["secret_token"] = token;
        }

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.PostAsync(apiUrl, content, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var apiResult = JsonSerializer.Deserialize<TelegramApiResponse<bool>>(responseBody, JsonOptions);

            if (apiResult != null && apiResult.Ok)
            {
                _logger.Info("Successfully registered Telegram webhook at {0}", url);
                return true;
            }

            _logger.Warn("Telegram setWebhook returned not ok: {0}", apiResult?.Description ?? responseBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to register Telegram webhook at {0}", url);
            return false;
        }
    }

    public async Task<bool> DeleteWebhookAsync(bool dropPendingUpdates = false, CancellationToken cancellationToken = default)
    {
        var botToken = GetBotToken();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.Warn("Cannot delete Telegram webhook: Bot token is not configured");
            return false;
        }

        var apiUrl = $"https://api.telegram.org/bot{botToken}/deleteWebhook";
        var payload = new Dictionary<string, object>
        {
            ["drop_pending_updates"] = dropPendingUpdates
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.PostAsync(apiUrl, content, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var apiResult = JsonSerializer.Deserialize<TelegramApiResponse<bool>>(responseBody, JsonOptions);

            if (apiResult != null && apiResult.Ok)
            {
                _logger.Info("Successfully deleted Telegram webhook");
                return true;
            }

            _logger.Warn("Telegram deleteWebhook returned not ok: {0}", apiResult?.Description ?? responseBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete Telegram webhook");
            return false;
        }
    }

    public async Task<TelegramWebhookInfo> GetWebhookInfoAsync(CancellationToken cancellationToken = default)
    {
        var botToken = GetBotToken();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return null;
        }

        var apiUrl = $"https://api.telegram.org/bot{botToken}/getWebhookInfo";

        try
        {
            using var response = await _httpClient.GetAsync(apiUrl, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var apiResult = JsonSerializer.Deserialize<TelegramApiResponse<TelegramWebhookInfo>>(responseBody, JsonOptions);

            return apiResult?.Result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get Telegram webhook info");
            return null;
        }
    }
}
