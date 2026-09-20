using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Http;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Validation;
using Polly;

namespace NzbDrone.Core.ArrIntegration.Webhook;

public interface IArrWebhookService
{
    ArrWebhookResult ProcessWebhook(ArrWebhookPayload payload);
}

public class ArrWebhookResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public string InfoHash { get; set; }
}

public class ArrWebhookService : IArrWebhookService
{
    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        AllowAutoRedirect = false,
    });
    private static readonly ResiliencePipeline SharedPolicy = ResiliencePolicies.GetArrApiPolicy();
    private static readonly Regex InfoHashRegex = new(
        @"^[0-9a-fA-F]{40}$|^[0-9a-fA-F]{64}$",
        RegexOptions.Compiled);

    private readonly HttpClient _client;
    private readonly ResiliencePipeline _policy;
    private readonly IArrConnectionFactory _connectionFactory;
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly IDownloadClientFactory _downloadClientFactory;
    private readonly IDownloadHistoryService _downloadHistoryService;
    private readonly IArchiveExtractorService _archiveExtractorService;
    private readonly IConfigService _configService;
    private readonly ITorrentMediaMetadataRepository _mediaMetadataRepository;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;

    public ArrWebhookService(
        IArrConnectionFactory connectionFactory,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        ITrackerEntryService trackerEntryService = null,
        ITorrentFileService torrentFileService = null,
        IDownloadClientFactory downloadClientFactory = null,
        IDownloadHistoryService downloadHistoryService = null,
        IArchiveExtractorService archiveExtractorService = null)
        : this(connectionFactory, torrentService, torrentFileParser, trackerEntryService, torrentFileService, downloadClientFactory, downloadHistoryService, archiveExtractorService, null, null, null, null, null)
    {
    }

    public ArrWebhookService(
        IArrConnectionFactory connectionFactory,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        HttpClient client,
        ResiliencePipeline policy)
        : this(connectionFactory, torrentService, torrentFileParser, null, null, null, null, null, client, policy, null, null, null)
    {
    }

    public ArrWebhookService(
        IArrConnectionFactory connectionFactory,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        ITrackerEntryService trackerEntryService,
        ITorrentFileService torrentFileService,
        IDownloadClientFactory downloadClientFactory,
        HttpClient client,
        ResiliencePipeline policy)
        : this(connectionFactory, torrentService, torrentFileParser, trackerEntryService, torrentFileService, downloadClientFactory, null, null, client, policy, null, null, null)
    {
    }

    public ArrWebhookService(
        IArrConnectionFactory connectionFactory,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        ITrackerEntryService trackerEntryService,
        ITorrentFileService torrentFileService,
        IDownloadClientFactory downloadClientFactory,
        IDownloadHistoryService downloadHistoryService,
        HttpClient client,
        ResiliencePipeline policy)
        : this(connectionFactory, torrentService, torrentFileParser, trackerEntryService, torrentFileService, downloadClientFactory, downloadHistoryService, null, client, policy, null, null, null)
    {
    }

    public ArrWebhookService(
        IArrConnectionFactory connectionFactory,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        ITrackerEntryService trackerEntryService,
        ITorrentFileService torrentFileService,
        IDownloadClientFactory downloadClientFactory,
        IDownloadHistoryService downloadHistoryService,
        IArchiveExtractorService archiveExtractorService,
        HttpClient client,
        ResiliencePipeline policy)
        : this(connectionFactory, torrentService, torrentFileParser, trackerEntryService, torrentFileService, downloadClientFactory, downloadHistoryService, archiveExtractorService, client, policy, null, null, null)
    {
    }

    public ArrWebhookService(
        IArrConnectionFactory connectionFactory,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        ITrackerEntryService trackerEntryService,
        ITorrentFileService torrentFileService,
        IDownloadClientFactory downloadClientFactory,
        IDownloadHistoryService downloadHistoryService,
        IArchiveExtractorService archiveExtractorService,
        HttpClient client,
        ResiliencePipeline policy,
        IConfigService configService = null,
        ITorrentMediaMetadataRepository mediaMetadataRepository = null,
        IEventAggregator eventAggregator = null)
    {
        _connectionFactory = connectionFactory;
        _torrentService = torrentService;
        _torrentFileParser = torrentFileParser;
        _trackerEntryService = trackerEntryService;
        _torrentFileService = torrentFileService;
        _downloadClientFactory = downloadClientFactory;
        _downloadHistoryService = downloadHistoryService;
        _archiveExtractorService = archiveExtractorService;
        _configService = configService;
        _mediaMetadataRepository = mediaMetadataRepository;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
        _client = client ?? SharedClient;
        _policy = policy ?? SharedPolicy;
    }

    public int EnrichDelayMs { get; set; } = 5000;

    public bool EnablePostImportCleanup { get; set; } = true;

    public ArrWebhookResult ProcessWebhook(ArrWebhookPayload payload)
    {
        if (payload == null)
        {
            return new ArrWebhookResult { Success = false, Message = "Payload is null" };
        }

        if (string.Equals(payload.EventType, "Test", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Info("Webhook: handled test event");
            return new ArrWebhookResult
            {
                Success = true,
                Message = "Seedarr webhook connection test successful"
            };
        }

        if (string.Equals(payload.EventType, "SiteDelete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "PerformerDelete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "MovieDelete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "MovieFileDelete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "EpisodeFileDelete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "SeriesDelete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "TrackFileDelete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "ArtistDelete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "BookFileDelete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "AuthorDelete", StringComparison.OrdinalIgnoreCase) ||
            (payload.EventType != null && payload.EventType.EndsWith("Delete", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.Info("Webhook: handled delete event '{0}'", payload.EventType);
            return new ArrWebhookResult { Success = true, Message = $"Handled {payload.EventType} event" };
        }

        if (string.Equals(payload.EventType, "MovieDownload", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "AlbumDownload", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "BookDownload", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "EpisodeDownload", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "Download", StringComparison.OrdinalIgnoreCase))
        {
            return ProcessDownload(payload);
        }

        if (string.Equals(payload.EventType, "Rename", StringComparison.OrdinalIgnoreCase))
        {
            return ProcessRename(payload);
        }

        var isGrab = string.Equals(payload.EventType, "Grab", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "MovieGrab", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "AlbumGrab", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "BookGrab", StringComparison.OrdinalIgnoreCase);

        if (!isGrab)
        {
            return new ArrWebhookResult { Success = true, Message = $"Ignored event type: {payload.EventType}" };
        }

        var downloadId = payload.DownloadId;
        if (string.IsNullOrEmpty(downloadId))
        {
            return new ArrWebhookResult { Success = false, Message = "No downloadId in webhook payload" };
        }

        if (IsUsenetGrab(payload))
        {
            _logger.Info(
                "Webhook: rejected Usenet grab for client '{0}' (type '{1}'), downloadId '{2}'",
                payload.DownloadClient,
                payload.DownloadClientType,
                downloadId);

            return new ArrWebhookResult
            {
                Success = false,
                Message = $"Rejected Usenet grab ({payload.DownloadClientType ?? payload.DownloadClient})"
            };
        }

        if (!IsValidInfoHash(downloadId))
        {
            _logger.Warn("Webhook: rejected non-hex or invalid infohash downloadId '{0}'", downloadId);
            return new ArrWebhookResult
            {
                Success = false,
                Message = $"Invalid infohash or non-torrent downloadId: {downloadId}"
            };
        }

        var infoHash = downloadId.Trim().ToLowerInvariant();

        var existing = _torrentService.GetAll().FirstOrDefault(t =>
            string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return new ArrWebhookResult { Success = true, Message = "Torrent already exists", InfoHash = infoHash };
        }

        var releaseTitle = payload.Release?.ReleaseTitle;
        if (string.IsNullOrWhiteSpace(releaseTitle))
        {
            if (payload.Album != null && !string.IsNullOrWhiteSpace(payload.Album.Title))
            {
                releaseTitle = payload.Artist != null && !string.IsNullOrWhiteSpace(payload.Artist.Name)
                    ? $"{payload.Artist.Name} - {payload.Album.Title}"
                    : payload.Album.Title;
            }
            else if (payload.Book != null && !string.IsNullOrWhiteSpace(payload.Book.Title))
            {
                releaseTitle = payload.Author != null && !string.IsNullOrWhiteSpace(payload.Author.Name)
                    ? $"{payload.Author.Name} - {payload.Book.Title}"
                    : payload.Book.Title;
            }
            else if (payload.Movie != null && !string.IsNullOrWhiteSpace(payload.Movie.Title))
            {
                releaseTitle = payload.Movie.Year > 0
                    ? $"{payload.Movie.Title} ({payload.Movie.Year})"
                    : payload.Movie.Title;
            }
            else if (payload.Series != null && !string.IsNullOrWhiteSpace(payload.Series.Title))
            {
                releaseTitle = payload.Series.Title;
            }
        }

        _logger.Info(
            "Webhook received: {0} grabbed '{1}' from {2}",
            payload.InstanceName,
            releaseTitle,
            payload.Release?.Indexer);

        var connection = FindConnection(payload);
        var category = connection?.Category ?? connection?.ArrType?.ToLowerInvariant();
        var savePath = connection?.SavePath ?? _configService?.WatchFolderPath;

        if (connection != null && !connection.EnableAutomaticAdd)
        {
            _logger.Info(
                "Webhook: automatic add is disabled for connection '{0}' ({1}), skipping torrent '{2}'",
                connection.Name,
                connection.ArrType,
                releaseTitle ?? infoHash);

            if (_downloadHistoryService != null && _downloadHistoryService.GetByInfoHash(infoHash) == null)
            {
                var historyTorrent = new Torrent
                {
                    Name = releaseTitle ?? infoHash,
                    InfoHash = infoHash,
                    TotalSize = payload.Release?.Size ?? 0,
                    DateAdded = DateTime.UtcNow,
                    Status = TorrentStatus.Queued,
                    Category = category,
                    SavePath = savePath
                };

                _downloadHistoryService.RecordTorrentAdded(
                    historyTorrent,
                    source: connection.ArrType,
                    indexerName: payload.Release?.Indexer);
            }

            return new ArrWebhookResult
            {
                Success = true,
                Message = "Skipped automatic add",
                InfoHash = infoHash
            };
        }

        var torrent = new Torrent
        {
            Name = releaseTitle ?? infoHash,
            InfoHash = infoHash,
            TotalSize = payload.Release?.Size ?? 0,
            DateAdded = DateTime.UtcNow,
            Status = TorrentStatus.Queued,
            Category = category,
            SavePath = savePath
        };

        _torrentService.Add(torrent);
        _logger.Info("Webhook: added '{0}' with basic metadata", torrent.Name);

        if (connection != null)
        {
            _ = EnrichTorrentFromHistoryAsync(torrent.Id, infoHash, downloadId.Trim(), connection, payload.InstanceName, CancellationToken.None);
        }

        return new ArrWebhookResult { Success = true, Message = "Added with basic metadata", InfoHash = infoHash };
    }

    private ArrWebhookResult ProcessDownload(ArrWebhookPayload payload)
    {
        var downloadId = payload.DownloadId;
        if (string.IsNullOrEmpty(downloadId))
        {
            return new ArrWebhookResult { Success = false, Message = "No downloadId in webhook payload" };
        }

        if (!IsValidInfoHash(downloadId))
        {
            _logger.Warn("Webhook: rejected non-hex or invalid infohash downloadId '{0}'", downloadId);
            return new ArrWebhookResult
            {
                Success = false,
                Message = $"Invalid infohash or non-torrent downloadId: {downloadId}"
            };
        }

        var infoHash = downloadId.Trim().ToLowerInvariant();
        var existing = _torrentService.GetAll().FirstOrDefault(t =>
            string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            _logger.Info("Webhook {0}: torrent with infohash '{1}' not found", payload.EventType, infoHash);
            return new ArrWebhookResult
            {
                Success = true,
                Message = $"Torrent not found for {payload.EventType}",
                InfoHash = infoHash
            };
        }

        existing.Status = TorrentStatus.Seeding;
        existing.Progress = 1.0;

        var connection = FindConnection(payload);
        if (connection != null)
        {
            if (string.IsNullOrEmpty(existing.Category))
            {
                existing.Category = connection.Category ?? connection.ArrType?.ToLowerInvariant();
            }

            if (string.IsNullOrEmpty(existing.SavePath))
            {
                existing.SavePath = connection.SavePath ?? _configService?.WatchFolderPath;
            }
        }

        _torrentService.Update(existing);
        _logger.Info("Webhook {0}: marked torrent '{1}' as Seeding (imported)", payload.EventType, existing.Name);

        if (_mediaMetadataRepository != null)
        {
            try
            {
                var meta = _mediaMetadataRepository.GetByTorrentId(existing.Id) ?? new TorrentMediaMetadata { TorrentId = existing.Id };
                var hasMetadataChanges = false;

                if (payload.Movie != null)
                {
                    meta.ArrType = "Radarr";
                    if (payload.Movie.Id > 0)
                    {
                        meta.ArrMediaId = payload.Movie.Id;
                    }

                    if (!string.IsNullOrWhiteSpace(payload.Movie.Title))
                    {
                        meta.Title = payload.Movie.Title;
                    }

                    if (payload.Movie.Year > 0)
                    {
                        meta.Year = payload.Movie.Year;
                    }

                    if (!string.IsNullOrWhiteSpace(payload.Movie.ImdbId))
                    {
                        meta.ImdbId = payload.Movie.ImdbId;
                    }

                    if (payload.Movie.TmdbId > 0)
                    {
                        meta.TmdbId = payload.Movie.TmdbId.ToString();
                    }

                    hasMetadataChanges = true;
                }
                else if (payload.Series != null)
                {
                    meta.ArrType = "Sonarr";
                    if (payload.Series.Id > 0)
                    {
                        meta.ArrMediaId = payload.Series.Id;
                    }

                    if (!string.IsNullOrWhiteSpace(payload.Series.Title))
                    {
                        meta.SeriesName = payload.Series.Title;
                        meta.Title = payload.Series.Title;
                    }

                    if (payload.Series.Year > 0)
                    {
                        meta.Year = payload.Series.Year;
                    }

                    if (payload.Series.TvdbId > 0)
                    {
                        meta.TvdbId = payload.Series.TvdbId.ToString();
                    }

                    if (!string.IsNullOrWhiteSpace(payload.Series.ImdbId))
                    {
                        meta.ImdbId = payload.Series.ImdbId;
                    }

                    hasMetadataChanges = true;
                }
                else if (payload.Album != null || payload.Artist != null)
                {
                    meta.ArrType = "Lidarr";
                    if (payload.Artist != null && !string.IsNullOrWhiteSpace(payload.Artist.Name))
                    {
                        meta.ArtistName = payload.Artist.Name;
                    }

                    if (payload.Album != null && !string.IsNullOrWhiteSpace(payload.Album.Title))
                    {
                        meta.AlbumTitle = payload.Album.Title;
                    }

                    if (payload.Album?.Id > 0)
                    {
                        meta.ArrMediaId = payload.Album.Id;
                    }

                    hasMetadataChanges = true;
                }
                else if (payload.Book != null || payload.Author != null)
                {
                    meta.ArrType = "Readarr";
                    if (payload.Author != null && !string.IsNullOrWhiteSpace(payload.Author.Name))
                    {
                        meta.Author = payload.Author.Name;
                    }

                    if (payload.Book != null && !string.IsNullOrWhiteSpace(payload.Book.Title))
                    {
                        meta.BookTitle = payload.Book.Title;
                    }

                    if (payload.Book?.Id > 0)
                    {
                        meta.ArrMediaId = payload.Book.Id;
                    }

                    hasMetadataChanges = true;
                }

                if (hasMetadataChanges)
                {
                    _mediaMetadataRepository.Upsert(meta);
                    _logger.Info("Webhook {0}: updated TorrentMediaMetadata for '{1}'", payload.EventType, existing.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Webhook {0}: failed to update TorrentMediaMetadata for '{1}'", payload.EventType, existing.Name);
            }
        }

        if (EnablePostImportCleanup && _archiveExtractorService != null)
        {
            try
            {
                var prunedCount = _archiveExtractorService.CleanupExtractedFiles(existing);
                if (prunedCount > 0)
                {
                    _logger.Info("Webhook {0}: pruned {1} extracted duplicate media file(s) for torrent '{2}'", payload.EventType, prunedCount, existing.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Webhook {0}: failed to clean up extracted duplicate media files for torrent '{1}'", payload.EventType, existing.Name);
            }
        }

        if (_eventAggregator != null)
        {
            try
            {
                _eventAggregator.PublishEvent(new TorrentImportedEvent(existing, connection?.ArrType, payload.InstanceName));
                _eventAggregator.PublishEvent(new ArrImportCompletedEvent(existing, payload.InstanceName ?? connection?.Name ?? "Arr", payload.Episodes?.Count ?? 1));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Webhook {0}: failed to publish import events for torrent '{1}'", payload.EventType, existing.Name);
            }
        }

        if (connection != null)
        {
            _ = EnrichTorrentFromHistoryAsync(existing.Id, infoHash, downloadId.Trim(), connection, payload.InstanceName, CancellationToken.None);
        }

        return new ArrWebhookResult
        {
            Success = true,
            Message = $"Processed {payload.EventType} event",
            InfoHash = infoHash
        };
    }

    private ArrWebhookResult ProcessMovieDownload(ArrWebhookPayload payload)
    {
        return ProcessDownload(payload);
    }

    private ArrWebhookResult ProcessRename(ArrWebhookPayload payload)
    {
        _logger.Info("Webhook: processing Rename event for {0}", payload.InstanceName ?? "Arr");

        Torrent existing = null;
        if (!string.IsNullOrEmpty(payload.DownloadId) && IsValidInfoHash(payload.DownloadId))
        {
            var infoHash = payload.DownloadId.Trim().ToLowerInvariant();
            existing = _torrentService.GetAll().FirstOrDefault(t =>
                string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
        }

        if (existing == null)
        {
            var title = payload.Movie?.Title ?? payload.Series?.Title;
            if (!string.IsNullOrWhiteSpace(title))
            {
                existing = _torrentService.GetAll().FirstOrDefault(t =>
                    t.Name != null && t.Name.Contains(title, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (existing != null)
        {
            var updated = false;

            if (!string.IsNullOrEmpty(payload.SourcePath) && !string.IsNullOrEmpty(payload.DestinationPath))
            {
                if (string.Equals(existing.SavePath, payload.SourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    existing.SavePath = payload.DestinationPath;
                    updated = true;
                }
            }

            if (_torrentFileService != null)
            {
                var files = _torrentFileService.GetByTorrentId(existing.Id);
                if (files != null && files.Count > 0)
                {
                    var renamedList = new List<ArrWebhookRenamedFile>();
                    if (payload.RenamedFiles != null)
                    {
                        renamedList.AddRange(payload.RenamedFiles);
                    }

                    if (payload.RenamedEpisodeFiles != null)
                    {
                        renamedList.AddRange(payload.RenamedEpisodeFiles);
                    }

                    if (payload.RenamedMovieFiles != null)
                    {
                        renamedList.AddRange(payload.RenamedMovieFiles);
                    }

                    if (renamedList.Count > 0)
                    {
                        foreach (var rf in renamedList)
                        {
                            var targetFile = files.FirstOrDefault(f =>
                                (!string.IsNullOrEmpty(rf.PreviousPath) && string.Equals(f.Path, rf.PreviousPath, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrEmpty(rf.PreviousRelativePath) && string.Equals(f.Path, rf.PreviousRelativePath, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrEmpty(rf.PreviousRelativePath) && f.Path != null && f.Path.EndsWith(rf.PreviousRelativePath, StringComparison.OrdinalIgnoreCase)));

                            if (targetFile != null)
                            {
                                targetFile.Path = rf.Path ?? rf.RelativePath ?? targetFile.Path;
                                _torrentFileService.Update(targetFile);
                                updated = true;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(payload.SourcePath) && !string.IsNullOrEmpty(payload.DestinationPath))
                    {
                        foreach (var file in files)
                        {
                            if (string.Equals(file.Path, payload.SourcePath, StringComparison.OrdinalIgnoreCase))
                            {
                                file.Path = payload.DestinationPath;
                                _torrentFileService.Update(file);
                                updated = true;
                            }
                            else if (file.Path != null && file.Path.StartsWith(payload.SourcePath, StringComparison.OrdinalIgnoreCase))
                            {
                                file.Path = string.Concat(payload.DestinationPath, file.Path.AsSpan(payload.SourcePath.Length));
                                _torrentFileService.Update(file);
                                updated = true;
                            }
                        }
                    }
                }
            }

            if (updated)
            {
                _torrentService.Update(existing);
                _logger.Info("Webhook: updated paths for torrent '{0}' on Rename event", existing.Name);
            }

            return new ArrWebhookResult
            {
                Success = true,
                Message = "Processed Rename event",
                InfoHash = existing.InfoHash
            };
        }

        return new ArrWebhookResult
        {
            Success = true,
            Message = "Processed Rename event"
        };
    }

    private static bool IsValidInfoHash(string downloadId)
    {
        if (string.IsNullOrWhiteSpace(downloadId))
        {
            return false;
        }

        return InfoHashRegex.IsMatch(downloadId.Trim());
    }

    private static bool IsUsenetGrab(ArrWebhookPayload payload)
    {
        if (!string.IsNullOrEmpty(payload.DownloadClientType) &&
            (payload.DownloadClientType.IndexOf("usenet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                payload.DownloadClientType.IndexOf("nzb", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(payload.DownloadClient) &&
            (payload.DownloadClient.IndexOf("sabnzbd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                payload.DownloadClient.IndexOf("nzbget", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return true;
        }

        return false;
    }

    private async Task EnrichTorrentFromHistoryAsync(int torrentId, string infoHash, string downloadId, ArrConnectionDefinition connection, string instanceName, CancellationToken cancellationToken)
    {
        try
        {
            if (EnrichDelayMs > 0)
            {
                await Task.Delay(EnrichDelayMs, cancellationToken);
            }

            var downloadUrl = await GetDownloadUrlFromHistoryAsync(connection, downloadId, cancellationToken);
            if (string.IsNullOrEmpty(downloadUrl))
            {
                _logger.Warn("Enrich: could not find downloadUrl in {0} history for {1}", connection.ArrType, downloadId);
                return;
            }

            var torrentBytes = FetchTorrentFile(downloadUrl);
            if (torrentBytes == null || torrentBytes.Length == 0)
            {
                _logger.Warn("Enrich: failed to fetch .torrent from {0}", downloadUrl);
                return;
            }

            using var stream = new MemoryStream(torrentBytes);
            var parsed = _torrentFileParser.Parse(stream);

            var torrent = _torrentService.Get(torrentId);
            if (torrent == null)
            {
                return;
            }

            torrent.Name = parsed.Name;
            torrent.InfoHash = parsed.InfoHash.ToLowerInvariant();
            torrent.TotalSize = parsed.TotalSize;
            torrent.PieceCount = parsed.PieceCount;
            torrent.PieceLength = parsed.PieceLength;
            torrent.Comment = parsed.Comment;
            torrent.IsPrivate = parsed.IsPrivate;
            if (!string.IsNullOrEmpty(parsed.AnnounceUrl))
            {
                torrent.TrackerUrl = parsed.AnnounceUrl;
            }

            _torrentService.Update(torrent);

            if (_torrentFileService != null && parsed.Files != null && parsed.Files.Count > 0)
            {
                var existingFiles = _torrentFileService.GetByTorrentId(torrentId);
                if (existingFiles.Count == 0)
                {
                    var filesToAdd = parsed.Files.Select(f => new TorrentFile
                    {
                        TorrentId = torrentId,
                        Path = f.Path,
                        Size = f.Size,
                        IsPaddingFile = f.IsPaddingFile
                    }).ToList();
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

            _logger.Info(
                "Enrich: upgraded '{0}' ({1}) with full metadata and trackers from {2}",
                torrent.Name,
                torrent.InfoHash,
                instanceName);
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Enrich: cancelled for torrent {0}", infoHash);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Enrich: failed to upgrade torrent {0}", infoHash);
        }

        // Fallback: If torrent has no trackers attached, attempt to query configured download clients
        try
        {
            if (_trackerEntryService != null && _downloadClientFactory != null)
            {
                var currentTrackers = _trackerEntryService.GetByTorrentId(torrentId);
                if (currentTrackers.Count == 0)
                {
                    var activeClients = _downloadClientFactory.All().Where(c => c.Enable).ToList();
                    foreach (var clientDef in activeClients)
                    {
                        try
                        {
                            var provider = _downloadClientFactory.CreateClient(clientDef);
                            if (provider != null)
                            {
                                var clientTrackers = provider.GetTrackers(infoHash);
                                if (clientTrackers != null && clientTrackers.Count > 0)
                                {
                                    var tier = 1;
                                    var torrent = _torrentService.Get(torrentId);
                                    foreach (var trUrl in clientTrackers)
                                    {
                                        var clean = trUrl.Trim();
                                        if (!string.IsNullOrEmpty(clean))
                                        {
                                            _trackerEntryService.Add(new TrackerEntry
                                            {
                                                TorrentId = torrentId,
                                                Url = clean,
                                                Tier = tier++,
                                                Enabled = true
                                            });
                                        }
                                    }

                                    if (torrent != null && string.IsNullOrEmpty(torrent.TrackerUrl) && clientTrackers.Count > 0)
                                    {
                                        torrent.TrackerUrl = clientTrackers[0].Trim();
                                        _torrentService.Update(torrent);
                                    }

                                    _logger.Info("Enrich: recovered {0} tracker(s) from download client {1} for {2}", clientTrackers.Count, clientDef.Name, infoHash);
                                    break;
                                }
                            }
                        }
                        catch (Exception clientEx)
                        {
                            _logger.Debug(clientEx, "Could not get trackers from download client {0} for {1}", clientDef.Name, infoHash);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Fallback download client tracker enrichment failed for {0}", infoHash);
        }
    }

    private ArrConnectionDefinition FindConnection(ArrWebhookPayload payload)
    {
        var definitions = _connectionFactory.All();
        var enabled = definitions.Where(d => d.Enable).ToList();
        if (enabled.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(payload.ApplicationUrl))
        {
            var match = enabled.FirstOrDefault(d =>
                !string.IsNullOrEmpty(d.Url) &&
                payload.ApplicationUrl.TrimEnd('/').Equals(d.Url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return match;
            }

            if (Uri.TryCreate(payload.ApplicationUrl, UriKind.Absolute, out var payloadUri))
            {
                var hostMatch = enabled.FirstOrDefault(d =>
                    (Uri.TryCreate(d.Url, UriKind.Absolute, out var connUri) &&
                        string.Equals(payloadUri.Host, connUri.Host, StringComparison.OrdinalIgnoreCase) &&
                        payloadUri.Port == connUri.Port) ||
                    (!string.IsNullOrEmpty(d.ArrType) && string.Equals(payloadUri.Host, d.ArrType, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(d.Name) && string.Equals(payloadUri.Host, d.Name, StringComparison.OrdinalIgnoreCase)));

                if (hostMatch != null)
                {
                    return hostMatch;
                }
            }
        }

        if (!string.IsNullOrEmpty(payload.InstanceName))
        {
            var match = enabled.FirstOrDefault(d =>
                (!string.IsNullOrEmpty(d.ArrType) && payload.InstanceName.Contains(d.ArrType, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(d.Name) && payload.InstanceName.Contains(d.Name, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(d.Name) && d.Name.Contains(payload.InstanceName, StringComparison.OrdinalIgnoreCase)));

            if (match != null)
            {
                return match;
            }
        }

        if (payload.Series != null || string.Equals(payload.EventType, "EpisodeDownload", StringComparison.OrdinalIgnoreCase) || string.Equals(payload.EventType, "EpisodeFileDelete", StringComparison.OrdinalIgnoreCase) || string.Equals(payload.EventType, "SeriesDelete", StringComparison.OrdinalIgnoreCase))
        {
            var match = enabled.FirstOrDefault(d => string.Equals(d.ArrType, "Sonarr", StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        if (payload.Movie != null || string.Equals(payload.EventType, "MovieDownload", StringComparison.OrdinalIgnoreCase) || string.Equals(payload.EventType, "MovieDelete", StringComparison.OrdinalIgnoreCase) || string.Equals(payload.EventType, "MovieFileDelete", StringComparison.OrdinalIgnoreCase))
        {
            var match = enabled.FirstOrDefault(d => string.Equals(d.ArrType, "Radarr", StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        if (payload.Album != null || payload.Artist != null || string.Equals(payload.EventType, "AlbumDownload", StringComparison.OrdinalIgnoreCase) || string.Equals(payload.EventType, "TrackFileDelete", StringComparison.OrdinalIgnoreCase) || string.Equals(payload.EventType, "ArtistDelete", StringComparison.OrdinalIgnoreCase))
        {
            var match = enabled.FirstOrDefault(d => string.Equals(d.ArrType, "Lidarr", StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        if (payload.Book != null || payload.Author != null || string.Equals(payload.EventType, "BookDownload", StringComparison.OrdinalIgnoreCase) || string.Equals(payload.EventType, "BookFileDelete", StringComparison.OrdinalIgnoreCase) || string.Equals(payload.EventType, "AuthorDelete", StringComparison.OrdinalIgnoreCase))
        {
            var match = enabled.FirstOrDefault(d => string.Equals(d.ArrType, "Readarr", StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        if (enabled.Count == 1)
        {
            return enabled[0];
        }

        if (!string.IsNullOrEmpty(payload.DownloadId) && IsValidInfoHash(payload.DownloadId))
        {
            foreach (var conn in enabled)
            {
                if (ConnectionHasDownloadInHistory(conn, payload.DownloadId.Trim()))
                {
                    _logger.Info(
                        "FindConnection: matched connection '{0}' ({1}) via history query for downloadId '{2}'",
                        conn.Name,
                        conn.ArrType,
                        payload.DownloadId);

                    return conn;
                }
            }
        }

        return null;
    }

    private bool ConnectionHasDownloadInHistory(ArrConnectionDefinition connection, string downloadId)
    {
        if (string.IsNullOrEmpty(downloadId) || string.IsNullOrEmpty(connection.Url) || string.IsNullOrEmpty(connection.ApiKey))
        {
            return false;
        }

        var apiVersion = string.Equals(connection.ArrType, "Lidarr", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(connection.ArrType, "Readarr", StringComparison.OrdinalIgnoreCase) ? "v1" : "v3";
        var variants = new[] { downloadId };

        foreach (var id in variants)
        {
            try
            {
                var hasRecord = _policy.Execute(ct =>
                {
                    var cleanUrl = connection.Url.TrimEnd('/');
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        $"{cleanUrl}/api/{apiVersion}/history?downloadId={id}&pageSize=1");
                    request.Headers.Add("X-Api-Key", connection.ApiKey);

                    using var response = _client.Send(request, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        return false;
                    }

                    using var stream = response.Content.ReadAsStream(ct);
                    using var doc = JsonDocument.Parse(stream);

                    if (!doc.RootElement.TryGetProperty("records", out var records))
                    {
                        return false;
                    }

                    return records.GetArrayLength() > 0;
                });

                if (hasRecord)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to check history for downloadId {0} in {1}", id, connection.Name);
            }
        }

        return false;
    }

    private async Task<string> GetDownloadUrlFromHistoryAsync(ArrConnectionDefinition connection, string downloadId, CancellationToken cancellationToken)
    {
        var apiVersion = string.Equals(connection.ArrType, "Lidarr", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(connection.ArrType, "Readarr", StringComparison.OrdinalIgnoreCase) ? "v1" : "v3";
        var variants = new[] { downloadId, downloadId.ToUpperInvariant() };

        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(2000, cancellationToken);
                _logger.Debug("Retrying history query for {0}, attempt {1}", downloadId, attempt + 1);
            }

            foreach (var id in variants)
            {
                var result = QueryHistoryForDownloadUrl(connection, apiVersion, id);
                if (result != null)
                {
                    return result;
                }
            }
        }

        _logger.Warn("DownloadUrl not found in {0} history after retries for {1}", connection.ArrType, downloadId);
        return null;
    }

    private string QueryHistoryForDownloadUrl(ArrConnectionDefinition connection, string apiVersion, string downloadId)
    {
        try
        {
            return _policy.Execute(ct =>
            {
                var cleanUrl = connection.Url?.TrimEnd('/');
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{cleanUrl}/api/{apiVersion}/history?downloadId={downloadId}&pageSize=1");
                request.Headers.Add("X-Api-Key", connection.ApiKey);

                using var response = _client.Send(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warn("Failed to query {0} history: {1}", connection.ArrType, response.StatusCode);
                    return null;
                }

                using var stream = response.Content.ReadAsStream(ct);
                using var doc = JsonDocument.Parse(stream);

                if (!doc.RootElement.TryGetProperty("records", out var records))
                {
                    return null;
                }

                foreach (var record in records.EnumerateArray())
                {
                    if (record.TryGetProperty("data", out var data) &&
                        data.TryGetProperty("downloadUrl", out var url))
                    {
                        return url.GetString();
                    }
                }

                return null;
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get downloadUrl from {0} history", connection.ArrType);
            return null;
        }
    }

    private byte[] FetchTorrentFile(string downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.Warn("Blocked non-HTTP torrent fetch URL: {0}", downloadUrl);
            return null;
        }

        if (!UrlValidator.IsSafeUrl(downloadUrl))
        {
            _logger.Warn("Blocked unsafe torrent fetch URL: {0}", downloadUrl);
            return null;
        }

        try
        {
            return _policy.Execute(ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-bittorrent"));

                using var response = _client.Send(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warn("Failed to fetch .torrent from {0}: {1}", downloadUrl, response.StatusCode);
                    return null;
                }

                using var ms = new MemoryStream();
                response.Content.ReadAsStream(ct).CopyTo(ms);
                return ms.ToArray();
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch .torrent from {0}", downloadUrl);
            return null;
        }
    }
}
