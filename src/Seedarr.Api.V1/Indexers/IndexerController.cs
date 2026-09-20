using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Newznab;
using NzbDrone.Core.Indexers.Prowlarr;
using NzbDrone.Core.Indexers.Torznab;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Network;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Validation;
using Seedarr.Api.V1.Torrents;
using Seedarr.Http;

namespace Seedarr.Api.V1.Indexers;

[V1ApiController("indexers")]
[Route("api/v1/indexer")]
public class IndexerController : Controller
{
    private static readonly HttpClient DefaultClient = new();
    private readonly HttpClient _httpClient;
    private readonly IProxySettingsProvider _proxySettingsProvider;
    private readonly IIndexerFactory _indexerFactory;
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly IDownloadHistoryService _downloadHistoryService;
    private readonly IIndexerStatusService _indexerStatusService;
    private readonly IRssRuleRepository _rssRuleRepository;
    private readonly IProwlarrIndexerSyncService _prowlarrSyncService;
    private readonly ITorrentMediaMetadataRepository _mediaMetadataRepository;

    public IndexerController(
        IIndexerFactory indexerFactory,
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITrackerEntryService trackerEntryService,
        ITorrentFileParser torrentFileParser,
        IDownloadHistoryService downloadHistoryService,
        IIndexerStatusService indexerStatusService = null,
        IProxySettingsProvider proxySettingsProvider = null,
        IRssRuleRepository rssRuleRepository = null,
        HttpClient httpClient = null,
        IProwlarrIndexerSyncService prowlarrSyncService = null,
        ITorrentMediaMetadataRepository mediaMetadataRepository = null)
    {
        _indexerFactory = indexerFactory;
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _trackerEntryService = trackerEntryService;
        _torrentFileParser = torrentFileParser;
        _downloadHistoryService = downloadHistoryService;
        _indexerStatusService = indexerStatusService ?? new IndexerStatusService();
        _proxySettingsProvider = proxySettingsProvider;
        _rssRuleRepository = rssRuleRepository;
        _prowlarrSyncService = prowlarrSyncService;
        _mediaMetadataRepository = mediaMetadataRepository;
        _httpClient = httpClient;
    }

    [HttpGet]
    public ActionResult<List<IndexerDefinition>> GetAll()
    {
        var definitions = _indexerFactory.All();
        return Ok(definitions.Select(MaskApiKey).ToList());
    }

    [HttpGet("{id}")]
    public ActionResult<IndexerDefinition> Get(int id)
    {
        var definition = _indexerFactory.Get(id);
        if (definition == null)
        {
            return NotFound();
        }

        return Ok(MaskApiKey(definition));
    }

    [HttpPost]
    public ActionResult<IndexerDefinition> Create([FromBody] IndexerDefinition definition)
    {
        if (definition == null)
        {
            return BadRequest("Request body cannot be null");
        }

        if (string.IsNullOrWhiteSpace(definition.Implementation))
        {
            definition.Implementation = $"{definition.IndexerType}Indexer";
        }

        if (string.IsNullOrWhiteSpace(definition.ConfigContract))
        {
            definition.ConfigContract = "IndexerDefinition";
        }

        var created = _indexerFactory.Create(definition);
        return Ok(MaskApiKey(created));
    }

    [HttpPut("{id}")]
    public ActionResult Update(int id, [FromBody] IndexerDefinition definition)
    {
        if (definition == null)
        {
            return BadRequest("Request body cannot be null");
        }

        var existing = _indexerFactory.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        definition.Id = id;

        if (string.IsNullOrWhiteSpace(definition.Implementation))
        {
            definition.Implementation = $"{definition.IndexerType}Indexer";
        }

        if (string.IsNullOrWhiteSpace(definition.ConfigContract))
        {
            definition.ConfigContract = "IndexerDefinition";
        }

        // If API key is omitted, empty, or masked, preserve existing value
        if (string.IsNullOrWhiteSpace(definition.ApiKey) || definition.ApiKey.Contains('*'))
        {
            definition.ApiKey = existing.ApiKey;
        }

        _indexerFactory.Update(definition);
        return Ok(MaskApiKey(definition));
    }

    [HttpDelete("{id}")]
    public ActionResult Delete(int id)
    {
        _indexerFactory.Delete(id);

        if (_rssRuleRepository != null)
        {
            var rules = _rssRuleRepository.All();
            foreach (var rule in rules)
            {
                if (rule.IndexerIds != null && rule.IndexerIds.Contains(id))
                {
                    rule.IndexerIds.RemoveAll(x => x == id);
                    _rssRuleRepository.Update(rule);
                }
            }
        }

        return Ok();
    }

