using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public class TorrentAddedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public TorrentAddedEvent()
    {
    }

    public TorrentAddedEvent(Torrent torrent)
    {
        this.Torrent = torrent;
    }
}

public class TorrentUpdatedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public TorrentUpdatedEvent()
    {
    }

    public TorrentUpdatedEvent(Torrent torrent)
    {
        this.Torrent = torrent;
    }
}

public class TorrentDeletedEvent : IEvent
{
    public int TorrentId { get; set; }

    public Torrent Torrent { get; set; }

    public bool DeleteFiles { get; set; }

    public TorrentDeletedEvent()
    {
    }

    public TorrentDeletedEvent(int torrentId, Torrent torrent = null)
    {
        this.TorrentId = torrentId;
        this.Torrent = torrent;
    }
}

public class TorrentStatusChangedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public TorrentStatus OldStatus { get; set; }

    public TorrentStatus NewStatus { get; set; }

    public bool IsQueueManagerInternal { get; set; }

    public TorrentStatusChangedEvent()
    {
    }

    public TorrentStatusChangedEvent(Torrent torrent, TorrentStatus oldStatus, TorrentStatus newStatus)
    {
        this.Torrent = torrent;
        this.OldStatus = oldStatus;
        this.NewStatus = newStatus;
    }
}

public class TorrentDownloadCompletedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public TorrentDownloadCompletedEvent()
    {
    }

    public TorrentDownloadCompletedEvent(Torrent torrent)
    {
        this.Torrent = torrent;
    }
}

public class TorrentSeedGoalReachedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public TorrentSeedGoalReachedEvent()
    {
    }

    public TorrentSeedGoalReachedEvent(Torrent torrent)
    {
        this.Torrent = torrent;
    }
}

public class HealthIssueEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public int TorrentId { get; set; }

    public string Source { get; set; }

    public string Message { get; set; }

    public bool IsResolved { get; set; }

    public HealthIssueEvent()
    {
    }

    public HealthIssueEvent(Torrent torrent, string source, string message, bool isResolved = false)
    {
        this.Torrent = torrent;
        this.TorrentId = torrent?.Id ?? 0;
        this.Source = source;
        this.Message = message;
        this.IsResolved = isResolved;
    }

    public HealthIssueEvent(int torrentId, string source, string message, bool isResolved = false)
    {
        this.TorrentId = torrentId;
        this.Source = source;
        this.Message = message;
        this.IsResolved = isResolved;
    }
}

public class ArchiveExtractionCompletedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public ArchiveExtractionCompletedEvent()
    {
    }

    public ArchiveExtractionCompletedEvent(Torrent torrent)
    {
        this.Torrent = torrent;
    }
}

public class ArchiveExtractionFailedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public string ErrorMessage { get; set; }

    public ArchiveExtractionFailedEvent()
    {
    }

    public ArchiveExtractionFailedEvent(Torrent torrent, string errorMessage = null)
    {
        this.Torrent = torrent;
        this.ErrorMessage = errorMessage;
    }
}

public class VpnKillSwitchTriggeredEvent : IEvent
{
    public string InterfaceName { get; set; }

    public VpnKillSwitchTriggeredEvent()
    {
    }

    public VpnKillSwitchTriggeredEvent(string interfaceName)
    {
        this.InterfaceName = interfaceName;
    }
}

public class ApplicationUpdatedEvent : IEvent
{
    public string PreviousVersion { get; set; }

    public string NewVersion { get; set; }

    public ApplicationUpdatedEvent()
    {
    }

    public ApplicationUpdatedEvent(string previousVersion, string newVersion)
    {
        this.PreviousVersion = previousVersion;
        this.NewVersion = newVersion;
    }
}
