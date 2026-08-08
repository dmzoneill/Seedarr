using System;
using System.Text.Json;
using NLog;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.ArrIntegration
{
    public class ArrMetadataEnricherService : IArrMetadataEnricherService
    {
        private readonly IArrConnectionFactory _connectionFactory;
        private readonly IDownloadHistoryRepository _downloadHistoryRepository;
        private readonly Logger _logger;

        public ArrMetadataEnricherService(
            IArrConnectionFactory connectionFactory,
            IDownloadHistoryRepository downloadHistoryRepository)
        {
            _connectionFactory = connectionFactory;
            _downloadHistoryRepository = downloadHistoryRepository;
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
