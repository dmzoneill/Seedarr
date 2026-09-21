using System;

namespace NzbDrone.Core.Simulation.ClientBehavior;

public class TorrentClientSession
{
    public string ProfileName { get; set; }

    public string PeerId { get; set; }

    public string AnnounceKey { get; set; }

    public DateTime CreatedAt { get; set; }

    public IClientProfile Profile { get; set; }
}
