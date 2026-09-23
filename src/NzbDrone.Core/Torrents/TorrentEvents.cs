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

    public string ErrorMessage { get; set; }

    public string Reason
    {
        get => this.ErrorMessage;
        set => this.ErrorMessage = value;
    }

    public TorrentStatusChangedEvent()
    {
    }

    public TorrentStatusChangedEvent(Torrent torrent, TorrentStatus oldStatus, TorrentStatus newStatus, string errorMessage = null)
    {
        this.Torrent = torrent;
        this.OldStatus = oldStatus;
        this.NewStatus = newStatus;
        this.ErrorMessage = errorMessage;
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

public class TorrentFinishedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public TorrentFinishedEvent()
    {
    }

    public TorrentFinishedEvent(Torrent torrent)
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

public class TorrentRatioReachedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public double Ratio { get; set; }

    public TorrentRatioReachedEvent()
    {
    }

    public TorrentRatioReachedEvent(Torrent torrent, double ratio = 0)
    {
        this.Torrent = torrent;
        this.Ratio = ratio > 0 ? ratio : torrent?.Ratio ?? 0;
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

public class TorrentStartedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public TorrentStartedEvent()
    {
    }

    public TorrentStartedEvent(Torrent torrent)
    {
        this.Torrent = torrent;
    }
}

public class TorrentPausedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public TorrentPausedEvent()
    {
    }

    public TorrentPausedEvent(Torrent torrent)
    {
        this.Torrent = torrent;
    }
}

public class TorrentStalledEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public int StalledMinutes { get; set; }

    public TorrentStalledEvent()
    {
    }

    public TorrentStalledEvent(Torrent torrent, int stalledMinutes = 0)
    {
        this.Torrent = torrent;
        this.StalledMinutes = stalledMinutes;
    }
}

public class TorrentStallResolvedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public TorrentStallResolvedEvent()
    {
    }

    public TorrentStallResolvedEvent(Torrent torrent)
    {
        this.Torrent = torrent;
    }
}

public class TorrentPieceExtinctionEvent : IEvent
{
    public Torrent Torrent { get; set; }
    public int ExtinctPieceCount { get; set; }
    public int TotalPieces { get; set; }

    public TorrentPieceExtinctionEvent()
    {
    }

    public TorrentPieceExtinctionEvent(Torrent torrent, int extinctPieceCount, int totalPieces)
    {
        this.Torrent = torrent;
        this.ExtinctPieceCount = extinctPieceCount;
        this.TotalPieces = totalPieces;
    }
}

public class TorrentSeedingTimeReachedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public System.TimeSpan SeedingTime { get; set; }

    public TorrentSeedingTimeReachedEvent()
    {
    }

    public TorrentSeedingTimeReachedEvent(Torrent torrent, System.TimeSpan seedingTime)
    {
        this.Torrent = torrent;
        this.SeedingTime = seedingTime;
    }
}

public class TorrentHashCheckCompletedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public bool IsSuccessful { get; set; }

    public TorrentHashCheckCompletedEvent()
    {
    }

    public TorrentHashCheckCompletedEvent(Torrent torrent, bool isSuccessful = true)
    {
        this.Torrent = torrent;
        this.IsSuccessful = isSuccessful;
    }
}

public class TorrentProgressMilestoneEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public double MilestonePercent { get; set; }

    public TorrentProgressMilestoneEvent()
    {
    }

    public TorrentProgressMilestoneEvent(Torrent torrent, double milestonePercent)
    {
        this.Torrent = torrent;
        this.MilestonePercent = milestonePercent;
    }
}

public class SpeedThresholdExceededEvent : IEvent
{
    public long DownloadSpeed { get; set; }

    public long UploadSpeed { get; set; }

    public int ActiveTorrents { get; set; }

    public SpeedThresholdExceededEvent()
    {
    }

    public SpeedThresholdExceededEvent(long downloadSpeed, long uploadSpeed, int activeTorrents)
    {
        this.DownloadSpeed = downloadSpeed;
        this.UploadSpeed = uploadSpeed;
        this.ActiveTorrents = activeTorrents;
    }
}

