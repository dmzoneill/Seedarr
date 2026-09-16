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

public interface IDownloadClientSyncService
{
    SyncResult Sync();
    List<DownloadClientRemoteItem> GetClientItems(int clientId);
    Torrent ImportTorrent(int clientId, string infoHash);
    SyncResult ImportTorrents(int clientId, List<string> infoHashes);
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
        _syncLock.Wait();
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
                var provider = CreateClient(definition);
                if (provider == null)
                {
                    continue;
                }

                try
                {
                    var items = provider.GetItems();
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
                            if (total > 0)
                            {
                                torrent.Progress = Math.Round((double)downloaded / total, 6);
                            }
                            else if (remaining == 0)
                            {
                                torrent.Progress = 1.0;
                            }

                            if (remaining == 0)
                            {
                                torrent.MarkForceCompleted();
                            }

                            torrent.Status = MapClientStatus(item.Status, remaining, total);

                            if (!string.IsNullOrWhiteSpace(item.Category))
                            {
                                torrent.Category = item.Category;
                            }

                            if (!string.IsNullOrWhiteSpace(item.OutputPath))
                            {
                                var remappedPath = _remotePathMappingService.Remap(definition.Host, item.OutputPath);
                                if (string.IsNullOrEmpty(torrent.SavePath))
                                {
                                    torrent.SavePath = remappedPath;
                                }

                                if (string.IsNullOrEmpty(torrent.SourcePath))
                                {
                                    torrent.SourcePath = remappedPath;
                                }
                            }

                            torrent.UpdateRatio();
                            _torrentService.Update(torrent);
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
                            var remappedPath = _remotePathMappingService.Remap(definition.Host, item.OutputPath);

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
                                Category = item.Category,
                                SavePath = remappedPath,
                                SourcePath = remappedPath,
                            };

                            if (remaining == 0)
                            {
                                torrent.MarkForceCompleted();
                            }
                            else if (total > 0)
                            {
                                torrent.Progress = Math.Round((double)downloaded / total, 6);
                            }

                            torrent.UpdateRatio();
                            _torrentService.Add(torrent);
                            SaveParsedTrackersAndFiles(torrent.Id, parsed);

