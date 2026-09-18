using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications;

public static class NotificationPayloadBuilder
{
    public static (string ChatId, string Token, string User) ExtractProviderSettings(string settings)
    {
        var chatId = string.Empty;
        var token = string.Empty;
        var user = string.Empty;

        if (string.IsNullOrWhiteSpace(settings))
        {
            return (chatId, token, user);
        }

        if (settings.TrimStart().StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(settings);
                var root = doc.RootElement;
                if (root.TryGetProperty("chat_id", out var c) || root.TryGetProperty("chatId", out c))
                {
                    chatId = c.GetString() ?? c.ToString();
                }

                if (root.TryGetProperty("token", out var t) || root.TryGetProperty("botToken", out t) || root.TryGetProperty("apiKey", out t) || root.TryGetProperty("appToken", out t))
                {
                    token = t.GetString() ?? t.ToString();
                }

                if (root.TryGetProperty("user", out var u) || root.TryGetProperty("userKey", out u))
                {
                    user = u.GetString() ?? u.ToString();
                }
            }
            catch
            {
            }
        }

        if (string.IsNullOrEmpty(chatId) && settings.Contains("chat_id="))
        {
            var match = Regex.Match(settings, @"chat_id=([^&]+)");
            if (match.Success)
            {
                chatId = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        if (string.IsNullOrEmpty(token))
        {
            var match = Regex.Match(settings, @"(?:token|appToken|botToken|apiKey)=([^&]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                token = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        if (string.IsNullOrEmpty(user) && settings.Contains("user="))
        {
            var match = Regex.Match(settings, @"user=([^&]+)");
            if (match.Success)
            {
                user = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        return (chatId, token, user);
    }

    public static (string Username, string AvatarUrl) ExtractDiscordSettings(string settings)
    {
        var username = "Seedarr";
        string avatarUrl = null;

        if (string.IsNullOrWhiteSpace(settings))
        {
            return (username, avatarUrl);
        }

        var trimmed = settings.Trim();
        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                if (root.TryGetProperty("username", out var u) ||
                    root.TryGetProperty("Username", out u) ||
                    root.TryGetProperty("user", out u) ||
                    root.TryGetProperty("User", out u))
                {
                    var uVal = u.GetString() ?? u.ToString();
                    if (!string.IsNullOrWhiteSpace(uVal))
                    {
                        username = uVal.Trim();
                    }
                }

                if (root.TryGetProperty("avatarUrl", out var a) ||
                    root.TryGetProperty("avatar_url", out a) ||
                    root.TryGetProperty("AvatarUrl", out a) ||
                    root.TryGetProperty("avatar", out a))
                {
                    var aVal = a.GetString() ?? a.ToString();
                    if (!string.IsNullOrWhiteSpace(aVal))
                    {
                        avatarUrl = aVal.Trim();
                    }
                }
            }
            catch
            {
            }
        }
        else
        {
            var uMatch = Regex.Match(trimmed, @"(?:^|[&?])(?:username|Username|user)=([^&]+)", RegexOptions.IgnoreCase);
            if (uMatch.Success)
            {
                var val = Uri.UnescapeDataString(uMatch.Groups[1].Value).Trim();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    username = val;
                }
            }

            var aMatch = Regex.Match(trimmed, @"(?:^|[&?])(?:avatarUrl|avatar_url|AvatarUrl|avatar)=([^&]+)", RegexOptions.IgnoreCase);
            if (aMatch.Success)
            {
                var val = Uri.UnescapeDataString(aMatch.Groups[1].Value).Trim();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    avatarUrl = val;
                }
            }
        }

        return (username, avatarUrl);
    }

    public static string ResolveTargetUrl(string implementation, string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return string.Empty;
        }

        var trimmed = settings.Trim();

        if (string.Equals(implementation, "Telegram", StringComparison.OrdinalIgnoreCase))
        {
            var (_, token, _) = ExtractProviderSettings(trimmed);
            if (!string.IsNullOrEmpty(token))
            {
                return $"https://api.telegram.org/bot{token}/sendMessage";
            }

            return "https://api.telegram.org/bot/sendMessage";
        }

        if (string.Equals(implementation, "Pushover", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api.pushover.net/1/messages.json";
        }

        var candidateUrl = trimmed;

        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                if (root.TryGetProperty("url", out var u) || root.TryGetProperty("Url", out u) || root.TryGetProperty("webhookUrl", out u) || root.TryGetProperty("serverUrl", out u) || root.TryGetProperty("ServerUrl", out u))
                {
                    candidateUrl = u.GetString() ?? trimmed;
                }
            }
            catch
            {
            }
        }
        else if (trimmed.Contains("url="))
        {
            var match = Regex.Match(trimmed, @"url=([^&]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                candidateUrl = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        if (string.Equals(implementation, "Apprise", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(candidateUrl))
        {
            var clean = candidateUrl.TrimEnd('/');
            return clean.EndsWith("/notify", StringComparison.OrdinalIgnoreCase) ? clean : $"{clean}/notify";
        }

        if (string.Equals(implementation, "Gotify", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(candidateUrl))
        {
            var clean = candidateUrl.TrimEnd('/');
            return clean.EndsWith("/message", StringComparison.OrdinalIgnoreCase) ? clean : $"{clean}/message";
        }

        return candidateUrl;
    }

    public static (string Username, string Password) ExtractBasicAuthSettings(string settings)
    {
        string username = null;
        string password = null;

        if (string.IsNullOrWhiteSpace(settings))
        {
            return (username, password);
        }

        var trimmed = settings.Trim();
        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                var userProps = new[] { "username", "Username", "basicAuthUsername", "BasicAuthUsername" };
                foreach (var prop in userProps)
                {
                    if (root.TryGetProperty(prop, out var u) && u.ValueKind == JsonValueKind.String)
                    {
                        username = u.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(username))
                        {
                            break;
                        }
                    }
                }

                var passProps = new[] { "password", "Password", "basicAuthPassword", "BasicAuthPassword" };
                foreach (var prop in passProps)
                {
                    if (root.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String)
                    {
                        password = p.GetString();
                        if (password != null)
                        {
                            break;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        if (string.IsNullOrEmpty(username) && settings.Contains("username=", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(settings, @"(?:^|[&?])(?:username|Username|basicAuthUsername)=([^&]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                username = Uri.UnescapeDataString(match.Groups[1].Value).Trim();
            }
        }

        if (password == null && settings.Contains("password=", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(settings, @"(?:^|[&?])(?:password|Password|basicAuthPassword)=([^&]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                password = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        return (username, password);
    }

    public static HttpMethod ResolveHttpMethod(string implementation, string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return HttpMethod.Post;
        }

        var trimmed = settings.Trim();
        string methodStr = null;

        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                var methodProps = new[] { "method", "Method", "httpMethod", "HttpMethod" };
                foreach (var prop in methodProps)
                {
                    if (root.TryGetProperty(prop, out var m) && m.ValueKind == JsonValueKind.String)
                    {
                        methodStr = m.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(methodStr))
                        {
                            break;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        if (string.IsNullOrEmpty(methodStr))
        {
            var match = Regex.Match(settings, @"(?:^|[&?])(?:method|Method|httpMethod|HttpMethod)=([^&]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                methodStr = Uri.UnescapeDataString(match.Groups[1].Value).Trim();
            }
        }

        if (string.Equals(methodStr, "PUT", StringComparison.OrdinalIgnoreCase))
        {
            return HttpMethod.Put;
        }

        return HttpMethod.Post;
    }

    public static string ExtractPayloadTemplate(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return null;
        }

        var trimmed = settings.Trim();
        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                var templateProps = new[] { "payloadTemplate", "PayloadTemplate", "bodyTemplate", "BodyTemplate", "template", "Template" };
                foreach (var prop in templateProps)
                {
                    if (root.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String)
                    {
                        var val = p.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            return val;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        var matchKeys = new[] { "payloadTemplate=", "PayloadTemplate=", "bodyTemplate=", "BodyTemplate=", "template=" };
        foreach (var key in matchKeys)
        {
            if (settings.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(settings, $@"{key}([^&]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var val = Uri.UnescapeDataString(match.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        return val;
                    }
                }
            }
        }

        return null;
    }

    public static string ResolveCustomHeaders(string implementation, string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return null;
        }

        var trimmed = settings.Trim();
        string explicitHeaders = null;

        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                var propertyNames = new[] { "headers", "Headers", "customHeaders", "CustomHeaders", "custom_headers" };
                foreach (var propName in propertyNames)
                {
                    if (root.TryGetProperty(propName, out var prop))
                    {
                        if (prop.ValueKind == JsonValueKind.Object)
                        {
                            var raw = prop.GetRawText()?.Trim();
                            explicitHeaders = string.IsNullOrWhiteSpace(raw) || raw == "{}" ? null : raw;
                            break;
                        }

                        if (prop.ValueKind == JsonValueKind.String)
                        {
                            var str = prop.GetString()?.Trim();
                            explicitHeaders = string.IsNullOrWhiteSpace(str) || str == "{}" ? null : str;
                            break;
                        }

                        if (prop.ValueKind == JsonValueKind.Array)
                        {
                            var raw = prop.GetRawText()?.Trim();
                            explicitHeaders = string.IsNullOrWhiteSpace(raw) || raw == "[]" ? null : raw;
                            break;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        if (explicitHeaders == null)
        {
            var matchKeys = new[] { "headers=", "customHeaders=", "custom_headers=" };
            foreach (var key in matchKeys)
            {
                if (settings.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(settings, $@"{key}([^&]+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var val = Uri.UnescapeDataString(match.Groups[1].Value).Trim();
                        explicitHeaders = string.IsNullOrWhiteSpace(val) || val == "{}" ? null : val;
                        break;
                    }
                }
            }
        }

        if (string.Equals(implementation, "Gotify", StringComparison.OrdinalIgnoreCase))
        {
            var (_, token, _) = ExtractProviderSettings(trimmed);
            if (!string.IsNullOrWhiteSpace(token))
            {
                if (string.IsNullOrWhiteSpace(explicitHeaders))
                {
                    return $"{{\"X-Gotify-Key\":\"{token}\"}}";
                }

                if (explicitHeaders.StartsWith("{"))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(explicitHeaders);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var p in doc.RootElement.EnumerateObject())
                            {
                                dict[p.Name] = p.Value.GetString() ?? p.Value.GetRawText();
                            }

                            if (!dict.ContainsKey("X-Gotify-Key"))
                            {
                                dict["X-Gotify-Key"] = token;
                            }

                            return JsonSerializer.Serialize(dict);
                        }
                    }
                    catch
                    {
                    }
                }

                return $"{explicitHeaders}\nX-Gotify-Key: {token}";
            }
        }

        var (username, password) = ExtractBasicAuthSettings(trimmed);
        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password) &&
            !string.Equals(implementation, "Pushover", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(implementation, "Telegram", StringComparison.OrdinalIgnoreCase))
        {
            var authBytes = Encoding.UTF8.GetBytes($"{username}:{password}");
            var basicAuth = $"Basic {Convert.ToBase64String(authBytes)}";

            if (string.IsNullOrWhiteSpace(explicitHeaders))
            {
                return $"{{\"Authorization\":\"{basicAuth}\"}}";
            }

            if (explicitHeaders.StartsWith("{"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(explicitHeaders);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var p in doc.RootElement.EnumerateObject())
                        {
                            dict[p.Name] = p.Value.GetString() ?? p.Value.GetRawText();
                        }

                        if (!dict.ContainsKey("Authorization"))
                        {
                            dict["Authorization"] = basicAuth;
                        }

                        return JsonSerializer.Serialize(dict);
                    }
                }
                catch
                {
                }
            }

            var lines = explicitHeaders.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var hasAuth = lines.Any(l =>
            {
                var clean = l.Trim();
                return clean.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase) ||
                    clean.StartsWith("Authorization=", StringComparison.OrdinalIgnoreCase);
            });

            if (!hasAuth)
            {
                return $"{explicitHeaders}\nAuthorization: {basicAuth}";
            }
        }

        return explicitHeaders;
    }

    public static object BuildProviderPayload(string implementation, string eventType, Torrent torrent, object meta, object genericPayload, string settings = null)
    {
        var (chatId, token, user) = ExtractProviderSettings(settings);
        var torrentName = torrent?.Name ?? ExtractMessage(genericPayload, eventType);

        if (string.Equals(implementation, "Discord", StringComparison.OrdinalIgnoreCase))
        {
            var (discordUsername, avatarUrl) = ExtractDiscordSettings(settings);
            var title = Truncate($"[{eventType}] {torrentName}", 256);
            var torrentDetails = torrent != null
                ? $"Category: {torrent.Category ?? torrent.Label ?? "None"} | Status: {torrent.Status} | Progress: {torrent.Progress * 100:F1}% | Size: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB"
                : ExtractMessage(genericPayload, $"Event: {eventType}");
            var err = ExtractErrorMessage(genericPayload);
            if (!string.IsNullOrWhiteSpace(err))
            {
                torrentDetails += $"\nError: {err}";
            }

            var overview = ExtractOverview(meta);
            var rawDesc = !string.IsNullOrWhiteSpace(overview)
                ? $"{torrentDetails}\n\n{overview}"
                : torrentDetails;

            var maxDescLength = Math.Min(4096, 6000 - (title?.Length ?? 0));
            if (maxDescLength < 0)
            {
                maxDescLength = 0;
            }

            var desc = Truncate(rawDesc, maxDescLength);
            var color = GetDiscordColor(eventType);

            var payloadDict = new Dictionary<string, object>
            {
                ["username"] = discordUsername,
                ["embeds"] = new object[]
                {
                    new
                    {
                        title,
                        description = desc,
                        color,
                        timestamp = DateTime.UtcNow.ToString("o"),
                    },
                },
            };

            if (!string.IsNullOrWhiteSpace(avatarUrl))
            {
                payloadDict["avatar_url"] = avatarUrl;
            }

            if (torrent != null)
            {
                payloadDict["components"] = new object[]
                {
                    new
                    {
                        type = 1,
                        components = new object[]
                        {
                            new
                            {
                                type = 2,
                                style = 2,
                                label = "Pause",
                                custom_id = $"pause:{torrent.Id}",
                            },
                            new
                            {
                                type = 2,
                                style = 3,
                                label = "Resume",
                                custom_id = $"resume:{torrent.Id}",
                            },
                        },
                    },
                };
            }

            return payloadDict;
        }

        if (string.Equals(implementation, "Slack", StringComparison.OrdinalIgnoreCase))
        {
            var err = ExtractErrorMessage(genericPayload);
            var escapedErr = EscapeSlackMrkdwn(err);
            var errSuffix = !string.IsNullOrWhiteSpace(escapedErr) ? $"\nError: {escapedErr}" : string.Empty;
            var escapedEventType = EscapeSlackMrkdwn(eventType);
            var escapedTorrentName = torrent != null ? EscapeSlackMrkdwn(torrent.Name) : string.Empty;
            var category = torrent?.Category ?? torrent?.Label ?? "None";
            var escapedCategory = EscapeSlackMrkdwn(category);
            var genericMsg = ExtractMessage(genericPayload, eventType);
            var escapedGenericMsg = EscapeSlackMrkdwn(genericMsg);

            var fallbackText = torrent != null
                ? $"*Seedarr [{escapedEventType}]* - *{escapedTorrentName}*\nCategory: {escapedCategory} | Status: {torrent.Status} | Size: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB{errSuffix}"
                : $"*Seedarr [{escapedEventType}]*\n{escapedGenericMsg}";

            var blocks = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["type"] = "header",
                    ["text"] = new Dictionary<string, object>
                    {
                        ["type"] = "plain_text",
                        ["text"] = Truncate($"Seedarr [{eventType}]", 150),
                        ["emoji"] = true,
                    },
                },
            };

            var sectionBlock = new Dictionary<string, object>
            {
                ["type"] = "section",
            };

            if (torrent != null)
            {
                sectionBlock["text"] = new Dictionary<string, object>
                {
                    ["type"] = "mrkdwn",
                    ["text"] = Truncate($"*{escapedTorrentName}*{errSuffix}", 3000),
                };

                var ratioOrEta = torrent.Eta > 0
                    ? $"{torrent.Ratio:F2} (ETA: {torrent.Eta}s)"
                    : $"{torrent.Ratio:F2}";

                sectionBlock["fields"] = new object[]
                {
                    new Dictionary<string, object> { ["type"] = "mrkdwn", ["text"] = $"*Category:*\n{escapedCategory}" },
                    new Dictionary<string, object> { ["type"] = "mrkdwn", ["text"] = $"*Status:*\n{torrent.Status}" },
                    new Dictionary<string, object> { ["type"] = "mrkdwn", ["text"] = $"*Size:*\n{torrent.TotalSize / (1024.0 * 1024.0):F2} MB" },
                    new Dictionary<string, object> { ["type"] = "mrkdwn", ["text"] = $"*Ratio/ETA:*\n{ratioOrEta}" },
                };
            }
            else
            {
                sectionBlock["text"] = new Dictionary<string, object>
                {
                    ["type"] = "mrkdwn",
                    ["text"] = Truncate(!string.IsNullOrWhiteSpace(escapedGenericMsg) ? escapedGenericMsg : $"Event: {escapedEventType}", 3000),
                };
            }

            blocks.Add(sectionBlock);

            blocks.Add(new Dictionary<string, object>
            {
                ["type"] = "context",
                ["elements"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "mrkdwn",
                        ["text"] = "Seedarr Notification",
                    },
                },
            });

            var attachments = new object[]
            {
                new Dictionary<string, object>
                {
                    ["color"] = GetSlackColor(eventType),
                },
            };

            return new Dictionary<string, object>
            {
                ["text"] = Truncate(fallbackText, 3500),
                ["username"] = "Seedarr",
                ["blocks"] = blocks,
                ["attachments"] = attachments,
            };
        }

        if (string.Equals(implementation, "Telegram", StringComparison.OrdinalIgnoreCase))
        {
            var err = ExtractErrorMessage(genericPayload);
            var errSuffix = !string.IsNullOrWhiteSpace(err) ? $"\nError: {EpisodicParser.EscapeMarkdownStatic(err)}" : string.Empty;
            var progress = torrent != null ? EpisodicParser.EscapeMarkdownStatic($"{torrent.Progress * 100:F1}%") : string.Empty;
            var status = torrent != null ? EpisodicParser.EscapeMarkdownStatic(torrent.Status.ToString()) : string.Empty;
            var text = torrent != null
                ? $"*Seedarr [{EpisodicParser.EscapeMarkdownStatic(eventType)}]*\n*{EpisodicParser.EscapeMarkdownStatic(torrent.Name)}*\nCategory: {EpisodicParser.EscapeMarkdownStatic(torrent.Category ?? torrent.Label ?? "None")}\nProgress: {progress}\nStatus: {status}{errSuffix}"
                : $"*Seedarr [{EpisodicParser.EscapeMarkdownStatic(eventType)}]*\n{EpisodicParser.EscapeMarkdownStatic(ExtractMessage(genericPayload, eventType))}";

            var payloadDict = new Dictionary<string, object>
            {
                ["text"] = Truncate(text, 4096),
                ["parse_mode"] = "Markdown",
            };

            if (!string.IsNullOrEmpty(chatId))
            {
                payloadDict["chat_id"] = chatId;
            }

            if (torrent != null)
            {
                var actionButtonText = torrent.Status == TorrentStatus.Paused ? "▶️ Resume" : "⏸️ Pause";
                var actionCallback = torrent.Status == TorrentStatus.Paused ? $"resume:{torrent.Id}" : $"pause:{torrent.Id}";
                payloadDict["reply_markup"] = new
                {
                    inline_keyboard = new[]
                    {
                        new[]
                        {
                            new { text = actionButtonText, callback_data = actionCallback },
                            new { text = "🐢 Turtle", callback_data = "turtle:toggle" },
                        }
                    }
                };
            }

            return payloadDict;
        }

        if (string.Equals(implementation, "Gotify", StringComparison.OrdinalIgnoreCase))
        {
            var err = ExtractErrorMessage(genericPayload);
            var errSuffix = !string.IsNullOrWhiteSpace(err) ? $" - Error: {err}" : string.Empty;
            var isHealthIssue = eventType != null && (
                eventType.Contains("HealthIssue", StringComparison.OrdinalIgnoreCase) ||
                eventType.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                eventType.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                eventType.Contains("Issue", StringComparison.OrdinalIgnoreCase));
            var defaultPriority = isHealthIssue ? 8 : 5;
            var priority = ExtractPriority(settings, defaultPriority);
            if (isHealthIssue && priority < 8)
            {
                priority = 8;
            }

            return new Dictionary<string, object>
            {
                ["title"] = $"Seedarr: {eventType}",
                ["message"] = torrent != null ? $"{torrent.Name} ({torrent.Category ?? torrent.Label ?? "Default"}) - {torrent.Status}{errSuffix}" : ExtractMessage(genericPayload, eventType),
                ["priority"] = priority,
                ["extras"] = new Dictionary<string, object>
                {
                    ["client::display"] = new Dictionary<string, object>
                    {
                        ["contentType"] = "text/markdown",
                    },
                },
            };
        }

        if (string.Equals(implementation, "Pushover", StringComparison.OrdinalIgnoreCase))
        {
            var err = ExtractErrorMessage(genericPayload);
            var errSuffix = !string.IsNullOrWhiteSpace(err) ? $" - Error: {err}" : string.Empty;
            var rawTitle = $"Seedarr: {eventType}";
            var rawMessage = torrent != null
                ? $"{torrent.Name} ({torrent.Category ?? torrent.Label ?? "Default"}) - {torrent.Status}{errSuffix}"
                : ExtractMessage(genericPayload, eventType);

            var (priority, retry, expire, device, sound) = ExtractPushoverSettings(settings);

            var payloadDict = new Dictionary<string, object>
            {
                ["title"] = Truncate(rawTitle, 250),
                ["message"] = Truncate(rawMessage, 1024),
                ["priority"] = priority,
            };

            if (!string.IsNullOrEmpty(token))
            {
                payloadDict["token"] = token;
            }

            if (!string.IsNullOrEmpty(user))
            {
                payloadDict["user"] = user;
            }

            if (priority == 2)
            {
                var clampedRetry = Math.Max(30, retry > 0 ? retry : 60);
                var clampedExpire = Math.Min(10800, expire > 0 ? expire : 3600);
                payloadDict["retry"] = clampedRetry;
                payloadDict["expire"] = clampedExpire;
            }

            if (!string.IsNullOrWhiteSpace(device))
            {
                payloadDict["device"] = device;
            }

            if (!string.IsNullOrWhiteSpace(sound))
            {
                payloadDict["sound"] = sound;
            }

            return payloadDict;
        }

        if (string.Equals(implementation, "Apprise", StringComparison.OrdinalIgnoreCase))
        {
            var err = ExtractErrorMessage(genericPayload);
            var errSuffix = !string.IsNullOrWhiteSpace(err) ? $"\nError: {err}" : string.Empty;
            var title = $"Seedarr: {eventType}";
            var body = torrent != null
                ? $"Torrent: {torrent.Name}\nCategory: {torrent.Category ?? torrent.Label ?? "None"}\nStatus: {torrent.Status}\nProgress: {torrent.Progress * 100:F1}%\nSize: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB{errSuffix}"
                : ExtractMessage(genericPayload, $"Event: {eventType}");

            var isWarning = eventType.Contains("HealthIssue", StringComparison.OrdinalIgnoreCase) ||
                            eventType.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                            eventType.Contains("Failed", StringComparison.OrdinalIgnoreCase);

            return new
            {
                title,
                body,
                type = isWarning ? "warning" : "info",
            };
        }

        var template = ExtractPayloadTemplate(settings);
        if (!string.IsNullOrWhiteSpace(template))
        {
            return InterpolateTemplate(template, eventType, torrent, meta, genericPayload);
        }

        return genericPayload;
    }

