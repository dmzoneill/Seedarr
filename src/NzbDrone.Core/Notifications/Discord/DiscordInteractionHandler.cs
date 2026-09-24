using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications.Discord;

public class DiscordInteractionHandler : IDiscordInteractionHandler
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
    private readonly IDiscordEmbedPaginator _paginator;
    private readonly DiscordSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

    public DiscordInteractionHandler(
        ITorrentService torrentService,
        IConfigService configService,
        IDiskSpaceService diskSpaceService = null,
        IDiscordEmbedPaginator paginator = null,
        DiscordSettings settings = null,
        HttpClient httpClient = null)
    {
        _torrentService = torrentService;
        _configService = configService;
        _diskSpaceService = diskSpaceService;
        _paginator = paginator ?? new DiscordEmbedPaginator();
        _settings = settings ?? new DiscordSettings();
        _httpClient = httpClient ?? SharedHttpClient;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool IsAuthorized(DiscordInteraction interaction)
    {
        if (interaction == null)
        {
            return false;
        }

        var hasUserFilter = _settings.AllowedUserIds != null && _settings.AllowedUserIds.Count > 0;
        var hasRoleFilter = _settings.AllowedRoleIds != null && _settings.AllowedRoleIds.Count > 0;
        var hasGuildFilter = _settings.AllowedGuildIds != null && _settings.AllowedGuildIds.Count > 0;

        if (!hasUserFilter && !hasRoleFilter && !hasGuildFilter)
        {
            return true;
        }

        if (hasGuildFilter && !string.IsNullOrWhiteSpace(interaction.GuildId) && ulong.TryParse(interaction.GuildId, out var guildId))
        {
            if (_settings.AllowedGuildIds.Contains(guildId))
            {
                return true;
            }
        }

        var effectiveUserId = interaction.EffectiveUserId;
        if (hasUserFilter && effectiveUserId.HasValue)
        {
            if (_settings.AllowedUserIds.Contains(effectiveUserId.Value))
            {
                return true;
            }
        }

        if (hasRoleFilter && interaction.Member?.Roles != null && interaction.Member.Roles.Count > 0)
        {
            foreach (var roleStr in interaction.Member.Roles)
            {
                if (ulong.TryParse(roleStr, out var roleId) && _settings.AllowedRoleIds.Contains(roleId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public Task<DiscordInteractionResponse> HandleInteractionAsync(string interactionJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(interactionJson))
        {
            return Task.FromResult(new DiscordInteractionResponse { Success = false, Handled = false });
        }

        DiscordInteraction interaction;
        try
        {
            interaction = JsonSerializer.Deserialize<DiscordInteraction>(interactionJson, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to deserialize Discord interaction JSON");
            return Task.FromResult(new DiscordInteractionResponse { Success = false, Handled = false });
        }

        return Task.FromResult(HandleInteraction(interaction, cancellationToken));
    }

    public Task<DiscordInteractionResponse> HandleInteractionAsync(DiscordInteraction interaction, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HandleInteraction(interaction, cancellationToken));
    }

    private DiscordInteractionResponse HandleInteraction(DiscordInteraction interaction, CancellationToken cancellationToken)
    {
        if (interaction == null)
        {
            return new DiscordInteractionResponse { Success = false, Handled = false };
        }

        if (interaction.Type == DiscordInteractionType.Ping)
        {
            return DiscordInteractionResponse.Pong();
        }

        if (!IsAuthorized(interaction))
        {
            _logger.Warn("Unauthorized Discord interaction from User ID {0}", interaction.EffectiveUserId);
            return new DiscordInteractionResponse
            {
                Type = DiscordInteractionResponseType.ChannelMessageWithSource,
                Data = new DiscordInteractionCallbackData
                {
                    Content = "Unauthorized: Access denied.",
                    Flags = 64, // Ephemeral
                },
                Success = false,
                Handled = true,
                Authorized = false,
            };
        }

        if (interaction.Type == DiscordInteractionType.ApplicationCommand)
        {
            return HandleApplicationCommand(interaction);
        }

        if (interaction.Type == DiscordInteractionType.MessageComponent)
        {
            return HandleMessageComponent(interaction, cancellationToken);
        }

        return new DiscordInteractionResponse { Success = true, Handled = false };
    }

    public async Task<bool> EditOriginalResponseAsync(
        string applicationId,
        string interactionToken,
        DiscordInteractionCallbackData data,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(applicationId) || string.IsNullOrWhiteSpace(interactionToken) || data == null)
        {
            return false;
        }

        var url = $"https://discord.com/api/v10/webhooks/{applicationId}/{interactionToken}/messages/@original";
        var json = JsonSerializer.Serialize(data, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to edit original Discord interaction response");
            return false;
        }
    }

    public async Task<bool> SendFollowupMessageAsync(
        string applicationId,
        string interactionToken,
        DiscordInteractionCallbackData data,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(applicationId) || string.IsNullOrWhiteSpace(interactionToken) || data == null)
        {
            return false;
        }

        var url = $"https://discord.com/api/v10/webhooks/{applicationId}/{interactionToken}";
        var json = JsonSerializer.Serialize(data, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to send followup Discord interaction message");
            return false;
        }
    }

    private DiscordInteractionResponse HandleApplicationCommand(DiscordInteraction interaction)
    {
        var commandName = interaction.Data?.Name?.ToLowerInvariant() ?? string.Empty;
        if (commandName.StartsWith('/'))
        {
            commandName = commandName[1..];
        }

        return commandName switch
        {
            "status" => HandleStatus(),
            "torrents" => HandleTorrents(interaction),
            "pause" => HandlePause(interaction),
            "resume" => HandleResume(interaction),
            "turtle" => HandleTurtle(),
            "speed" => HandleSpeed(interaction),
            _ => new DiscordInteractionResponse
            {
                Type = DiscordInteractionResponseType.ChannelMessageWithSource,
                Data = new DiscordInteractionCallbackData
                {
                    Content = $"Unknown slash command: `/{commandName}`",
                    Flags = 64,
                },
                Success = false,
                Handled = true,
                Authorized = true,
                Command = commandName,
            }
        };
    }

    private DiscordInteractionResponse HandleStatus()
    {
        var torrents = _torrentService?.GetAll() ?? new List<Torrent>();
        var uploadSpeed = torrents.Sum(t => t.UploadSpeed);
        var downloadSpeed = torrents.Sum(t => t.DownloadSpeed);
        var totalCount = torrents.Count;
        var downloadingCount = torrents.Count(t => t.Status == TorrentStatus.Downloading);
        var seedingCount = torrents.Count(t => t.Status == TorrentStatus.Seeding);
        var pausedCount = torrents.Count(t => t.Status == TorrentStatus.Paused);
        var turtleActive = _configService?.AlternativeSpeedEnabled ?? false;
        var diskInfo = GetDiskSpaceSummary() ?? "N/A";

        var speedsLine = $"⚡ **DL:** {_paginator.FormatSpeed(downloadSpeed)} | **UL:** {_paginator.FormatSpeed(uploadSpeed)}";
        var torrentsLine = $"📦 **Torrents:** {totalCount} total (⬇️ {downloadingCount} dl, ⬆️ {seedingCount} seed, ⏸️ {pausedCount} paused)";
        var turtleLine = $"🐢 **Turtle Mode:** {(turtleActive ? "Active" : "Off")}";
        var diskLine = $"💾 **Disk:** {diskInfo}";

        var description = $"{speedsLine}\n{torrentsLine}\n{turtleLine}\n{diskLine}";

        var embed = new DiscordEmbed
        {
            Title = "Seedarr Status",
            Description = description,
            Color = 0x35C5F4,
            Fields = new List<DiscordEmbedField>
            {
                new DiscordEmbedField { Name = "Speeds", Value = speedsLine, Inline = false },
                new DiscordEmbedField { Name = "Torrents", Value = torrentsLine, Inline = false },
                new DiscordEmbedField { Name = "Turtle Mode", Value = turtleActive ? "Active" : "Off", Inline = true },
                new DiscordEmbedField { Name = "Disk Space", Value = diskInfo, Inline = true },
            },
            Footer = new DiscordEmbedFooter { Text = "Seedarr" },
            Timestamp = DateTime.UtcNow.ToString("o"),
        };

        var response = DiscordInteractionResponse.ChannelMessage(embed: embed);
        response.Command = "status";
        return _paginator.GuardResponse(response);
    }

    private DiscordInteractionResponse HandleTorrents(DiscordInteraction interaction)
    {
        var filter = interaction.Data?.Options?
            .FirstOrDefault(o => string.Equals(o.Name, "filter", StringComparison.OrdinalIgnoreCase))?
            .GetStringValue();

        if (string.IsNullOrWhiteSpace(filter) && interaction.Data?.Options != null && interaction.Data.Options.Count > 0)
        {
            filter = interaction.Data.Options[0].GetStringValue();
        }

        var torrents = _torrentService?.GetAll() ?? new List<Torrent>();
        var response = _paginator.CreateTorrentsPage(torrents, page: 1, pageSize: 5, filter: filter, responseType: DiscordInteractionResponseType.ChannelMessageWithSource);
        response.Command = "torrents";
        return response;
    }

    private DiscordInteractionResponse HandlePause(DiscordInteraction interaction)
    {
        var option = interaction.Data?.Options?
            .FirstOrDefault(o => string.Equals(o.Name, "id", StringComparison.OrdinalIgnoreCase))
            ?? interaction.Data?.Options?.FirstOrDefault();

        var torrentId = option?.GetIntValue();
        if (!torrentId.HasValue)
        {
            return new DiscordInteractionResponse
            {
                Type = DiscordInteractionResponseType.ChannelMessageWithSource,
                Data = new DiscordInteractionCallbackData
                {
                    Content = "Usage: `/pause <id>`",
                    Flags = 64,
                },
                Success = false,
                Handled = true,
                Authorized = true,
                Command = "pause",
            };
        }

        var torrent = _torrentService?.Get(torrentId.Value);
        if (torrent == null)
        {
            return new DiscordInteractionResponse
            {
                Type = DiscordInteractionResponseType.ChannelMessageWithSource,
                Data = new DiscordInteractionCallbackData
                {
                    Content = $"Torrent with ID {torrentId.Value} not found.",
                    Flags = 64,
                },
                Success = false,
                Handled = true,
                Authorized = true,
                Command = "pause",
            };
        }

        _torrentService.Pause(torrentId.Value);

        var resumeButton = new DiscordComponent
        {
            Type = DiscordComponentType.Button,
            Style = DiscordButtonStyle.Success,
            Label = "▶️ Resume",
            CustomId = $"resume:{torrentId.Value}",
        };

        var actionRow = new DiscordComponent
        {
            Type = DiscordComponentType.ActionRow,
            Components = new List<DiscordComponent> { resumeButton },
        };

        var response = DiscordInteractionResponse.ChannelMessage(
            content: $"⏸️ Paused torrent: **{torrent.Name}** (ID: {torrentId.Value})",
            components: new List<DiscordComponent> { actionRow });
        response.Command = "pause";
        return response;
    }

    private DiscordInteractionResponse HandleResume(DiscordInteraction interaction)
    {
        var option = interaction.Data?.Options?
            .FirstOrDefault(o => string.Equals(o.Name, "id", StringComparison.OrdinalIgnoreCase))
            ?? interaction.Data?.Options?.FirstOrDefault();

        var torrentId = option?.GetIntValue();
        if (!torrentId.HasValue)
        {
            return new DiscordInteractionResponse
            {
                Type = DiscordInteractionResponseType.ChannelMessageWithSource,
                Data = new DiscordInteractionCallbackData
                {
                    Content = "Usage: `/resume <id>`",
                    Flags = 64,
                },
                Success = false,
                Handled = true,
                Authorized = true,
                Command = "resume",
            };
        }

        var torrent = _torrentService?.Get(torrentId.Value);
        if (torrent == null)
        {
            return new DiscordInteractionResponse
            {
                Type = DiscordInteractionResponseType.ChannelMessageWithSource,
                Data = new DiscordInteractionCallbackData
                {
                    Content = $"Torrent with ID {torrentId.Value} not found.",
                    Flags = 64,
                },
                Success = false,
                Handled = true,
                Authorized = true,
                Command = "resume",
            };
        }

        _torrentService.Start(torrentId.Value);

        var pauseButton = new DiscordComponent
        {
            Type = DiscordComponentType.Button,
            Style = DiscordButtonStyle.Secondary,
            Label = "⏸️ Pause",
            CustomId = $"pause:{torrentId.Value}",
        };

        var actionRow = new DiscordComponent
        {
            Type = DiscordComponentType.ActionRow,
            Components = new List<DiscordComponent> { pauseButton },
        };

        var response = DiscordInteractionResponse.ChannelMessage(
            content: $"▶️ Resumed torrent: **{torrent.Name}** (ID: {torrentId.Value})",
            components: new List<DiscordComponent> { actionRow });
        response.Command = "resume";
        return response;
    }

    private DiscordInteractionResponse HandleTurtle()
    {
        var current = _configService?.AlternativeSpeedEnabled ?? false;
        var newState = !current;
        _configService?.SaveConfigDictionary(new Dictionary<string, object>
        {
            ["AlternativeSpeedEnabled"] = newState,
        });

        var message = newState
            ? "🐢 **Turtle mode enabled** (Alternate speed limits active)"
            : "🚀 **Turtle mode disabled** (Normal speed limits active)";

        var toggleButton = new DiscordComponent
        {
            Type = DiscordComponentType.Button,
            Style = newState ? DiscordButtonStyle.Success : DiscordButtonStyle.Secondary,
            Label = newState ? "🐢 Turtle: ON" : "🐢 Turtle: OFF",
            CustomId = "turtle:toggle",
        };

        var actionRow = new DiscordComponent
        {
            Type = DiscordComponentType.ActionRow,
            Components = new List<DiscordComponent> { toggleButton },
        };

        var response = DiscordInteractionResponse.ChannelMessage(
            content: message,
            components: new List<DiscordComponent> { actionRow });
        response.Command = "turtle";
        return response;
    }

    private DiscordInteractionResponse HandleSpeed(DiscordInteraction interaction)
    {
        int? downKbps = null;
        int? upKbps = null;

        if (interaction.Data?.Options != null)
        {
            var downOpt = interaction.Data.Options.FirstOrDefault(o => string.Equals(o.Name, "down", StringComparison.OrdinalIgnoreCase));
            var upOpt = interaction.Data.Options.FirstOrDefault(o => string.Equals(o.Name, "up", StringComparison.OrdinalIgnoreCase));

            downKbps = downOpt?.GetIntValue();
            upKbps = upOpt?.GetIntValue();

            if (!downKbps.HasValue && !upKbps.HasValue && interaction.Data.Options.Count > 0)
            {
                downKbps = interaction.Data.Options[0].GetIntValue();
                if (interaction.Data.Options.Count > 1)
                {
                    upKbps = interaction.Data.Options[1].GetIntValue();
                }
            }
        }

        var updateDict = new Dictionary<string, object>();
        if (downKbps.HasValue && downKbps.Value > 0)
        {
            updateDict["MaxDownloadSpeedKbps"] = downKbps.Value;
        }

        if (upKbps.HasValue && upKbps.Value > 0)
        {
            updateDict["MaxUploadSpeedKbps"] = upKbps.Value;
        }

        if (updateDict.Count > 0)
        {
            _configService?.SaveConfigDictionary(updateDict);
        }

        var currentDown = _configService?.MaxDownloadSpeedKbps ?? 0;
        var currentUp = _configService?.MaxUploadSpeedKbps ?? 0;

        var text = updateDict.Count > 0
            ? $"⚡ **Bandwidth limits updated:** DL: `{currentDown} KB/s`, UL: `{currentUp} KB/s`"
            : $"⚡ **Current bandwidth limits:** DL: `{currentDown} KB/s`, UL: `{currentUp} KB/s`";

        var response = DiscordInteractionResponse.ChannelMessage(content: text);
        response.Command = "speed";
        return response;
    }

    private DiscordInteractionResponse HandleMessageComponent(
        DiscordInteraction interaction,
        CancellationToken cancellationToken)
    {
        var customId = interaction.Data?.CustomId ?? string.Empty;

        if (customId.StartsWith("pause:", StringComparison.OrdinalIgnoreCase) ||
            customId.StartsWith("pause_", StringComparison.OrdinalIgnoreCase))
        {
            var idStr = customId.StartsWith("pause:", StringComparison.OrdinalIgnoreCase)
                ? customId["pause:".Length..]
                : customId["pause_".Length..];

            if (int.TryParse(idStr, out var torrentId))
            {
                _torrentService?.Pause(torrentId);
                var torrent = _torrentService?.Get(torrentId);
                var torrentName = torrent?.Name ?? $"#{torrentId}";

                var resumeButton = new DiscordComponent
                {
                    Type = DiscordComponentType.Button,
                    Style = DiscordButtonStyle.Success,
                    Label = "▶️ Resume",
                    CustomId = $"resume:{torrentId}",
                };

                var actionRow = new DiscordComponent
                {
                    Type = DiscordComponentType.ActionRow,
                    Components = new List<DiscordComponent> { resumeButton },
                };

                var response = DiscordInteractionResponse.UpdateMessage(
                    content: $"⏸️ Paused torrent: **{torrentName}** (ID: {torrentId})",
                    components: new List<DiscordComponent> { actionRow });
                response.Command = customId;
                return response;
            }

            return new DiscordInteractionResponse
            {
                Type = DiscordInteractionResponseType.ChannelMessageWithSource,
                Data = new DiscordInteractionCallbackData { Content = "Invalid torrent ID", Flags = 64 },
                Success = false,
                Handled = true,
                Command = customId,
            };
        }

        if (customId.StartsWith("resume:", StringComparison.OrdinalIgnoreCase) ||
            customId.StartsWith("resume_", StringComparison.OrdinalIgnoreCase))
        {
            var idStr = customId.StartsWith("resume:", StringComparison.OrdinalIgnoreCase)
                ? customId["resume:".Length..]
                : customId["resume_".Length..];

            if (int.TryParse(idStr, out var torrentId))
            {
                _torrentService?.Start(torrentId);
                var torrent = _torrentService?.Get(torrentId);
                var torrentName = torrent?.Name ?? $"#{torrentId}";

                var pauseButton = new DiscordComponent
                {
                    Type = DiscordComponentType.Button,
                    Style = DiscordButtonStyle.Secondary,
                    Label = "⏸️ Pause",
                    CustomId = $"pause:{torrentId}",
                };

                var actionRow = new DiscordComponent
                {
                    Type = DiscordComponentType.ActionRow,
                    Components = new List<DiscordComponent> { pauseButton },
                };

                var response = DiscordInteractionResponse.UpdateMessage(
                    content: $"▶️ Resumed torrent: **{torrentName}** (ID: {torrentId})",
                    components: new List<DiscordComponent> { actionRow });
                response.Command = customId;
                return response;
            }

            return new DiscordInteractionResponse
            {
                Type = DiscordInteractionResponseType.ChannelMessageWithSource,
                Data = new DiscordInteractionCallbackData { Content = "Invalid torrent ID", Flags = 64 },
                Success = false,
                Handled = true,
                Command = customId,
            };
        }

        if (string.Equals(customId, "turtle:toggle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(customId, "turtle_toggle", StringComparison.OrdinalIgnoreCase))
        {
            var current = _configService?.AlternativeSpeedEnabled ?? false;
            var newState = !current;
            _configService?.SaveConfigDictionary(new Dictionary<string, object>
            {
                ["AlternativeSpeedEnabled"] = newState,
            });

            var message = newState
                ? "🐢 **Turtle mode enabled** (Alternate speed limits active)"
                : "🚀 **Turtle mode disabled** (Normal speed limits active)";

            var toggleButton = new DiscordComponent
            {
                Type = DiscordComponentType.Button,
                Style = newState ? DiscordButtonStyle.Success : DiscordButtonStyle.Secondary,
                Label = newState ? "🐢 Turtle: ON" : "🐢 Turtle: OFF",
                CustomId = "turtle:toggle",
            };

            var actionRow = new DiscordComponent
            {
                Type = DiscordComponentType.ActionRow,
                Components = new List<DiscordComponent> { toggleButton },
            };

            var response = DiscordInteractionResponse.UpdateMessage(
                content: message,
                components: new List<DiscordComponent> { actionRow });
            response.Command = customId;
            return response;
        }

        if (customId.StartsWith("torrents_page:", StringComparison.OrdinalIgnoreCase) ||
            customId.StartsWith("torrents_page_", StringComparison.OrdinalIgnoreCase))
        {
            var pageStr = customId.StartsWith("torrents_page:", StringComparison.OrdinalIgnoreCase)
                ? customId["torrents_page:".Length..]
                : customId["torrents_page_".Length..];

            if (int.TryParse(pageStr, out var page))
            {
                var torrents = _torrentService?.GetAll() ?? new List<Torrent>();
                var response = _paginator.CreateTorrentsPage(
                    torrents,
                    page: page,
                    pageSize: 5,
                    filter: null,
                    responseType: DiscordInteractionResponseType.UpdateMessage);
                response.Command = customId;
                return response;
            }
        }

        return new DiscordInteractionResponse
        {
            Type = DiscordInteractionResponseType.ChannelMessageWithSource,
            Data = new DiscordInteractionCallbackData
            {
                Content = $"Unknown component interaction: `{customId}`",
                Flags = 64,
            },
            Success = false,
            Handled = true,
            Command = customId,
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
                    return $"{_paginator.FormatBytes(primary.FreeSpace)} free / {_paginator.FormatBytes(primary.TotalSpace)}";
                }
            }

            var root = new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory) ?? "/");
            if (root.IsReady)
            {
                return $"{_paginator.FormatBytes(root.AvailableFreeSpace)} free / {_paginator.FormatBytes(root.TotalSize)}";
            }
        }
        catch
        {
            // Ignore drive access errors
        }

        return null;
    }
}
