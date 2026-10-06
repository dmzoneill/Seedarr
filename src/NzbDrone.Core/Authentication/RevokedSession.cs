using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public class RevokedSession : ModelBase
{
    public string SessionKey { get; set; }

    public DateTime RevokedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }
}
