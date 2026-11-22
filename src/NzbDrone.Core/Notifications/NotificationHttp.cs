using System;

namespace NzbDrone.Core.Notifications;

public static class NotificationHttp
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
}