public class SpeedThresholdDroppedEvent : IEvent
{
    public long CurrentSpeed { get; set; }

    public long ExpectedMinimumSpeed { get; set; }

    public int ActiveTorrents { get; set; }

    public SpeedThresholdDroppedEvent()
    {
    }

    public SpeedThresholdDroppedEvent(long currentSpeed, long expectedMinimumSpeed, int activeTorrents)
    {
        this.CurrentSpeed = currentSpeed;
        this.ExpectedMinimumSpeed = expectedMinimumSpeed;
        this.ActiveTorrents = activeTorrents;
    }
}

public class BandwidthQuotaApproachingEvent : IEvent
{
    public long BytesUsed { get; set; }

    public long QuotaLimitBytes { get; set; }

    public double PercentageUsed { get; set; }

    public BandwidthQuotaApproachingEvent()
    {
    }

    public BandwidthQuotaApproachingEvent(long bytesUsed, long quotaLimitBytes, double percentageUsed)
    {
        this.BytesUsed = bytesUsed;
        this.QuotaLimitBytes = quotaLimitBytes;
        this.PercentageUsed = percentageUsed;
    }
}

public class PortForwardingFailedEvent : IEvent
{
    public int Port { get; set; }

    public string Protocol { get; set; }

    public string ErrorMessage { get; set; }

    public PortForwardingFailedEvent()
    {
    }

    public PortForwardingFailedEvent(int port, string protocol, string errorMessage)
    {
        this.Port = port;
        this.Protocol = protocol;
        this.ErrorMessage = errorMessage;
    }
}

public class PeerBannedEvent : IEvent
{
    public string PeerIp { get; set; }

    public string Reason { get; set; }

    public string InfoHash { get; set; }

    public PeerBannedEvent()
    {
    }

    public PeerBannedEvent(string peerIp, string reason, string infoHash = "")
    {
        this.PeerIp = peerIp;
        this.Reason = reason;
        this.InfoHash = infoHash;
    }
}

public class TrackerUnreachableEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public string TrackerUrl { get; set; }

    public string ErrorMessage { get; set; }

    public TrackerUnreachableEvent()
    {
    }

    public TrackerUnreachableEvent(Torrent torrent, string trackerUrl, string errorMessage)
    {
        this.Torrent = torrent;
        this.TrackerUrl = trackerUrl;
        this.ErrorMessage = errorMessage;
    }
}

public class TrackerBoostAppliedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public int AddedTrackersCount { get; set; }

    public TrackerBoostAppliedEvent()
    {
    }

    public TrackerBoostAppliedEvent(Torrent torrent, int addedTrackersCount)
    {
        this.Torrent = torrent;
        this.AddedTrackersCount = addedTrackersCount;
    }
}

public class FileMoveFailedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public string SourcePath { get; set; }

    public string DestinationPath { get; set; }

    public string ErrorMessage { get; set; }

    public FileMoveFailedEvent()
    {
    }

    public FileMoveFailedEvent(Torrent torrent, string sourcePath, string destinationPath, string errorMessage)
    {
        this.Torrent = torrent;
        this.SourcePath = sourcePath;
        this.DestinationPath = destinationPath;
        this.ErrorMessage = errorMessage;
    }
}

public class FileMoveCompletedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public string SourcePath { get; set; }

    public string DestinationPath { get; set; }

    public FileMoveCompletedEvent()
    {
    }

    public FileMoveCompletedEvent(Torrent torrent, string sourcePath = null, string destinationPath = null)
    {
        this.Torrent = torrent;
        this.SourcePath = sourcePath;
        this.DestinationPath = destinationPath;
    }
}

public class MediaInspectionFailedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public string FilePath { get; set; }

    public string Reason { get; set; }

    public MediaInspectionFailedEvent()
    {
    }

    public MediaInspectionFailedEvent(Torrent torrent, string filePath, string reason)
    {
        this.Torrent = torrent;
        this.FilePath = filePath;
        this.Reason = reason;
    }
}

