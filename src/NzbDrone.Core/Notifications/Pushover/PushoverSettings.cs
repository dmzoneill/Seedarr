namespace NzbDrone.Core.Notifications.Pushover;

public class PushoverSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string UserKey { get; set; } = string.Empty;
    public int Priority { get; set; }
    public int RetrySeconds { get; set; } = 60;
    public int ExpireSeconds { get; set; } = 3600;
    public string Device { get; set; }
    public string Sound { get; set; }

    public string ApiToken
    {
        get => ApiKey;
        set => ApiKey = value;
    }
}
