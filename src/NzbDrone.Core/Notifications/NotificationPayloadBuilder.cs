using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        return explicitHeaders;
    }

    public static object BuildProviderPayload(string implementation, string eventType, Torrent torrent, dynamic meta, object genericPayload, string settings = null)
    {
        var (chatId, token, user) = ExtractProviderSettings(settings);
        var torrentName = torrent?.Name ?? ExtractMessage(genericPayload, eventType);

        if (string.Equals(implementation, "Discord", StringComparison.OrdinalIgnoreCase))
        {
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
            var desc = Truncate(rawDesc, 4096);

            var payloadDict = new Dictionary<string, object>
            {
                ["username"] = "Seedarr",
                ["embeds"] = new object[]
                {
                    new
                    {
                        title,
                        description = desc,
                        color = 16765286,
                        timestamp = DateTime.UtcNow.ToString("o"),
                    },
                },
            };

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
            var errSuffix = !string.IsNullOrWhiteSpace(err) ? $"\nError: {err}" : string.Empty;
            var fallbackText = torrent != null
                ? $"*Seedarr [{eventType}]* - *{torrent.Name}*\nCategory: {torrent.Category ?? torrent.Label ?? "None"} | Status: {torrent.Status} | Size: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB{errSuffix}"
                : $"*Seedarr [{eventType}]*\n{ExtractMessage(genericPayload, eventType)}";

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
                    ["text"] = Truncate($"*{torrent.Name}*{errSuffix}", 3000),
                };

                var ratioOrEta = torrent.Eta > 0
                    ? $"{torrent.Ratio:F2} (ETA: {torrent.Eta}s)"
                    : $"{torrent.Ratio:F2}";

                sectionBlock["fields"] = new object[]
                {
                    new Dictionary<string, object> { ["type"] = "mrkdwn", ["text"] = $"*Category:*\n{torrent.Category ?? torrent.Label ?? "None"}" },
                    new Dictionary<string, object> { ["type"] = "mrkdwn", ["text"] = $"*Status:*\n{torrent.Status}" },
                    new Dictionary<string, object> { ["type"] = "mrkdwn", ["text"] = $"*Size:*\n{torrent.TotalSize / (1024.0 * 1024.0):F2} MB" },
                    new Dictionary<string, object> { ["type"] = "mrkdwn", ["text"] = $"*Ratio/ETA:*\n{ratioOrEta}" },
                };
            }
            else
            {
                var msg = ExtractMessage(genericPayload, eventType);
                sectionBlock["text"] = new Dictionary<string, object>
                {
                    ["type"] = "mrkdwn",
                    ["text"] = Truncate(!string.IsNullOrWhiteSpace(msg) ? msg : $"Event: {eventType}", 3000),
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
            var text = torrent != null
                ? $"*Seedarr [{EpisodicParser.EscapeMarkdownStatic(eventType)}]*\n*{EpisodicParser.EscapeMarkdownStatic(torrent.Name)}*\nCategory: {EpisodicParser.EscapeMarkdownStatic(torrent.Category ?? torrent.Label ?? "None")}\nProgress: {torrent.Progress * 100:F1}%\nStatus: {torrent.Status}{errSuffix}"
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
            var priority = ExtractPriority(settings, 5);

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

        return value.Length > maxLength ? string.Concat(value.AsSpan(0, maxLength - 3), "...") : value;
    }

    internal static string ExtractOverview(dynamic meta)
    {
        if (meta == null)
        {
            return null;
        }

        try
        {
            return (string)meta.Overview;
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
}
