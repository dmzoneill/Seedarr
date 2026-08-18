using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.DownloadClients.Sync;

public interface IDownloadClientSyncService
{
    SyncResult Sync();
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

                    // User requested a framework/strategy to query indexers (e.g. Prowlarr) for metadata/torrent
                    // or to get it from the download client.
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

    private byte[] SearchIndexersForTorrent(string infoHash)
    {
        // Strategy pattern to query indexers (Prowlarr, Torznab, etc)
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

    private IIndexer CreateIndexer(IndexerDefinition definition)
    {
        return definition.IndexerType switch
        {
            "Prowlarr" => new NzbDrone.Core.Indexers.Prowlarr.ProwlarrIndexer(),
            "Torznab" => new NzbDrone.Core.Indexers.Torznab.TorznabIndexer(),
            "Newznab" => new NzbDrone.Core.Indexers.Newznab.NewznabIndexer(),
            _ => null
        };
    }

    private IDownloadClient CreateClient(DownloadClientDefinition definition)
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

public class SyncResult
{
    public int Added { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}
