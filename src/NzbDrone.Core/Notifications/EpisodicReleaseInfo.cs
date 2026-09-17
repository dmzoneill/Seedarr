using System.Collections.Generic;

namespace NzbDrone.Core.Notifications;

public class EpisodicReleaseInfo
{
    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public int? EndingEpisodeNumber { get; set; }

    public List<int> EpisodeNumbers { get; set; } = new List<int>();

    public string EpisodeTitle { get; set; }

    public bool IsSeasonPack { get; set; }

    public string AirDate { get; set; }

    public int? AbsoluteEpisodeNumber { get; set; }
}
