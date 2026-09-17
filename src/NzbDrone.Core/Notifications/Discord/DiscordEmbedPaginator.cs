using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications.Discord;

public class DiscordEmbedPaginator : IDiscordEmbedPaginator
{
    public const int MaxDescriptionLength = 4096;
    public const int MaxFields = 25;
    public const int MaxContentLength = 2000;
    public const int MaxTotalCharacters = 6000;
    public const int MaxFieldNameLength = 256;
    public const int MaxFieldValueLength = 1024;
    public const int MaxTitleLength = 256;
    public const int MaxFooterTextLength = 2048;
    public const int MaxAuthorNameLength = 256;

    public DiscordEmbed GuardEmbed(DiscordEmbed embed)
    {
        if (embed == null)
        {
            return null;
        }

        if (embed.Title != null && embed.Title.Length > MaxTitleLength)
        {
            embed.Title = Truncate(embed.Title, MaxTitleLength);
        }

        if (embed.Description != null && embed.Description.Length > MaxDescriptionLength)
        {
            embed.Description = Truncate(embed.Description, MaxDescriptionLength);
        }

        if (embed.Footer?.Text != null && embed.Footer.Text.Length > MaxFooterTextLength)
        {
            embed.Footer.Text = Truncate(embed.Footer.Text, MaxFooterTextLength);
        }

        if (embed.Author?.Name != null && embed.Author.Name.Length > MaxAuthorNameLength)
        {
            embed.Author.Name = Truncate(embed.Author.Name, MaxAuthorNameLength);
        }

        if (embed.Fields != null)
        {
            if (embed.Fields.Count > MaxFields)
            {
                embed.Fields = embed.Fields.Take(MaxFields).ToList();
            }

            foreach (var field in embed.Fields)
            {
                if (string.IsNullOrEmpty(field.Name))
                {
                    field.Name = "-";
                }
                else if (field.Name.Length > MaxFieldNameLength)
                {
                    field.Name = Truncate(field.Name, MaxFieldNameLength);
                }

                if (string.IsNullOrEmpty(field.Value))
                {
                    field.Value = "-";
                }
                else if (field.Value.Length > MaxFieldValueLength)
                {
                    field.Value = Truncate(field.Value, MaxFieldValueLength);
                }
            }
        }

        EnforceTotalEmbedLength(embed);

        return embed;
    }

    public DiscordInteractionResponse GuardResponse(DiscordInteractionResponse response)
    {
        if (response == null)
        {
            return null;
        }

        if (response.Data != null)
        {
            if (response.Data.Content != null && response.Data.Content.Length > MaxContentLength)
            {
                response.Data.Content = Truncate(response.Data.Content, MaxContentLength);
            }

            if (response.Data.Embeds != null)
            {
                for (var i = 0; i < response.Data.Embeds.Count; i++)
                {
                    response.Data.Embeds[i] = GuardEmbed(response.Data.Embeds[i]);
                }
            }
        }

        return response;
    }

    public DiscordComponent CreatePaginationRow(int currentPage, int totalPages, string customIdPrefix = "torrents_page_")
    {
        var prevButton = new DiscordComponent
        {
            Type = DiscordComponentType.Button,
            Style = DiscordButtonStyle.Secondary,
            Label = "⬅️ Previous",
            CustomId = $"{customIdPrefix}{currentPage - 1}",
            Disabled = currentPage <= 1,
        };

        var nextButton = new DiscordComponent
        {
            Type = DiscordComponentType.Button,
            Style = DiscordButtonStyle.Primary,
            Label = "Next ➡️",
            CustomId = $"{customIdPrefix}{currentPage + 1}",
            Disabled = currentPage >= totalPages,
        };

        return new DiscordComponent
        {
            Type = DiscordComponentType.ActionRow,
            Components = new List<DiscordComponent> { prevButton, nextButton },
        };
    }

