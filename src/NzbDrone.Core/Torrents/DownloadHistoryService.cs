using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public class DownloadHistoryService : IDownloadHistoryService, IHandle<TorrentAddedEvent>, IHandle<TorrentDeletedEvent>, IHandle<TorrentDownloadCompletedEvent>, IHandle<TorrentStatusChangedEvent>
{
    private readonly IDownloadHistoryRepository _historyRepository;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITrackerEntryRepository _trackerEntryRepository;
    private readonly ICategoryService _categoryService;
    private readonly IDownloadClientFactory _downloadClientFactory;
    private readonly Logger _logger;

    public DownloadHistoryService(
        IDownloadHistoryRepository historyRepository,
        ITorrentRepository torrentRepository,
        ITrackerEntryRepository trackerEntryRepository,
        ICategoryService categoryService = null,
        IDownloadClientFactory downloadClientFactory = null)
    {
        _historyRepository = historyRepository;
        _torrentRepository = torrentRepository;
        _trackerEntryRepository = trackerEntryRepository;
        _categoryService = categoryService;
        _downloadClientFactory = downloadClientFactory;
        _logger = LogManager.GetCurrentClassLogger();
    }

    private static void EnrichDataJson(DownloadHistory entry)
    {
        if (string.IsNullOrEmpty(entry.SavePath) &&
            string.IsNullOrEmpty(entry.Category) &&
            !entry.DownloadClientId.HasValue &&
            string.IsNullOrEmpty(entry.SourcePath) &&
            !entry.IsPrivate.HasValue)
        {
            return;
        }

        try
        {
            Dictionary<string, object> dict;
            if (!string.IsNullOrEmpty(entry.DataJson))
            {
                dict = JsonSerializer.Deserialize<Dictionary<string, object>>(entry.DataJson) ?? new Dictionary<string, object>();
            }
            else
            {
                dict = new Dictionary<string, object>();
            }

            if (!string.IsNullOrEmpty(entry.SavePath) && !dict.ContainsKey("savePath"))
            {
                dict["savePath"] = entry.SavePath;
            }

            if (!string.IsNullOrEmpty(entry.Category) && !dict.ContainsKey("category"))
            {
                dict["category"] = entry.Category;
            }

            if (!string.IsNullOrEmpty(entry.SourcePath) && !dict.ContainsKey("sourcePath"))
            {
                dict["sourcePath"] = entry.SourcePath;
            }

            if (entry.DownloadClientId.HasValue && !dict.ContainsKey("downloadClientId"))
            {
                dict["downloadClientId"] = entry.DownloadClientId.Value;
            }

            if (entry.IsPrivate.HasValue && !dict.ContainsKey("isPrivate"))
            {
                dict["isPrivate"] = entry.IsPrivate.Value;
            }

            entry.DataJson = JsonSerializer.Serialize(dict);
        }
        catch
        {
            // Ignore serialization issues
        }
    }

    private static void PopulateFromDataJson(DownloadHistory entry)
    {
        if (string.IsNullOrEmpty(entry.DataJson))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(entry.DataJson);
            var root = doc.RootElement;
            if (string.IsNullOrEmpty(entry.SavePath) && root.TryGetProperty("savePath", out var spProp))
            {
                entry.SavePath = spProp.GetString();
            }

            if (string.IsNullOrEmpty(entry.Category) && root.TryGetProperty("category", out var catProp))
            {
                entry.Category = catProp.GetString();
            }

            if (string.IsNullOrEmpty(entry.SourcePath) && root.TryGetProperty("sourcePath", out var srcProp))
            {
                entry.SourcePath = srcProp.GetString();
            }

            if (!entry.DownloadClientId.HasValue && root.TryGetProperty("downloadClientId", out var dcProp) && dcProp.TryGetInt32(out var dcId))
            {
                entry.DownloadClientId = dcId;
            }

            if (!entry.IsPrivate.HasValue && root.TryGetProperty("isPrivate", out var privProp) &&
                (privProp.ValueKind == JsonValueKind.True || privProp.ValueKind == JsonValueKind.False))
            {
                entry.IsPrivate = privProp.GetBoolean();
            }
        }
        catch
        {
            // Ignore parse errors
        }
    }

    public List<DownloadHistory> GetAll(string query = null, string status = null, int limit = 500, int offset = 0)
    {
        return _historyRepository.GetHistory(query, status, limit, offset);
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

        var effectiveMagnetUrl = !string.IsNullOrEmpty(magnetUrl) ? magnetUrl : torrent.MagnetUrl;
        var effectiveDownloadUrl = !string.IsNullOrEmpty(downloadUrl) ? downloadUrl : torrent.DownloadUrl;

        if (existing != null)
        {
            existing.TorrentId = torrent.Id;
            existing.Title = torrent.Name ?? existing.Title;
            existing.TotalSize = torrent.TotalSize > 0 ? torrent.TotalSize : existing.TotalSize;
            existing.PrimaryTracker = torrent.TrackerUrl ?? existing.PrimaryTracker;
            existing.Status = "Active";
            existing.DateRemoved = null;

            if (!string.IsNullOrEmpty(torrent.SavePath))
            {
                existing.SavePath = torrent.SavePath;
            }

            if (!string.IsNullOrEmpty(torrent.Category))
            {
                existing.Category = torrent.Category;
            }

            if (torrent.DownloadClientId.HasValue)
            {
                existing.DownloadClientId = torrent.DownloadClientId;
            }

            if (!string.IsNullOrEmpty(torrent.SourcePath))
            {
                existing.SourcePath = torrent.SourcePath;
            }

            if (!string.IsNullOrEmpty(source))
            {
                existing.Source = source;
            }

            if (!string.IsNullOrEmpty(effectiveMagnetUrl))
            {
                existing.MagnetUrl = effectiveMagnetUrl;
            }

            if (!string.IsNullOrEmpty(effectiveDownloadUrl))
            {
                existing.DownloadUrl = effectiveDownloadUrl;
            }

            if (!string.IsNullOrEmpty(indexerName))
            {
                existing.IndexerName = indexerName;
            }

            EnrichDataJson(existing);
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
            MagnetUrl = effectiveMagnetUrl,
            DownloadUrl = effectiveDownloadUrl,
            SavePath = torrent.SavePath,
            Category = torrent.Category,
            DownloadClientId = torrent.DownloadClientId,
            SourcePath = torrent.SourcePath,
            IsPrivate = torrent.IsPrivate,
            Status = "Active"
        };

        EnrichDataJson(entry);
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

        if (!string.IsNullOrEmpty(torrent.SavePath))
        {
            entry.SavePath = torrent.SavePath;
        }

        if (!string.IsNullOrEmpty(torrent.Category))
        {
            entry.Category = torrent.Category;
        }

        if (torrent.DownloadClientId.HasValue)
        {
            entry.DownloadClientId = torrent.DownloadClientId;
        }

        if (!string.IsNullOrEmpty(torrent.SourcePath))
        {
            entry.SourcePath = torrent.SourcePath;
        }

        if (!string.IsNullOrEmpty(torrent.MagnetUrl))
        {
            entry.MagnetUrl = torrent.MagnetUrl;
        }

        if (!string.IsNullOrEmpty(torrent.DownloadUrl))
        {
            entry.DownloadUrl = torrent.DownloadUrl;
        }

        if (torrent.Progress >= 1.0 && entry.DateCompleted == null)
        {
            entry.DateCompleted = DateTime.UtcNow;
            entry.Status = "Completed";
        }

        EnrichDataJson(entry);
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
                RemovalReason = reason,
                SavePath = torrent.SavePath,
                Category = torrent.Category,
                DownloadClientId = torrent.DownloadClientId,
                SourcePath = torrent.SourcePath,
                MagnetUrl = torrent.MagnetUrl,
                DownloadUrl = torrent.DownloadUrl,
                IsPrivate = torrent.IsPrivate
            };

            EnrichDataJson(entry);
            _historyRepository.Insert(entry);
            return;
        }

        entry.TorrentId = null;
        entry.DateRemoved = DateTime.UtcNow;
        entry.IsPrivate ??= torrent.IsPrivate;
        entry.Uploaded = torrent.Uploaded;
        entry.Downloaded = torrent.Downloaded;
        entry.Ratio = torrent.Ratio;
        entry.SeedingTime = torrent.SeedingTime;
        entry.Status = "Removed";
        entry.RemovalReason = reason;

        if (!string.IsNullOrEmpty(torrent.SavePath))
        {
            entry.SavePath = torrent.SavePath;
        }

        if (!string.IsNullOrEmpty(torrent.Category))
        {
            entry.Category = torrent.Category;
        }

        if (torrent.DownloadClientId.HasValue)
        {
            entry.DownloadClientId = torrent.DownloadClientId;
        }

        if (!string.IsNullOrEmpty(torrent.SourcePath))
        {
            entry.SourcePath = torrent.SourcePath;
        }

        if (!string.IsNullOrEmpty(torrent.MagnetUrl))
        {
            entry.MagnetUrl = torrent.MagnetUrl;
        }

        if (!string.IsNullOrEmpty(torrent.DownloadUrl))
        {
            entry.DownloadUrl = torrent.DownloadUrl;
        }

        EnrichDataJson(entry);
        _historyRepository.Update(entry);
    }

    public Torrent ReAdd(int historyId)
    {
        var entry = _historyRepository.Get(historyId);
        if (entry == null)
        {
            throw new ArgumentException($"History entry {historyId} not found");
        }

        if (!string.IsNullOrWhiteSpace(entry.InfoHash) && _torrentRepository.ExistsByInfoHash(entry.InfoHash))
        {
            throw new InvalidOperationException($"Torrent '{entry.Title}' with info hash '{entry.InfoHash}' is already in the active library");
        }

        if (entry.TorrentId.HasValue && _torrentRepository.Get(entry.TorrentId.Value) != null)
        {
            throw new InvalidOperationException($"Torrent '{entry.Title}' with ID {entry.TorrentId.Value} is already in the active library");
        }

        if (_downloadClientFactory != null && !string.IsNullOrWhiteSpace(entry.InfoHash))
        {
            var clients = _downloadClientFactory.All().Where(c => c.Enable).ToList();
            foreach (var clientDef in clients)
            {
                try
                {
                    var client = _downloadClientFactory.CreateClient(clientDef);
                    if (client != null)
                    {
                        var items = client.GetItems();
                        if (items != null && items.Any(i => string.Equals(i.InfoHash, entry.InfoHash, StringComparison.OrdinalIgnoreCase)))
                        {
                            throw new InvalidOperationException($"Torrent '{entry.Title}' with info hash '{entry.InfoHash}' is already tracked in download client '{clientDef.Name}'");
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to check download client {0} for duplicate torrent {1}", clientDef.Name, entry.InfoHash);
                }
            }
        }

        PopulateFromDataJson(entry);

        var category = entry.Category;
        var savePath = entry.SavePath;
        var sourcePath = entry.SourcePath;
        var downloadClientId = entry.DownloadClientId;

        if (string.IsNullOrWhiteSpace(savePath) && !string.IsNullOrWhiteSpace(category) && _categoryService != null)
        {
            savePath = _categoryService.GetSavePathForCategory(category);
        }

        savePath ??= string.Empty;
        sourcePath ??= savePath;

        var primaryTracker = entry.PrimaryTracker;
        string[] trackersFromMagnet = null;

        if (!string.IsNullOrWhiteSpace(entry.MagnetUrl))
        {
            try
            {
                var parsed = MagnetLinkParser.Parse(entry.MagnetUrl);
                if (string.IsNullOrWhiteSpace(primaryTracker) && parsed.Trackers.Length > 0)
                {
                    primaryTracker = parsed.Trackers[0];
                }

                trackersFromMagnet = parsed.Trackers;
            }
            catch
            {
                // Ignore parse errors
            }
        }

        var torrent = new Torrent
        {
            Name = entry.Title,
            InfoHash = entry.InfoHash,
            TotalSize = entry.TotalSize,
            TrackerUrl = primaryTracker,
            Status = TorrentStatus.Queued,
            DateAdded = DateTime.UtcNow,
            Uploaded = entry.Uploaded,
            Downloaded = entry.Downloaded,
            Ratio = entry.Ratio,
            SeedingTime = entry.SeedingTime,
            Category = category,
            SavePath = savePath,
            SourcePath = sourcePath,
            DownloadClientId = downloadClientId,
            MagnetUrl = entry.MagnetUrl,
            DownloadUrl = entry.DownloadUrl
        };

        var all = _torrentRepository.All().ToList();
        torrent.SortOrder = all.Count > 0 ? all.Max(t => t.SortOrder) + 1 : 0;

        var added = _torrentRepository.Insert(torrent);

        if (!string.IsNullOrWhiteSpace(primaryTracker))
        {
            _trackerEntryRepository.Insert(new TrackerEntry
            {
                TorrentId = added.Id,
                Url = primaryTracker,
                Tier = 0,
                Enabled = true
            });
        }

        if (trackersFromMagnet != null)
        {
            var tier = 1;
            foreach (var tr in trackersFromMagnet)
            {
                if (string.Equals(tr, primaryTracker, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _trackerEntryRepository.Insert(new TrackerEntry
                {
                    TorrentId = added.Id,
                    Url = tr,
                    Tier = tier++,
                    Enabled = true
                });
            }
        }

        entry.TorrentId = added.Id;
        entry.Status = "Active";
        entry.DateRemoved = null;
        _historyRepository.Update(entry);

        _logger.Info("Re-added historical torrent '{0}' (InfoHash: {1}) with ID {2}", entry.Title, entry.InfoHash, added.Id);
        return added;
    }

    public void Update(DownloadHistory history)
    {
        if (history != null)
        {
            _historyRepository.Update(history);
        }
    }

    public void AddMany(IList<DownloadHistory> entries)
    {
        if (entries == null || entries.Count == 0)
        {
            return;
        }

        foreach (var entry in entries)
        {
            EnrichDataJson(entry);
        }

        _historyRepository.InsertMany(entries);
    }

    public void AddMany(IEnumerable<DownloadHistory> entries)
    {
        if (entries == null)
        {
            return;
        }

        var list = entries as IList<DownloadHistory> ?? entries.ToList();
        AddMany(list);
    }

    public int ReconcileAllTorrents()
    {
        var allTorrents = _torrentRepository.All().ToList();
        var backfilled = 0;
        var toInsert = new List<DownloadHistory>();

        foreach (var torrent in allTorrents)
        {
            if (string.IsNullOrWhiteSpace(torrent.InfoHash))
            {
                continue;
            }

            var existing = _historyRepository.FindByInfoHash(torrent.InfoHash);
            if (existing == null)
            {
                var entry = new DownloadHistory
                {
                    TorrentId = torrent.Id,
                    Title = torrent.Name ?? torrent.InfoHash,
                    InfoHash = torrent.InfoHash.ToLowerInvariant(),
                    TotalSize = torrent.TotalSize,
                    DateAdded = torrent.DateAdded != default ? torrent.DateAdded : DateTime.UtcNow,
                    Uploaded = torrent.Uploaded,
                    Downloaded = torrent.Downloaded,
                    Ratio = torrent.Ratio,
                    PrimaryTracker = torrent.TrackerUrl,
                    Status = "Active",
                    SeedingTime = torrent.SeedingTime,
                    Source = torrent.IsPrivate ? "Private Tracker" : "Public Tracker",
                    SavePath = torrent.SavePath,
                    Category = torrent.Category,
                    DownloadClientId = torrent.DownloadClientId,
                    SourcePath = torrent.SourcePath,
                    MagnetUrl = torrent.MagnetUrl,
                    DownloadUrl = torrent.DownloadUrl,
                    IsPrivate = torrent.IsPrivate
                };

                toInsert.Add(entry);
                backfilled++;
            }
            else if (existing.TorrentId == null || existing.TorrentId == 0)
            {
                existing.TorrentId = torrent.Id;
                existing.Status = "Active";
                _historyRepository.Update(existing);
            }
        }

        if (toInsert.Count > 0)
        {
            AddMany(toInsert);
        }

        if (backfilled > 0)
        {
            _logger.Info("Reconciled and backfilled {0} missing torrents into Download History", backfilled);
        }

        return backfilled;
    }

    public int PruneHistory(int retentionDays)
    {
        if (retentionDays <= 0)
        {
            return 0;
        }

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        _logger.Info("Pruning download history older than {0} days (cutoff: {1})", retentionDays, cutoff);
        return _historyRepository.DeleteOlderThan(cutoff);
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

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        var torrent = message.Torrent;
        var entry = _historyRepository.FindByTorrentId(torrent.Id)
            ?? (!string.IsNullOrEmpty(torrent.InfoHash) ? _historyRepository.FindByInfoHash(torrent.InfoHash) : null);

        if (entry != null)
        {
            entry.DateCompleted ??= DateTime.UtcNow;
            entry.Status = "Completed";
            entry.Downloaded = torrent.Downloaded > 0 ? torrent.Downloaded : (torrent.TotalSize > 0 ? torrent.TotalSize : entry.Downloaded);
            entry.Uploaded = torrent.Uploaded;
            entry.Ratio = torrent.Ratio;
            entry.SeedingTime = torrent.SeedingTime;
            _historyRepository.Update(entry);
        }
    }

    public void Handle(TorrentStatusChangedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        var torrent = message.Torrent;
        var entry = _historyRepository.FindByTorrentId(torrent.Id)
            ?? (!string.IsNullOrEmpty(torrent.InfoHash) ? _historyRepository.FindByInfoHash(torrent.InfoHash) : null);

        if (entry != null)
        {
            if (message.NewStatus == TorrentStatus.Error)
            {
                entry.Status = "Error";
                _historyRepository.Update(entry);
            }
            else if (message.NewStatus == TorrentStatus.Seeding)
            {
                entry.DateCompleted ??= DateTime.UtcNow;
                if (entry.Status == "Active")
                {
                    entry.Status = "Completed";
                }

                _historyRepository.Update(entry);
            }
        }
    }
}
