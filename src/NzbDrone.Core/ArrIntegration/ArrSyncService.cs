using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.ArrIntegration;

public interface IArrSyncService
{
    SyncResult Sync();
}

public class SyncResult
{
    public int Added { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}

public class ArrSyncService : IArrSyncService
{
    private readonly IArrConnectionFactory _connectionFactory;
    private readonly ITorrentService _torrentService;
    private readonly Logger _logger;

    public ArrSyncService(
        IArrConnectionFactory connectionFactory,
        ITorrentService torrentService)
    {
        _connectionFactory = connectionFactory;
        _torrentService = torrentService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public SyncResult Sync()
    {
        var result = new SyncResult();
        var existingTorrents = _torrentService.GetAll();
        var existingHashes = new HashSet<string>(
            existingTorrents
                .Where(t => !string.IsNullOrEmpty(t.InfoHash))
                .Select(t => t.InfoHash.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        var providers = _connectionFactory.GetAvailableProviders();

        foreach (var provider in providers)
        {
            try
            {
                var records = provider.GetDownloadHistory();

                foreach (var record in records)
                {
                    if (string.IsNullOrEmpty(record.InfoHash))
                    {
                        continue;
                    }

                    if (existingHashes.Contains(record.InfoHash.ToLowerInvariant()))
                    {
                        result.Skipped++;
                        continue;
                    }

                    var torrent = new Torrent
                    {
                        Name = record.Title,
                        InfoHash = record.InfoHash.ToLowerInvariant(),
                        TotalSize = record.Size,
                        DateAdded = DateTime.UtcNow,
                        Status = TorrentStatus.Queued
                    };

                    _torrentService.Add(torrent);
                    existingHashes.Add(torrent.InfoHash);
                    result.Added++;
                    _logger.Info("Synced from {0}: {1}", provider.Name, record.Title);
                }
            }
            catch (Exception ex)
            {
                result.Failed++;
                _logger.Error(ex, "Sync failed for {0}", provider.Name);
            }
        }

        _logger.Info("Arr sync complete: {0} added, {1} skipped, {2} failed", result.Added, result.Skipped, result.Failed);
        return result;
    }
}
