using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
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
    Task<TorrentMediaMetadata> EnrichTorrentAsync(Torrent torrent, string filePath = null, CancellationToken cancellationToken = default);

    TorrentMediaMetadata GetMetadata(int torrentId);

    Dictionary<int, TorrentMediaMetadata> GetAllMetadata();

    void DeleteMetadata(int torrentId);

    void CleanupTorrentCache(int torrentId);

    void DeleteMediaCache(int torrentId);

    Task<string> CacheArtworkAsync(string url, int torrentId, string type, CancellationToken cancellationToken = default);

    void EvictMediaCoverCache();
}

public class MediaEnrichmentService : IMediaEnrichmentService, IHandle<TorrentDeletedEvent>
{
    private const long MaxArtworkSizeBytes = 15 * 1024 * 1024;

    private readonly ITorrentMediaMetadataRepository _repository;
    private readonly IMediaContainerInspector _inspector;
    private readonly IConfigService _configService;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly IEventAggregator _eventAggregator;
    private readonly IArrConnectionRepository _arrRepository;
    private readonly IArrConnectionFactory _connectionFactory;
    private readonly ITmdbMetadataProvider _tmdbProvider;
    private readonly HttpClient _explicitHttpClient;
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _torrentLocks = new();
    private readonly object _evictionLock = new();

