using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications.Telegram;

public class TelegramUpdate
{
    [JsonPropertyName("update_id")]
    public long UpdateId { get; set; }

    [JsonPropertyName("message")]
    public TelegramMessage Message { get; set; }

    [JsonPropertyName("callback_query")]
    public TelegramCallbackQuery CallbackQuery { get; set; }
}

public class TelegramMessage
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }

    [JsonPropertyName("from")]
    public TelegramUser From { get; set; }

    [JsonPropertyName("chat")]
    public TelegramChat Chat { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; }

    [JsonPropertyName("date")]
    public long Date { get; set; }
}

public class TelegramCallbackQuery
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("from")]
    public TelegramUser From { get; set; }

    [JsonPropertyName("message")]
    public TelegramMessage Message { get; set; }

    [JsonPropertyName("data")]
    public string Data { get; set; }
}

public class TelegramChat
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string LastName { get; set; }
}

public class TelegramUser
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("is_bot")]
    public bool IsBot { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string LastName { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; }
}

public class TelegramResponse
{
    public bool Success { get; set; }
    public bool Handled { get; set; }
    public bool Authorized { get; set; }
    public string Command { get; set; }
    public string ResponseText { get; set; }
    public string CallbackQueryAnswer { get; set; }
}

public interface ITelegramUpdateHandler
{
    Task<TelegramResponse> HandleUpdateAsync(TelegramUpdate update, CancellationToken cancellationToken = default);
    Task<TelegramResponse> HandleUpdateAsync(string updateJson, CancellationToken cancellationToken = default);
    bool IsAuthorized(long chatId, long? userId = null);
}

