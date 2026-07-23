using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class Torrent : ModelBase
{
    public string Name { get; set; }
    public string InfoHash { get; set; }
    public long TotalSize { get; set; }
    public int PieceCount { get; set; }
    public int PieceLength { get; set; }
    public string Comment { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? CreationDate { get; set; }
    public bool IsPrivate { get; set; }
    public TorrentStatus Status { get; set; }
    public long Uploaded { get; set; }
    public long Downloaded { get; set; }
    public double Ratio { get; set; }
    public int Seeders { get; set; }
    public int Leechers { get; set; }
    public string TrackerUrl { get; set; }
    public string SourcePath { get; set; }
    public DateTime DateAdded { get; set; }
    public DateTime? LastActive { get; set; }
    public List<int> TagIds { get; set; } = new();
    public int Priority { get; set; }
    public int UploadLimit { get; set; }
    public int DownloadLimit { get; set; }
    public bool SuperSeeding { get; set; }
    public bool ForceStart { get; set; }
    public string Label { get; set; }
    public double Progress { get; set; }
    public bool SequentialDownload { get; set; }
    public int AnnounceInterval { get; set; }
    public int NextUpdate { get; set; }
    public long SessionUploaded { get; set; }
    public long SessionDownloaded { get; set; }
    public long SmallTorrentLimit { get; set; }
    public int Threshold { get; set; }
    public long UploadSpeed { get; set; }
    public long DownloadSpeed { get; set; }
    public bool Active { get; set; }
    public double Availability { get; set; }
    public int Eta { get; set; }
    public int SortOrder { get; set; }
    public bool ForceCompleted { get; set; }
}

public enum TorrentStatus
{
    Stopped = 0,
    Seeding = 1,
    Paused = 2,
    Error = 3,
    Queued = 4,
    Downloading = 5
}