public class TorrentImportedEvent : IEvent
{
    public Torrent Torrent { get; set; }
    public string ArrType { get; set; }
    public string ArrInstance { get; set; }

    public TorrentImportedEvent()
    {
    }

    public TorrentImportedEvent(Torrent torrent, string arrType = null, string arrInstance = null)
    {
        this.Torrent = torrent;
        this.ArrType = arrType;
        this.ArrInstance = arrInstance;
    }
}

public class ArrImportCompletedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public string ArrInstance { get; set; }

    public int ImportedFilesCount { get; set; }

    public ArrImportCompletedEvent()
    {
    }

    public ArrImportCompletedEvent(Torrent torrent, string arrInstance, int importedFilesCount)
    {
        this.Torrent = torrent;
        this.ArrInstance = arrInstance;
        this.ImportedFilesCount = importedFilesCount;
    }
}

public class TaskFailedEvent : IEvent
{
    public string TaskName { get; set; }

    public string ErrorMessage { get; set; }

    public TaskFailedEvent()
    {
    }

    public TaskFailedEvent(string taskName, string errorMessage)
    {
        this.TaskName = taskName;
        this.ErrorMessage = errorMessage;
    }
}

public class TorrentMetadataResolvedEvent : IEvent
{
    public Torrent Torrent { get; set; }

    public TorrentMetadataResolvedEvent()
    {
    }

    public TorrentMetadataResolvedEvent(Torrent torrent)
    {
        this.Torrent = torrent;
    }
}

public class TorrentStreamPlayheadMovedEvent : IEvent
{
    public int TorrentId { get; set; }

    public long ByteOffset { get; set; }

    public int HeadPiece { get; set; }

    public TorrentStreamPlayheadMovedEvent()
    {
    }

    public TorrentStreamPlayheadMovedEvent(int torrentId, long byteOffset, int headPiece)
    {
        this.TorrentId = torrentId;
        this.ByteOffset = byteOffset;
        this.HeadPiece = headPiece;
    }
}

public class TorrentRelocationProgressEvent : IEvent
{
    public int TorrentId { get; set; }

    public long BytesTransferred { get; set; }

    public long TotalBytes { get; set; }

    public double Progress { get; set; }

    public double BytesPerSecond { get; set; }

    public string CurrentFilePath { get; set; }

    public int CurrentFileIndex { get; set; }

    public int TotalFiles { get; set; }

    public bool IsComplete { get; set; }

    public string ErrorMessage { get; set; }

    public TorrentRelocationProgressEvent()
    {
    }

    public TorrentRelocationProgressEvent(
        int torrentId,
        long bytesTransferred,
        long totalBytes,
        double progress,
        double bytesPerSecond = 0,
        string currentFilePath = null,
        int currentFileIndex = 0,
        int totalFiles = 0,
        bool isComplete = false,
        string errorMessage = null)
    {
        this.TorrentId = torrentId;
        this.BytesTransferred = bytesTransferred;
        this.TotalBytes = totalBytes;
        this.Progress = progress;
        this.BytesPerSecond = bytesPerSecond;
        this.CurrentFilePath = currentFilePath;
        this.CurrentFileIndex = currentFileIndex;
        this.TotalFiles = totalFiles;
        this.IsComplete = isComplete;
        this.ErrorMessage = errorMessage;
    }
}

public class TorrentRecheckProgressEvent : IEvent
{
    public int TorrentId { get; set; }

    public double Progress { get; set; }

    public int CheckedPieces { get; set; }

    public int TotalPieces { get; set; }

    public TorrentRecheckProgressEvent()
    {
    }

    public TorrentRecheckProgressEvent(int torrentId, double progress, int checkedPieces, int totalPieces)
    {
        this.TorrentId = torrentId;
        this.Progress = progress;
        this.CheckedPieces = checkedPieces;
        this.TotalPieces = totalPieces;
    }
}