                            existingTorrents[hash] = torrent;
                            result.Added++;
                            _logger.Info("Synced torrent {0} from download client {1}", torrent.Name, definition.Name);
                        }
                        else if (item != null)
                        {
                            var clientTrackers = provider.GetTrackers(hash);
                            var total = item.TotalSize;
                            var remaining = item.RemainingSize;
                            var downloaded = Math.Max(0, total - remaining);
                            var remappedPath = _remotePathMappingService.Remap(definition.Host, item.OutputPath);

                            torrent = new Torrent
                            {
                                Name = !string.IsNullOrEmpty(item.Title) ? item.Title : hash,
                                InfoHash = hash,
                                TotalSize = total,
                                Downloaded = downloaded,
                                TrackerUrl = clientTrackers.Count > 0 ? clientTrackers[0] : null,
                                DateAdded = DateTime.UtcNow,
                                Status = MapClientStatus(item.Status, remaining, total),
                                Category = item.Category,
                                SavePath = remappedPath,
                                SourcePath = remappedPath,
                            };

                            if (remaining == 0)
                            {
                                torrent.MarkForceCompleted();
                            }
                            else if (total > 0)
                            {
                                torrent.Progress = Math.Round((double)downloaded / total, 6);
                            }

                            torrent.UpdateRatio();
                            _torrentService.Add(torrent);
                            SaveClientTrackers(torrent.Id, clientTrackers);

                            existingTorrents[hash] = torrent;
                            result.Added++;
                            _logger.Info("Synced torrent {0} (client metadata) from download client {1}", torrent.Name, definition.Name);
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

        var items = provider.GetItems();
        var result = new List<DownloadClientRemoteItem>();

        foreach (var item in items)
        {
            var hash = item.InfoHash?.ToLowerInvariant() ?? "";
            var isInLibrary = !string.IsNullOrEmpty(hash) && existingTorrents.ContainsKey(hash);
            var libraryId = isInLibrary ? (int?)existingTorrents[hash].Id : null;

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
                LibraryTorrentId = libraryId
            });
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
                matchingItem = items?.FirstOrDefault(i => string.Equals(i.InfoHash, normalizedHash, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
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

        var remappedPath = _remotePathMappingService.Remap(definition?.Host, matchingItem?.OutputPath);

        Torrent torrent;
        if (torrentBytes != null && torrentBytes.Length > 0)
        {
            using var ms = new System.IO.MemoryStream(torrentBytes);
            var parsed = _torrentFileParser.Parse(ms);

            var total = parsed.TotalSize > 0 ? parsed.TotalSize : (matchingItem?.TotalSize ?? 0);
            var remaining = matchingItem?.RemainingSize ?? 0;
            var downloaded = Math.Max(0, total - remaining);

            torrent = new Torrent
            {
                Name = matchingItem?.Title ?? parsed.Name ?? normalizedHash,
                InfoHash = normalizedHash,
                TotalSize = total,
                Downloaded = downloaded,
                PieceCount = parsed.PieceCount,
                PieceLength = parsed.PieceLength,
                Comment = parsed.Comment,
                IsPrivate = parsed.IsPrivate,
                TrackerUrl = parsed.AnnounceUrl,
                DateAdded = DateTime.UtcNow,
                Status = MapClientStatus(matchingItem?.Status, remaining, total),
                Category = matchingItem?.Category,
                SavePath = remappedPath,
                SourcePath = remappedPath,
            };

            if (remaining == 0)
            {
                torrent.MarkForceCompleted();
                torrent.Status = MapClientStatus(matchingItem?.Status, remaining, total);
            }
            else if (total > 0)
            {
                torrent.Progress = Math.Round((double)downloaded / total, 6);
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
                Category = matchingItem.Category,
                SavePath = remappedPath,
                SourcePath = remappedPath,
            };

            if (remaining == 0)
            {
                torrent.MarkForceCompleted();
                torrent.Status = MapClientStatus(matchingItem.Status, remaining, total);
            }
            else if (total > 0)
            {
                torrent.Progress = Math.Round((double)downloaded / total, 6);
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
                foreach (var f in parsed.Files)
                {
                    _torrentFileService.Add(new TorrentFile
                    {
                        TorrentId = torrentId,
                        Path = f.Path,
                        Size = f.Size
                    });
                }
            }
        }

        if (_trackerEntryService != null)
        {
            var existingTrackers = _trackerEntryService.GetByTorrentId(torrentId)
                .Select(t => t.Url.Trim().ToLowerInvariant())
                .ToHashSet();

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
                            _trackerEntryService.Add(new TrackerEntry
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
                    _trackerEntryService.Add(new TrackerEntry
                    {
                        TorrentId = torrentId,
                        Url = clean,
                        Tier = 1,
                        Enabled = true
                    });
                }
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

        var tier = 1;
        foreach (var tr in clientTrackers)
        {
            var clean = tr.Trim();
            if (!string.IsNullOrEmpty(clean) && !existingTrackers.Contains(clean.ToLowerInvariant()))
            {
                _trackerEntryService.Add(new TrackerEntry
                {
                    TorrentId = torrentId,
                    Url = clean,
                    Tier = tier++,
                    Enabled = true
                });
                existingTrackers.Add(clean.ToLowerInvariant());
            }
        }
    }

    public SyncResult ImportTorrents(int clientId, List<string> infoHashes)
    {
        var result = new SyncResult();
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
                _logger.Debug(ex, "Failed to query items from client {0}", definition.Name);
            }

            foreach (var rawHash in infoHashes)
            {
                if (string.IsNullOrWhiteSpace(rawHash))
                {
                    result.Failed++;
                    continue;
                }

                var hash = rawHash.Trim().ToLowerInvariant();
                if (existingHashes.Contains(hash))
                {
                    result.Skipped++;
                    continue;
                }

                try
                {
                    clientItems.TryGetValue(hash, out var matchingItem);
                    ImportTorrentInternal(definition, provider, hash, matchingItem);
                    existingHashes.Add(hash);
                    result.Added++;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to import torrent {0} from client {1}", hash, clientId);
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

    protected virtual IIndexer CreateIndexer(IndexerDefinition definition)
    {
        return definition.IndexerType switch
        {
            "Prowlarr" => new NzbDrone.Core.Indexers.Prowlarr.ProwlarrIndexer(),
            "Torznab" => new NzbDrone.Core.Indexers.Torznab.TorznabIndexer(),
            "Newznab" => new NzbDrone.Core.Indexers.Newznab.NewznabIndexer(),
            _ => null
        };
    }

    protected virtual IDownloadClient CreateClient(DownloadClientDefinition definition)
    {
        return definition.ClientType switch
        {
            "QBitTorrent" => new NzbDrone.Core.DownloadClients.QBitTorrent.QBitTorrentClient
            {
                Host = definition.Host,
                Port = definition.Port,
                UseSsl = definition.UseSsl,
                Username = definition.Username,
                Password = definition.Password,
                Category = definition.Category,
            },
            "Transmission" => new NzbDrone.Core.DownloadClients.Transmission.TransmissionClient
            {
                Host = definition.Host,
                Port = definition.Port,
                UseSsl = definition.UseSsl,
                Username = definition.Username,
                Password = definition.Password,
                Category = definition.Category,
            },
            "Deluge" => new NzbDrone.Core.DownloadClients.Deluge.DelugeClient
            {
                Host = definition.Host,
                Port = definition.Port,
                UseSsl = definition.UseSsl,
                Username = definition.Username,
                Password = definition.Password,
                Category = definition.Category,
            },
            _ => null
        };
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
