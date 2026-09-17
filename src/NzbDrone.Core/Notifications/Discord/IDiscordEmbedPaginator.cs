using System.Collections.Generic;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications.Discord;

public interface IDiscordEmbedPaginator
{
    DiscordEmbed GuardEmbed(DiscordEmbed embed);

    DiscordInteractionResponse GuardResponse(DiscordInteractionResponse response);

    DiscordComponent CreatePaginationRow(int currentPage, int totalPages, string customIdPrefix = "torrents_page_");

    DiscordInteractionResponse CreateTorrentsPage(
        IReadOnlyList<Torrent> torrents,
        int page = 1,
        int pageSize = 5,
        string filter = null,
        int responseType = DiscordInteractionResponseType.ChannelMessageWithSource);

    string BuildProgressBar(double percentage, int length = 10);

    string FormatSpeed(long bytesPerSec);

    string FormatBytes(long bytes);

    string FormatEta(long? etaSeconds);

    string GetStatusBadge(Torrent torrent);
}
