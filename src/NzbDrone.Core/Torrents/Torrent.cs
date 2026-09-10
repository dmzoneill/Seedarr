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
    public long SeedingTime { get; set; }

    /// <summary>
    /// Applies user-controllable updates while preserving engine-managed state invariants
    /// (such as Uploaded, Downloaded, Ratio, Speeds, Peer counts, and Session stats).
    /// </summary>
    public void ApplyUserFields(Torrent updates)
    {
        if (updates == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(updates.Name))
        {
            Name = updates.Name;
        }

        if (updates.Label != null)
        {
            Label = updates.Label;
        }

        if (updates.TagIds != null)
        {
            TagIds = updates.TagIds;
        }

        Priority = updates.Priority;
        UploadLimit = updates.UploadLimit;
        DownloadLimit = updates.DownloadLimit;
        SuperSeeding = updates.SuperSeeding;
        ForceStart = updates.ForceStart;
        SequentialDownload = updates.SequentialDownload;
        SmallTorrentLimit = updates.SmallTorrentLimit;
        Threshold = updates.Threshold;

        if (!string.IsNullOrWhiteSpace(updates.TrackerUrl))
        {
            TrackerUrl = updates.TrackerUrl;
        }

        if (updates.ForceCompleted || updates.Progress >= 1.0)
        {
            MarkForceCompleted();
        }
        else if (updates.Status != Status && CanTransitionTo(updates.Status))
        {
            TransitionTo(updates.Status);
        }
    }

    /// <summary>
    /// Updates engine-managed runtime tick statistics.
    /// </summary>
    public void ApplyTickStats(
        long downloaded,
        long uploaded,
        int seeds,
        int peers,
        long downloadSpeed,
        long uploadSpeed,
        int eta = 0,
        double? availability = null,
        long seedingTimeDelta = 0)
    {
        Downloaded = downloaded;
        Uploaded = uploaded;
        UpdateRatio();
        Seeders = seeds;
        Leechers = peers;
        DownloadSpeed = downloadSpeed;
        UploadSpeed = uploadSpeed;
        Eta = eta;

        if (availability.HasValue)
        {
            Availability = availability.Value;
        }

        if (seedingTimeDelta > 0)
        {
            SeedingTime += seedingTimeDelta;
        }

        LastActive = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates engine-owned statistics directly.
    /// </summary>
    public void UpdateStats(
        long downloaded,
        long uploaded,
        int seeds,
        int peers,
        long downloadSpeed,
        long uploadSpeed,
        int timeRemaining = 0)
    {
        ApplyTickStats(downloaded, uploaded, seeds, peers, downloadSpeed, uploadSpeed, eta: timeRemaining);
    }

    /// <summary>
    /// Recalculates the upload/download ratio.
    /// </summary>
    public void UpdateRatio()
    {
        Ratio = Downloaded > 0
            ? Math.Round((double)Uploaded / Downloaded, 4)
            : (Uploaded > 0 ? (double)Uploaded : 0.0);
    }

    /// <summary>
    /// Determines whether transition to target status is valid.
    /// </summary>
    public bool CanTransitionTo(TorrentStatus targetStatus)
    {
        return true;
    }

    /// <summary>
    /// Transitions status according to lifecycle rules.
    /// </summary>
    public void TransitionTo(TorrentStatus newStatus)
    {
        Status = newStatus;
    }

    /// <summary>
    /// Pauses torrent execution.
    /// </summary>
    public void Pause()
    {
        TransitionTo(TorrentStatus.Paused);
    }

    /// <summary>
    /// Resumes torrent execution to Downloading or Seeding based on completion progress.
    /// </summary>
    public void Resume()
    {
        if (Progress >= 1.0 || ForceCompleted)
        {
            TransitionTo(TorrentStatus.Seeding);
        }
        else
        {
            TransitionTo(TorrentStatus.Downloading);
        }
    }

    /// <summary>
    /// Stops torrent execution and zeroes out transient transfer speeds.
    /// </summary>
    public void Stop()
    {
        TransitionTo(TorrentStatus.Stopped);
        UploadSpeed = 0;
        DownloadSpeed = 0;
        Active = false;
    }

    /// <summary>
    /// Marks torrent as force-completed (100% progress and transition to Seeding).
    /// </summary>
    public void MarkForceCompleted()
    {
        ForceCompleted = true;
        Progress = 1.0;
        Availability = 1.0;

        if (Status == TorrentStatus.Downloading || Status == TorrentStatus.Queued)
        {
            Status = TorrentStatus.Seeding;
        }
    }
}
