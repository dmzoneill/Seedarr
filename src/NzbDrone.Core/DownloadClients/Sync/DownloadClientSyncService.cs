using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.DownloadClients.Sync;

public interface IDownloadClientSyncService
{
    SyncResult Sync();
    List<DownloadClientRemoteItem> GetClientItems(int clientId);
    Torrent ImportTorrent(int clientId, string infoHash);
    SyncResult ImportTorrents(int clientId, List<string> infoHashes);
}

public class DownloadClientSyncService : IDownloadClientSyncService
{
    private readonly IDownloadClientFactory _downloadClientFactory;
    private readonly IIndexerFactory _indexerFactory;
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly Logger _logger;

    public DownloadClientSyncService(
        IDownloadClientFactory downloadClientFactory,
        IIndexerFactory indexerFactory,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser)
    {
        _downloadClientFactory = downloadClientFactory;
        _indexerFactory = indexerFactory;
        _torrentService = torrentService;
        _torrentFileParser = torrentFileParser;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public SyncResult Sync()
    {
        var result = new SyncResult();
        var existingHashes = new HashSet<string>(
            _torrentService.GetAll()
                .Where(t => !string.IsNullOrEmpty(t.InfoHash))
                .Select(t => t.InfoHash.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

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
                    if (existingHashes.Contains(hash))
                    {
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

                        var torrent = new Torrent
                        {
                            Name = parsed.Name ?? item.Title,
                            InfoHash = hash,
                            TotalSize = parsed.TotalSize,
                            PieceCount = parsed.PieceCount,
                            PieceLength = parsed.PieceLength,
                            DateAdded = DateTime.UtcNow,
                            Status = TorrentStatus.Stopped
                        };

                        _torrentService.Add(torrent);
                        existingHashes.Add(hash);
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
                _logger.Error(ex, "Failed to sync download client {0}", definition.Name);
                result.Failed++;
            }
        }

        return result;
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
            matchingItem = items.FirstOrDefault(i => string.Equals(i.InfoHash, normalizedHash, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to query items from client {0}", definition.Name);
        }

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

        Torrent torrent;
        if (torrentBytes != null && torrentBytes.Length > 0)
        {
            using var ms = new System.IO.MemoryStream(torrentBytes);
            var parsed = _torrentFileParser.Parse(ms);

            torrent = new Torrent
            {
                Name = matchingItem?.Title ?? parsed.Name ?? normalizedHash,
                InfoHash = normalizedHash,
                TotalSize = parsed.TotalSize > 0 ? parsed.TotalSize : (matchingItem?.TotalSize ?? 0),
                PieceCount = parsed.PieceCount,
                PieceLength = parsed.PieceLength,
                DateAdded = DateTime.UtcNow,
                Status = TorrentStatus.Stopped
            };
        }
        else if (matchingItem != null)
        {
            torrent = new Torrent
            {
                Name = !string.IsNullOrEmpty(matchingItem.Title) ? matchingItem.Title : normalizedHash,
                InfoHash = normalizedHash,
                TotalSize = matchingItem.TotalSize,
                DateAdded = DateTime.UtcNow,
                Status = TorrentStatus.Stopped
            };
        }
        else
        {
            throw new InvalidOperationException($"Could not fetch torrent metadata for hash {normalizedHash} from download client or indexers.");
        }

        _torrentService.Add(torrent);
        _logger.Info("Imported torrent {0} from download client {1}", torrent.Name, definition.Name);
        return torrent;
    }

    public SyncResult ImportTorrents(int clientId, List<string> infoHashes)
    {
        var result = new SyncResult();
        if (infoHashes == null || infoHashes.Count == 0)
        {
            return result;
        }

        foreach (var hash in infoHashes)
        {
            try
            {
                var existing = _torrentService.GetAll()
                    .FirstOrDefault(t => string.Equals(t.InfoHash, hash, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    result.Skipped++;
                    continue;
                }

                ImportTorrent(clientId, hash);
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
}
