using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Trackers;

public class TrackerAnnounceEvent : IEvent
{
    public Torrent Torrent { get; set; }
    public string TrackerUrl { get; set; }
    public int Seeders { get; set; }
    public int Leechers { get; set; }
    public int PeersCount { get; set; }
    public long ResponseTimeMs { get; set; }
    public bool IsSuccess { get; set; }
    public string ErrorMessage { get; set; }

    public int TrackerId { get; set; }
    public TrackerStatus Status { get; set; }

    public TrackerAnnounceEvent()
    {
    }

    public TrackerAnnounceEvent(Torrent torrent, string trackerUrl, int seeders, int leechers, int peersCount, long responseTimeMs, bool isSuccess, string errorMessage = null, int trackerId = 0, TrackerStatus status = TrackerStatus.Unknown)
    {
        Torrent = torrent;
        TrackerUrl = trackerUrl;
        Seeders = seeders;
        Leechers = leechers;
        PeersCount = peersCount;
        ResponseTimeMs = responseTimeMs;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        TrackerId = trackerId;
        Status = status;
    }
}

public class TrackerScrapeEvent : IEvent
{
    public string TrackerUrl { get; set; }
    public int Seeders { get; set; }
    public int Leechers { get; set; }
    public int Completed { get; set; }
    public long ResponseTimeMs { get; set; }
    public bool IsSuccess { get; set; }
    public string ErrorMessage { get; set; }

    public TrackerScrapeEvent()
    {
    }

    public TrackerScrapeEvent(string trackerUrl, int seeders, int leechers, int completed, long responseTimeMs, bool isSuccess, string errorMessage = null)
    {
        TrackerUrl = trackerUrl;
        Seeders = seeders;
        Leechers = leechers;
        Completed = completed;
        ResponseTimeMs = responseTimeMs;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }
}

public class TrackerMetricUpdatedEvent : IEvent
{
    public string TrackerUrl { get; set; }
    public long TotalAnnounces { get; set; }
    public long TotalUploaded { get; set; }
    public long TotalDownloaded { get; set; }
    public string Status { get; set; }

    public TrackerMetricUpdatedEvent()
    {
    }

    public TrackerMetricUpdatedEvent(string trackerUrl, long totalAnnounces, long totalUploaded, long totalDownloaded, string status)
    {
        TrackerUrl = trackerUrl;
        TotalAnnounces = totalAnnounces;
        TotalUploaded = totalUploaded;
        TotalDownloaded = totalDownloaded;
        Status = status;
    }
}

public class TrackerWarningEvent : IEvent
{
    public Torrent Torrent { get; set; }
    public string TrackerUrl { get; set; }
    public string WarningMessage { get; set; }

    public TrackerWarningEvent()
    {
    }

    public TrackerWarningEvent(Torrent torrent, string trackerUrl, string warningMessage)
    {
        Torrent = torrent;
        TrackerUrl = trackerUrl;
        WarningMessage = warningMessage;
    }
}

public class TrackerStatusChangedEvent : IEvent
{
    public Torrent Torrent { get; set; }
    public TrackerEntry Tracker { get; set; }
    public TrackerStatus PreviousStatus { get; set; }
    public TrackerStatus NewStatus { get; set; }

    public TrackerStatusChangedEvent()
    {
    }

    public TrackerStatusChangedEvent(Torrent torrent, TrackerEntry tracker, TrackerStatus previousStatus, TrackerStatus newStatus)
    {
        Torrent = torrent;
        Tracker = tracker;
        PreviousStatus = previousStatus;
        NewStatus = newStatus;
    }
}