    [HttpPost("test")]
    public ActionResult<IndexerTestResult> TestDirect([FromBody] IndexerDefinition definition)
    {
        if (definition == null)
        {
            return BadRequest("Request body cannot be null");
        }

        if (string.IsNullOrWhiteSpace(definition.Url))
        {
            return BadRequest("URL cannot be empty");
        }

        if (!UrlValidator.IsSafeUrl(definition.Url))
        {
            return BadRequest("Target host/URL is not permitted.");
        }

        if (definition.Id > 0 && (string.IsNullOrWhiteSpace(definition.ApiKey) || definition.ApiKey.Contains('*')))
        {
            var existing = _indexerFactory.Get(definition.Id);
            if (existing != null)
            {
                definition.ApiKey = existing.ApiKey;
            }
        }

        IIndexer indexer;
        try
        {
            indexer = CreateIndexer(definition);
        }
        catch (ArgumentException ex)
        {
            return Ok(new IndexerTestResult { Success = false, Message = ex.Message });
        }

        IndexerTestResult result;
        try
        {
            result = indexer.TestConnectionDetailed(definition);
        }
        catch (HttpRequestException ex)
        {
            if (!ex.Data.Contains("Recorded") || !(bool)ex.Data["Recorded"])
            {
                var retryAfter = ex.Data["RetryAfter"] as TimeSpan?;
                if (definition.Id > 0 && !definition.ApiKey.Contains('*'))
                {
                    _indexerStatusService.RecordFailure(definition.Id, (int?)ex.StatusCode, ex.Message, ex, retryAfter);
                }
            }

            return Ok(new IndexerTestResult { Success = false, Message = ex.Message, StatusCode = (int?)ex.StatusCode });
        }
        catch (Exception ex)
        {
            if (definition.Id > 0 && !definition.ApiKey.Contains('*'))
            {
                _indexerStatusService.RecordFailure(definition.Id, null, ex.Message, ex);
            }

            return Ok(new IndexerTestResult { Success = false, Message = ex.Message });
        }

        if (definition.Id > 0 && !definition.ApiKey.Contains('*'))
        {
            if (result.Success)
            {
                _indexerStatusService.RecordSuccess(definition.Id);
            }
            else
            {
                _indexerStatusService.RecordFailure(definition.Id, result.StatusCode, errorMessage: result.Message, retryAfter: result.RetryAfter);
            }
        }

        return Ok(result);
    }

    [HttpPost("{id}/test")]
    public ActionResult<IndexerTestResult> TestConnection(int id)
    {
        var definition = _indexerFactory.Get(id);
        if (definition == null)
        {
            return NotFound();
        }

        if (!UrlValidator.IsSafeUrl(definition.Url, allowLoopback: true, allowInternal: true))
        {
            return BadRequest("Target host/URL is not permitted.");
        }

        IIndexer indexer;
        try
        {
            indexer = CreateIndexer(definition);
        }
        catch (ArgumentException ex)
        {
            return Ok(new IndexerTestResult { Success = false, Message = ex.Message });
        }

        IndexerTestResult result;
        try
        {
            result = indexer.TestConnectionDetailed(definition);
        }
        catch (HttpRequestException ex)
        {
            if (!ex.Data.Contains("Recorded") || !(bool)ex.Data["Recorded"])
            {
                var retryAfter = ex.Data["RetryAfter"] as TimeSpan?;
                _indexerStatusService.RecordFailure(definition.Id, (int?)ex.StatusCode, ex.Message, ex, retryAfter);
            }

            return Ok(new IndexerTestResult { Success = false, Message = ex.Message, StatusCode = (int?)ex.StatusCode });
        }
        catch (Exception ex)
        {
            _indexerStatusService.RecordFailure(definition.Id, null, ex.Message, ex);
            return Ok(new IndexerTestResult { Success = false, Message = ex.Message });
        }

        if (result.Success)
        {
            _indexerStatusService.RecordSuccess(definition.Id);
        }
        else
        {
            _indexerStatusService.RecordFailure(definition.Id, result.StatusCode, errorMessage: result.Message, retryAfter: result.RetryAfter);
        }

        return Ok(result);
    }

