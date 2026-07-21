using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.ArrIntegration;

public interface IArrSyncService
{
    SyncResult Sync();
    bool TestConnection(int id);
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

        var definitions = _connectionFactory.All();

        foreach (var definition in definitions)
        {
            if (!definition.Enable || !definition.SyncEnabled)
            {
                continue;
            }

            var provider = CreateProvider(definition);
            if (provider == null)
            {
                _logger.Warn("Unknown ArrType '{0}' for connection '{1}'", definition.ArrType, definition.Name);
                result.Failed++;
                continue;
            }

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

    public bool TestConnection(int id)
    {
        var definition = _connectionFactory.Get(id);
        if (definition == null)
        {
            _logger.Warn("No ArrConnection found with id {0}", id);
            return false;
        }

        var provider = CreateProvider(definition);
        if (provider == null)
        {
            _logger.Warn("Unknown ArrType '{0}' for connection '{1}'", definition.ArrType, definition.Name);
            return false;
        }

        return provider.TestConnection();
    }

    protected virtual IArrConnection CreateProvider(ArrConnectionDefinition definition)
    {
        IArrConnection provider;

        switch (definition.ArrType)
        {
            case "Sonarr":
                provider = new SonarrConnection();
                break;
            case "Radarr":
                provider = new RadarrConnection();
                break;
            case "Lidarr":
                provider = new LidarrConnection();
                break;
            default:
                return null;
        }

        provider.Url = definition.Url;
        provider.ApiKey = definition.ApiKey;

        return provider;
    }
}