    public static string ExtractErrorMessage(object payload)
    {
        if (payload == null)
        {
            return null;
        }

        if (payload is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        if (payload is Dictionary<string, object> dict)
        {
            if (dict.TryGetValue("ErrorMessage", out var em1) && em1 != null && !string.IsNullOrWhiteSpace(em1.ToString()))
            {
                return em1.ToString();
            }

            if (dict.TryGetValue("errorMessage", out var em2) && em2 != null && !string.IsNullOrWhiteSpace(em2.ToString()))
            {
                return em2.ToString();
            }

            if (dict.TryGetValue("Message", out var m1) && m1 != null && !string.IsNullOrWhiteSpace(m1.ToString()))
            {
                return m1.ToString();
            }

            if (dict.TryGetValue("message", out var m2) && m2 != null && !string.IsNullOrWhiteSpace(m2.ToString()))
            {
                return m2.ToString();
            }
        }

        var prop = payload.GetType().GetProperty("ErrorMessage", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? payload.GetType().GetProperty("errorMessage", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        var value = prop?.GetValue(payload)?.ToString();
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var torrentProp = payload.GetType().GetProperty("Torrent", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? payload.GetType().GetProperty("torrent", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (torrentProp != null)
        {
            var torrentObj = torrentProp.GetValue(payload);
            if (torrentObj != null)
            {
                var tErrProp = torrentObj.GetType().GetProperty("ErrorMessage", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                    ?? torrentObj.GetType().GetProperty("errorMessage", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                var tErrVal = tErrProp?.GetValue(torrentObj)?.ToString();
                if (!string.IsNullOrWhiteSpace(tErrVal))
                {
                    return tErrVal;
                }
            }
        }

        return null;
    }

    internal static string ExtractMessage(object payload, string fallback)
    {
        if (payload == null)
        {
            return fallback;
        }

        if (payload is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        if (payload is Dictionary<string, object> dict)
        {
            if (dict.TryGetValue("ErrorMessage", out var em1) && em1 != null && !string.IsNullOrWhiteSpace(em1.ToString()))
            {
                return em1.ToString();
            }

            if (dict.TryGetValue("errorMessage", out var em2) && em2 != null && !string.IsNullOrWhiteSpace(em2.ToString()))
            {
                return em2.ToString();
            }

            if (dict.TryGetValue("Message", out var m1) && m1 != null && !string.IsNullOrWhiteSpace(m1.ToString()))
            {
                return m1.ToString();
            }

            if (dict.TryGetValue("message", out var m2) && m2 != null && !string.IsNullOrWhiteSpace(m2.ToString()))
            {
                return m2.ToString();
            }
        }

        var prop = payload.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => string.Equals(p.Name, "ErrorMessage", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, "Message", StringComparison.OrdinalIgnoreCase));
        var value = prop?.GetValue(payload)?.ToString();

        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    public static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        if (maxLength <= 0)
        {
            return string.Empty;
        }

        if (maxLength <= 3)
        {
            return value[..maxLength];
        }

        return string.Concat(value.AsSpan(0, maxLength - 3), "...");
    }

    internal static string ExtractOverview(object meta)
    {
        if (meta == null)
        {
            return null;
        }

        try
        {
            var prop = meta.GetType().GetProperty("Overview");
            return prop?.GetValue(meta)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    public static string GetSlackColor(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return "#3AA3E3";
        }

        if (eventType.Contains("Grab", StringComparison.OrdinalIgnoreCase) ||
            eventType.Contains("Complete", StringComparison.OrdinalIgnoreCase) ||
            eventType.Contains("Success", StringComparison.OrdinalIgnoreCase) ||
            eventType.Contains("Restored", StringComparison.OrdinalIgnoreCase) ||
            eventType.Contains("GoalReached", StringComparison.OrdinalIgnoreCase))
        {
            return "#2eb886";
        }

        if (eventType.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
            eventType.Contains("Fail", StringComparison.OrdinalIgnoreCase) ||
            eventType.Contains("Issue", StringComparison.OrdinalIgnoreCase))
        {
            return "#a30200";
        }

        return "#3AA3E3";
    }

    public static int GetDiscordColor(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return 16765286;
        }

        var normalized = eventType.StartsWith("On", StringComparison.OrdinalIgnoreCase)
            ? eventType[2..]
            : eventType;

        if (normalized.Equals("Grab", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("DownloadComplete", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Complete", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("SeedGoalReached", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("HealthRestored", StringComparison.OrdinalIgnoreCase))
        {
            return 3066993;
        }

        if (normalized.Equals("HealthIssue", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("ArchiveExtractionFailed", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("ExtractionFailed", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Failed", StringComparison.OrdinalIgnoreCase))
        {
            return 15158332;
        }

        if (normalized.Equals("ManualInteractionRequired", StringComparison.OrdinalIgnoreCase))
        {
            return 15105570;
        }

        if (normalized.Equals("ApplicationUpdate", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("MediaInspected", StringComparison.OrdinalIgnoreCase))
        {
            return 3447003;
        }

        if (normalized.Equals("TorrentDeleted", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Deleted", StringComparison.OrdinalIgnoreCase))
        {
            return 9807270;
        }

        return 16765286;
    }

    public static string EscapeSlackMrkdwn(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    public static int ExtractPriority(string settings, int defaultPriority = 5)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return Math.Clamp(defaultPriority, 1, 10);
        }

        var priority = defaultPriority;
        if (settings.TrimStart().StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(settings);
                var root = doc.RootElement;
                if (root.TryGetProperty("priority", out var p) || root.TryGetProperty("Priority", out p))
                {
                    if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var pVal))
                    {
                        priority = pVal;
                    }
                    else if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var pStrVal))
                    {
                        priority = pStrVal;
                    }
                }
            }
            catch
            {
            }
        }
        else
        {
            var match = Regex.Match(settings, @"(?:^|[&?])(?:priority|Priority)=(\d+)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var pVal))
            {
                priority = pVal;
            }
        }

        return Math.Clamp(priority, 1, 10);
    }

    public static (int Priority, int Retry, int Expire, string Device, string Sound) ExtractPushoverSettings(string settings)
    {
        var priority = 0;
        var retry = 60;
        var expire = 3600;
        string device = null;
        string sound = null;

        if (string.IsNullOrWhiteSpace(settings))
        {
            return (0, 60, 3600, null, null);
        }

        var trimmed = settings.Trim();
        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                if (root.TryGetProperty("priority", out var p) || root.TryGetProperty("Priority", out p))
                {
                    if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var pVal))
                    {
                        priority = pVal;
                    }
                    else if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var pStrVal))
                    {
                        priority = pStrVal;
                    }
                }

                if (root.TryGetProperty("retry", out var r) || root.TryGetProperty("Retry", out r) ||
                    root.TryGetProperty("retrySeconds", out r) || root.TryGetProperty("RetrySeconds", out r))
                {
                    if (r.ValueKind == JsonValueKind.Number && r.TryGetInt32(out var rVal))
                    {
                        retry = rVal;
                    }
                    else if (r.ValueKind == JsonValueKind.String && int.TryParse(r.GetString(), out var rStrVal))
                    {
                        retry = rStrVal;
                    }
                }

                if (root.TryGetProperty("expire", out var e) || root.TryGetProperty("Expire", out e) ||
                    root.TryGetProperty("expireSeconds", out e) || root.TryGetProperty("ExpireSeconds", out e))
                {
                    if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var eVal))
                    {
                        expire = eVal;
                    }
                    else if (e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out var eStrVal))
                    {
                        expire = eStrVal;
                    }
                }

                if (root.TryGetProperty("device", out var d) || root.TryGetProperty("Device", out d))
                {
                    var dVal = d.GetString() ?? d.ToString();
                    if (!string.IsNullOrWhiteSpace(dVal))
                    {
                        device = dVal;
                    }
                }

                if (root.TryGetProperty("sound", out var s) || root.TryGetProperty("Sound", out s))
                {
                    var sVal = s.GetString() ?? s.ToString();
                    if (!string.IsNullOrWhiteSpace(sVal))
                    {
                        sound = sVal;
                    }
                }
            }
            catch
            {
            }
        }
        else
        {
            var pMatch = Regex.Match(trimmed, @"(?:^|[&?])(?:priority|Priority)=(-?\d+)", RegexOptions.IgnoreCase);
            if (pMatch.Success && int.TryParse(pMatch.Groups[1].Value, out var pVal))
            {
                priority = pVal;
            }

            var rMatch = Regex.Match(trimmed, @"(?:^|[&?])(?:retry|Retry|retrySeconds|RetrySeconds)=(\d+)", RegexOptions.IgnoreCase);
            if (rMatch.Success && int.TryParse(rMatch.Groups[1].Value, out var rVal))
            {
                retry = rVal;
            }

            var eMatch = Regex.Match(trimmed, @"(?:^|[&?])(?:expire|Expire|expireSeconds|ExpireSeconds)=(\d+)", RegexOptions.IgnoreCase);
            if (eMatch.Success && int.TryParse(eMatch.Groups[1].Value, out var eVal))
            {
                expire = eVal;
            }

            var dMatch = Regex.Match(trimmed, @"(?:^|[&?])(?:device|Device)=([^&]+)", RegexOptions.IgnoreCase);
            if (dMatch.Success)
            {
                device = Uri.UnescapeDataString(dMatch.Groups[1].Value);
            }

            var sMatch = Regex.Match(trimmed, @"(?:^|[&?])(?:sound|Sound)=([^&]+)", RegexOptions.IgnoreCase);
            if (sMatch.Success)
            {
                sound = Uri.UnescapeDataString(sMatch.Groups[1].Value);
            }
        }

        priority = Math.Clamp(priority, -2, 2);
        return (priority, retry, expire, device, sound);
    }

    public static bool IsLikelyJson(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        var trimmed = template.Trim();
        if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
            (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
        {
            if (trimmed.StartsWith("{") && trimmed.EndsWith("}") && !trimmed.Contains(':') && !trimmed.Contains(','))
            {
                return false;
            }

            return true;
        }

        return false;
    }

    public static string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return HttpUtility.JavaScriptStringEncode(value);
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

    public static string FormatSpeed(long bytesPerSec)
    {
        return $"{FormatBytes(bytesPerSec)}/s";
    }

    public static string InterpolateTemplate(
        string template,
        string eventType = null,
        Torrent torrent = null,
        object meta = null,
        object genericPayload = null,
        bool? isJson = null)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template ?? string.Empty;
        }

        var isJsonTemplate = isJson ?? IsLikelyJson(template);

        return Regex.Replace(template, @"\{([A-Za-z0-9_.]+)\}", match =>
        {
            var token = match.Groups[1].Value;
            var lower = token.ToLowerInvariant();

            string ResolveString(string s) => isJsonTemplate ? EscapeJsonString(s) : (s ?? string.Empty);

            switch (lower)
            {
                case "eventtype":
                    return ResolveString(eventType ?? ExtractPropertyString(genericPayload, "eventType") ?? string.Empty);

                case "instancename":
                    return ResolveString(ExtractPropertyString(genericPayload, "instanceName") ?? "Seedarr");

                case "timestamp":
                    return ResolveString(ExtractPropertyString(genericPayload, "timestamp") ?? DateTime.UtcNow.ToString("o"));

                case "torrent.id":
                    var id = torrent?.Id ?? ExtractPropertyLong(genericPayload, "torrent", "id") ?? 0;
                    return id.ToString(CultureInfo.InvariantCulture);

                case "torrent.name":
                    var tName = torrent?.Name ?? ExtractPropertyString(genericPayload, "torrent", "name") ?? ExtractMessage(genericPayload, eventType ?? string.Empty) ?? string.Empty;
                    return ResolveString(tName);

                case "torrent.infohash":
                    var hash = torrent?.InfoHash ?? ExtractPropertyString(genericPayload, "torrent", "infoHash") ?? string.Empty;
                    return ResolveString(hash);

                case "torrent.category":
                    var cat = torrent?.Category ?? torrent?.Label ?? ExtractPropertyString(genericPayload, "torrent", "category") ?? string.Empty;
                    return ResolveString(cat);

                case "torrent.status":
                    var stat = torrent != null ? torrent.Status.ToString() : (ExtractPropertyString(genericPayload, "torrent", "status") ?? ExtractPropertyString(genericPayload, "torrent", "state") ?? string.Empty);
                    return ResolveString(stat);

                case "torrent.sizeformatted":
                    var sfBytes = torrent?.TotalSize ?? ExtractPropertyLong(genericPayload, "torrent", "totalSize") ?? 0;
                    return ResolveString(FormatBytes(sfBytes));

                case "torrent.totalsize":
                    var ts = torrent?.TotalSize ?? ExtractPropertyLong(genericPayload, "torrent", "totalSize") ?? 0;
                    return ts.ToString(CultureInfo.InvariantCulture);

                case "torrent.downloaded":
                    var dl = torrent?.Downloaded ?? ExtractPropertyLong(genericPayload, "torrent", "downloaded") ?? 0;
                    return dl.ToString(CultureInfo.InvariantCulture);

                case "torrent.uploaded":
                    var ul = torrent?.Uploaded ?? ExtractPropertyLong(genericPayload, "torrent", "uploaded") ?? 0;
                    return ul.ToString(CultureInfo.InvariantCulture);

                case "torrent.ratio":
                    var ratio = torrent != null ? (double.IsFinite(torrent.Ratio) ? torrent.Ratio : 0.0) : (ExtractPropertyDouble(genericPayload, "torrent", "ratio") ?? 0.0);
                    return ratio.ToString("0.##", CultureInfo.InvariantCulture);

                case "torrent.progress":
                    var rawProg = torrent != null ? (double.IsFinite(torrent.Progress) ? torrent.Progress : 0.0) : (ExtractPropertyDouble(genericPayload, "torrent", "progress") ?? 0.0);
                    var progRatio = rawProg > 1.0 ? rawProg / 100.0 : rawProg;
                    return progRatio.ToString("0.####", CultureInfo.InvariantCulture);

                case "torrent.progresspercent":
                    var rawProgPct = torrent != null ? (double.IsFinite(torrent.Progress) ? torrent.Progress : 0.0) : (ExtractPropertyDouble(genericPayload, "torrent", "progress") ?? 0.0);
                    var progPct = rawProgPct <= 1.0 ? rawProgPct * 100.0 : rawProgPct;
                    return progPct.ToString("0.##", CultureInfo.InvariantCulture);

                case "torrent.downloadspeedformatted":
                    var dSpeed = torrent?.DownloadSpeed ?? ExtractPropertyLong(genericPayload, "torrent", "downloadSpeed") ?? 0;
                    return ResolveString(FormatSpeed(dSpeed));

                case "torrent.uploadspeedformatted":
                    var uSpeed = torrent?.UploadSpeed ?? ExtractPropertyLong(genericPayload, "torrent", "uploadSpeed") ?? 0;
                    return ResolveString(FormatSpeed(uSpeed));

                case "torrent.downloadspeed":
                    var rawDSpeed = torrent?.DownloadSpeed ?? ExtractPropertyLong(genericPayload, "torrent", "downloadSpeed") ?? 0;
                    return rawDSpeed.ToString(CultureInfo.InvariantCulture);

                case "torrent.uploadspeed":
                    var rawUSpeed = torrent?.UploadSpeed ?? ExtractPropertyLong(genericPayload, "torrent", "uploadSpeed") ?? 0;
                    return rawUSpeed.ToString(CultureInfo.InvariantCulture);

                case "torrent.etastring":
                    var etaStr = ExtractPropertyString(genericPayload, "torrent", "etaString") ?? (torrent != null ? EpisodicParser.FormatEtaStatic(torrent.Eta) : "00:00:00");
                    return ResolveString(etaStr);

                case "torrent.eta":
                    var rawEta = torrent != null ? torrent.Eta : (ExtractPropertyLong(genericPayload, "torrent", "eta") ?? 0);
                    return rawEta.ToString(CultureInfo.InvariantCulture);

                case "torrent.savepath":
                    var savePath = torrent?.SavePath ?? torrent?.SourcePath ?? ExtractPropertyString(genericPayload, "torrent", "savePath") ?? ExtractPropertyString(genericPayload, "torrent", "downloadPath") ?? string.Empty;
                    return ResolveString(savePath);

                case "media.title":
                    var mTitle = ExtractMediaTitle(meta) ?? ExtractPropertyString(genericPayload, "media", "title") ?? torrent?.Name ?? string.Empty;
                    return ResolveString(mTitle);

                case "media.year":
                    var mYear = ExtractMediaYear(meta) ?? ExtractPropertyInt(genericPayload, "media", "year") ?? 0;
                    return mYear.ToString(CultureInfo.InvariantCulture);

                case "media.overview":
                    var mOverview = ExtractOverview(meta) ?? ExtractPropertyString(genericPayload, "media", "overview") ?? string.Empty;
                    return ResolveString(mOverview);

                case "message":
                    var msg = ExtractMessage(genericPayload, string.Empty);
                    return ResolveString(msg);

                default:
                    return match.Value;
            }
        });
    }

    internal static object GetNestedProperty(object obj, params string[] propertyNames)
    {
        if (obj == null || propertyNames == null || propertyNames.Length == 0)
        {
            return null;
        }

        var current = obj;
        foreach (var propName in propertyNames)
        {
            if (current == null)
            {
                return null;
            }

            current = GetSingleProperty(current, propName);
        }

        return current;
    }

    private static object GetSingleProperty(object obj, string propName)
    {
        if (obj == null || string.IsNullOrWhiteSpace(propName))
        {
            return null;
        }

        if (obj is IDictionary<string, object> dict)
        {
            foreach (var kvp in dict)
            {
                if (string.Equals(kvp.Key, propName, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        if (obj is JsonElement jsonElem)
        {
            if (jsonElem.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in jsonElem.EnumerateObject())
                {
                    if (string.Equals(prop.Name, propName, StringComparison.OrdinalIgnoreCase))
                    {
                        return prop.Value;
                    }
                }
            }

            return null;
        }

        var propInfo = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return propInfo?.GetValue(obj);
    }

    internal static string ExtractPropertyString(object obj, params string[] path)
    {
        var val = GetNestedProperty(obj, path);
        if (val == null)
        {
            return null;
        }

        if (val is JsonElement elem)
        {
            return elem.ValueKind == JsonValueKind.String ? elem.GetString() : elem.GetRawText();
        }

        return val.ToString();
    }

    internal static long? ExtractPropertyLong(object obj, params string[] path)
    {
        var val = GetNestedProperty(obj, path);
        if (val == null)
        {
            return null;
        }

        if (val is long l)
        {
            return l;
        }

        if (val is int i)
        {
            return i;
        }

        if (val is double d)
        {
            return (long)d;
        }

        if (val is JsonElement elem)
        {
            if (elem.ValueKind == JsonValueKind.Number && elem.TryGetInt64(out var jsonLong))
            {
                return jsonLong;
            }

            if (elem.ValueKind == JsonValueKind.String && long.TryParse(elem.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedLong))
            {
                return parsedLong;
            }
        }

        if (long.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    internal static double? ExtractPropertyDouble(object obj, params string[] path)
    {
        var val = GetNestedProperty(obj, path);
        if (val == null)
        {
            return null;
        }

        if (val is double d)
        {
            return d;
        }

        if (val is float f)
        {
            return f;
        }

        if (val is int i)
        {
            return i;
        }

        if (val is long l)
        {
            return l;
        }

        if (val is JsonElement elem)
        {
            if (elem.ValueKind == JsonValueKind.Number && elem.TryGetDouble(out var jsonDouble))
            {
                return jsonDouble;
            }

            if (elem.ValueKind == JsonValueKind.String && double.TryParse(elem.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDouble))
            {
                return parsedDouble;
            }
        }

        if (double.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    internal static int? ExtractPropertyInt(object obj, params string[] path)
    {
        var l = ExtractPropertyLong(obj, path);
        return l.HasValue ? (int)l.Value : null;
    }

    internal static string ExtractMediaTitle(object meta)
    {
        if (meta == null)
        {
            return null;
        }

        try
        {
            var prop = meta.GetType().GetProperty("Title", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return prop?.GetValue(meta)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    internal static int? ExtractMediaYear(object meta)
    {
        if (meta == null)
        {
            return null;
        }

        try
        {
            var prop = meta.GetType().GetProperty("Year", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var val = prop?.GetValue(meta);
            if (val is int i && i > 0)
            {
                return i;
            }

            if (val is string s && int.TryParse(s, out var parsed) && parsed > 0)
            {
                return parsed;
            }
        }
        catch
        {
        }

        return null;
    }
}