    [HttpPost("prowlarr/sync")]
    public ActionResult<ProwlarrSyncResult> SyncProwlarr(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ProwlarrSyncRequest request = null,
        [FromQuery] int? prowlarrIndexerId = null)
    {
        if (_prowlarrSyncService == null)
        {
            return BadRequest(new { message = "Prowlarr sync service is not available." });
        }

        var targetId = request?.ProwlarrIndexerId ?? prowlarrIndexerId;
        var baseUrl = request?.BaseUrl;
        var apiKey = request?.ApiKey;

        var result = _prowlarrSyncService.Sync(targetId, baseUrl, apiKey);
        if (!result.Success && result.Errors.Count > 0 && result.Added == 0 && result.Updated == 0)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<ReleaseInfo>>> Search(
        [FromQuery] string query = null,
        [FromQuery] string category = null,
        [FromQuery] int? indexerId = null,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string searchType = "search",
        [FromQuery] int? season = null,
        [FromQuery] int? ep = null,
        [FromQuery] string imdbId = null,
        [FromQuery] string tmdbId = null,
        [FromQuery] string tvdbId = null,
        [FromQuery] string rid = null,
        [FromQuery] string artist = null,
        [FromQuery] string album = null,
        [FromQuery] string author = null,
        [FromQuery] string title = null,
        [FromQuery] int? year = null,
        [FromQuery] string categories = null)
    {
        var criteria = new TorznabSearchCriteria
        {
            SearchType = searchType,
            Query = query,
            Category = category,
            Categories = categories,
            Offset = offset,
            Limit = limit,
            Season = season,
            Episode = ep,
            ImdbId = imdbId,
            TmdbId = tmdbId,
            TvdbId = tvdbId,
            Rid = rid,
            Artist = artist,
            Album = album,
            Author = author,
            Title = title,
            Year = year
        };

        if (string.IsNullOrWhiteSpace(criteria.Query) &&
            !criteria.Season.HasValue &&
            !criteria.Episode.HasValue &&
            string.IsNullOrWhiteSpace(criteria.ImdbId) &&
            string.IsNullOrWhiteSpace(criteria.TmdbId) &&
            string.IsNullOrWhiteSpace(criteria.TvdbId) &&
            string.IsNullOrWhiteSpace(criteria.Rid) &&
            string.IsNullOrWhiteSpace(criteria.Artist) &&
            string.IsNullOrWhiteSpace(criteria.Album) &&
            string.IsNullOrWhiteSpace(criteria.Author) &&
            string.IsNullOrWhiteSpace(criteria.Title))
        {
            return Ok(new List<ReleaseInfo>());
        }

        List<IndexerDefinition> definitions;
        if (indexerId.HasValue && indexerId.Value > 0)
        {
            var def = _indexerFactory.Get(indexerId.Value);
            if (def == null)
            {
                return NotFound(new { message = $"Indexer with ID {indexerId.Value} not found." });
            }

            if (!def.Enable || !def.EnableSearch)
            {
                return BadRequest(new { message = $"Indexer '{def.Name}' is disabled." });
            }

            if (_indexerStatusService.IsDisabled(def.Id))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = $"Indexer '{def.Name}' is temporarily disabled due to recent failures." });
            }

