using System;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;

namespace Seedarr.Api.V1.Torrents;

public static class TorrentResourceMapper
{
    public static TorrentResource ToResource(Torrent model)
    {
        return new TorrentResource
        {
            Id = model.Id,
            Name = model.Name,
            InfoHash = model.InfoHash,
            TotalSize = model.TotalSize,
            PieceCount = model.PieceCount,
            PieceLength = model.PieceLength,
            Comment = model.Comment,
            CreatedBy = model.CreatedBy,
            CreationDate = model.CreationDate,
            IsPrivate = model.IsPrivate,
            Status = model.Status.ToString(),
            Uploaded = model.Uploaded,
            Downloaded = model.Downloaded,
            Ratio = model.Ratio,
            Seeders = model.Seeders,
            Leechers = model.Leechers,
            TrackerUrl = model.TrackerUrl,
            DateAdded = model.DateAdded,
            LastActive = model.LastActive,
            Priority = model.Priority,
            UploadLimit = model.UploadLimit,
            DownloadLimit = model.DownloadLimit,
            SuperSeeding = model.SuperSeeding,
            ForceStart = model.ForceStart,
            Label = model.Label,
            Progress = model.Progress,
            SequentialDownload = model.SequentialDownload,
            AnnounceInterval = model.AnnounceInterval,
            NextUpdate = model.NextUpdate,
            SessionUploaded = model.SessionUploaded,
            SessionDownloaded = model.SessionDownloaded,
            SmallTorrentLimit = model.SmallTorrentLimit,
            Threshold = model.Threshold,
            UploadSpeed = model.UploadSpeed,
            DownloadSpeed = model.DownloadSpeed,
            Active = model.Active,
            Availability = model.Availability,
            Eta = model.Eta,
            SortOrder = model.SortOrder,
            ForceCompleted = model.ForceCompleted
        };
    }

    public static Torrent ToModel(TorrentResource resource)
    {
        return new Torrent
        {
            Id = resource.Id,
            Name = resource.Name,
            InfoHash = resource.InfoHash,
            TotalSize = resource.TotalSize,
            PieceCount = resource.PieceCount,
            PieceLength = resource.PieceLength,
            Comment = resource.Comment,
            CreatedBy = resource.CreatedBy,
            CreationDate = resource.CreationDate,
            IsPrivate = resource.IsPrivate,
            Status = Enum.TryParse<TorrentStatus>(resource.Status, true, out var status) ? status : TorrentStatus.Stopped,
            Uploaded = resource.Uploaded,
            Downloaded = resource.Downloaded,
            Ratio = resource.Ratio,
            Seeders = resource.Seeders,
            Leechers = resource.Leechers,
            TrackerUrl = resource.TrackerUrl,
            DateAdded = resource.DateAdded,
            LastActive = resource.LastActive,
            Priority = resource.Priority,
            UploadLimit = resource.UploadLimit,
            DownloadLimit = resource.DownloadLimit,
            SuperSeeding = resource.SuperSeeding,
            ForceStart = resource.ForceStart,
            Label = resource.Label,
            Progress = resource.Progress,
            SequentialDownload = resource.SequentialDownload,
            AnnounceInterval = resource.AnnounceInterval,
            NextUpdate = resource.NextUpdate,
            SessionUploaded = resource.SessionUploaded,
            SessionDownloaded = resource.SessionDownloaded,
            SmallTorrentLimit = resource.SmallTorrentLimit,
            Threshold = resource.Threshold,
            UploadSpeed = resource.UploadSpeed,
            DownloadSpeed = resource.DownloadSpeed,
            Active = resource.Active,
            Availability = resource.Availability,
            Eta = resource.Eta,
            SortOrder = resource.SortOrder,
            ForceCompleted = resource.ForceCompleted
        };
    }

    public static TorrentFileResource ToFileResource(TorrentFile model)
    {
        return new TorrentFileResource
        {
            Id = model.Id,
            TorrentId = model.TorrentId,
            Path = model.Path,
            Size = model.Size,
            PieceOffset = model.PieceOffset,
            PieceCount = model.PieceCount
        };
    }

    public static TrackerEntryResource ToTrackerResource(TrackerEntry model)
    {
        return new TrackerEntryResource
        {
            Id = model.Id,
            TorrentId = model.TorrentId,
            Url = model.Url,
            Tier = model.Tier,
            Status = model.Status.ToString(),
            Enabled = model.Enabled,
            Seeders = model.Seeders,
            Leechers = model.Leechers,
            Downloaded = model.Downloaded,
            TotalAnnounces = model.TotalAnnounces,
            SuccessfulAnnounces = model.SuccessfulAnnounces,
            ConsecutiveFailures = model.ConsecutiveFailures,
            LastResponseTime = model.LastResponseTime,
            AverageResponseTime = model.AverageResponseTime,
            AnnounceInterval = model.AnnounceInterval,
            MinAnnounceInterval = model.MinAnnounceInterval,
            LastAnnounce = model.LastAnnounce,
            LastScrape = model.LastScrape,
            NextAnnounce = model.NextAnnounce,
            ErrorMessage = model.ErrorMessage,
            LastErrorTime = model.LastErrorTime,
            WarningMessage = model.WarningMessage
        };
    }

    public static PeerResource ToPeerResource(PeerConnection connection, int id)
    {
        var flags = string.Empty;
        if (connection.IsEncrypted)
        {
            flags += "E";
        }

        if (connection.PeerInterested)
        {
            flags += "I";
        }

        if (!connection.AmChoking)
        {
            flags += "U";
        }

        return new PeerResource
        {
            Id = id,
            Ip = connection.RemoteIp,
            Port = connection.RemotePort,
            Client = connection.PeerId ?? string.Empty,
            UploadSpeed = 0,
            DownloadSpeed = 0,
            Uploaded = 0,
            Downloaded = 0,
            Progress = 0,
            Flags = flags
        };
    }
}