    private static readonly HashSet<string> AllowedArtworkTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "poster",
        "backdrop",
        "banner",
        "fanart",
        "logo",
        "thumb",
    };

    private static readonly Regex ImdbIdRegex = new(@"\b(tt\d{7,10})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public MediaEnrichmentService(
        ITorrentMediaMetadataRepository repository,
        IMediaContainerInspector inspector = null,
        IConfigService configService = null,
        IAppFolderInfo appFolderInfo = null,
        IEventAggregator eventAggregator = null,
        IArrConnectionRepository arrRepository = null,
        IArrConnectionFactory connectionFactory = null,
        ITmdbMetadataProvider tmdbProvider = null,
        HttpClient httpClient = null)
    {
        _repository = repository;
        _inspector = inspector ?? new MediaContainerInspector();
        _configService = configService;
        _appFolderInfo = appFolderInfo;
        _eventAggregator = eventAggregator;
        _arrRepository = arrRepository;
        _connectionFactory = connectionFactory;
        _tmdbProvider = tmdbProvider;
        _explicitHttpClient = httpClient;
        _httpClient = httpClient ?? ArrConnectionResources.SharedClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _logger = LogManager.GetCurrentClassLogger();
    }

    public MediaEnrichmentService(
        ITorrentMediaMetadataRepository repository,
        IMediaContainerInspector inspector,
        IConfigService configService,
        IAppFolderInfo appFolderInfo,
        IEventAggregator eventAggregator,
        IArrConnectionRepository arrRepository,
        IArrConnectionFactory connectionFactory,
        HttpClient httpClient)
        : this(repository, inspector, configService, appFolderInfo, eventAggregator, arrRepository, connectionFactory, null, httpClient)
    {
    }

    public async Task<TorrentMediaMetadata> EnrichTorrentAsync(Torrent torrent, string filePath = null, CancellationToken cancellationToken = default)
    {
        if (torrent == null)
        {
            return null;
        }

        SemaphoreSlim semaphore = null;
        if (torrent.Id > 0)
        {
            semaphore = _torrentLocks.GetOrAdd(torrent.Id, static _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            _logger.Debug("Enriching metadata for torrent: {0}", torrent.Name);

            var existing = torrent.Id > 0 && _repository != null ? _repository.GetByTorrentId(torrent.Id) : null;
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
            var parsedRelease = ReleaseTitleParser.Parse(torrent.Name);
            var cleanTitle = parsedRelease.CleanTitle;
            var parsedYear = parsedRelease.Year ?? 0;

            // 3. Query connected Servarr APIs (Sonarr / Radarr / Lidarr) if configured
            var arrMetadata = await QueryServarrAsync(torrent, cleanTitle, cancellationToken).ConfigureAwait(false);
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

                if (!string.IsNullOrEmpty(arrMetadata.MusicBrainzId))
                {
                    metadata.MusicBrainzId = arrMetadata.MusicBrainzId;
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

                if (!string.IsNullOrEmpty(arrMetadata.Studio))
                {
                    metadata.Studio = arrMetadata.Studio;
                }
                else if (!string.IsNullOrEmpty(arrMetadata.StudioOrNetwork))
                {
                    metadata.Studio = arrMetadata.StudioOrNetwork;
                }

                if (!string.IsNullOrEmpty(arrMetadata.SiteName))
                {
                    metadata.SiteName = arrMetadata.SiteName;
                }

                if (!string.IsNullOrEmpty(arrMetadata.Performers))
                {
                    metadata.Performers = arrMetadata.Performers;
                }

                if (!string.IsNullOrEmpty(arrMetadata.SceneCode))
                {
                    metadata.SceneCode = arrMetadata.SceneCode;
                }

                if (arrMetadata.ReleaseDate.HasValue)
                {
                    metadata.ReleaseDate = arrMetadata.ReleaseDate.Value;
                }
            }
            else if (_tmdbProvider != null)
            {
                var imdbId = ExtractImdbId(torrent.Name, filePath) ?? metadata.ImdbId;
                var guessedArrType = GuessArrType(torrent.Label, torrent.Name);
                var tmdbMetadata = await _tmdbProvider.LookupMediaAsync(
                    cleanTitle,
                    parsedYear > 0 ? parsedYear : null,
                    imdbId,
                    guessedArrType,
                    cancellationToken).ConfigureAwait(false);

                if (tmdbMetadata != null)
                {
                    if (!string.IsNullOrEmpty(tmdbMetadata.Title))
                    {
                        metadata.Title = tmdbMetadata.Title;
                    }

                    if (tmdbMetadata.Year > 0)
                    {
                        metadata.Year = tmdbMetadata.Year;
                    }

                    if (!string.IsNullOrEmpty(tmdbMetadata.Overview))
                    {
                        metadata.Overview = tmdbMetadata.Overview;
                    }

                    if (!string.IsNullOrEmpty(tmdbMetadata.PosterUrl))
                    {
                        metadata.PosterUrl = tmdbMetadata.PosterUrl;
                    }

                    if (!string.IsNullOrEmpty(tmdbMetadata.BackdropUrl))
                    {
                        metadata.BackdropUrl = tmdbMetadata.BackdropUrl;
                    }

                    if (!string.IsNullOrEmpty(tmdbMetadata.Genres))
                    {
                        metadata.Genres = tmdbMetadata.Genres;
                    }

                    if (tmdbMetadata.Rating > 0)
                    {
                        metadata.Rating = tmdbMetadata.Rating;
                    }

                    if (!string.IsNullOrEmpty(tmdbMetadata.ImdbId))
                    {
                        metadata.ImdbId = tmdbMetadata.ImdbId;
                    }

                    if (!string.IsNullOrEmpty(tmdbMetadata.TmdbId))
                    {
                        metadata.TmdbId = tmdbMetadata.TmdbId;
                    }

                    if (!string.IsNullOrEmpty(tmdbMetadata.Cast))
                    {
                        metadata.Cast = tmdbMetadata.Cast;
                    }

                    if (!string.IsNullOrEmpty(tmdbMetadata.ArrType) && tmdbMetadata.ArrType != "Unknown")
                    {
                        metadata.ArrType = tmdbMetadata.ArrType;
                    }

                    if (tmdbMetadata.ReleaseDate.HasValue)
                    {
                        metadata.ReleaseDate = tmdbMetadata.ReleaseDate.Value;
                    }
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
            if (!string.IsNullOrEmpty(metadata.PosterUrl) && (string.IsNullOrEmpty(metadata.PosterLocalPath) || !File.Exists(metadata.PosterLocalPath)))
            {
                metadata.PosterLocalPath = await CacheArtworkAsync(metadata.PosterUrl, torrent.Id, "poster", cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(metadata.BackdropUrl) && (string.IsNullOrEmpty(metadata.BackdropLocalPath) || !File.Exists(metadata.BackdropLocalPath)))
            {
                metadata.BackdropLocalPath = await CacheArtworkAsync(metadata.BackdropUrl, torrent.Id, "backdrop", cancellationToken).ConfigureAwait(false);
            }

            // 6. Persist to database
            if (torrent.Id > 0 && _repository != null)
            {
                metadata = _repository.Upsert(metadata) ?? metadata;

                _eventAggregator?.PublishEvent(new MediaEnrichedEvent { TorrentId = torrent.Id, Metadata = metadata });
            }

            return metadata;
        }
        finally
        {
            semaphore?.Release();
        }
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

        _torrentLocks.TryRemove(torrentId, out _);
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

    public async Task<string> CacheArtworkAsync(string url, int torrentId, string type, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url) || _appFolderInfo == null || string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        var sanitizedType = Path.GetFileName(type)?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(sanitizedType) ||
            !AllowedArtworkTypes.Contains(sanitizedType) ||
            type.Contains("..") ||
            type.Contains('/') ||
            type.Contains('\\'))
        {
            _logger.Warn("Invalid or unauthorized artwork type requested: {0}", type);
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
                    var fileInfo = new FileInfo(url);
                    if (fileInfo.Length > MaxArtworkSizeBytes)
                    {
                        _logger.Warn("Local artwork file exceeds size limit: {0}", url);
                        return null;
                    }

                    var ext = Path.GetExtension(url);
                    if (string.IsNullOrEmpty(ext) || ext.Length > 5)
                    {
                        ext = ".jpg";
                    }

                    var localFile = Path.Combine(cacheDir, $"{sanitizedType}{ext}");
                    var tmpLocalFile = $"{localFile}.tmp.{Guid.NewGuid():N}";
                    try
                    {
                        using (var srcStream = new FileStream(url, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
                        using (var destStream = new FileStream(tmpLocalFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true))
                        {
                            var headerBytes = new byte[16];
                            var readHeader = await srcStream.ReadAsync(headerBytes.AsMemory(), cancellationToken).ConfigureAwait(false);
                            if (readHeader < 4 || !IsValidImage(headerBytes[..readHeader]))
                            {
                                _logger.Warn("Local artwork file is invalid, not an image, or corrupted: {0}", url);
                                return null;
                            }

                            srcStream.Position = 0;
                            await srcStream.CopyToAsync(destStream, cancellationToken).ConfigureAwait(false);
                        }

                        File.Move(tmpLocalFile, localFile, overwrite: true);
                        CleanUpAlternativeFormats(cacheDir, sanitizedType, localFile);
                        _logger.Debug("Copied validated local {0} artwork from {1} to {2}", sanitizedType, url, localFile);
                        EvictMediaCoverCache();
                        return localFile;
                    }
                    finally
                    {
                        if (File.Exists(tmpLocalFile))
                        {
                            try
                            {
                                File.Delete(tmpLocalFile);
                            }
                            catch
                            {
                            }
                        }
                    }
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

            var destFile = Path.Combine(cacheDir, $"{sanitizedType}{extRemote}");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var apiKey = GetServarrApiKey(url);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
            }

            using var response = await GetImageHttpClient(url).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn("Failed downloading artwork from {0}: {1}", url, response.StatusCode);
                return null;
            }

            if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > MaxArtworkSizeBytes)
            {
                _logger.Warn("Artwork download from {0} exceeds size limit: {1} bytes", url, response.Content.Headers.ContentLength.Value);
                return null;
            }

            var tmpFile = $"{destFile}.tmp.{Guid.NewGuid():N}";
            try
            {
                using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                using (var fileStream = new FileStream(tmpFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true))
                {
                    var buffer = new byte[81920];
                    long totalBytesRead = 0;
                    int bytesRead;

                    while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        totalBytesRead += bytesRead;
                        if (totalBytesRead > MaxArtworkSizeBytes)
                        {
                            _logger.Warn("Artwork download from {0} exceeded size limit of {1} bytes during transfer", url, MaxArtworkSizeBytes);
                            return null;
                        }

                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    }

                    if (totalBytesRead == 0)
                    {
                        _logger.Warn("Downloaded artwork from {0} is empty. Discarding.", url);
                        return null;
                    }

                    fileStream.Position = 0;
                    var headerBytes = new byte[Math.Min(16, (int)totalBytesRead)];
                    var readHeader = await fileStream.ReadAsync(headerBytes.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (readHeader < 4 || !IsValidImage(headerBytes[..readHeader]))
                    {
                        _logger.Warn("Downloaded artwork from {0} has invalid image magic bytes or is corrupted. Discarding.", url);
                        return null;
                    }
                }

                File.Move(tmpFile, destFile, overwrite: true);
                CleanUpAlternativeFormats(cacheDir, sanitizedType, destFile);
                _logger.Debug("Cached {0} artwork to {1}", sanitizedType, destFile);
                EvictMediaCoverCache();
                return destFile;
            }
            finally
            {
                if (File.Exists(tmpFile))
                {
                    try
                    {
                        File.Delete(tmpFile);
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to cache artwork from URL/path: {0}", url);
            return null;
        }
    }

    private static void CleanUpAlternativeFormats(string cacheDir, string type, string currentFile)
    {
        if (!Directory.Exists(cacheDir))
        {
            return;
        }

        try
        {
            var normalizedCurrent = Path.GetFullPath(currentFile);
            foreach (var file in Directory.EnumerateFiles(cacheDir))
            {
                var fileName = Path.GetFileName(file);
                if (!fileName.StartsWith(type + ".", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(file);
                if (string.Equals(fullPath, normalizedCurrent, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (file.Contains(".tmp.", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    File.Delete(fullPath);
                }
                catch
                {
                }
            }
        }
        catch
        {
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

    public void EvictMediaCoverCache()
    {
        if (_appFolderInfo == null || string.IsNullOrWhiteSpace(_appFolderInfo.AppDataFolder))
        {
            return;
        }

        lock (_evictionLock)
        {
            try
            {
                var baseDirs = new[]
                {
                    Path.Combine(_appFolderInfo.AppDataFolder, "MediaCover"),
                    Path.Combine(_appFolderInfo.AppDataFolder, "MediaCache"),
                };

                var existingDirs = baseDirs.Where(Directory.Exists).ToList();
                if (existingDirs.Count == 0)
                {
                    return;
                }

                var allFiles = new List<FileInfo>();
                foreach (var dir in existingDirs)
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        allFiles.AddRange(dirInfo.EnumerateFiles("*", SearchOption.AllDirectories));
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to enumerate files in {0}", dir);
                    }
                }

                var ttlDays = _configService?.MediaCoverCacheTtlDays ?? 60;
                var maxMb = _configService?.MediaCoverMaxCacheSizeMb ?? 1024;

                var remainingFiles = new List<FileInfo>();
                long totalSize = 0;

                // 1. Prune expired files based on TTL
                var hasTtl = ttlDays > 0;
                var ttlCutoff = hasTtl ? DateTime.UtcNow.AddDays(-ttlDays) : DateTime.MinValue;

                foreach (var file in allFiles)
                {
                    // Clean up orphaned tmp files older than 1 hour
                    if (file.Name.Contains(".tmp.") && file.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1))
                    {
                        try
                        {
                            file.Delete();
                        }
                        catch
                        {
                        }

                        continue;
                    }

                    var effectiveTime = GetLastAccessOrWriteTime(file);
                    if (hasTtl && effectiveTime < ttlCutoff)
                    {
                        try
                        {
                            file.Delete();
                            _logger.Debug("Evicted expired artwork file (TTL {0} days): {1}", ttlDays, file.FullName);
                            continue;
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn(ex, "Failed to delete expired artwork file: {0}", file.FullName);
                        }
                    }

                    remainingFiles.Add(file);
                    try
                    {
                        totalSize += file.Length;
                    }
                    catch
                    {
                    }
                }

                // 2. Enforce disk quota via LRU eviction
                var maxQuotaBytes = maxMb * 1024L * 1024L;
                if (maxQuotaBytes > 0 && totalSize > maxQuotaBytes)
                {
                    var targetQuotaBytes = (long)(maxQuotaBytes * 0.85);
                    _logger.Info(
                        "MediaCover cache size ({0:N0} bytes) exceeds quota ({1:N0} bytes). Evicting down to 85% ({2:N0} bytes)...",
                        totalSize,
                        maxQuotaBytes,
                        targetQuotaBytes);

                    var orderedFiles = remainingFiles
                        .OrderBy(GetLastAccessOrWriteTime)
                        .ToList();

                    foreach (var file in orderedFiles)
                    {
                        if (totalSize < targetQuotaBytes)
                        {
                            break;
                        }

                        try
                        {
                            var len = file.Length;
                            file.Delete();
                            totalSize -= len;
                            _logger.Debug("Evicted LRU artwork file: {0}", file.FullName);
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn(ex, "Failed to delete artwork file during quota eviction: {0}", file.FullName);
                        }
                    }
                }

                // 3. Clean up empty subdirectories
                foreach (var dir in existingDirs)
                {
                    try
                    {
                        CleanEmptySubdirectories(new DirectoryInfo(dir));
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Failed to clean empty subdirectories in {0}", dir);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to execute MediaCover cache eviction");
            }
        }
    }

    private static DateTime GetLastAccessOrWriteTime(FileInfo file)
    {
        try
        {
            var access = file.LastAccessTimeUtc;
            if (access > DateTime.MinValue && access.Year > 1980)
            {
                return access;
            }
        }
        catch
        {
        }

        try
        {
            var write = file.LastWriteTimeUtc;
            if (write > DateTime.MinValue && write.Year > 1980)
            {
                return write;
            }
        }
        catch
        {
        }

        return DateTime.UtcNow;
    }

    private static void CleanEmptySubdirectories(DirectoryInfo root)
    {
        if (!root.Exists)
        {
            return;
        }

        foreach (var subDir in root.EnumerateDirectories())
        {
            CleanEmptySubdirectoriesRecursive(subDir);
        }
    }

    private static void CleanEmptySubdirectoriesRecursive(DirectoryInfo directory)
    {
        foreach (var subDir in directory.EnumerateDirectories())
        {
            CleanEmptySubdirectoriesRecursive(subDir);
        }

        if (!directory.EnumerateFileSystemInfos().Any())
        {
            try
            {
                directory.Delete();
            }
            catch
            {
            }
        }
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

    private HttpClient GetImageHttpClient(string url)
    {
        if (_explicitHttpClient != null)
        {
            return _explicitHttpClient;
        }

        var definitions = _arrRepository?.All() ?? _connectionFactory?.All();
        if (definitions != null && !string.IsNullOrWhiteSpace(url))
        {
            var matched = definitions.FirstOrDefault(c =>
                !string.IsNullOrWhiteSpace(c.Url) &&
                url.StartsWith(c.Url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));

            if (matched != null && matched.AcceptInvalidCertificates)
            {
                return ArrConnectionResources.SharedInsecureClient;
            }
        }

        return _httpClient;
    }

    private async Task<MediaMetadata> QueryServarrAsync(Torrent torrent, string cleanTitle, CancellationToken cancellationToken = default)
    {
        var definitions = (_arrRepository?.All() ?? _connectionFactory?.All())?.Where(d => d.Enable).ToList();
        if (definitions == null || definitions.Count == 0)
        {
            return null;
        }

        foreach (var def in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
                    var records = await provider.GetDownloadHistoryAsync(cancellationToken).ConfigureAwait(false);
                    if (records != null)
                    {
                        var match = records.FirstOrDefault(r => string.Equals(r.InfoHash, torrent.InfoHash, StringComparison.OrdinalIgnoreCase));
                        if (match != null && match.MediaId.HasValue)
                        {
                            var details = await provider.GetMediaDetailsAsync(match.MediaId.Value, cancellationToken).ConfigureAwait(false);
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
                    var lookup = await provider.LookupMediaAsync(cleanTitle, cancellationToken).ConfigureAwait(false);
                    if (lookup != null)
                    {
                        return lookup;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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
                provider = new SonarrConnection(_explicitHttpClient);
                break;
            case "radarr":
                provider = new RadarrConnection(_explicitHttpClient);
                break;
            case "lidarr":
                provider = new LidarrConnection(_explicitHttpClient);
                break;
            case "whisparr":
                provider = new WhisparrConnection(_explicitHttpClient);
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

    public static string CleanReleaseTitle(string rawTitle)
    {
        return ReleaseTitleParser.CleanTitle(rawTitle);
    }

    public static int ExtractYear(string title)
    {
        return ReleaseTitleParser.ExtractYear(title) ?? 0;
    }

    public static string ExtractImdbId(string releaseTitle, string filePath = null)
    {
        if (!string.IsNullOrWhiteSpace(releaseTitle))
        {
            var match = ImdbIdRegex.Match(releaseTitle);
            if (match.Success)
            {
                return match.Groups[1].Value.ToLowerInvariant();
            }
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            try
            {
                string nfoPath = null;
                if (File.Exists(filePath))
                {
                    if (filePath.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase))
                    {
                        nfoPath = filePath;
                    }
                    else
                    {
                        var dir = Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                        {
                            nfoPath = Directory.EnumerateFiles(dir, "*.nfo", SearchOption.TopDirectoryOnly).FirstOrDefault();
                        }
                    }
                }
                else if (Directory.Exists(filePath))
                {
                    nfoPath = Directory.EnumerateFiles(filePath, "*.nfo", SearchOption.TopDirectoryOnly).FirstOrDefault();
                }

                if (!string.IsNullOrWhiteSpace(nfoPath) && File.Exists(nfoPath))
                {
                    var text = File.ReadAllText(nfoPath);
                    var match = ImdbIdRegex.Match(text);
                    if (match.Success)
                    {
                        return match.Groups[1].Value.ToLowerInvariant();
                    }
                }
            }
            catch
            {
                // Suppress NFO inspection errors
            }
        }

        return null;
    }

    public static string GuessArrType(string category, string name)
    {
        var cat = (category ?? string.Empty).ToLowerInvariant();
        var n = (name ?? string.Empty).ToLowerInvariant();

        if (cat.Contains("whisparr") || cat.Contains("adult") || cat.Contains("xxx") || cat.Contains("nsfw") || cat.Contains("porn"))
        {
            return "Whisparr";
        }

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
        if (mt.Contains("whisparr") || mt.Contains("adult"))
        {
            return "Whisparr";
        }

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
