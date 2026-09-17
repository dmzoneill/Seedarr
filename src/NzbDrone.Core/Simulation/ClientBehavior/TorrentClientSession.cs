using System;

namespace NzbDrone.Core.Simulation.ClientBehavior;

public class TorrentClientSession
{
    public string ProfileName { get; init; }

    public string PeerId { get; init; }

    public string AnnounceKey { get; init; }

    public DateTime CreatedAt { get; init; }

    public IClientProfile Profile { get; init; }
}
