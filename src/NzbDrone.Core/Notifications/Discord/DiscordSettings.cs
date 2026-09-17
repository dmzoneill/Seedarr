using System.Collections.Generic;

namespace NzbDrone.Core.Notifications.Discord;

public class DiscordSettings
{
    public string WebhookUrl { get; set; } = string.Empty;
    public string BotToken { get; set; } = string.Empty;
    public string ApplicationId { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public List<ulong> AllowedUserIds { get; set; } = new();
    public List<ulong> AllowedRoleIds { get; set; } = new();
    public List<ulong> AllowedGuildIds { get; set; } = new();
}