public class TelegramUpdateHandler : ITelegramUpdateHandler
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

    private readonly ITorrentService _torrentService;
    private readonly IConfigService _configService;
    private readonly IDiskSpaceService _diskSpaceService;
    private readonly TelegramSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

    public TelegramUpdateHandler(
        ITorrentService torrentService,
        IConfigService configService,
        TelegramSettings settings = null,
        HttpClient httpClient = null,
        IDiskSpaceService diskSpaceService = null)
    {
        _torrentService = torrentService;
        _configService = configService;
        _settings = settings ?? new TelegramSettings();
        _httpClient = httpClient ?? SharedHttpClient;
        _diskSpaceService = diskSpaceService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public static string FormatTelegramHtml(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return WebUtility.HtmlEncode(input);
    }

    public static string FormatSpeed(long bytesPerSec)
    {
        return $"{FormatBytes(bytesPerSec)}/s";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            return "0 B";
        }

        return bytes switch
        {
            >= 1024L * 1024L * 1024L * 1024L => $"{(double)bytes / (1024L * 1024L * 1024L * 1024L):F2} TB",
            >= 1024L * 1024L * 1024L => $"{(double)bytes / (1024L * 1024L * 1024L):F2} GB",
            >= 1024L * 1024L => $"{(double)bytes / (1024L * 1024L):F2} MB",
            >= 1024L => $"{(double)bytes / 1024L:F2} KB",
            _ => $"{bytes} B"
        };
    }

    public bool IsAuthorized(long chatId, long? userId = null)
    {
        if (_settings.AllowedChatIds != null && _settings.AllowedChatIds.Count > 0)
        {
            return _settings.AllowedChatIds.Contains(chatId) || (userId.HasValue && _settings.AllowedChatIds.Contains(userId.Value));
        }

        if (!string.IsNullOrWhiteSpace(_settings.ChatId) && long.TryParse(_settings.ChatId, out var configuredId))
        {
            return configuredId == chatId || (userId.HasValue && configuredId == userId.Value);
        }

        return false;
    }

    public async Task<TelegramResponse> HandleUpdateAsync(string updateJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(updateJson))
        {
            return new TelegramResponse { Success = false, Handled = false };
        }

        TelegramUpdate update;
        try
        {
            update = JsonSerializer.Deserialize<TelegramUpdate>(updateJson, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to deserialize Telegram update JSON");
            return new TelegramResponse { Success = false, Handled = false };
        }

        return await HandleUpdateAsync(update, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TelegramResponse> HandleUpdateAsync(TelegramUpdate update, CancellationToken cancellationToken = default)
    {
        if (update == null)
        {
            return new TelegramResponse { Success = false, Handled = false };
        }

        if (update.CallbackQuery != null)
        {
            return await HandleCallbackQueryAsync(update.CallbackQuery, cancellationToken).ConfigureAwait(false);
        }

        if (update.Message != null)
        {
            return await HandleMessageAsync(update.Message, cancellationToken).ConfigureAwait(false);
        }

        return new TelegramResponse { Success = true, Handled = false };
    }

    private async Task<TelegramResponse> HandleMessageAsync(TelegramMessage message, CancellationToken cancellationToken)
    {
        var chatId = message.Chat?.Id ?? 0;
        var userId = message.From?.Id;

        if (!IsAuthorized(chatId, userId))
        {
            _logger.Warn("Unauthorized Telegram message from Chat ID {0}, User ID {1}", chatId, userId);
            var unauthResponse = new TelegramResponse
            {
                Success = false,
                Handled = true,
                Authorized = false,
                ResponseText = "Unauthorized: Access denied."
            };

            await SendMessageAsync(chatId, unauthResponse.ResponseText, cancellationToken: cancellationToken).ConfigureAwait(false);
            return unauthResponse;
        }

        var text = message.Text?.Trim() ?? string.Empty;
        if (!text.StartsWith('/'))
        {
            return new TelegramResponse { Success = true, Handled = false, Authorized = true };
        }

        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();
        if (command.Contains('@'))
        {
            command = command.Split('@')[0];
        }

        var args = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        TelegramResponse response;
        switch (command)
        {
            case "/start":
            case "/help":
                response = HandleStart(chatId);
                break;
            case "/status":
                response = HandleStatus(chatId);
                break;
            case "/torrents":
                response = HandleTorrents(chatId, args);
                break;
            case "/pause":
                response = HandlePause(chatId, args);
                break;
            case "/resume":
                response = HandleResume(chatId, args);
                break;
            case "/turtle":
                response = HandleTurtle(chatId);
                break;
            default:
                response = new TelegramResponse
                {
                    Success = false,
                    Handled = true,
                    Authorized = true,
                    Command = command,
                    ResponseText = $"Unknown command: {FormatTelegramHtml(command)}. Type /start for available commands."
                };
                break;
        }

        if (!string.IsNullOrWhiteSpace(response.ResponseText))
        {
            await SendMessageAsync(chatId, response.ResponseText, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private async Task<TelegramResponse> HandleCallbackQueryAsync(TelegramCallbackQuery query, CancellationToken cancellationToken)
    {
        var chatId = query.Message?.Chat?.Id ?? query.From?.Id ?? 0;
        var userId = query.From?.Id;

        if (!IsAuthorized(chatId, userId))
        {
            _logger.Warn("Unauthorized Telegram callback query from Chat ID {0}, User ID {1}", chatId, userId);
            await AnswerCallbackQueryAsync(query.Id, "Unauthorized: Access denied.", cancellationToken).ConfigureAwait(false);
            return new TelegramResponse
            {
                Success = false,
                Handled = true,
                Authorized = false,
                ResponseText = "Unauthorized: Access denied."
            };
        }

        var data = query.Data ?? string.Empty;
        string answerText;
        object updatedReplyMarkup = null;

        if (data.StartsWith("pause:", StringComparison.OrdinalIgnoreCase))
        {
            var idStr = data.Substring("pause:".Length);
            if (int.TryParse(idStr, out var torrentId))
            {
                _torrentService?.Pause(torrentId);
                answerText = $"Paused torrent #{torrentId}";

                updatedReplyMarkup = new
                {
                    inline_keyboard = new[]
                    {
                        new[]
                        {
                            new { text = "▶️ Resume", callback_data = $"resume:{torrentId}" },
                            new { text = "🐢 Turtle", callback_data = "turtle:toggle" },
                        }
                    }
                };
            }
            else
            {
                answerText = "Invalid torrent ID";
            }
        }
        else if (data.StartsWith("resume:", StringComparison.OrdinalIgnoreCase))
        {
            var idStr = data.Substring("resume:".Length);
            if (int.TryParse(idStr, out var torrentId))
            {
                _torrentService?.Start(torrentId);
                answerText = $"Resumed torrent #{torrentId}";

                updatedReplyMarkup = new
                {
                    inline_keyboard = new[]
                    {
                        new[]
                        {
                            new { text = "⏸️ Pause", callback_data = $"pause:{torrentId}" },
                            new { text = "🐢 Turtle", callback_data = "turtle:toggle" },
                        }
                    }
                };
            }
            else
            {
                answerText = "Invalid torrent ID";
            }
        }
        else if (string.Equals(data, "turtle:toggle", StringComparison.OrdinalIgnoreCase))
        {
            var current = _configService?.AlternativeSpeedEnabled ?? false;
            var newState = !current;
            _configService?.SaveConfigDictionary(new Dictionary<string, object>
            {
                ["AlternativeSpeedEnabled"] = newState
            });

            answerText = newState ? "Turtle mode ON" : "Turtle mode OFF";
        }
        else
        {
            answerText = "Unknown action";
        }

        await AnswerCallbackQueryAsync(query.Id, answerText, cancellationToken).ConfigureAwait(false);

        if (updatedReplyMarkup != null && query.Message != null)
        {
            await EditMessageReplyMarkupAsync(chatId, query.Message.MessageId, updatedReplyMarkup, cancellationToken).ConfigureAwait(false);
        }

        return new TelegramResponse
        {
            Success = true,
            Handled = true,
            Authorized = true,
            Command = data,
            CallbackQueryAnswer = answerText
        };
    }

    private TelegramResponse HandleStart(long chatId)
    {
        var text = "<b>Seedarr Telegram Bot</b>\n\n" +
                   "Status: <b>Authorized</b> ✅\n\n" +
                   "<b>Available Commands:</b>\n" +
                   "/status — View speeds, torrent counts & disk space\n" +
                   "/torrents — List active torrents with progress\n" +
                   "/pause &lt;id&gt; — Pause a torrent\n" +
                   "/resume &lt;id&gt; — Resume a torrent\n" +
                   "/turtle — Toggle alternate speed mode\n" +
                   "/start — Show this help message";

        return new TelegramResponse
        {
            Success = true,
            Handled = true,
            Authorized = true,
            Command = "/start",
            ResponseText = text
        };
    }

    private TelegramResponse HandleStatus(long chatId)
    {
        var torrents = _torrentService?.GetAll() ?? new List<Torrent>();
        var uploadSpeed = torrents.Sum(t => t.UploadSpeed);
        var downloadSpeed = torrents.Sum(t => t.DownloadSpeed);
        var totalCount = torrents.Count;
        var downloadingCount = torrents.Count(t => t.Status == TorrentStatus.Downloading);
        var seedingCount = torrents.Count(t => t.Status == TorrentStatus.Seeding);
        var pausedCount = torrents.Count(t => t.Status == TorrentStatus.Paused);
        var turtleActive = _configService?.AlternativeSpeedEnabled ?? false;

        var sb = new StringBuilder();
        sb.AppendLine("<b>Seedarr Status</b>");
        sb.AppendLine($"⚡ <b>DL:</b> {FormatSpeed(downloadSpeed)} | <b>UL:</b> {FormatSpeed(uploadSpeed)}");
        sb.AppendLine($"📦 <b>Torrents:</b> {totalCount} total (⬇️ {downloadingCount} dl, ⬆️ {seedingCount} seed, ⏸️ {pausedCount} paused)");
        sb.AppendLine($"🐢 <b>Turtle Mode:</b> {(turtleActive ? "Active" : "Off")}");

        var diskInfo = GetDiskSpaceSummary();
        if (!string.IsNullOrWhiteSpace(diskInfo))
        {
            sb.AppendLine($"💾 <b>Disk:</b> {diskInfo}");
        }

        return new TelegramResponse
        {
            Success = true,
            Handled = true,
            Authorized = true,
            Command = "/status",
            ResponseText = sb.ToString().TrimEnd()
        };
    }

    private TelegramResponse HandleTorrents(long chatId, string args)
    {
        var torrents = _torrentService?.GetAll() ?? new List<Torrent>();
        if (torrents.Count == 0)
        {
            return new TelegramResponse
            {
                Success = true,
                Handled = true,
                Authorized = true,
                Command = "/torrents",
                ResponseText = "No torrents found."
            };
        }

        var sb = new StringBuilder();
        sb.AppendLine("<b>Active Torrents:</b>\n");

        var activeTorrents = torrents
            .OrderByDescending(t => t.DownloadSpeed + t.UploadSpeed)
            .ThenBy(t => t.Name)
            .Take(10)
            .ToList();

        foreach (var t in activeTorrents)
        {
            var safeName = FormatTelegramHtml(t.Name);
            var progressPct = Math.Clamp(t.Progress <= 1.0 && t.Progress > 0 ? t.Progress * 100.0 : t.Progress, 0.0, 100.0);
            var progressBar = BuildTextProgressBar(progressPct);

            sb.AppendLine($"• <b>{safeName}</b> (ID: {t.Id})");
            sb.AppendLine($"  {progressBar} {progressPct:F1}% | {t.Status}");
            sb.AppendLine($"  ⬇️ {FormatSpeed(t.DownloadSpeed)} | ⬆️ {FormatSpeed(t.UploadSpeed)} | Ratio: {t.Ratio:F2}\n");
        }

        if (torrents.Count > activeTorrents.Count)
        {
            sb.AppendLine($"<i>Showing {activeTorrents.Count} of {torrents.Count} torrents.</i>");
        }

        return new TelegramResponse
        {
            Success = true,
            Handled = true,
            Authorized = true,
            Command = "/torrents",
            ResponseText = sb.ToString().TrimEnd()
        };
    }

    private TelegramResponse HandlePause(long chatId, string args)
    {
        if (string.IsNullOrWhiteSpace(args) || !int.TryParse(args.Trim(), out var torrentId))
        {
            return new TelegramResponse
            {
                Success = false,
                Handled = true,
                Authorized = true,
                Command = "/pause",
                ResponseText = "Usage: /pause &lt;id&gt;"
            };
        }

        var torrent = _torrentService?.Get(torrentId);
        if (torrent == null)
        {
            return new TelegramResponse
            {
                Success = false,
                Handled = true,
                Authorized = true,
                Command = "/pause",
                ResponseText = $"Torrent with ID {torrentId} not found."
            };
        }

        _torrentService.Pause(torrentId);
        var safeName = FormatTelegramHtml(torrent.Name);

        return new TelegramResponse
        {
            Success = true,
            Handled = true,
            Authorized = true,
            Command = "/pause",
            ResponseText = $"⏸️ Paused torrent: <b>{safeName}</b> (ID: {torrentId})"
        };
    }

    private TelegramResponse HandleResume(long chatId, string args)
    {
        if (string.IsNullOrWhiteSpace(args) || !int.TryParse(args.Trim(), out var torrentId))
        {
            return new TelegramResponse
            {
                Success = false,
                Handled = true,
                Authorized = true,
                Command = "/resume",
                ResponseText = "Usage: /resume &lt;id&gt;"
            };
        }

        var torrent = _torrentService?.Get(torrentId);
        if (torrent == null)
        {
            return new TelegramResponse
            {
                Success = false,
                Handled = true,
                Authorized = true,
                Command = "/resume",
                ResponseText = $"Torrent with ID {torrentId} not found."
            };
        }

        _torrentService.Start(torrentId);
        var safeName = FormatTelegramHtml(torrent.Name);

        return new TelegramResponse
        {
            Success = true,
            Handled = true,
            Authorized = true,
            Command = "/resume",
            ResponseText = $"▶️ Resumed torrent: <b>{safeName}</b> (ID: {torrentId})"
        };
    }

    private TelegramResponse HandleTurtle(long chatId)
    {
        var current = _configService?.AlternativeSpeedEnabled ?? false;
        var newState = !current;
        _configService?.SaveConfigDictionary(new Dictionary<string, object>
        {
            ["AlternativeSpeedEnabled"] = newState
        });

        var responseText = newState
            ? "🐢 <b>Turtle mode enabled</b> (Alternate speed limits active)"
            : "🚀 <b>Turtle mode disabled</b> (Normal speed limits active)";

        return new TelegramResponse
        {
            Success = true,
            Handled = true,
            Authorized = true,
            Command = "/turtle",
            ResponseText = responseText
        };
    }

    private string GetDiskSpaceSummary()
    {
        try
        {
            if (_diskSpaceService != null)
            {
                var spaces = _diskSpaceService.GetDiskSpace();
                if (spaces != null && spaces.Count > 0)
                {
                    var primary = spaces.FirstOrDefault(s => !string.IsNullOrEmpty(s.Path)) ?? spaces[0];
                    return $"{FormatBytes(primary.FreeSpace)} free / {FormatBytes(primary.TotalSpace)}";
                }
            }

            var root = new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory) ?? "/");
            if (root.IsReady)
            {
                return $"{FormatBytes(root.AvailableFreeSpace)} free / {FormatBytes(root.TotalSize)}";
            }
        }
        catch
        {
            // Ignore drive access errors
        }

        return null;
    }

    private static string BuildTextProgressBar(double pct, int length = 8)
    {
        var filled = (int)Math.Round(pct / 100.0 * length);
        filled = Math.Clamp(filled, 0, length);
        var empty = length - filled;
        return "[" + new string('█', filled) + new string('░', empty) + "]";
    }

    public async Task AnswerCallbackQueryAsync(string callbackQueryId, string text = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings?.BotToken) || string.IsNullOrWhiteSpace(callbackQueryId))
        {
            return;
        }

        var url = $"https://api.telegram.org/bot{_settings.BotToken}/answerCallbackQuery";
        var payload = new Dictionary<string, object>
        {
            ["callback_query_id"] = callbackQueryId
        };

        if (!string.IsNullOrWhiteSpace(text))
        {
            payload["text"] = text;
        }

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to answer Telegram callback query {0}", callbackQueryId);
        }
    }

    public async Task SendMessageAsync(long chatId, string text, object replyMarkup = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings?.BotToken) || chatId == 0)
        {
            return;
        }

        var url = $"https://api.telegram.org/bot{_settings.BotToken}/sendMessage";
        var payload = new Dictionary<string, object>
        {
            ["chat_id"] = chatId,
            ["text"] = text,
            ["parse_mode"] = "HTML"
        };

        if (replyMarkup != null)
        {
            payload["reply_markup"] = replyMarkup;
        }

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to send Telegram message to chat {0}", chatId);
        }
    }

    public async Task EditMessageReplyMarkupAsync(long chatId, long messageId, object replyMarkup, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings?.BotToken) || chatId == 0 || messageId == 0)
        {
            return;
        }

        var url = $"https://api.telegram.org/bot{_settings.BotToken}/editMessageReplyMarkup";
        var payload = new Dictionary<string, object>
        {
            ["chat_id"] = chatId,
            ["message_id"] = messageId,
            ["reply_markup"] = replyMarkup
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to edit Telegram message reply markup in chat {0}", chatId);
        }
    }
}
