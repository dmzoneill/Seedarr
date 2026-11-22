using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

/// <summary>
/// In-memory torrent store whose <see cref="Update"/> / <see cref="UpdateMany"/> replace every
/// persisted column, matching <c>BasicRepository.Update</c>. Reads return detached copies so a
/// caller can hold a stale snapshot while another writer mutates the row.
/// </summary>
internal sealed class FullRowTorrentStore : ITorrentService
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Torrent> _rows = new();

    public Action AfterSnapshot { get; set; }

    public int FullRowWrites { get; private set; }

    public void Seed(Torrent torrent)
    {
        lock (_gate)
        {
            _rows[torrent.Id] = Copy(torrent);
        }
    }

    public void Mutate(int id, Action<Torrent> mutate)
    {
        lock (_gate)
        {
            mutate(_rows[id]);
        }
    }

    public List<Torrent> GetAll()
    {
        List<Torrent> snapshot;
        lock (_gate)
        {
            snapshot = _rows.Values.Select(Copy).ToList();
        }

        AfterSnapshot?.Invoke();
        return snapshot;
    }

    public List<Torrent> GetByInfoHashes(IEnumerable<string> infoHashes)
    {
        var hashes = new HashSet<string>(infoHashes ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            return _rows.Values.Where(t => hashes.Contains(t.InfoHash)).Select(Copy).ToList();
        }
    }

    public Torrent Get(int id)
    {
        lock (_gate)
        {
            return _rows.TryGetValue(id, out var row) ? Copy(row) : null;
        }
    }

    public Torrent GetByInfoHash(string infoHash) => FindByInfoHash(infoHash);

    public Torrent FindByInfoHash(string infoHash)
    {
        lock (_gate)
        {
            return _rows.Values.FirstOrDefault(t => string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase)) is { } row
                ? Copy(row)
                : null;
        }
    }

    public bool ExistsByInfoHash(string infoHash) => FindByInfoHash(infoHash) != null;

    public Torrent Add(Torrent torrent)
    {
        lock (_gate)
        {
            _rows[torrent.Id] = Copy(torrent);
            return Copy(torrent);
        }
    }

    public Torrent Update(Torrent torrent)
    {
        lock (_gate)
        {
            FullRowWrites++;
            _rows[torrent.Id] = Copy(torrent);
            return Copy(torrent);
        }
    }

    public void UpdateMany(IEnumerable<Torrent> torrents)
    {
        foreach (var torrent in torrents ?? Enumerable.Empty<Torrent>())
        {
            Update(torrent);
        }
    }

    public Torrent UpdateUserFields(int id, Torrent updates) => throw new NotSupportedException();

    public Torrent UpdateUserFields(Torrent torrent) => throw new NotSupportedException();

    public void Delete(int id, bool deleteFiles = false)
    {
        lock (_gate)
        {
            _rows.Remove(id);
        }
    }

    public void DeleteMany(List<int> torrentIds, bool deleteFiles = false)
    {
        foreach (var id in torrentIds ?? new List<int>())
        {
            Delete(id);
        }
    }

    public Torrent Recheck(int id) => Get(id);

    public void MoveQueue(int id, string position) => throw new NotSupportedException();

    public void BatchMoveQueue(IEnumerable<int> ids, string position) => throw new NotSupportedException();

    public Torrent Start(int id) => throw new NotSupportedException();

    public Torrent Pause(int id, string reason = null)
    {
        lock (_gate)
        {
            var row = _rows[id];
            row.Status = TorrentStatus.Paused;
            row.Active = false;
            row.UploadSpeed = 0;
            row.DownloadSpeed = 0;
            return Copy(row);
        }
    }

    public int UpdateRuntimeStats(IEnumerable<TorrentRuntimeWrite> writes)
    {
        var applied = 0;
        lock (_gate)
        {
            foreach (var write in writes ?? Enumerable.Empty<TorrentRuntimeWrite>())
            {
                if (!_rows.TryGetValue(write.Id, out var row) || (int)row.Status != write.ExpectedStatus)
                {
                    continue;
                }

                if (write.Uploaded >= row.Uploaded)
                {
                    row.SessionUploaded = write.SessionUploaded;
                }

                if (write.Downloaded >= row.Downloaded)
                {
                    row.SessionDownloaded = write.SessionDownloaded;
                }

                row.Uploaded = Math.Max(row.Uploaded, write.Uploaded);
                row.Downloaded = Math.Max(row.Downloaded, write.Downloaded);
                row.SeedingTime = Math.Max(row.SeedingTime, write.SeedingTime);
                row.UpdateRatio();
                row.UploadSpeed = write.UploadSpeed;
                row.DownloadSpeed = write.DownloadSpeed;
                row.Progress = write.Progress;
                row.Eta = write.Eta;
                row.LastActive = write.LastActive;
                row.Active = write.Active;
                row.Seeders = write.Seeders;
                row.Leechers = write.Leechers;
                row.Availability = write.Availability;
                if (row.Threshold == 0 && write.Threshold != 0)
                {
                    row.Threshold = write.Threshold;
                }

                applied++;
            }
        }

        return applied;
    }

    public int TryTransitionStatuses(IEnumerable<TorrentStatusTransition> transitions)
    {
        var applied = 0;
        lock (_gate)
        {
            foreach (var transition in transitions ?? Enumerable.Empty<TorrentStatusTransition>())
            {
                if (!_rows.TryGetValue(transition.Id, out var row) || (int)row.Status != transition.ExpectedStatus)
                {
                    continue;
                }

                row.Status = (TorrentStatus)transition.Status;
                row.Active = transition.Active;
                row.UploadSpeed = transition.UploadSpeed;
                row.DownloadSpeed = transition.DownloadSpeed;
                if (transition.LastActive.HasValue)
                {
                    row.LastActive = transition.LastActive;
                }

                applied++;
            }
        }

        return applied;
    }

    public void ClearActivity(IEnumerable<int> torrentIds)
    {
        lock (_gate)
        {
            foreach (var id in torrentIds ?? Enumerable.Empty<int>())
            {
                if (_rows.TryGetValue(id, out var row))
                {
                    row.UploadSpeed = 0;
                    row.DownloadSpeed = 0;
                    row.Active = false;
                }
            }
        }
    }

    public void UpdateSwarmStats(int torrentId, int seeders, int leechers, double? availability)
    {
        lock (_gate)
        {
            if (!_rows.TryGetValue(torrentId, out var row))
            {
                return;
            }

            row.Seeders = seeders;
            row.Leechers = leechers;
            if (availability.HasValue)
            {
                row.Availability = availability.Value;
            }
        }
    }

    public void UpdateExecutionStates(IEnumerable<TorrentExecutionWrite> writes)
    {
        lock (_gate)
        {
            foreach (var write in writes ?? Enumerable.Empty<TorrentExecutionWrite>())
            {
                if (!_rows.TryGetValue(write.Id, out var row))
                {
                    continue;
                }

                row.Status = (TorrentStatus)write.Status;
                row.Active = write.Active;
                row.UploadSpeed = write.UploadSpeed;
                row.DownloadSpeed = write.DownloadSpeed;
                if (write.SetLastActive == 1)
                {
                    row.LastActive = write.LastActive;
                }

                if (write.SetIsVpnPaused == 1)
                {
                    row.IsVpnPaused = write.IsVpnPaused;
                }

                if (write.SetForceStart == 1)
                {
                    row.ForceStart = write.ForceStart;
                }
            }
        }
    }

    public void UpdateSuperSeeding(int torrentId, bool superSeeding)
    {
        lock (_gate)
        {
            if (_rows.TryGetValue(torrentId, out var row))
            {
                row.SuperSeeding = superSeeding;
            }
        }
    }

    internal static Torrent Copy(Torrent source)
    {
        return new Torrent
        {
            Id = source.Id,
            Name = source.Name,
            InfoHash = source.InfoHash,
            InfoHashV2 = source.InfoHashV2,
            TotalSize = source.TotalSize,
            PieceCount = source.PieceCount,
            PieceLength = source.PieceLength,
            Comment = source.Comment,
            CreatedBy = source.CreatedBy,
            CreationDate = source.CreationDate,
            IsPrivate = source.IsPrivate,
            Status = source.Status,
            Uploaded = source.Uploaded,
            Downloaded = source.Downloaded,
            Ratio = source.Ratio,
            Seeders = source.Seeders,
            Leechers = source.Leechers,
            TrackerUrl = source.TrackerUrl,
            SourcePath = source.SourcePath,
            DateAdded = source.DateAdded,
            LastActive = source.LastActive,
            TagIds = source.TagIds != null ? new List<int>(source.TagIds) : new List<int>(),
            Priority = source.Priority,
            UploadLimit = source.UploadLimit,
            DownloadLimit = source.DownloadLimit,
            SuperSeeding = source.SuperSeeding,
            ForceStart = source.ForceStart,
            Label = source.Label,
            Category = source.Category,
            SavePath = source.SavePath,
            Progress = source.Progress,
            SequentialDownload = source.SequentialDownload,
            FirstLastPiecePrio = source.FirstLastPiecePrio,
            AnnounceInterval = source.AnnounceInterval,
            NextUpdate = source.NextUpdate,
            SessionUploaded = source.SessionUploaded,
            SessionDownloaded = source.SessionDownloaded,
            SmallTorrentLimit = source.SmallTorrentLimit,
            Threshold = source.Threshold,
            UploadSpeed = source.UploadSpeed,
            DownloadSpeed = source.DownloadSpeed,
            Active = source.Active,
            Availability = source.Availability,
            Eta = source.Eta,
            SortOrder = source.SortOrder,
            ForceCompleted = source.ForceCompleted,
            SeedingTime = source.SeedingTime,
            RatioLimit = source.RatioLimit,
            SeedingTimeLimit = source.SeedingTimeLimit,
            IsVpnPaused = source.IsVpnPaused,
            DownloadClientId = source.DownloadClientId
        };
    }
}
