using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.MediaEnrichment;

public class MediaEnrichedEvent : IEvent
{
    public int TorrentId { get; set; }

    public TorrentMediaMetadata Metadata { get; set; }
}

public interface IMediaEnrichmentService
{
    Task<TorrentMediaMetadata> EnrichTorrentAsync(Torrent torrent, string filePath = null);

    TorrentMediaMetadata GetMetadata(int torrentId);

    Dictionary<int, TorrentMediaMetadata> GetAllMetadata();

    void DeleteMetadata(int torrentId);

    void CleanupTorrentCache(int torrentId);

    void DeleteMediaCache(int torrentId);

    Task<string> CacheArtworkAsync(string url, int torrentId, string type);
}

public class MediaEnrichmentService : IMediaEnrichmentService, IHandle<TorrentDeletedEvent>
{
    private readonly ITorrentMediaMetadataRepository _repository;
    private readonly IMediaContainerInspector _inspector;
    private readonly IConfigService _configService;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly IEventAggregator _eventAggregator;
    private readonly IArrConnectionRepository _arrRepository;
    private readonly IArrConnectionFactory _connectionFactory;
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

    private static readonly Regex SceneTagsRegex = new(
        @"\b(1080p|720p|2160p|4k|uhd|hdr|hdr10|hdr10plus|dv|dovi|remux|bluray|blu-ray|bdrip|web-dl|webrip|web|hdtv|x264|x265|h264|h265|hevc|av1|xvid|aac|dts|dts-hd|truehd|atmos|flac|mp3|extended|repack|proper|complete|season|\bS\d{1,2}(E\d{1,2})?\b|\bEP?\d{1,3}\b)\b.*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex YearRegex = new(@"\b(19\d{2}|20\d{2})\b", RegexOptions.Compiled);

    public MediaEnrichmentService(
        ITorrentMediaMetadataRepository repository,
        IMediaContainerInspector inspector = null,
        IConfigService configService = null,
        IAppFolderInfo appFolderInfo = null,
        IEventAggregator eventAggregator = null,
        IArrConnectionRepository arrRepository = null,
        IArrConnectionFactory connectionFactory = null,
        HttpClient httpClient = null)
    {
        _repository = repository;
        _inspector = inspector ?? new MediaContainerInspector();
        _configService = configService;
        _appFolderInfo = appFolderInfo;
        _eventAggregator = eventAggregator;
        _arrRepository = arrRepository;
        _connectionFactory = connectionFactory;
        _httpClient = httpClient ?? ArrConnectionResources.SharedClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _logger = LogManager.GetCurrentClassLogger();
    }

    public async Task<TorrentMediaMetadata> EnrichTorrentAsync(Torrent torrent, string filePath = null)
    {
        if (torrent == null)
        {
            return null;
        }

        _logger.Debug("Enriching metadata for torrent: {0}", torrent.Name);

        var existing = torrent.Id > 0 ? _repository.GetByTorrentId(torrent.Id) : null;
        var metadata = existing ?? new TorrentMediaMetadata { TorrentId = torrent.Id };

        // 1. Inspect container metadata if local file is available or from torrent name
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            try
            {
                var containerInfo = _inspector.InspectFile(filePath);
                if (containerInfo != null)
                {
                    metadata.MediaInfoJson = JsonSerializer.Serialize(containerInfo);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to inspect media file: {0}", filePath);
            }
        }

        // 2. Parse release title & year
        var cleanTitle = CleanReleaseTitle(torrent.Name);
        var parsedYear = ExtractYear(torrent.Name);

        // 3. Query connected Servarr APIs (Sonarr / Radarr / Lidarr) if configured
        var arrMetadata = await QueryServarrAsync(torrent, cleanTitle);
        if (arrMetadata != null)
        {
            if (!string.IsNullOrEmpty(arrMetadata.Title))
            {
                metadata.Title = arrMetadata.Title;
            }

            if (arrMetadata.Year.HasValue && arrMetadata.Year.Value > 0)
            {
                metadata.Year = arrMetadata.Year.Value;
            }

            if (!string.IsNullOrEmpty(arrMetadata.Overview))
            {
                metadata.Overview = arrMetadata.Overview;
            }

            if (!string.IsNullOrEmpty(arrMetadata.PosterUrl))
            {
                metadata.PosterUrl = arrMetadata.PosterUrl;
            }

            if (!string.IsNullOrEmpty(arrMetadata.FanartUrl))
            {
                metadata.BackdropUrl = arrMetadata.FanartUrl;
            }

            if (!string.IsNullOrEmpty(arrMetadata.BannerUrl))
            {
                metadata.BannerUrl = arrMetadata.BannerUrl;
            }

            if (arrMetadata.Genres != null && arrMetadata.Genres.Count > 0)
            {
                metadata.Genres = string.Join(", ", arrMetadata.Genres);
            }

            if (arrMetadata.Rating.HasValue && arrMetadata.Rating.Value > 0)
            {
                metadata.Rating = arrMetadata.Rating.Value;
            }

            if (!string.IsNullOrEmpty(arrMetadata.ImdbId))
            {
                metadata.ImdbId = arrMetadata.ImdbId;
            }

            if (arrMetadata.TmdbId.HasValue && arrMetadata.TmdbId.Value > 0)
            {
                metadata.TmdbId = arrMetadata.TmdbId.Value.ToString();
            }

            if (arrMetadata.TvdbId.HasValue && arrMetadata.TvdbId.Value > 0)
            {
                metadata.TvdbId = arrMetadata.TvdbId.Value.ToString();
            }

            if (!string.IsNullOrEmpty(arrMetadata.MediaType))
            {
                metadata.ArrType = NormalizeArrType(arrMetadata.MediaType);
            }

            if (arrMetadata.MediaId.HasValue && arrMetadata.MediaId.Value > 0)
            {
                metadata.ArrMediaId = arrMetadata.MediaId.Value;
            }

            if (arrMetadata.Actors != null && arrMetadata.Actors.Count > 0)
            {
                metadata.Cast = string.Join(", ", arrMetadata.Actors.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
            }
        }

        // 4. Fallbacks
        if (string.IsNullOrEmpty(metadata.Title))
        {
            metadata.Title = torrent.Name;
        }

        if (metadata.Year == 0 && parsedYear > 0)
        {
            metadata.Year = parsedYear;
        }

        if (string.IsNullOrEmpty(metadata.ArrType))
        {
            metadata.ArrType = GuessArrType(torrent.Label, torrent.Name);
        }

        if (string.IsNullOrEmpty(metadata.MediaInfoJson))
        {
            var guessed = _inspector.Inspect(new MemoryStream(new byte[8]), torrent.Name);
            if (guessed != null)
            {
                metadata.MediaInfoJson = JsonSerializer.Serialize(guessed);
            }
        }

        // 5. Cache remote or local poster & backdrop
        if (!string.IsNullOrEmpty(metadata.PosterUrl) && string.IsNullOrEmpty(metadata.PosterLocalPath))
        {
            metadata.PosterLocalPath = await CacheArtworkAsync(metadata.PosterUrl, torrent.Id, "poster");
        }

        if (!string.IsNullOrEmpty(metadata.BackdropUrl) && string.IsNullOrEmpty(metadata.BackdropLocalPath))
        {
            metadata.BackdropLocalPath = await CacheArtworkAsync(metadata.BackdropUrl, torrent.Id, "backdrop");
        }

        // 6. Persist to database
        if (torrent.Id > 0 && _repository != null)
        {
            if (existing == null)
            {
                _repository.Insert(metadata);
            }
            else
            {
                _repository.Update(metadata);
            }

            _eventAggregator?.PublishEvent(new MediaEnrichedEvent { TorrentId = torrent.Id, Metadata = metadata });
        }

        return metadata;
    }

    public TorrentMediaMetadata GetMetadata(int torrentId)
    {
        return _repository?.GetByTorrentId(torrentId);
    }

    public Dictionary<int, TorrentMediaMetadata> GetAllMetadata()
    {
        try
        {
            return _repository?.All()
                .GroupBy(m => m.TorrentId)
                .ToDictionary(g => g.Key, g => g.First()) ?? new Dictionary<int, TorrentMediaMetadata>();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to load all media metadata");
            return new Dictionary<int, TorrentMediaMetadata>();
        }
    }

    public void DeleteMetadata(int torrentId)
    {
        CleanupTorrentCache(torrentId);

        var metadata = _repository?.GetByTorrentId(torrentId);
        if (metadata != null)
        {
            if (_configService == null || _configService.AutoPruneRemovedArtwork)
            {
                DeleteLocalFile(metadata.PosterLocalPath);
                DeleteLocalFile(metadata.BackdropLocalPath);
            }

            _repository.DeleteByTorrentId(torrentId);
        }
    }

    public void DeleteMediaCache(int torrentId)
    {
        CleanupTorrentCache(torrentId);
    }

    public void Handle(TorrentDeletedEvent message)
    {
        if (message != null && message.TorrentId > 0)
        {
            DeleteMetadata(message.TorrentId);
        }
    }

    public void CleanupTorrentCache(int torrentId)
    {
        if (_appFolderInfo == null)
        {
            return;
        }

        try
        {
            var baseDirs = new[]
            {
                Path.Combine(_appFolderInfo.AppDataFolder, "MediaCover"),
                Path.Combine(_appFolderInfo.AppDataFolder, "MediaCache"),
            };

            foreach (var baseDir in baseDirs)
            {
                if (!Directory.Exists(baseDir))
                {
                    continue;
                }

                var cacheDir = Path.Combine(baseDir, torrentId.ToString());
                if (Directory.Exists(cacheDir))
                {
                    Directory.Delete(cacheDir, recursive: true);
                    _logger.Debug("Cleaned up media cache directory for torrent {0}", torrentId);
                }

                var metadata = _repository?.GetByTorrentId(torrentId);
                if (metadata != null)
                {
                    if (!string.IsNullOrWhiteSpace(metadata.PosterUrl))
                    {
                        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(metadata.PosterUrl)))[..16].ToLowerInvariant();
                        var hashDir = Path.Combine(baseDir, hash);
                        if (Directory.Exists(hashDir))
                        {
                            Directory.Delete(hashDir, recursive: true);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(metadata.BackdropUrl))
                    {
                        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(metadata.BackdropUrl)))[..16].ToLowerInvariant();
                        var hashDir = Path.Combine(baseDir, hash);
                        if (Directory.Exists(hashDir))
                        {
                            Directory.Delete(hashDir, recursive: true);
                        }
                    }

                    PruneCacheDirForFilePath(baseDir, metadata.PosterLocalPath);
                    PruneCacheDirForFilePath(baseDir, metadata.BackdropLocalPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to clean up media cache directory for torrent: {0}", torrentId);
        }
    }

    public async Task<string> CacheArtworkAsync(string url, int torrentId, string type)
    {
        if (string.IsNullOrWhiteSpace(url) || _appFolderInfo == null)
        {
            return null;
        }

        try
        {
            var folderKey = torrentId > 0
                ? torrentId.ToString()
                : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();
            var cacheDir = Path.Combine(_appFolderInfo.AppDataFolder, "MediaCover", folderKey);
            Directory.CreateDirectory(cacheDir);

            // Handle local file path
            if (File.Exists(url) || Path.IsPathRooted(url))
            {
                if (File.Exists(url))
                {
                    var localBytes = await File.ReadAllBytesAsync(url);
                    if (localBytes == null || localBytes.Length > 15 * 1024 * 1024 || !IsValidImage(localBytes))
                    {
                        _logger.Warn("Local artwork file is invalid, not an image, or exceeds size limit: {0}", url);
                        return null;
                    }

                    var ext = Path.GetExtension(url);
                    if (string.IsNullOrEmpty(ext) || ext.Length > 5)
                    {
                        ext = ".jpg";
                    }

                    var localFile = Path.Combine(cacheDir, $"{type}{ext}");
                    await File.WriteAllBytesAsync(localFile, localBytes);
                    _logger.Debug("Copied validated local {0} artwork from {1} to {2}", type, url, localFile);
                    return localFile;
                }

                _logger.Warn("Local artwork file does not exist: {0}", url);
                return null;
            }

            // Remote URL
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.Warn("Refusing to cache artwork from non-HTTP/HTTPS URL: {0}", url);
                return null;
            }

            var extRemote = ".jpg";
            var uriExt = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrEmpty(uriExt) && uriExt.Length <= 5)
            {
                extRemote = uriExt;
            }

            var destFile = Path.Combine(cacheDir, $"{type}{extRemote}");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var apiKey = GetServarrApiKey(url);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn("Failed downloading artwork from {0}: {1}", url, response.StatusCode);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes == null || bytes.Length == 0 || bytes.Length > 15 * 1024 * 1024 || !IsValidImage(bytes))
            {
                _logger.Warn("Downloaded artwork from {0} has invalid image magic bytes or is empty. Discarding.", url);
                return null;
            }

            await File.WriteAllBytesAsync(destFile, bytes);
            _logger.Debug("Cached {0} artwork to {1}", type, destFile);
            return destFile;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to cache artwork from URL/path: {0}", url);
            return null;
        }
    }

    public static bool IsValidImage(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 4)
        {
            return false;
        }

        // JPEG: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return true;
        }

        // PNG: 89 50 4E 47
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return true;
        }

        // WebP: RIFF ???? WEBP
        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return true;
        }

        // GIF: GIF87a or GIF89a
        if (bytes.Length >= 6 &&
            bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38 &&
            (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61)
        {
            return true;
        }

        return false;
    }

    internal string GetServarrApiKey(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var definitions = _arrRepository?.All() ?? _connectionFactory?.All();
        if (definitions != null)
        {
            var matched = definitions.FirstOrDefault(c =>
                !string.IsNullOrWhiteSpace(c.Url) &&
                url.StartsWith(c.Url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(c.ApiKey));

            return matched?.ApiKey;
        }

        return null;
    }

    private async Task<MediaMetadata> QueryServarrAsync(Torrent torrent, string cleanTitle)
    {
        var definitions = (_arrRepository?.All() ?? _connectionFactory?.All())?.Where(d => d.Enable).ToList();
        if (definitions == null || definitions.Count == 0)
        {
            return null;
        }

        foreach (var def in definitions)
        {
            var provider = CreateProvider(def);
            if (provider == null)
            {
                continue;
            }

            try
            {
                // 1. Try info_hash matching from download history
                if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
                {
                    var records = provider.GetDownloadHistory();
                    if (records != null)
                    {
                        var match = records.FirstOrDefault(r => string.Equals(r.InfoHash, torrent.InfoHash, StringComparison.OrdinalIgnoreCase));
                        if (match != null && match.MediaId.HasValue)
                        {
                            var details = provider.GetMediaDetails(match.MediaId.Value);
                            if (details != null)
                            {
                                return details;
                            }
                        }
                    }
                }

                // 2. Try title lookup
                if (!string.IsNullOrWhiteSpace(cleanTitle))
                {
                    var lookup = provider.LookupMedia(cleanTitle);
                    if (lookup != null)
                    {
                        return lookup;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to query {0} for torrent {1}", def.Name, torrent.Name);
            }
        }

        return null;
    }

    protected virtual IArrConnection CreateProvider(ArrConnectionDefinition definition)
    {
        IArrConnection provider;
        switch (definition.ArrType?.ToLowerInvariant())
        {
            case "sonarr":
                provider = new SonarrConnection(_httpClient);
                break;
            case "radarr":
                provider = new RadarrConnection(_httpClient);
                break;
            case "lidarr":
                provider = new LidarrConnection(_httpClient);
                break;
            default:
                return null;
        }

        provider.Url = definition.Url;
        provider.ApiKey = definition.ApiKey;
        return provider;
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
                clean = clean[..dotIdx];
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

    public static int ExtractYear(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return 0;
        }

        var match = YearRegex.Match(title);
        if (match.Success && int.TryParse(match.Value, out var year))
        {
            return year;
        }

        return 0;
    }

    public static string GuessArrType(string category, string name)
    {
        var cat = (category ?? string.Empty).ToLowerInvariant();
        var n = (name ?? string.Empty).ToLowerInvariant();

        if (cat.Contains("tv") || cat.Contains("sonarr") || cat.Contains("show") || cat.Contains("season") || cat.Contains("series") || cat.Contains("episode") || cat.Contains("anime") ||
            Regex.IsMatch(n, @"(?i)\b(s\d{1,2}(e\d{1,2})?|season\s*\d+|episode\s*\d+)\b"))
        {
            return "Sonarr";
        }

        if (cat.Contains("movie") || cat.Contains("radarr") || cat.Contains("film") || cat.Contains("cinema"))
        {
            return "Radarr";
        }

        if (cat.Contains("music") || cat.Contains("lidarr") || cat.Contains("album") || cat.Contains("audio") || cat.Contains("flac"))
        {
            return "Lidarr";
        }

        if (cat.Contains("book") || cat.Contains("readarr") || cat.Contains("ebook") || cat.Contains("audiobook"))
        {
            return "Readarr";
        }

        return "Unknown";
    }

    private static string NormalizeArrType(string mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return "Unknown";
        }

        var mt = mediaType.ToLowerInvariant();
        if (mt.Contains("series") || mt.Contains("tv") || mt.Contains("show") || mt.Contains("sonarr"))
        {
            return "Sonarr";
        }

        if (mt.Contains("movie") || mt.Contains("radarr") || mt.Contains("film"))
        {
            return "Radarr";
        }

        if (mt.Contains("album") || mt.Contains("artist") || mt.Contains("music") || mt.Contains("lidarr"))
        {
            return "Lidarr";
        }

        if (mt.Contains("book") || mt.Contains("readarr"))
        {
            return "Readarr";
        }

        return mediaType;
    }

    private static void DeleteLocalFile(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Suppress cleanup failure
        }
    }

    private static void PruneCacheDirForFilePath(string mediaCacheBase, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(mediaCacheBase))
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
            var fullCacheBase = Path.GetFullPath(mediaCacheBase);
            if (dir != null &&
                dir.StartsWith(fullCacheBase, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(dir, fullCacheBase, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // Suppress cleanup failure
        }
    }
}
