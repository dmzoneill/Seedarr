using System;

namespace NzbDrone.Core.Torrents;

/// <summary>
/// Engine-owned counters and gauges for one torrent. Counters are applied monotonically and only
/// while the row is still in <see cref="ExpectedStatus"/>, so a concurrent pause is left alone.
/// </summary>
public sealed class TorrentRuntimeWrite
{
    public int Id { get; init; }

    public int ExpectedStatus { get; init; }

    public long Uploaded { get; init; }

    public long Downloaded { get; init; }

    public long SessionUploaded { get; init; }

    public long SessionDownloaded { get; init; }

    public long UploadSpeed { get; init; }

    public long DownloadSpeed { get; init; }

    public double Progress { get; init; }

    public int Eta { get; init; }

    public long SeedingTime { get; init; }

    public DateTime? LastActive { get; init; }

    public bool Active { get; init; }

    public int Seeders { get; init; }

    public int Leechers { get; init; }

    public double Availability { get; init; }

    public int Threshold { get; init; }
}

/// <summary>
/// Status change that is stored only when the row is still in <see cref="ExpectedStatus"/>.
/// </summary>
public sealed class TorrentStatusTransition
{
    public int Id { get; init; }

    public int ExpectedStatus { get; init; }

    public int Status { get; init; }

    public bool Active { get; init; }

    public long UploadSpeed { get; init; }

    public long DownloadSpeed { get; init; }

    public DateTime? LastActive { get; init; }
}

/// <summary>
/// User or safety pause/resume. Writes execution columns only; counters and labels stay put.
/// Flag fields use 0/1 so the SQL stays valid on both SQLite and Postgres.
/// </summary>
public sealed class TorrentExecutionWrite
{
    public int Id { get; init; }

    public int Status { get; init; }

    public bool Active { get; init; }

    public long UploadSpeed { get; init; }

    public long DownloadSpeed { get; init; }

    public int SetLastActive { get; init; }

    public DateTime? LastActive { get; init; }

    public int SetIsVpnPaused { get; init; }

    public bool IsVpnPaused { get; init; }

    public int SetForceStart { get; init; }

    public bool ForceStart { get; init; }
}
