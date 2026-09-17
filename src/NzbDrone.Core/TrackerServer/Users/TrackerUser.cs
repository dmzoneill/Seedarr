using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.TrackerServer.Users;

public class TrackerUser : ModelBase
{
    public string Passkey { get; set; }

    public string Username { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsBanned { get; set; }

    public long Uploaded { get; set; }

    public long Downloaded { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastAnnounceAt { get; set; }
}
