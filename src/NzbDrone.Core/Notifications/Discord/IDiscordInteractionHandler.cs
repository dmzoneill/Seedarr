using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Notifications.Discord;

public interface IDiscordInteractionHandler
{
    Task<DiscordInteractionResponse> HandleInteractionAsync(DiscordInteraction interaction, CancellationToken cancellationToken = default);

    Task<DiscordInteractionResponse> HandleInteractionAsync(string interactionJson, CancellationToken cancellationToken = default);

    bool IsAuthorized(DiscordInteraction interaction);

    Task<bool> EditOriginalResponseAsync(string applicationId, string interactionToken, DiscordInteractionCallbackData data, CancellationToken cancellationToken = default);

    Task<bool> SendFollowupMessageAsync(string applicationId, string interactionToken, DiscordInteractionCallbackData data, CancellationToken cancellationToken = default);
}
