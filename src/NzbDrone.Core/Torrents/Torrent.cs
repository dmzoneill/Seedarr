using System;
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
    public int TagIds { get; set; }
}

public enum TorrentStatus
{
    Stopped = 0,
    Seeding = 1,
    Paused = 2,
    Error = 3,
    Queued = 4
}
