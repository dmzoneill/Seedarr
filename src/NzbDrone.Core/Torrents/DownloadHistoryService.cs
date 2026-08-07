using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public class DownloadHistoryService : IDownloadHistoryService, IHandle<TorrentAddedEvent>, IHandle<TorrentDeletedEvent>
{
    private readonly IDownloadHistoryRepository _historyRepository;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITrackerEntryRepository _trackerEntryRepository;
    private readonly Logger _logger;

    public DownloadHistoryService(
        IDownloadHistoryRepository historyRepository,
        ITorrentRepository torrentRepository,
        ITrackerEntryRepository trackerEntryRepository)
    {
        _historyRepository = historyRepository;
        _torrentRepository = torrentRepository;
        _trackerEntryRepository = trackerEntryRepository;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public List<DownloadHistory> GetAll(string query = null, string status = null, int limit = 500)
    {
        return _historyRepository.GetHistory(query, status, limit);
    }

    public DownloadHistory Get(int id)
    {
        return _historyRepository.Get(id);
    }

    public DownloadHistory GetByInfoHash(string infoHash)
    {
        return _historyRepository.FindByInfoHash(infoHash);
    }

    public void Delete(int id)
    {
        _logger.Info("Deleting download history entry {0}", id);
        _historyRepository.Delete(id);
    }

    public void ClearAll()
    {
        _logger.Info("Clearing all download history entries");
        _historyRepository.DeleteAll();
    }

    public DownloadHistory RecordTorrentAdded(
        Torrent torrent,
        string source = null,
        string magnetUrl = null,
        string downloadUrl = null,
        string indexerName = null)
    {
        if (torrent == null)
        {
            return null;
        }

        var existing = !string.IsNullOrEmpty(torrent.InfoHash)
            ? _historyRepository.FindByInfoHash(torrent.InfoHash)
            : null;

        if (existing != null)
        {
            existing.TorrentId = torrent.Id;
            existing.Title = torrent.Name ?? existing.Title;
            existing.TotalSize = torrent.TotalSize > 0 ? torrent.TotalSize : existing.TotalSize;
            existing.PrimaryTracker = torrent.TrackerUrl ?? existing.PrimaryTracker;
            existing.Status = "Active";
            existing.DateRemoved = null;

            if (!string.IsNullOrEmpty(source))
            {
                existing.Source = source;
            }

            if (!string.IsNullOrEmpty(magnetUrl))
            {
                existing.MagnetUrl = magnetUrl;
            }

            if (!string.IsNullOrEmpty(downloadUrl))
            {
                existing.DownloadUrl = downloadUrl;
            }

            if (!string.IsNullOrEmpty(indexerName))
            {
                existing.IndexerName = indexerName;
            }

            _historyRepository.Update(existing);
            return existing;
        }

        var entry = new DownloadHistory
        {
            TorrentId = torrent.Id,
            Title = torrent.Name ?? "Unknown Release",
            InfoHash = torrent.InfoHash ?? string.Empty,
            TotalSize = torrent.TotalSize,
            DateAdded = torrent.DateAdded != default ? torrent.DateAdded : DateTime.UtcNow,
            DateCompleted = torrent.Progress >= 1.0 ? DateTime.UtcNow : null,
            DateRemoved = null,
            Uploaded = torrent.Uploaded,
            Downloaded = torrent.Downloaded,
            Ratio = torrent.Ratio,
            SeedingTime = torrent.SeedingTime,
            PrimaryTracker = torrent.TrackerUrl,
            IndexerName = indexerName,
            Source = source ?? "Manual",
            MagnetUrl = magnetUrl,
            DownloadUrl = downloadUrl,
            Status = "Active"
        };

        return _historyRepository.Insert(entry);
    }

    public void RecordTorrentUpdated(Torrent torrent)
    {
        if (torrent == null)
        {
            return;
        }

        var entry = _historyRepository.FindByTorrentId(torrent.Id)
            ?? (!string.IsNullOrEmpty(torrent.InfoHash) ? _historyRepository.FindByInfoHash(torrent.InfoHash) : null);

        if (entry == null)
        {
            return;
        }

        entry.Uploaded = torrent.Uploaded;
        entry.Downloaded = torrent.Downloaded;
        entry.Ratio = torrent.Ratio;
        entry.SeedingTime = torrent.SeedingTime;
        if (torrent.Progress >= 1.0 && entry.DateCompleted == null)
        {
            entry.DateCompleted = DateTime.UtcNow;
            entry.Status = "Completed";
        }

        _historyRepository.Update(entry);
    }

    public void RecordTorrentRemoved(Torrent torrent, string reason = "Deleted from library")
    {
        if (torrent == null)
        {
            return;
        }

        var entry = _historyRepository.FindByTorrentId(torrent.Id)
            ?? (!string.IsNullOrEmpty(torrent.InfoHash) ? _historyRepository.FindByInfoHash(torrent.InfoHash) : null);

        if (entry == null)
        {
            entry = new DownloadHistory
            {
                Title = torrent.Name ?? "Unknown Release",
                InfoHash = torrent.InfoHash ?? string.Empty,
                TotalSize = torrent.TotalSize,
                DateAdded = torrent.DateAdded != default ? torrent.DateAdded : DateTime.UtcNow,
                DateCompleted = torrent.Progress >= 1.0 ? DateTime.UtcNow : null,
                DateRemoved = DateTime.UtcNow,
                Uploaded = torrent.Uploaded,
                Downloaded = torrent.Downloaded,
                Ratio = torrent.Ratio,
                SeedingTime = torrent.SeedingTime,
                PrimaryTracker = torrent.TrackerUrl,
                Source = "Library",
                Status = "Removed",
                RemovalReason = reason
            };
            _historyRepository.Insert(entry);
            return;
        }

        entry.TorrentId = null;
        entry.DateRemoved = DateTime.UtcNow;
        entry.Uploaded = torrent.Uploaded;
        entry.Downloaded = torrent.Downloaded;
        entry.Ratio = torrent.Ratio;
        entry.SeedingTime = torrent.SeedingTime;
        entry.Status = "Removed";
        entry.RemovalReason = reason;

        _historyRepository.Update(entry);
    }

    public Torrent ReAdd(int historyId)
    {
        var entry = _historyRepository.Get(historyId);
        if (entry == null)
        {
            throw new ArgumentException($"History entry {historyId} not found");
        }

        if (_torrentRepository.ExistsByInfoHash(entry.InfoHash))
        {
            throw new InvalidOperationException($"Torrent with info hash {entry.InfoHash} is already in the active library");
        }

        var torrent = new Torrent
        {
            Name = entry.Title,
            InfoHash = entry.InfoHash,
            TotalSize = entry.TotalSize,
            TrackerUrl = entry.PrimaryTracker,
            Status = TorrentStatus.Queued,
            DateAdded = DateTime.UtcNow,
            Uploaded = entry.Uploaded,
            Downloaded = entry.Downloaded,
            Ratio = entry.Ratio,
            SeedingTime = entry.SeedingTime
        };

        var all = _torrentRepository.All().ToList();
        torrent.SortOrder = all.Count > 0 ? all.Max(t => t.SortOrder) + 1 : 0;

        var added = _torrentRepository.Insert(torrent);

        if (!string.IsNullOrWhiteSpace(entry.PrimaryTracker))
        {
            _trackerEntryRepository.Insert(new TrackerEntry
            {
                TorrentId = added.Id,
                Url = entry.PrimaryTracker,
                Tier = 0,
                Enabled = true
            });
        }

        entry.TorrentId = added.Id;
        entry.Status = "Active";
        entry.DateRemoved = null;
        _historyRepository.Update(entry);

        _logger.Info("Re-added historical torrent '{0}' (InfoHash: {1}) with ID {2}", entry.Title, entry.InfoHash, added.Id);
        return added;
    }

    public void Handle(TorrentAddedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        RecordTorrentAdded(message.Torrent);
    }

    public void Handle(TorrentDeletedEvent message)
    {
        if (message?.Torrent != null)
        {
            RecordTorrentRemoved(message.Torrent, "Deleted from active library");
            return;
        }

        var entry = _historyRepository.FindByTorrentId(message.TorrentId);
        if (entry != null)
        {
            entry.TorrentId = null;
            entry.DateRemoved = DateTime.UtcNow;
            entry.Status = "Removed";
            entry.RemovalReason = "Deleted from active library";
            _historyRepository.Update(entry);
        }
    }
}