    public DiscordInteractionResponse CreateTorrentsPage(
        IReadOnlyList<Torrent> torrents,
        int page = 1,
        int pageSize = 5,
        string filter = null,
        int responseType = DiscordInteractionResponseType.ChannelMessageWithSource)
    {
        var source = torrents ?? Array.Empty<Torrent>();
        var filtered = FilterTorrents(source, filter);

        if (filtered.Count == 0)
        {
            var emptyMessage = string.IsNullOrWhiteSpace(filter)
                ? "No torrents found."
                : $"No torrents found matching filter: '{filter}'.";

            var emptyEmbed = new DiscordEmbed
            {
                Title = "Seedarr Torrents",
                Description = emptyMessage,
                Color = 0x5865F2,
            };

            return GuardResponse(new DiscordInteractionResponse
            {
                Type = responseType,
                Data = new DiscordInteractionCallbackData
                {
                    Embeds = new List<DiscordEmbed> { emptyEmbed },
                },
                Success = true,
                Handled = true,
                Authorized = true,
            });
        }

        var totalPages = Math.Max(1, (int)Math.Ceiling((double)filtered.Count / pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var pageItems = filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var title = string.IsNullOrWhiteSpace(filter)
            ? "Seedarr Torrents"
            : $"Seedarr Torrents — {filter}";

        var descBuilder = new StringBuilder();
        descBuilder.AppendLine($"**Active Torrents (Page {page} of {totalPages} • Total: {filtered.Count})**\n");

        var fields = new List<DiscordEmbedField>();

        foreach (var t in pageItems)
        {
            var badge = GetStatusBadge(t);
            var progressPct = Math.Clamp(t.Progress <= 1.0 && t.Progress > 0 ? t.Progress * 100.0 : t.Progress, 0.0, 100.0);
            var bar = BuildProgressBar(progressPct, 10);
            var etaStr = FormatEta(t.Eta);
            var speedStr = $"⬇️ {FormatSpeed(t.DownloadSpeed)} | ⬆️ {FormatSpeed(t.UploadSpeed)}";

            descBuilder.AppendLine($"• {badge} **{t.Name}** (#{t.Id})");
            descBuilder.AppendLine($"  {bar} | {t.Status}");
            descBuilder.AppendLine($"  {speedStr} | Ratio: {t.Ratio:F2} | ETA: {etaStr}\n");

            fields.Add(new DiscordEmbedField
            {
                Name = $"{badge} {Truncate(t.Name, 200)} (#{t.Id})",
                Value = $"{bar} | {t.Status}\n{speedStr} | Ratio: {t.Ratio:F2} | ETA: {etaStr}",
            });
        }

        var embed = new DiscordEmbed
        {
            Title = title,
            Description = descBuilder.ToString().TrimEnd(),
            Color = 0x5865F2,
            Fields = fields,
            Footer = new DiscordEmbedFooter
            {
                Text = $"Page {page} of {totalPages} • Total: {filtered.Count}",
            },
            Timestamp = DateTime.UtcNow.ToString("o"),
        };

        var paginationRow = CreatePaginationRow(page, totalPages);

        var response = new DiscordInteractionResponse
        {
            Type = responseType,
            Data = new DiscordInteractionCallbackData
            {
                Embeds = new List<DiscordEmbed> { embed },
                Components = new List<DiscordComponent> { paginationRow },
            },
            Success = true,
            Handled = true,
            Authorized = true,
        };

        return GuardResponse(response);
    }

    public string BuildProgressBar(double percentage, int length = 10)
    {
        var pct = Math.Clamp(percentage, 0.0, 100.0);
        var filled = (int)Math.Round(pct / 100.0 * length);
        filled = Math.Clamp(filled, 0, length);
        var empty = length - filled;
        var bar = "[" + new string('█', filled) + new string('░', empty) + "]";
        var pctText = pct % 1 == 0 ? $"{pct:F0}%" : $"{pct:F1}%";
        return $"{bar} {pctText}";
    }

    public string FormatSpeed(long bytesPerSec)
    {
        return $"{FormatBytes(bytesPerSec)}/s";
    }

    public string FormatBytes(long bytes)
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

    public string FormatEta(long? etaSeconds)
    {
        if (!etaSeconds.HasValue || etaSeconds.Value <= 0 || etaSeconds.Value >= 8640000)
        {
            return "∞";
        }

        var ts = TimeSpan.FromSeconds(etaSeconds.Value);
        if (ts.TotalDays >= 1)
        {
            return $"{(int)ts.TotalDays}d {ts.Hours}h";
        }

        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        }

        if (ts.TotalMinutes >= 1)
        {
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        }

        return $"{ts.Seconds}s";
    }

    public string GetStatusBadge(Torrent torrent)
    {
        if (torrent == null)
        {
            return "[UNKNOWN]";
        }

        return torrent.Status switch
        {
            TorrentStatus.Downloading => "⬇️ [DL]",
            TorrentStatus.Seeding => "⬆️ [SEED]",
            TorrentStatus.Paused => "⏸️ [PAUSED]",
            TorrentStatus.Queued => "⏳ [QUEUED]",
            TorrentStatus.Checking => "🔍 [CHECKING]",
            TorrentStatus.Error => "⚠️ [ERROR]",
            _ => $"[{torrent.Status.ToString().ToUpperInvariant()}]"
        };
    }

    private static List<Torrent> FilterTorrents(IEnumerable<Torrent> torrents, string filter)
    {
        var list = torrents.ToList();
        if (string.IsNullOrWhiteSpace(filter) || string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase))
        {
            return list.OrderByDescending(t => t.DownloadSpeed + t.UploadSpeed).ThenBy(t => t.Name).ToList();
        }

        var trimmed = filter.Trim();

        if (string.Equals(trimmed, "downloading", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "dl", StringComparison.OrdinalIgnoreCase))
        {
            return list.Where(t => t.Status == TorrentStatus.Downloading)
                .OrderByDescending(t => t.DownloadSpeed)
                .ThenBy(t => t.Name)
                .ToList();
        }

        if (string.Equals(trimmed, "seeding", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "seed", StringComparison.OrdinalIgnoreCase))
        {
            return list.Where(t => t.Status == TorrentStatus.Seeding)
                .OrderByDescending(t => t.UploadSpeed)
                .ThenBy(t => t.Name)
                .ToList();
        }

        if (string.Equals(trimmed, "paused", StringComparison.OrdinalIgnoreCase))
        {
            return list.Where(t => t.Status == TorrentStatus.Paused)
                .OrderBy(t => t.Name)
                .ToList();
        }

        if (string.Equals(trimmed, "active", StringComparison.OrdinalIgnoreCase))
        {
            return list.Where(t => t.DownloadSpeed > 0 || t.UploadSpeed > 0)
                .OrderByDescending(t => t.DownloadSpeed + t.UploadSpeed)
                .ThenBy(t => t.Name)
                .ToList();
        }

        if (string.Equals(trimmed, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return list.Where(t => t.Progress >= 1.0)
                .OrderByDescending(t => t.UploadSpeed)
                .ThenBy(t => t.Name)
                .ToList();
        }

        return list.Where(t =>
            (t.Name != null && t.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)) ||
            (t.Category != null && t.Category.Contains(trimmed, StringComparison.OrdinalIgnoreCase)) ||
            (t.Label != null && t.Label.Contains(trimmed, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(t => t.DownloadSpeed + t.UploadSpeed)
            .ThenBy(t => t.Name)
            .ToList();
    }

    private static void EnforceTotalEmbedLength(DiscordEmbed embed)
    {
        var total = CalculateTotalCharacters(embed);
        if (total <= MaxTotalCharacters)
        {
            return;
        }

        if (embed.Description != null && embed.Description.Length > 200)
        {
            var excess = total - MaxTotalCharacters;
            var newLen = Math.Max(100, embed.Description.Length - excess);
            embed.Description = Truncate(embed.Description, newLen);
            total = CalculateTotalCharacters(embed);
        }

        while (total > MaxTotalCharacters && embed.Fields != null && embed.Fields.Count > 1)
        {
            embed.Fields.RemoveAt(embed.Fields.Count - 1);
            total = CalculateTotalCharacters(embed);
        }
    }

    private static int CalculateTotalCharacters(DiscordEmbed embed)
    {
        var count = 0;
        if (!string.IsNullOrEmpty(embed.Title))
        {
            count += embed.Title.Length;
        }

        if (!string.IsNullOrEmpty(embed.Description))
        {
            count += embed.Description.Length;
        }

        if (embed.Footer != null && !string.IsNullOrEmpty(embed.Footer.Text))
        {
            count += embed.Footer.Text.Length;
        }

        if (embed.Author != null && !string.IsNullOrEmpty(embed.Author.Name))
        {
            count += embed.Author.Name.Length;
        }

        if (embed.Fields != null)
        {
            foreach (var field in embed.Fields)
            {
                if (!string.IsNullOrEmpty(field.Name))
                {
                    count += field.Name.Length;
                }

                if (!string.IsNullOrEmpty(field.Value))
                {
                    count += field.Value.Length;
                }
            }
        }

        return count;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value.Length > maxLength ? string.Concat(value.AsSpan(0, maxLength - 3), "...") : value;
    }
}
