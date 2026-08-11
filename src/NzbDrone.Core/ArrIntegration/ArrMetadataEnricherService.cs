using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.ArrIntegration
{
    public class ArrMetadataEnricherService : IArrMetadataEnricherService
    {
        private readonly IArrConnectionFactory _connectionFactory;
        private readonly IDownloadHistoryRepository _downloadHistoryRepository;
        private readonly ITorrentRepository _torrentRepository;
        private readonly Logger _logger;

        private static readonly Regex SceneTagsRegex = new(
            @"\b(1080p|720p|2160p|4k|uhd|hdr|hdr10|dv|remux|bluray|blu-ray|bdrip|web-dl|webrip|web|hdtv|x264|x265|h264|h265|hevc|aac|dts|dts-hd|truehd|atmos|flac|mp3|extended|repack|proper|complete|season|\bS\d{1,2}(E\d{1,2})?\b|\bEP?\d{1,3}\b)\b.*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex YearRegex = new(@"\b(19\d{2}|20\d{2})\b", RegexOptions.Compiled);

        public ArrMetadataEnricherService(
            IArrConnectionFactory connectionFactory,
            IDownloadHistoryRepository downloadHistoryRepository,
            ITorrentRepository torrentRepository = null)
        {
            _connectionFactory = connectionFactory;
            _downloadHistoryRepository = downloadHistoryRepository;
            _torrentRepository = torrentRepository;
            _logger = LogManager.GetCurrentClassLogger();
        }

        public MediaMetadata EnrichHistoryEntry(int historyId)
        {
            var history = _downloadHistoryRepository.Get(historyId);
            if (history == null)
            {
                return null;
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

            var provider = CreateProvider(definition);
            if (provider == null)
            {
                return null;
            }

            try
            {
                var metadata = provider.GetMediaDetails(record.MediaId.Value);
                if (metadata != null && string.IsNullOrEmpty(metadata.Title))
                {
                    metadata.Title = record.Title;
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
            var all = _downloadHistoryRepository.All();
            foreach (var item in all)
            {
                if (string.IsNullOrEmpty(item.DataJson))
                {
                    EnrichHistoryEntry(item.Id);
                }
            }
        }

        public int ReconcileAndEnrichAll()
        {
            var allTorrents = _torrentRepository.All().ToList();
            var reconciledCount = 0;
            var enrichedCount = 0;

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
                    var meta = EnrichHistoryEntry(existing.Id);
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
            if (string.IsNullOrWhiteSpace(rawTitle))
            {
                return string.Empty;
            }

            var clean = rawTitle.Trim();

            // Strip extension if present
            if (clean.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) ||
                clean.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                clean.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                clean.EndsWith(".avi", StringComparison.OrdinalIgnoreCase))
            {
                var dotIdx = clean.LastIndexOf('.');
                if (dotIdx > 0)
                {
                    clean = clean.Substring(0, dotIdx);
                }
            }

            // Replace separators with spaces
            clean = clean.Replace('.', ' ').Replace('_', ' ').Replace('+', ' ');

            // Strip release scene tags
            clean = SceneTagsRegex.Replace(clean, string.Empty);

            // Clean multiple whitespace
            clean = Regex.Replace(clean, @"\s+", " ").Trim();

            return clean;
        }

        private IArrConnection CreateProvider(ArrConnectionDefinition definition)
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
}
