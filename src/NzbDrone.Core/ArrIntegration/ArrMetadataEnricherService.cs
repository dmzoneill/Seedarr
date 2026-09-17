using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NLog;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.ArrIntegration
{
    public class ArrMetadataEnricherService : IArrMetadataEnricherService
    {
        private readonly IArrConnectionFactory _connectionFactory;
        private readonly IDownloadHistoryRepository _downloadHistoryRepository;
        private readonly ITorrentRepository _torrentRepository;
        private readonly Func<ArrConnectionDefinition, IArrConnection> _providerFactory;
        private readonly ConcurrentDictionary<string, MediaMetadata> _mediaDetailsCache = new();
        private readonly Logger _logger;

        public ArrMetadataEnricherService(
            IArrConnectionFactory connectionFactory,
            IDownloadHistoryRepository downloadHistoryRepository,
            ITorrentRepository torrentRepository = null,
            Func<ArrConnectionDefinition, IArrConnection> providerFactory = null)
        {
            _connectionFactory = connectionFactory;
            _downloadHistoryRepository = downloadHistoryRepository;
            _torrentRepository = torrentRepository;
            _providerFactory = providerFactory;
            _logger = LogManager.GetCurrentClassLogger();
        }

        public Dictionary<string, ArrHistoryRecord> PreFetchDownloadHistories()
        {
            var cache = new Dictionary<string, ArrHistoryRecord>(StringComparer.OrdinalIgnoreCase);
            var definitions = _connectionFactory?.All();
            if (definitions == null)
            {
                return cache;
            }

            foreach (var def in definitions)
            {
                if (!def.Enable)
                {
                    continue;
                }

                var provider = CreateProvider(def);
                if (provider == null)
                {
                    continue;
                }

                try
                {
                    var records = provider.GetDownloadHistory();
                    if (records != null)
                    {
                        foreach (var rec in records)
                        {
                            if (!string.IsNullOrWhiteSpace(rec.InfoHash))
                            {
                                var normalized = rec.InfoHash.Trim().ToLowerInvariant();
                                cache.TryAdd(normalized, new ArrHistoryRecord(rec, def));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to query download history from {0} during batch pre-fetch", def.Name);
                }
            }

            return cache;
        }

        public MediaMetadata EnrichHistoryEntry(int historyId) =>
            EnrichHistoryEntry(historyId, null);

        public MediaMetadata EnrichHistoryEntry(
            int historyId,
            IReadOnlyDictionary<string, ArrHistoryRecord> cachedHistories)
        {
            var history = _downloadHistoryRepository.Get(historyId);
            if (history == null)
            {
                return null;
            }

            if (cachedHistories != null)
            {
                if (!string.IsNullOrWhiteSpace(history.InfoHash))
                {
                    var normalized = history.InfoHash.Trim().ToLowerInvariant();
                    if (cachedHistories.TryGetValue(normalized, out var match) && match != null)
                    {
                        var metadata = FetchMetadataForRecord(match.Record, match.Definition);
                        if (metadata != null)
                        {
                            history.DataJson = JsonSerializer.Serialize(metadata);
                            if (string.IsNullOrEmpty(history.Source))
                            {
                                history.Source = match.Definition.ArrType;
                            }

                            _downloadHistoryRepository.Update(history);
                            return metadata;
                        }
                    }
                }

                return LookupAndEnrichByTitle(history);
            }

            var definitions = _connectionFactory.All();

            // Step 1: Query connected Arr download history for matching info_hash
            foreach (var def in definitions)
            {
                if (!def.Enable)
                {
                    continue;
                }

                var provider = CreateProvider(def);
                if (provider == null)
                {
                    continue;
                }

                try
                {
                    var records = provider.GetDownloadHistory();
                    foreach (var rec in records)
                    {
                        if (string.Equals(rec.InfoHash, history.InfoHash, StringComparison.OrdinalIgnoreCase))
                        {
                            var metadata = FetchMetadataForRecord(rec, def);
                            if (metadata != null)
                            {
                                history.DataJson = JsonSerializer.Serialize(metadata);
                                if (string.IsNullOrEmpty(history.Source))
                                {
                                    history.Source = def.ArrType;
                                }

                                _downloadHistoryRepository.Update(history);
                                return metadata;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to query {0} during metadata enrichment for history {1}", def.Name, historyId);
                }
            }

            // Step 2: Fallback to smart title-based lookup on Sonarr, Radarr, or Lidarr
            return LookupAndEnrichByTitle(history);
        }

        public MediaMetadata LookupAndEnrichByTitle(DownloadHistory history)
        {
            if (history == null || string.IsNullOrWhiteSpace(history.Title))
            {
                return null;
            }

            var cleanTitle = CleanReleaseTitle(history.Title);
            if (string.IsNullOrWhiteSpace(cleanTitle))
            {
                return null;
            }

            var definitions = _connectionFactory.All().Where(d => d.Enable).ToList();

            foreach (var def in definitions)
            {
                var provider = CreateProvider(def);
                if (provider == null)
                {
                    continue;
                }

                try
                {
                    var metadata = provider.LookupMedia(cleanTitle);
                    if (metadata != null)
                    {
                        history.DataJson = JsonSerializer.Serialize(metadata);
                        if (string.IsNullOrEmpty(history.Source))
                        {
                            history.Source = def.ArrType;
                        }

                        _downloadHistoryRepository.Update(history);
                        _logger.Info("Enriched metadata for '{0}' via {1} title lookup", history.Title, def.ArrType);
                        return metadata;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Title lookup on {0} for '{1}' failed", def.Name, cleanTitle);
                }
            }

            return null;
        }

        public MediaMetadata FetchMetadataForRecord(ArrDownloadRecord record, ArrConnectionDefinition definition)
        {
            if (record == null || definition == null)
            {
                return null;
            }

            if (!record.MediaId.HasValue)
            {
                return null;
            }

            var cacheKey = $"{definition.Id}:{record.MediaId.Value}";
            if (_mediaDetailsCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var provider = CreateProvider(definition);
            if (provider == null)
            {
                return null;
            }

            try
            {
                var metadata = provider.GetMediaDetails(record.MediaId.Value);
                if (metadata != null)
                {
                    if (string.IsNullOrEmpty(metadata.Title))
                    {
                        metadata.Title = record.Title;
                    }

                    _mediaDetailsCache[cacheKey] = metadata;
                }

                return metadata;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error fetching media details for record {0}", record.Title);
                return null;
            }
        }

        public void EnrichAll()
        {
            _mediaDetailsCache.Clear();
            var all = _downloadHistoryRepository.All();
            var toEnrich = all.Where(item => string.IsNullOrEmpty(item.DataJson)).ToList();
            if (toEnrich.Count == 0)
            {
                return;
            }

            var cachedHistories = PreFetchDownloadHistories();
            foreach (var item in toEnrich)
            {
                EnrichHistoryEntry(item.Id, cachedHistories);
            }
        }

        public int ReconcileAndEnrichAll()
        {
            _mediaDetailsCache.Clear();
            var allTorrents = _torrentRepository.All().ToList();
            var reconciledCount = 0;
            var enrichedCount = 0;

            var toEnrich = new List<DownloadHistory>();

            foreach (var torrent in allTorrents)
            {
                if (string.IsNullOrWhiteSpace(torrent.InfoHash))
                {
                    continue;
                }

                var existing = _downloadHistoryRepository.FindByInfoHash(torrent.InfoHash);
                if (existing == null)
                {
                    existing = new DownloadHistory
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
                        Source = torrent.IsPrivate ? "Private Tracker" : "Public Tracker"
                    };

                    _downloadHistoryRepository.Insert(existing);
                    reconciledCount++;
                }

                if (string.IsNullOrEmpty(existing.DataJson))
                {
                    toEnrich.Add(existing);
                }
            }

            if (toEnrich.Count > 0)
            {
                var cachedHistories = PreFetchDownloadHistories();
                foreach (var history in toEnrich)
                {
                    var meta = EnrichHistoryEntry(history.Id, cachedHistories);
                    if (meta != null)
                    {
                        enrichedCount++;
                    }
                }
            }

            _logger.Info("Reconciliation complete: {0} backfilled, {1} metadata enriched", reconciledCount, enrichedCount);
            return reconciledCount + enrichedCount;
        }

        public static string CleanReleaseTitle(string rawTitle)
        {
            return ReleaseTitleParser.CleanTitle(rawTitle);
        }

        protected virtual IArrConnection CreateProvider(ArrConnectionDefinition definition)
        {
            if (_providerFactory != null)
            {
                return _providerFactory(definition);
            }

            IArrConnection provider;
            switch (definition.ArrType?.ToLowerInvariant())
            {
                case "sonarr":
                    provider = new SonarrConnection();
                    break;
                case "radarr":
                    provider = new RadarrConnection();
                    break;
                case "lidarr":
                    provider = new LidarrConnection();
                    break;
                case "readarr":
                    provider = new ReadarrConnection();
                    break;
                case "whisparr":
                    provider = new WhisparrConnection();
                    break;
                default:
                    return null;
            }

            provider.Url = ArrConnectionResources.NormalizeUrl(definition.Url);
            provider.ApiKey = definition.ApiKey;
            provider.AcceptInvalidCertificates = definition.AcceptInvalidCertificates;
            provider.ConnectionId = definition.Id;
            return provider;
        }
    }
}
