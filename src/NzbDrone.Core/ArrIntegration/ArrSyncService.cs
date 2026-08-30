using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.ArrIntegration;

public interface IArrSyncService
{
    SyncResult Sync();
    bool TestConnection(int id);
    bool TestConnectionDirect(ArrConnectionDefinition definition);
    ArrTestResult TestConnectionDetailed(int id);
    ArrTestResult TestConnectionDetailedDirect(ArrConnectionDefinition definition);
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
    private readonly IDownloadHistoryService _downloadHistoryService;
    private readonly IArrMetadataEnricherService _metadataEnricherService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly IDownloadClientFactory _downloadClientFactory;
    private readonly Logger _logger;

    public ArrSyncService(
        IArrConnectionFactory connectionFactory,
        ITorrentService torrentService,
        IDownloadHistoryService downloadHistoryService = null,
        IArrMetadataEnricherService metadataEnricherService = null,
        ITrackerEntryService trackerEntryService = null,
        IDownloadClientFactory downloadClientFactory = null)
    {
        _connectionFactory = connectionFactory;
        _torrentService = torrentService;
        _downloadHistoryService = downloadHistoryService;
        _metadataEnricherService = metadataEnricherService;
        _trackerEntryService = trackerEntryService;
        _downloadClientFactory = downloadClientFactory;
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

                        // Ensure existing skipped torrent is in history and has metadata
                        if (_downloadHistoryService != null)
                        {
                            var hist = _downloadHistoryService.GetByInfoHash(record.InfoHash);
                            if (hist != null && string.IsNullOrEmpty(hist.DataJson) && _metadataEnricherService != null && record.MediaId.HasValue)
                            {
                                try
                                {
                                    var meta = _metadataEnricherService.FetchMetadataForRecord(record, definition);
                                    if (meta != null)
                                    {
                                        hist.DataJson = System.Text.Json.JsonSerializer.Serialize(meta);
                                        hist.Source = definition.ArrType;
                                        _downloadHistoryService.Update(hist);
                                    }
                                }
                                catch (Exception enrichEx)
                                {
                                    _logger.Debug(enrichEx, "Could not enrich metadata for existing record {0}", record.Title);
                                }
                            }
                        }

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

                    if (_downloadClientFactory != null && _trackerEntryService != null)
                    {
                        var activeClients = _downloadClientFactory.All().Where(c => c.Enable).ToList();
                        foreach (var clientDef in activeClients)
                        {
                            try
                            {
                                var dcProvider = _downloadClientFactory.CreateClient(clientDef);
                                if (dcProvider != null)
                                {
                                    var clientTrackers = dcProvider.GetTrackers(torrent.InfoHash);
                                    if (clientTrackers != null && clientTrackers.Count > 0)
                                    {
                                        var tier = 1;
                                        foreach (var trUrl in clientTrackers)
                                        {
                                            var clean = trUrl.Trim();
                                            if (!string.IsNullOrEmpty(clean))
                                            {
                                                _trackerEntryService.Add(new TrackerEntry
                                                {
                                                    TorrentId = torrent.Id,
                                                    Url = clean,
                                                    Tier = tier++,
                                                    Enabled = true
                                                });
                                            }
                                        }

                                        if (string.IsNullOrEmpty(torrent.TrackerUrl) && clientTrackers.Count > 0)
                                        {
                                            torrent.TrackerUrl = clientTrackers[0].Trim();
                                            _torrentService.Update(torrent);
                                        }

                                        break;
                                    }
                                }
                            }
                            catch (Exception dcEx)
                            {
                                _logger.Debug(dcEx, "Failed to get trackers from download client {0} during Arr sync", clientDef.Name);
                            }
                        }
                    }

                    if (_metadataEnricherService != null && record.MediaId.HasValue && _downloadHistoryService != null)
                    {
                        try
                        {
                            var meta = _metadataEnricherService.FetchMetadataForRecord(record, definition);
                            if (meta != null)
                            {
                                var hist = _downloadHistoryService.GetByInfoHash(torrent.InfoHash);
                                if (hist != null)
                                {
                                    hist.DataJson = System.Text.Json.JsonSerializer.Serialize(meta);
                                    hist.Source = definition.ArrType;
                                    _downloadHistoryService.Update(hist);
                                }
                            }
                        }
                        catch (Exception enrichEx)
                        {
                            _logger.Warn(enrichEx, "Could not enrich metadata for {0}", record.Title);
                        }
                    }
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
        return TestConnectionDetailed(id).Success;
    }

    public bool TestConnectionDirect(ArrConnectionDefinition definition)
    {
        return TestConnectionDetailedDirect(definition).Success;
    }

    public ArrTestResult TestConnectionDetailed(int id)
    {
        var definition = _connectionFactory.Get(id);
        if (definition == null)
        {
            _logger.Warn("No ArrConnection found with id {0}", id);
            return ArrTestResult.Fail($"No ArrConnection found with id {id}");
        }

        return TestConnectionDetailedDirect(definition);
    }

    public ArrTestResult TestConnectionDetailedDirect(ArrConnectionDefinition definition)
    {
        var provider = CreateProvider(definition);
        if (provider == null)
        {
            _logger.Warn("Unknown ArrType '{0}' for connection '{1}'", definition.ArrType, definition.Name);
            return ArrTestResult.Fail($"Unknown ArrType '{definition.ArrType}'. Supported types are Sonarr, Radarr, Lidarr.");
        }

        return provider.TestConnectionDetailed();
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