            definitions = new List<IndexerDefinition> { def };
        }
        else
        {
            definitions = _indexerFactory.All()
                .Where(d => d.Enable && d.EnableSearch && !_indexerStatusService.IsDisabled(d.Id))
                .ToList();
        }

        if (definitions.Count == 0)
        {
            return Ok(new List<ReleaseInfo>());
        }

        Exception singleTargetException = null;

        var searchTasks = definitions.Select(async def =>
        {
            try
            {
                var indexer = CreateIndexer(def);
                var results = await Task.Run(() => indexer.Search(def, criteria)).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                _indexerStatusService.RecordSuccess(def.Id);
                return results ?? new List<ReleaseInfo>();
            }
            catch (TimeoutException ex)
            {
                var message = $"Search timed out after 10 seconds for indexer '{def.Name}'.";
                _indexerStatusService.RecordFailure(def.Id, StatusCodes.Status504GatewayTimeout, message, ex);
                if (indexerId.HasValue && indexerId.Value == def.Id)
                {
                    singleTargetException = new TimeoutException(message, ex);
                }

                return new List<ReleaseInfo>();
            }
            catch (HttpRequestException ex)
            {
                if (!ex.Data.Contains("Recorded") || !(bool)ex.Data["Recorded"])
                {
                    var retryAfter = ex.Data["RetryAfter"] as TimeSpan?;
                    _indexerStatusService.RecordFailure(def.Id, (int?)ex.StatusCode, ex.Message, ex, retryAfter);
                }

                if (indexerId.HasValue && indexerId.Value == def.Id)
                {
                    singleTargetException = ex;
                }

                return new List<ReleaseInfo>();
            }
            catch (IndexerException ex)
            {
                if (!ex.Recorded)
                {
                    _indexerStatusService.RecordFailure(def.Id, ex.StatusCode, ex.Message, ex, ex.RetryAfter);
                }

                if (indexerId.HasValue && indexerId.Value == def.Id)
                {
                    singleTargetException = ex;
                }

                return new List<ReleaseInfo>();
            }
            catch (Exception ex)
            {
                _indexerStatusService.RecordFailure(def.Id, null, ex.Message, ex);
                if (indexerId.HasValue && indexerId.Value == def.Id)
                {
                    singleTargetException = ex;
                }

                return new List<ReleaseInfo>();
            }
        });

        var resultsArray = await Task.WhenAll(searchTasks).ConfigureAwait(false);

        if (indexerId.HasValue && indexerId.Value > 0 && singleTargetException != null)
        {
            if (singleTargetException is HttpRequestException httpEx)
            {
                var statusCode = (int?)httpEx.StatusCode ?? StatusCodes.Status502BadGateway;
                return StatusCode(statusCode, new { message = httpEx.Message });
            }

            if (singleTargetException is IndexerException idxEx)
            {
                var statusCode = idxEx.StatusCode ?? StatusCodes.Status502BadGateway;
                return StatusCode(statusCode, new { message = idxEx.Message });
            }

            if (singleTargetException is TimeoutException timeoutEx)
            {
                return StatusCode(StatusCodes.Status504GatewayTimeout, new { message = timeoutEx.Message });
            }

            return StatusCode(StatusCodes.Status500InternalServerError, new { message = singleTargetException.Message });
        }

        var allResults = resultsArray.SelectMany(r => r).ToList();

        var sorted = allResults
            .OrderByDescending(r => r.Seeders ?? 0)
            .ThenByDescending(r => r.PublishDate ?? DateTime.MinValue)
            .ToList();

        return Ok(sorted);
    }

    [HttpPost("download")]
    public ActionResult<TorrentResource> DownloadRelease([FromBody] DownloadReleaseRequest request)
    {
        if (request == null)
        {
            return BadRequest("Invalid request");
        }

        var magnetUri = !string.IsNullOrWhiteSpace(request.MagnetUrl)
            ? request.MagnetUrl
            : (!string.IsNullOrWhiteSpace(request.DownloadUrl) && request.DownloadUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)
                ? request.DownloadUrl
                : null);

        if (!string.IsNullOrWhiteSpace(magnetUri))
        {
            ParsedMagnetLink parsed;
            try
            {
                parsed = MagnetLinkParser.Parse(magnetUri);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }

            var primaryHash = parsed.InfoHash ?? parsed.InfoHashV2;
            if (_torrentService.ExistsByInfoHash(primaryHash))
            {
                return Conflict(new { message = "Torrent with this info hash already exists in active library" });
            }

            var torrent = new Torrent
            {
                Name = !string.IsNullOrWhiteSpace(request.Title) ? request.Title : parsed.Name,
                InfoHash = parsed.InfoHash ?? parsed.InfoHashV2,
                InfoHashV2 = parsed.InfoHashV2,
                TrackerUrl = parsed.Trackers.Length > 0 ? parsed.Trackers[0] : null,
                Status = TorrentStatus.Queued,
                DateAdded = DateTime.UtcNow,
                RatioLimit = request.MinimumRatio,
                SeedingTimeLimit = request.MinimumSeedTime.HasValue ? (int?)Math.Min(request.MinimumSeedTime.Value, int.MaxValue) : null
            };

            var added = _torrentService.Add(torrent);
            SaveReleaseMediaMetadata(added.Id, added.Name, request);

            var tier = 0;
            foreach (var url in parsed.Trackers)
            {
                _trackerEntryService.Add(new TrackerEntry
                {
                    TorrentId = added.Id,
                    Url = url,
                    Tier = tier++,
                    Enabled = true
                });
            }

            _downloadHistoryService.RecordTorrentAdded(
                added,
                source: DetermineSource(request),
                magnetUrl: magnetUri,
                indexerName: request.IndexerName);

            return Ok(TorrentResourceMapper.ToResource(added));
        }

        if (!string.IsNullOrWhiteSpace(request.DownloadUrl))
        {
            if (!UrlValidator.IsSafeUrl(request.DownloadUrl))
            {
                return BadRequest("Invalid or unsafe download URL.");
            }

            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.DownloadUrl);

                if (request.IndexerId.HasValue && request.IndexerId.Value > 0)
                {
                    var def = _indexerFactory.Get(request.IndexerId.Value);
                    if (def != null && !string.IsNullOrWhiteSpace(def.ApiKey))
                    {
                        if (Uri.TryCreate(request.DownloadUrl, UriKind.Absolute, out var downloadUri) &&
                            !string.IsNullOrWhiteSpace(def.Url) &&
                            Uri.TryCreate(def.Url, UriKind.Absolute, out var indexerUri) &&
                            string.Equals(downloadUri.Host, indexerUri.Host, StringComparison.OrdinalIgnoreCase))
                        {
                            httpRequest.Headers.Add("X-Api-Key", def.ApiKey);
                        }
                    }
                }

                using var httpResponse = GetHttpClient().Send(httpRequest);
                if (!httpResponse.IsSuccessStatusCode)
                {
                    return BadRequest($"Failed to download torrent file from indexer (HTTP {(int)httpResponse.StatusCode})");
                }

                using var stream = httpResponse.Content.ReadAsStream();
                var parsed = _torrentFileParser.Parse(stream);

                if (_torrentService.ExistsByInfoHash(parsed.InfoHash))
                {
                    return Conflict(new { message = "Torrent with this info hash already exists in active library" });
                }

                var torrent = new Torrent
                {
                    Name = !string.IsNullOrWhiteSpace(request.Title) ? request.Title : parsed.Name,
                    InfoHash = parsed.InfoHash,
                    TotalSize = parsed.TotalSize,
                    PieceCount = parsed.PieceCount,
                    PieceLength = parsed.PieceLength,
                    Comment = parsed.Comment,
                    CreatedBy = parsed.CreatedBy,
                    CreationDate = parsed.CreationDate,
                    IsPrivate = parsed.IsPrivate,
                    TrackerUrl = parsed.AnnounceUrl,
                    Status = TorrentStatus.Queued,
                    DateAdded = DateTime.UtcNow,
                    RatioLimit = request.MinimumRatio,
                    SeedingTimeLimit = request.MinimumSeedTime.HasValue ? (int?)Math.Min(request.MinimumSeedTime.Value, int.MaxValue) : null
                };

                var addedTorrent = _torrentService.Add(torrent);
                SaveReleaseMediaMetadata(addedTorrent.Id, addedTorrent.Name, request);

                if (parsed.Files != null)
                {
                    foreach (var f in parsed.Files)
                    {
                        _torrentFileService.Add(new TorrentFile { TorrentId = addedTorrent.Id, Path = f.Path, Size = f.Size, IsPaddingFile = f.IsPaddingFile });
                    }
                }

                if (parsed.AnnounceList != null)
                {
                    var tier = 0;
                    foreach (var tierUrls in parsed.AnnounceList)
                    {
                        foreach (var url in tierUrls)
                        {
                            _trackerEntryService.Add(new TrackerEntry { TorrentId = addedTorrent.Id, Url = url, Tier = tier, Enabled = true });
                        }

                        tier++;
                    }
                }
                else if (!string.IsNullOrEmpty(parsed.AnnounceUrl))
                {
                    _trackerEntryService.Add(new TrackerEntry { TorrentId = addedTorrent.Id, Url = parsed.AnnounceUrl, Tier = 0, Enabled = true });
                }

                _downloadHistoryService.RecordTorrentAdded(
                    addedTorrent,
                    source: DetermineSource(request),
                    downloadUrl: request.DownloadUrl,
                    indexerName: request.IndexerName);

                return Ok(TorrentResourceMapper.ToResource(addedTorrent));
            }
            catch (Exception ex)
            {
                return BadRequest($"Failed to parse and add torrent release: {ex.Message}");
            }
        }

        return BadRequest("Neither DownloadUrl nor MagnetUrl was provided");
    }

    private static IndexerDefinition MaskApiKey(IndexerDefinition definition)
    {
        var clone = definition.Clone();
        clone.ApiKey = clone.ApiKey?.Length > 4
            ? new string('*', clone.ApiKey.Length - 4) + clone.ApiKey[^4..]
            : new string('*', clone.ApiKey?.Length ?? 0);
        if (clone.Capabilities == null)
        {
            clone.Capabilities = TorznabIndexer.GetCachedCapabilities(definition);
        }

        return clone;
    }

    [HttpGet("{id}/caps")]
    public ActionResult<TorznabCapabilities> GetCapabilities(int id)
    {
        var definition = _indexerFactory.Get(id);
        if (definition == null)
        {
            return NotFound();
        }

        if (definition.Capabilities != null)
        {
            return Ok(definition.Capabilities);
        }

        var cached = TorznabIndexer.GetCachedCapabilities(definition);
        if (cached != null)
        {
            return Ok(cached);
        }

        IIndexer indexer;
        try
        {
            indexer = CreateIndexer(definition);
        }
        catch (Exception)
        {
            return Ok(new TorznabCapabilities());
        }

        if (indexer is TorznabIndexer torznabIndexer)
        {
            var caps = torznabIndexer.GetCapabilities(definition);
            return Ok(caps ?? new TorznabCapabilities());
        }

        return Ok(new TorznabCapabilities());
    }

    private IIndexer CreateIndexer(IndexerDefinition definition)
    {
        return definition.IndexerType switch
        {
            "Prowlarr" => new ProwlarrIndexer(_httpClient, _indexerStatusService, _proxySettingsProvider),
            "Torznab" => new TorznabIndexer(_httpClient, _indexerStatusService, _proxySettingsProvider),
            "Newznab" => new NewznabIndexer(_httpClient, _indexerStatusService, _proxySettingsProvider),
            _ => throw new ArgumentException($"Unknown indexer type: {definition.IndexerType}"),
        };
    }

    private HttpClient GetHttpClient()
    {
        if (_proxySettingsProvider != null && _proxySettingsProvider.IsEnabled)
        {
            var handler = _proxySettingsProvider.CreateHandler();
            return handler != null ? new HttpClient(handler) : DefaultClient;
        }

        return _httpClient ?? DefaultClient;
    }

    private string DetermineSource(DownloadReleaseRequest request)
    {
        if (request.IndexerId.HasValue && request.IndexerId.Value > 0)
        {
            try
            {
                var def = _indexerFactory.Get(request.IndexerId.Value);
                if (def != null)
                {
                    var baseType = !string.IsNullOrWhiteSpace(def.IndexerType) ? def.IndexerType : (!string.IsNullOrWhiteSpace(def.Name) ? def.Name : "Indexer");
                    if (!string.IsNullOrWhiteSpace(request.IndexerName))
                    {
                        if (string.Equals(baseType, request.IndexerName, StringComparison.OrdinalIgnoreCase))
                        {
                            return baseType;
                        }

                        return $"{baseType} ({request.IndexerName})";
                    }

                    return baseType;
                }
            }
            catch
            {
                // Fallback if indexer retrieval fails
            }
        }

        if (!string.IsNullOrWhiteSpace(request.IndexerName))
        {
            return request.IndexerName;
        }

        return "Indexer";
    }

    private void SaveReleaseMediaMetadata(int torrentId, string title, DownloadReleaseRequest request)
    {
        if (_mediaMetadataRepository == null || request == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.ImdbId) && !request.TmdbId.HasValue && !request.TvdbId.HasValue)
        {
            return;
        }

        try
        {
            _mediaMetadataRepository.Upsert(new TorrentMediaMetadata
            {
                TorrentId = torrentId,
                Title = title,
                ImdbId = ReleaseInfo.NormalizeImdbId(request.ImdbId),
                TmdbId = request.TmdbId?.ToString(),
                TvdbId = request.TvdbId?.ToString()
            });
        }
        catch
        {
            // Non-fatal if media metadata persistence fails
        }
    }
}

public class DownloadReleaseRequest
{
    public string Title { get; set; }
    public string DownloadUrl { get; set; }
    public string MagnetUrl { get; set; }
    public string InfoHash { get; set; }
    public int? IndexerId { get; set; }
    public string IndexerName { get; set; }
    public string ImdbId { get; set; }
    public int? TmdbId { get; set; }
    public int? TvdbId { get; set; }
    public double? MinimumRatio { get; set; }
    public long? MinimumSeedTime { get; set; }
}

public class ProwlarrSyncRequest
{
    public int? ProwlarrIndexerId { get; set; }
    public string BaseUrl { get; set; }
    public string ApiKey { get; set; }
}
