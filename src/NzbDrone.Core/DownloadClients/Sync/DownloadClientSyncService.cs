using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.DownloadClients.Sync;

public class DownloadClientStatus
{
    public int ClientId { get; set; }
    public bool? IsOnline { get; set; }
    public string Version { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public string LastErrorMessage { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime? BackoffUntil { get; set; }

    public bool IsInBackoff => BackoffUntil.HasValue && BackoffUntil.Value > DateTime.UtcNow;
}

public interface IDownloadClientSyncService
{
    SyncResult Sync();
    List<DownloadClientRemoteItem> GetClientItems(int clientId);
    List<DownloadClientRemoteItem> GetAllClientItems();
    Torrent ImportTorrent(int clientId, string infoHash);
    BatchImportResponse ImportTorrents(int clientId, List<string> infoHashes);
    DownloadClientStatus GetClientStatus(int clientId);
    IReadOnlyDictionary<int, DownloadClientStatus> GetAllClientStatuses();
    void ResetClientStatus(int clientId);
}

public class DownloadClientSyncService : IDownloadClientSyncService, IDisposable
{
    private readonly IDownloadClientFactory _downloadClientFactory;
    private readonly IIndexerFactory _indexerFactory;
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly IRemotePathMappingService _remotePathMappingService;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly Logger _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, DownloadClientStatus> _clientStatuses = new();

    public DownloadClientStatus GetClientStatus(int clientId)
    {
        return _clientStatuses.TryGetValue(clientId, out var status) ? status : null;
    }

    public IReadOnlyDictionary<int, DownloadClientStatus> GetAllClientStatuses()
    {
        return _clientStatuses;
    }

    public void ResetClientStatus(int clientId)
    {
        _clientStatuses.TryRemove(clientId, out _);
    }

    public static TimeSpan CalculateBackoff(int consecutiveFailures)
    {
        var exponent = Math.Max(0, Math.Min(consecutiveFailures - 1, 5));
        var seconds = Math.Min(30 * (int)Math.Pow(2, exponent), 600);
        return TimeSpan.FromSeconds(seconds);
    }

    private void RecordSuccess(int clientId)
    {
        var status = _clientStatuses.GetOrAdd(clientId, id => new DownloadClientStatus { ClientId = id });
        status.IsOnline = true;
        status.ConsecutiveFailures = 0;
        status.BackoffUntil = null;
        status.LastErrorMessage = null;
        status.LastSyncTime = DateTime.UtcNow;
    }

    private void RecordFailure(int clientId, Exception ex)
    {
        var status = _clientStatuses.GetOrAdd(clientId, id => new DownloadClientStatus { ClientId = id });
        status.IsOnline = false;
        status.ConsecutiveFailures++;
        status.LastErrorMessage = ex?.Message;
        status.LastSyncTime = DateTime.UtcNow;
        status.BackoffUntil = DateTime.UtcNow.Add(CalculateBackoff(status.ConsecutiveFailures));
    }

    public DownloadClientSyncService(
        IDownloadClientFactory downloadClientFactory,
        IIndexerFactory indexerFactory,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        ITrackerEntryService trackerEntryService = null,
        ITorrentFileService torrentFileService = null,
        IRemotePathMappingService remotePathMappingService = null)
    {
        _downloadClientFactory = downloadClientFactory;
        _indexerFactory = indexerFactory;
        _torrentService = torrentService;
        _torrentFileParser = torrentFileParser;
        _trackerEntryService = trackerEntryService;
        _torrentFileService = torrentFileService;
        _remotePathMappingService = remotePathMappingService ?? new RemotePathMappingService();
        _logger = LogManager.GetCurrentClassLogger();
    }

    public SyncResult Sync()
    {
        if (!_syncLock.Wait(200))
        {
            return new SyncResult();
        }

        try
        {
            var result = new SyncResult();
            var existingTorrents = _torrentService.GetAll()
                .Where(t => !string.IsNullOrEmpty(t.InfoHash))
                .GroupBy(t => t.InfoHash.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var clients = _downloadClientFactory.All().Where(c => c.Enable).ToList();

            foreach (var definition in clients)
            {
                var status = _clientStatuses.GetOrAdd(definition.Id, id => new DownloadClientStatus { ClientId = id });
                if (status.IsInBackoff)
                {
                    _logger.Warn(
                        "Download client {0} is degraded and backing off until {1} UTC (failures: {2}). Skipping sync.",
                        definition.Name,
                        status.BackoffUntil.Value,
                        status.ConsecutiveFailures);
                    result.Skipped++;
                    continue;
                }

                var provider = CreateClient(definition);
                if (provider == null)
                {
                    continue;
                }

                try
                {
                    var items = provider.GetItems();
                    RecordSuccess(definition.Id);
                    foreach (var item in items)
                    {
                        if (string.IsNullOrEmpty(item.InfoHash))
                        {
                            continue;
                        }

                        var hash = item.InfoHash.ToLowerInvariant();
                        if (existingTorrents.TryGetValue(hash, out var torrent))
                        {
                            var total = item.TotalSize > 0 ? item.TotalSize : torrent.TotalSize;
                            var remaining = item.RemainingSize;
                            var downloaded = Math.Max(0, total - remaining);

                            torrent.TotalSize = total;
                            torrent.Downloaded = downloaded;

                            var progress = total > 0 ? (double)(total - remaining) / total : (remaining == 0 ? 1.0 : 0.0);
                            torrent.Progress = Math.Clamp(Math.Round(progress, 6), 0.0, 1.0);

                            if (remaining == 0)
                            {
                                torrent.MarkForceCompleted();
                            }

                            if (!string.IsNullOrWhiteSpace(item.Status))
                            {
                                torrent.Status = MapClientStatus(item.Status, remaining, total);
                            }

                            if (item.DownloadSpeed.HasValue)
                            {
                                torrent.DownloadSpeed = item.DownloadSpeed.Value;
                            }

                            if (item.UploadSpeed.HasValue)
                            {
                                torrent.UploadSpeed = item.UploadSpeed.Value;
                            }

                            if (!string.IsNullOrWhiteSpace(item.Category))
                            {
                                torrent.Category = item.Category;
                            }

                            if (!string.IsNullOrWhiteSpace(item.OutputPath))
                            {
                                var remappedPath = RemapRemotePath(definition.Host, item.OutputPath);
                                if (string.IsNullOrEmpty(torrent.SavePath))
                                {
                                    torrent.SavePath = remappedPath;
                                }

                                if (string.IsNullOrEmpty(torrent.SourcePath))
                                {
                                    torrent.SourcePath = remappedPath;
                                }
                            }

                            if (!torrent.DownloadClientId.HasValue && definition.Id > 0)
                            {
                                torrent.DownloadClientId = definition.Id;
                            }

                            if (string.IsNullOrEmpty(torrent.Category) && !string.IsNullOrWhiteSpace(definition.Category))
                            {
                                torrent.Category = definition.Category;
                            }

                            torrent.UpdateRatio();
                            _torrentService.Update(torrent);
                            result.Updated++;
                            result.Skipped++;
                            continue;
                        }

                        // Query indexers or get from client
                        byte[] torrentBytes = null;

                        try
                        {
                            torrentBytes = provider.GetTorrentFile(hash);
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug(ex, "Failed to get torrent file from client for {0}", hash);
                        }

                        if (torrentBytes == null || torrentBytes.Length == 0)
                        {
                            torrentBytes = SearchIndexersForTorrent(hash);
                        }

                        if (torrentBytes != null && torrentBytes.Length > 0)
                        {
                            using var ms = new System.IO.MemoryStream(torrentBytes);
                            var parsed = _torrentFileParser.Parse(ms);

                            var total = parsed.TotalSize > 0 ? parsed.TotalSize : item.TotalSize;
                            var remaining = item.RemainingSize;
                            var downloaded = Math.Max(0, total - remaining);
                            var remappedPath = RemapRemotePath(definition.Host, item.OutputPath);
                            var initialProgress = total > 0 ? (double)(total - remaining) / total : (remaining == 0 ? 1.0 : 0.0);

                            torrent = new Torrent
                            {
                                Name = parsed.Name ?? item.Title,
                                InfoHash = hash,
                                TotalSize = total,
                                Downloaded = downloaded,
                                PieceCount = parsed.PieceCount,
                                PieceLength = parsed.PieceLength,
                                Comment = parsed.Comment,
                                IsPrivate = parsed.IsPrivate,
                                TrackerUrl = parsed.AnnounceUrl,
                                DateAdded = DateTime.UtcNow,
                                Status = MapClientStatus(item.Status, remaining, total),
                                Category = !string.IsNullOrWhiteSpace(item.Category) ? item.Category : definition.Category,
                                SavePath = remappedPath,
                                SourcePath = remappedPath,
                                Progress = Math.Clamp(Math.Round(initialProgress, 6), 0.0, 1.0),
                                DownloadClientId = definition.Id,
                            };

                            if (item.DownloadSpeed.HasValue)
                            {
                                torrent.DownloadSpeed = item.DownloadSpeed.Value;
                            }

                            if (item.UploadSpeed.HasValue)
                            {
                                torrent.UploadSpeed = item.UploadSpeed.Value;
                            }

                            if (remaining == 0)
                            {
                                torrent.MarkForceCompleted();
                                if (!string.IsNullOrWhiteSpace(item.Status))
                                {
                                    torrent.Status = MapClientStatus(item.Status, remaining, total);
                                }
                            }

                            torrent.UpdateRatio();
                            _torrentService.Add(torrent);
                            SaveParsedTrackersAndFiles(torrent.Id, parsed);

                            existingTorrents[hash] = torrent;
                            result.Added++;
                            _logger.Info("Synced torrent {0} from download client {1}", torrent.Name, definition.Name);
                        }
                        else
                        {
                            _logger.Warn("Could not fetch torrent data for {0} ({1}). Seedarr cannot sync it.", item.Title, hash);
                            result.Failed++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    RecordFailure(definition.Id, ex);
                    _logger.Error(ex, "Failed to sync download client {0}", definition.Name);
                    result.Failed++;
                }
            }

            return result;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public List<DownloadClientRemoteItem> GetClientItems(int clientId)
    {
        var definition = _downloadClientFactory.Get(clientId);
        if (definition == null)
        {
            throw new ArgumentException($"Download client with id {clientId} not found.");
        }

        var provider = CreateClient(definition);
        if (provider == null)
        {
            throw new ArgumentException($"Could not create provider for client type {definition.ClientType}.");
        }

        var existingTorrents = _torrentService.GetAll()
            .Where(t => !string.IsNullOrEmpty(t.InfoHash))
            .GroupBy(t => t.InfoHash.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        List<DownloadClientItem> items;
        try
        {
            items = provider.GetItems();
            RecordSuccess(clientId);
        }
        catch (Exception ex)
        {
            RecordFailure(clientId, ex);
            throw;
        }

        var result = new List<DownloadClientRemoteItem>();

        foreach (var item in items)
        {
            var hash = item.InfoHash?.ToLowerInvariant() ?? "";
            Torrent existing = null;
            var isInLibrary = !string.IsNullOrEmpty(hash) &&
                existingTorrents.TryGetValue(hash, out existing) &&
                (!existing.DownloadClientId.HasValue || existing.DownloadClientId.Value == clientId);
            var libraryId = isInLibrary && existing != null ? (int?)existing.Id : null;

            double progress = 0;
            if (item.TotalSize > 0)
            {
                var downloaded = Math.Max(0, item.TotalSize - item.RemainingSize);
                progress = Math.Round((double)downloaded / item.TotalSize * 100.0, 1);
            }
            else if (item.Status?.Equals("seeding", StringComparison.OrdinalIgnoreCase) == true)
            {
                progress = 100.0;
            }

            result.Add(new DownloadClientRemoteItem
            {
                DownloadId = item.DownloadId,
                Title = item.Title,
                InfoHash = item.InfoHash,
                TotalSize = item.TotalSize,
                RemainingSize = item.RemainingSize,
                Progress = progress,
                Status = item.Status,
                OutputPath = item.OutputPath,
                Category = item.Category,
                IsPrivate = item.IsPrivate,
                IsInLibrary = isInLibrary,
                LibraryTorrentId = libraryId,
                DownloadSpeed = item.DownloadSpeed,
                UploadSpeed = item.UploadSpeed,
                ClientId = clientId,
                ClientName = definition.Name,
            });
        }

        return result;
    }

    public List<DownloadClientRemoteItem> GetAllClientItems()
    {
        var clients = _downloadClientFactory.All().Where(c => c.Enable).ToList();
        var result = new List<DownloadClientRemoteItem>();

        foreach (var client in clients)
        {
            var status = _clientStatuses.GetOrAdd(client.Id, id => new DownloadClientStatus { ClientId = id });
            if (status.IsInBackoff)
            {
                _logger.Warn("Download client {0} is in backoff until {1}. Skipping item fetch.", client.Name, status.BackoffUntil);
                continue;
            }

            try
            {
                var items = GetClientItems(client.Id);
                result.AddRange(items);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to retrieve items from download client {0} ({1}) for aggregated view", client.Name, client.Id);
            }
        }

        return result;
    }

    public Torrent ImportTorrent(int clientId, string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            throw new ArgumentException("InfoHash cannot be empty.");
        }

        var definition = _downloadClientFactory.Get(clientId);
        if (definition == null)
        {
            throw new ArgumentException($"Download client with id {clientId} not found.");
        }

        var provider = CreateClient(definition);
        if (provider == null)
        {
            throw new ArgumentException($"Could not create provider for client type {definition.ClientType}.");
        }

        _syncLock.Wait();
        try
        {
            var normalizedHash = infoHash.ToLowerInvariant();
            var existing = _torrentService.GetAll()
                .FirstOrDefault(t => string.Equals(t.InfoHash, normalizedHash, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                return existing;
            }

            DownloadClientItem matchingItem = null;
            try
            {
                var items = provider.GetItems();
                RecordSuccess(clientId);
                matchingItem = items?.FirstOrDefault(i => string.Equals(i.InfoHash, normalizedHash, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                RecordFailure(clientId, ex);
                _logger.Debug(ex, "Failed to query items from client {0}", definition.Name);
            }

            return ImportTorrentInternal(definition, provider, normalizedHash, matchingItem);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    protected virtual Torrent ImportTorrentInternal(
        DownloadClientDefinition definition,
        IDownloadClient provider,
        string normalizedHash,
        DownloadClientItem matchingItem)
    {
        byte[] torrentBytes = null;
        try
        {
            torrentBytes = provider.GetTorrentFile(normalizedHash);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to get torrent file from client for {0}", normalizedHash);
        }

        if (torrentBytes == null || torrentBytes.Length == 0)
        {
            torrentBytes = SearchIndexersForTorrent(normalizedHash);
        }

        var remappedPath = RemapRemotePath(definition?.Host, matchingItem?.OutputPath);

        Torrent torrent;
        if (torrentBytes != null && torrentBytes.Length > 0)
        {
            using var ms = new System.IO.MemoryStream(torrentBytes);
            var parsed = _torrentFileParser.Parse(ms);

            var total = parsed.TotalSize > 0 ? parsed.TotalSize : (matchingItem?.TotalSize ?? 0);
            var remaining = matchingItem?.RemainingSize ?? 0;
            var downloaded = Math.Max(0, total - remaining);
            var initialProgress = total > 0 ? (double)(total - remaining) / total : (remaining == 0 ? 1.0 : 0.0);

            torrent = new Torrent
            {
                Name = matchingItem?.Title ?? parsed.Name ?? normalizedHash,
                InfoHash = normalizedHash,
                TotalSize = total,
                Downloaded = downloaded,
                PieceCount = parsed.PieceCount,
                PieceLength = parsed.PieceLength,
                PieceHashes = parsed.PieceHashes,
                Comment = parsed.Comment,
                IsPrivate = parsed.IsPrivate,
                TrackerUrl = parsed.AnnounceUrl,
                DateAdded = DateTime.UtcNow,
                Status = MapClientStatus(matchingItem?.Status, remaining, total),
                Category = !string.IsNullOrWhiteSpace(matchingItem?.Category) ? matchingItem.Category : definition?.Category,
                SavePath = remappedPath,
                SourcePath = remappedPath,
                Progress = Math.Clamp(Math.Round(initialProgress, 6), 0.0, 1.0),
                DownloadClientId = definition?.Id,
            };

            if (matchingItem?.DownloadSpeed.HasValue == true)
            {
                torrent.DownloadSpeed = matchingItem.DownloadSpeed.Value;
            }

            if (matchingItem?.UploadSpeed.HasValue == true)
            {
                torrent.UploadSpeed = matchingItem.UploadSpeed.Value;
            }

            if (remaining == 0)
            {
                torrent.MarkForceCompleted();
                if (!string.IsNullOrWhiteSpace(matchingItem?.Status))
                {
                    torrent.Status = MapClientStatus(matchingItem?.Status, remaining, total);
                }
            }

            torrent.UpdateRatio();
            _torrentService.Add(torrent);
            SaveParsedTrackersAndFiles(torrent.Id, parsed);
        }
        else if (matchingItem != null)
        {
            var clientTrackers = provider.GetTrackers(normalizedHash);
            var total = matchingItem.TotalSize;
            var remaining = matchingItem.RemainingSize;
            var downloaded = Math.Max(0, total - remaining);
            var initialProgress = total > 0 ? (double)(total - remaining) / total : (remaining == 0 ? 1.0 : 0.0);

            torrent = new Torrent
            {
                Name = !string.IsNullOrEmpty(matchingItem.Title) ? matchingItem.Title : normalizedHash,
                InfoHash = normalizedHash,
                TotalSize = matchingItem.TotalSize,
                Downloaded = downloaded,
                IsPrivate = matchingItem.IsPrivate,
                TrackerUrl = clientTrackers.Count > 0 ? clientTrackers[0] : null,
                DateAdded = DateTime.UtcNow,
                Status = MapClientStatus(matchingItem.Status, remaining, total),
                Category = !string.IsNullOrWhiteSpace(matchingItem.Category) ? matchingItem.Category : definition?.Category,
                SavePath = remappedPath,
                SourcePath = remappedPath,
                Progress = Math.Clamp(Math.Round(initialProgress, 6), 0.0, 1.0),
                DownloadClientId = definition?.Id,
            };

            if (matchingItem.DownloadSpeed.HasValue)
            {
                torrent.DownloadSpeed = matchingItem.DownloadSpeed.Value;
            }

            if (matchingItem.UploadSpeed.HasValue)
            {
                torrent.UploadSpeed = matchingItem.UploadSpeed.Value;
            }

            if (remaining == 0)
            {
                torrent.MarkForceCompleted();
                if (!string.IsNullOrWhiteSpace(matchingItem.Status))
                {
                    torrent.Status = MapClientStatus(matchingItem.Status, remaining, total);
                }
            }

            torrent.UpdateRatio();
            _torrentService.Add(torrent);
            SaveClientTrackers(torrent.Id, clientTrackers);
        }
        else
        {
            throw new InvalidOperationException($"Could not fetch torrent metadata for hash {normalizedHash} from download client or indexers.");
        }

        _logger.Info("Imported torrent {0} from download client {1}", torrent.Name, definition.Name);
        return torrent;
    }

    private void SaveParsedTrackersAndFiles(int torrentId, ParsedTorrent parsed)
    {
        if (_torrentFileService != null && parsed.Files != null && parsed.Files.Count > 0)
        {
            var existingFiles = _torrentFileService.GetByTorrentId(torrentId);
            if (existingFiles.Count == 0)
            {
                var pieceLength = parsed.PieceLength > 0 ? (long)parsed.PieceLength : 0L;
                var runningByteOffset = 0L;
                var filesToAdd = new List<TorrentFile>();

                foreach (var f in parsed.Files)
                {
                    var (pieceOffset, pieceCount) = TorrentPieceCalculator.CalculateForFile(runningByteOffset, f.Size, pieceLength);
                    if (pieceLength > 0)
                    {
                        runningByteOffset += f.Size;
                    }

                    filesToAdd.Add(new TorrentFile
                    {
                        TorrentId = torrentId,
                        Path = f.Path,
                        Size = f.Size,
                        PieceOffset = pieceOffset,
                        PieceCount = pieceCount,
                        IsPaddingFile = f.IsPaddingFile
                    });
                }
                _torrentFileService.AddMany(filesToAdd);
            }
        }

        if (_trackerEntryService != null)
        {
            var existingTrackers = _trackerEntryService.GetByTorrentId(torrentId)
                .Select(t => t.Url.Trim().ToLowerInvariant())
                .ToHashSet();

            var newTrackers = new List<TrackerEntry>();

            if (parsed.AnnounceList != null && parsed.AnnounceList.Count > 0)
            {
                var tier = 1;
                foreach (var tierUrls in parsed.AnnounceList)
                {
                    foreach (var url in tierUrls)
                    {
                        var clean = url.Trim();
                        if (!string.IsNullOrEmpty(clean) && !existingTrackers.Contains(clean.ToLowerInvariant()))
                        {
                            newTrackers.Add(new TrackerEntry
                            {
                                TorrentId = torrentId,
                                Url = clean,
                                Tier = tier,
                                Enabled = true
                            });
                            existingTrackers.Add(clean.ToLowerInvariant());
                        }
                    }

                    tier++;
                }
            }
            else if (!string.IsNullOrEmpty(parsed.AnnounceUrl))
            {
                var clean = parsed.AnnounceUrl.Trim();
                if (!existingTrackers.Contains(clean.ToLowerInvariant()))
                {
                    newTrackers.Add(new TrackerEntry
                    {
                        TorrentId = torrentId,
                        Url = clean,
                        Tier = 1,
                        Enabled = true
                    });
                }
            }

            if (newTrackers.Count > 0)
            {
                _trackerEntryService.AddMany(newTrackers);
            }
        }
    }

    private void SaveClientTrackers(int torrentId, List<string> clientTrackers)
    {
        if (_trackerEntryService == null || clientTrackers == null || clientTrackers.Count == 0)
        {
            return;
        }

        var existingTrackers = _trackerEntryService.GetByTorrentId(torrentId)
            .Select(t => t.Url.Trim().ToLowerInvariant())
            .ToHashSet();

        var newTrackers = new List<TrackerEntry>();
        var tier = 1;
        foreach (var tr in clientTrackers)
        {
            var clean = tr.Trim();
            if (!string.IsNullOrEmpty(clean) && !existingTrackers.Contains(clean.ToLowerInvariant()))
            {
                newTrackers.Add(new TrackerEntry
                {
                    TorrentId = torrentId,
                    Url = clean,
                    Tier = tier++,
                    Enabled = true
                });
                existingTrackers.Add(clean.ToLowerInvariant());
            }
        }

        if (newTrackers.Count > 0)
        {
            _trackerEntryService.AddMany(newTrackers);
        }
    }

    public BatchImportResponse ImportTorrents(int clientId, List<string> infoHashes)
    {
        var result = new BatchImportResponse();
        if (infoHashes == null || infoHashes.Count == 0)
        {
            return result;
        }

        var definition = _downloadClientFactory.Get(clientId);
        if (definition == null)
        {
            throw new ArgumentException($"Download client with id {clientId} not found.");
        }

        var provider = CreateClient(definition);
        if (provider == null)
        {
            throw new ArgumentException($"Could not create provider for client type {definition.ClientType}.");
        }

        _syncLock.Wait();
        try
        {
            var existingHashes = _torrentService.GetAll()
                .Where(t => !string.IsNullOrEmpty(t.InfoHash))
                .Select(t => t.InfoHash.ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var clientItems = new Dictionary<string, DownloadClientItem>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var items = provider.GetItems();
                RecordSuccess(clientId);
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        if (!string.IsNullOrEmpty(item.InfoHash))
                        {
                            clientItems.TryAdd(item.InfoHash.ToLowerInvariant(), item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RecordFailure(clientId, ex);
                _logger.Debug(ex, "Failed to query items from client {0}", definition.Name);
            }

            foreach (var rawHash in infoHashes)
            {
                if (string.IsNullOrWhiteSpace(rawHash))
                {
                    result.Failed++;
                    result.Items.Add(new BatchImportItemResult
                    {
                        InfoHash = rawHash ?? string.Empty,
                        Title = string.Empty,
                        Success = false,
                        ErrorMessage = "InfoHash cannot be empty."
                    });
                    continue;
                }

                var hash = rawHash.Trim().ToLowerInvariant();
                clientItems.TryGetValue(hash, out var matchingItem);
                var title = matchingItem?.Title ?? hash;

                if (existingHashes.Contains(hash))
                {
                    result.Skipped++;
                    result.Items.Add(new BatchImportItemResult
                    {
                        InfoHash = hash,
                        Title = title,
                        Success = true,
                        ErrorMessage = null
                    });
                    continue;
                }

                try
                {
                    ImportTorrentInternal(definition, provider, hash, matchingItem);
                    existingHashes.Add(hash);
                    result.Added++;
                    result.Items.Add(new BatchImportItemResult
                    {
                        InfoHash = hash,
                        Title = title,
                        Success = true,
                        ErrorMessage = null
                    });
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to import torrent {0} from client {1}", hash, clientId);
                    result.Failed++;
                    result.Items.Add(new BatchImportItemResult
                    {
                        InfoHash = hash,
                        Title = title,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            }

            return result;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private byte[] SearchIndexersForTorrent(string infoHash)
    {
        var indexers = _indexerFactory.All().Where(i => i.Enable).ToList();
        foreach (var indexerDef in indexers)
        {
            try
            {
                _logger.Debug("Querying indexer {0} for hash {1}", indexerDef.Name, infoHash);

                var provider = CreateIndexer(indexerDef);
                if (provider != null)
                {
                    var result = provider.FetchTorrentByHash(indexerDef, infoHash);
                    if (result != null && result.Length > 0)
                    {
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Indexer search failed for {0}", indexerDef.Name);
            }
        }

        return null;
    }

    private string RemapRemotePath(string host, string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath) || _remotePathMappingService == null)
        {
            return remotePath;
        }

        var remapped = _remotePathMappingService.Remap(host, remotePath);
        if (!string.IsNullOrEmpty(remapped) && !string.Equals(remapped, remotePath, StringComparison.Ordinal))
        {
            return remapped;
        }

        var remappedLocal = _remotePathMappingService.RemapRemoteToLocal(host, remotePath);
        if (!string.IsNullOrEmpty(remappedLocal))
        {
            return remappedLocal;
        }

        return remapped ?? remotePath;
    }

    protected virtual IIndexer CreateIndexer(IndexerDefinition definition)
    {
        return definition.IndexerType?.Trim().ToLowerInvariant() switch
        {
            "prowlarr" => new NzbDrone.Core.Indexers.Prowlarr.ProwlarrIndexer(),
            "torznab" => new NzbDrone.Core.Indexers.Torznab.TorznabIndexer(),
            "newznab" => new NzbDrone.Core.Indexers.Newznab.NewznabIndexer(),
            _ => null
        };
    }

    protected virtual IDownloadClient CreateClient(DownloadClientDefinition definition)
    {
        var client = _downloadClientFactory.CreateClient(definition);
        if (client is Transmission.TransmissionClient tc && tc.RemotePathMappingService == null)
        {
            tc.RemotePathMappingService = _remotePathMappingService;
        }
        else if (client is Deluge.DelugeClient dc && dc.RemotePathMappingService == null)
        {
            dc.RemotePathMappingService = _remotePathMappingService;
        }

        return client;
    }

    public static TorrentStatus MapClientStatus(string status, long remainingSize, long totalSize)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim().ToLowerInvariant();
            switch (s)
            {
                case "seeding" or "forcedup" or "stalledup" or "queuedup" or "uploading":
                    return TorrentStatus.Seeding;
                case "downloading" or "forceddl" or "stalleddl" or "queueddl":
                    return TorrentStatus.Downloading;
                case "paused" or "pausedup" or "pauseddl":
                    return TorrentStatus.Paused;
                case "checking" or "checkingup" or "checkingdl" or "checkingresumedata":
                    return TorrentStatus.Checking;
                case "error":
                    return TorrentStatus.Error;
                case "queued":
                    return TorrentStatus.Queued;
                case "stopped":
                    return TorrentStatus.Stopped;
            }
        }

        return TorrentStatus.Stopped;
    }

    public void Dispose()
    {
        _syncLock.Dispose();
    }
}
