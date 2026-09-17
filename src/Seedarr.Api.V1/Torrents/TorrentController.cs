using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerBoost;
using NzbDrone.Core.Trackers;
using NzbDrone.SignalR;
using Seedarr.Api.V1.MediaCover;
using Seedarr.Http;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Torrents;

[V1ApiController("torrent")]
public class TorrentController : RestControllerWithSignalR<TorrentResource, Torrent>
{
    private readonly Logger _logger;
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly ITorrentImportService _torrentImportService;
    private readonly IConnectionManager _connectionManager;
    private readonly ITorrentEventLogService _eventLogService;
    private readonly IConfigService _configService;
    private readonly IDownloadHistoryRepository _downloadHistoryRepository;
    private readonly ITrackerBoostService _trackerBoostService;
    private readonly ITrackerAnnounceService _trackerAnnounceService;
    private readonly IMediaEnrichmentService _mediaEnrichmentService;
    private readonly ICategoryService _categoryService;
    private readonly NzbDrone.Core.Network.GeoIp.IGeoIpService _geoIpService;

    private readonly ConcurrentDictionary<int, (List<TrackerEntry> Trackers, DateTime Expiry)> _broadcastTrackersCache = new();
    private readonly ConcurrentDictionary<int, (TorrentMediaMetadata Metadata, DateTime Expiry)> _broadcastMediaMetaCache = new();
    private readonly ConcurrentDictionary<string, (DownloadHistory History, MediaMetadata ParsedMetadata, DateTime Expiry)> _broadcastHistoryCache = new();

    public TorrentController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITrackerEntryService trackerEntryService,
        ITorrentImportService torrentImportService,
        IConnectionManager connectionManager,
        ITorrentEventLogService eventLogService,
        IConfigService configService,
        IBroadcastSignalRMessage signalRBroadcaster,
        TorrentResourceValidator torrentResourceValidator,
        IDownloadHistoryRepository downloadHistoryRepository = null,
        ITrackerBoostService trackerBoostService = null,
        ITrackerAnnounceService trackerAnnounceService = null,
        IMediaEnrichmentService mediaEnrichmentService = null,
        ICategoryService categoryService = null,
        NzbDrone.Core.Network.GeoIp.IGeoIpService geoIpService = null,
        TimeSpan? coalesceWindow = null)
        : base(signalRBroadcaster, null, coalesceWindow)
    {
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _trackerEntryService = trackerEntryService;
        _torrentImportService = torrentImportService;
        _connectionManager = connectionManager;
        _eventLogService = eventLogService;
        _configService = configService;
        _downloadHistoryRepository = downloadHistoryRepository;
        _trackerBoostService = trackerBoostService;
        _trackerAnnounceService = trackerAnnounceService;
        _mediaEnrichmentService = mediaEnrichmentService;
        _categoryService = categoryService;
        _geoIpService = geoIpService;
        _logger = LogManager.GetCurrentClassLogger();

        SharedValidator = torrentResourceValidator;
    }

    private TorrentResource MapTorrentToResource(Torrent torrent)
    {
        var trackers = _trackerEntryService.GetByTorrentId(torrent.Id);
        return MapTorrentToResource(torrent, trackers);
    }

    private TorrentResource MapTorrentToResource(
        Torrent torrent,
        List<TrackerEntry> trackers,
        Dictionary<string, DownloadHistory> histories = null,
        Dictionary<int, TorrentMediaMetadata> mediaMetas = null)
    {
        var torrentTrackers = trackers.Where(t => t.TorrentId == torrent.Id).ToList();
        var resource = TorrentResourceMapper.ToResource(torrent, torrentTrackers.Select(t => t.Url));

        if (torrentTrackers.Any())
        {
            var mainTracker = torrentTrackers.OrderBy(tr => tr.Tier).First();
            resource.AnnounceInterval = mainTracker.AnnounceInterval;

            if (string.IsNullOrWhiteSpace(resource.TrackerUrl))
            {
                resource.TrackerUrl = mainTracker.Url;
            }

            if (mainTracker.NextAnnounce.HasValue && mainTracker.NextAnnounce.Value > DateTime.UtcNow)
            {
                resource.NextUpdate = (int)(mainTracker.NextAnnounce.Value - DateTime.UtcNow).TotalSeconds;
            }
            else
            {
                resource.NextUpdate = 0;
            }
        }
        else
        {
            resource.AnnounceInterval = _configService.AnnounceIntervalSeconds;
            resource.NextUpdate = 0;
        }

        // 1. Direct Media Enrichment metadata
        TorrentMediaMetadata mediaMetadata = null;
        if (mediaMetas != null)
        {
            mediaMetas.TryGetValue(torrent.Id, out mediaMetadata);
        }
        else if (_mediaEnrichmentService != null && torrent.Id > 0)
        {
            mediaMetadata = _mediaEnrichmentService.GetMetadata(torrent.Id);
        }

        if (mediaMetadata != null)
        {
            ApplyMediaMetadataToResource(resource, mediaMetadata);
        }

        // 2. DownloadHistory metadata fallback
        if (!string.IsNullOrEmpty(torrent.InfoHash))
        {
            try
            {
                DownloadHistory history = null;
                if (histories != null)
                {
                    histories.TryGetValue(torrent.InfoHash, out history);
                }
                else if (_downloadHistoryRepository != null)
                {
                    history = _downloadHistoryRepository.FindByInfoHash(torrent.InfoHash);
                }

                if (history != null)
                {
                    if (string.IsNullOrEmpty(resource.Source))
                    {
                        resource.Source = history.Source;
                    }

                    if (!string.IsNullOrEmpty(history.DataJson))
                    {
                        var metadata = JsonSerializer.Deserialize<MediaMetadata>(
                            history.DataJson,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (metadata != null)
                        {
                            ApplyHistoryMetadataToResource(resource, metadata);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to deserialize media metadata for torrent {0}", torrent.InfoHash);
            }
        }

        return resource;
    }

    public void InvalidateBroadcastCache(int torrentId, string infoHash = null)
    {
        _broadcastTrackersCache.TryRemove(torrentId, out _);
        _broadcastMediaMetaCache.TryRemove(torrentId, out _);
        if (!string.IsNullOrEmpty(infoHash))
        {
            _broadcastHistoryCache.TryRemove(infoHash, out _);
        }
    }

    protected override TorrentResource GetResourceById(Torrent model)
    {
        return MapTorrentToResourceForBroadcast(model);
    }

    private TorrentResource MapTorrentToResourceForBroadcast(Torrent torrent)
    {
        var trackers = GetBroadcastTrackers(torrent.Id);
        var mediaMeta = GetBroadcastMediaMetadata(torrent.Id);
        var (history, historyParsedMeta) = GetBroadcastHistoryMetadata(torrent.InfoHash);

        var torrentTrackers = trackers.Where(t => t.TorrentId == torrent.Id).ToList();
        var resource = TorrentResourceMapper.ToResource(torrent, torrentTrackers.Select(t => t.Url));

        if (torrentTrackers.Any())
        {
            var mainTracker = torrentTrackers.OrderBy(tr => tr.Tier).First();
            resource.AnnounceInterval = mainTracker.AnnounceInterval;

            if (string.IsNullOrWhiteSpace(resource.TrackerUrl))
            {
                resource.TrackerUrl = mainTracker.Url;
            }

            if (mainTracker.NextAnnounce.HasValue && mainTracker.NextAnnounce.Value > DateTime.UtcNow)
            {
                resource.NextUpdate = (int)(mainTracker.NextAnnounce.Value - DateTime.UtcNow).TotalSeconds;
            }
            else
            {
                resource.NextUpdate = 0;
            }
        }
        else
        {
            resource.AnnounceInterval = _configService?.AnnounceIntervalSeconds ?? 0;
            resource.NextUpdate = 0;
        }

        if (mediaMeta != null)
        {
            ApplyMediaMetadataToResource(resource, mediaMeta);
        }

        if (history != null)
        {
            if (string.IsNullOrEmpty(resource.Source))
            {
                resource.Source = history.Source;
            }

            if (historyParsedMeta != null)
            {
                ApplyHistoryMetadataToResource(resource, historyParsedMeta);
            }
        }

        return resource;
    }

    private List<TrackerEntry> GetBroadcastTrackers(int torrentId)
    {
        var now = DateTime.UtcNow;
        if (_broadcastTrackersCache.TryGetValue(torrentId, out var entry) && entry.Expiry > now)
        {
            return entry.Trackers;
        }

        var trackers = _trackerEntryService?.GetByTorrentId(torrentId) ?? new List<TrackerEntry>();
        _broadcastTrackersCache[torrentId] = (trackers, now.AddSeconds(30));
        return trackers;
    }

    private TorrentMediaMetadata GetBroadcastMediaMetadata(int torrentId)
    {
        if (_mediaEnrichmentService == null || torrentId <= 0)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        if (_broadcastMediaMetaCache.TryGetValue(torrentId, out var entry) && entry.Expiry > now)
        {
            return entry.Metadata;
        }

        var metadata = _mediaEnrichmentService.GetMetadata(torrentId);
        _broadcastMediaMetaCache[torrentId] = (metadata, now.AddMinutes(2));
        return metadata;
    }

    private (DownloadHistory History, MediaMetadata ParsedMetadata) GetBroadcastHistoryMetadata(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash) || _downloadHistoryRepository == null)
        {
            return (null, null);
        }

        var now = DateTime.UtcNow;
        if (_broadcastHistoryCache.TryGetValue(infoHash, out var entry) && entry.Expiry > now)
        {
            return (entry.History, entry.ParsedMetadata);
        }

        var history = _downloadHistoryRepository.FindByInfoHash(infoHash);
        MediaMetadata parsed = null;
        if (history != null && !string.IsNullOrEmpty(history.DataJson))
        {
            try
            {
                parsed = JsonSerializer.Deserialize<MediaMetadata>(
                    history.DataJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to deserialize media metadata for torrent {0}", infoHash);
            }
        }

        _broadcastHistoryCache[infoHash] = (history, parsed, now.AddMinutes(5));
        return (history, parsed);
    }

    private static void ApplyMediaMetadataToResource(TorrentResource resource, TorrentMediaMetadata mediaMetadata)
    {
        if (mediaMetadata == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(mediaMetadata.Title))
        {
            resource.MediaTitle = mediaMetadata.Title;
        }

        if (mediaMetadata.Year > 0)
        {
            resource.Year = mediaMetadata.Year;
        }

        if (!string.IsNullOrEmpty(mediaMetadata.Overview))
        {
            resource.Overview = mediaMetadata.Overview;
        }

        if (mediaMetadata.Rating > 0)
        {
            resource.Rating = mediaMetadata.Rating;
        }

        if (!string.IsNullOrEmpty(mediaMetadata.ArrType))
        {
            resource.Source = mediaMetadata.ArrType;
        }

        if (!string.IsNullOrEmpty(mediaMetadata.PosterLocalPath))
        {
            resource.PosterUrl = $"/api/v1/mediacover/{mediaMetadata.TorrentId}/poster.jpg";
        }
        else if (!string.IsNullOrEmpty(mediaMetadata.PosterUrl))
        {
            resource.PosterUrl = mediaMetadata.PosterUrl;
        }

        if (!string.IsNullOrEmpty(mediaMetadata.BackdropLocalPath))
        {
            resource.FanartUrl = $"/api/v1/mediacover/{mediaMetadata.TorrentId}/backdrop.jpg";
        }
        else if (!string.IsNullOrEmpty(mediaMetadata.BackdropUrl))
        {
            resource.FanartUrl = mediaMetadata.BackdropUrl;
        }

        if (!string.IsNullOrEmpty(mediaMetadata.BannerUrl))
        {
            resource.BannerUrl = mediaMetadata.BannerUrl;
        }

        if (!string.IsNullOrEmpty(mediaMetadata.Genres))
        {
            resource.Genres = mediaMetadata.Genres.Split(',').Select(g => g.Trim()).Where(g => g.Length > 0).ToList();
        }
    }

    private static void ApplyHistoryMetadataToResource(TorrentResource resource, MediaMetadata metadata)
    {
        if (metadata == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(resource.PosterUrl))
        {
            resource.PosterUrl = metadata.PosterUrl;
        }

        if (string.IsNullOrEmpty(resource.FanartUrl))
        {
            resource.FanartUrl = metadata.FanartUrl;
        }

        if (string.IsNullOrEmpty(resource.BannerUrl))
        {
            resource.BannerUrl = metadata.BannerUrl;
        }

        if (string.IsNullOrEmpty(resource.MediaTitle))
        {
            resource.MediaTitle = metadata.Title;
        }

        if (!resource.Year.HasValue)
        {
            resource.Year = metadata.Year;
        }

        if (string.IsNullOrEmpty(resource.Overview))
        {
            resource.Overview = metadata.Overview;
        }

        if (!resource.Rating.HasValue)
        {
            resource.Rating = metadata.Rating;
        }

        if (resource.Genres == null || resource.Genres.Count == 0)
        {
            resource.Genres = metadata.Genres ?? new();
        }
    }

    [HttpGet]
    public List<TorrentResource> GetAll()
    {
        var torrents = _torrentService.GetAll();
        var trackers = _trackerEntryService.All();
        var histories = _downloadHistoryRepository?.All()
            .GroupBy(h => h.InfoHash, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First(), StringComparer.OrdinalIgnoreCase);
        var mediaMetas = _mediaEnrichmentService?.GetAllMetadata();

        return torrents.Select(t => MapTorrentToResource(t, trackers, histories, mediaMetas)).ToList();
    }

    [HttpGet("{id:int}")]
    public ActionResult<TorrentResource> GetById(int id)
    {
        var torrent = _torrentService.Get(id);
        if (torrent == null)
        {
            return NotFound();
        }

        return MapTorrentToResource(torrent);
    }

    [HttpGet("{torrentId:int}/media")]
    public ActionResult<MediaMetadataResource> GetMedia(int torrentId)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return NotFound();
        }

        var meta = _mediaEnrichmentService?.GetMetadata(torrentId);
        if (meta == null)
        {
            return NotFound();
        }

        return Ok(MediaMetadataResourceMapper.ToResource(meta));
    }

    [HttpGet("{torrentId:int}/files")]
    public ActionResult<List<TorrentFileResource>> GetFiles(int torrentId, [FromQuery] bool? includePadding = null)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return NotFound();
        }

        var files = _torrentFileService.GetByTorrentId(torrentId);
        if (includePadding == false)
        {
            files = files.Where(f => !f.IsPaddingFile).ToList();
        }

        return files.Select(TorrentResourceMapper.ToFileResource).ToList();
    }

    [HttpGet("{torrentId:int}/trackers")]
    public ActionResult<List<TrackerEntryResource>> GetTrackers(int torrentId)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return NotFound();
        }

        var trackers = _trackerEntryService.GetByTorrentId(torrentId);
        return trackers.Select(TorrentResourceMapper.ToTrackerResource).ToList();
    }

    [HttpPost("{torrentId:int}/trackers")]
    public ActionResult<TrackerEntryResource> AddTracker(int torrentId, [FromBody] AddTorrentTrackerResource resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Url))
        {
            return BadRequest(new { message = "Tracker URL is required." });
        }

        if (!Uri.TryCreate(resource.Url.Trim(), UriKind.Absolute, out var uri) ||
            (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
             !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) &&
             !uri.Scheme.Equals("udp", StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest("Invalid tracker URL. Must be an HTTP, HTTPS, or UDP URL.");
        }

        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return NotFound();
        }

        if (torrent.IsPrivate)
        {
            return BadRequest(new { message = "Cannot add external trackers to a private torrent (BEP 27)." });
        }

        var clean = resource.Url.Trim();
        var existing = _trackerEntryService.GetByTorrentId(torrentId)
            .FirstOrDefault(t => string.Equals((t.Url ?? string.Empty).Trim(), clean, StringComparison.OrdinalIgnoreCase));

        TrackerEntry entry;
        if (existing != null)
        {
            entry = existing;
        }
        else
        {
            entry = new TrackerEntry
            {
                TorrentId = torrentId,
                Url = clean,
                Tier = resource.Tier > 0 ? resource.Tier : 1,
                Status = TrackerStatus.Unknown,
                Enabled = true,
                Seeders = 0,
                Leechers = 0,
                AnnounceInterval = 1800,
                MinAnnounceInterval = 900
            };
            entry = _trackerEntryService.Add(entry);
        }

        if (string.IsNullOrWhiteSpace(torrent.TrackerUrl))
        {
            torrent.TrackerUrl = clean;
            _torrentService.Update(torrent);
        }

        // Also inject into download clients
        if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            _trackerBoostService?.InjectIntoDownloadClients(torrent.InfoHash, new[] { clean });
        }

        TriggerAnnounceInternal(torrent);
        _eventLogService.Info(torrentId, "Tracker", $"Added tracker {clean} and triggered announce");

        _broadcastTrackersCache.TryRemove(torrentId, out _);

        var updatedEntry = _trackerEntryService.GetByTorrentId(torrentId).FirstOrDefault(t => t.Id == entry.Id) ?? entry;

        return Ok(TorrentResourceMapper.ToTrackerResource(updatedEntry));
    }

    [HttpPut("{torrentId:int}/trackers/{trackerId:int}")]
    public ActionResult<TrackerEntryResource> UpdateTracker(int torrentId, int trackerId, [FromBody] UpdateTorrentTrackerResource resource)
    {
        if (resource == null)
        {
            return BadRequest(new { message = "Update resource is required." });
        }

        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return NotFound();
        }

        var trackers = _trackerEntryService.GetByTorrentId(torrentId);
        var target = trackers.FirstOrDefault(t => t.Id == trackerId);
        if (target == null)
        {
            return NotFound();
        }

        if (resource.Tier.HasValue)
        {
            target.Tier = Math.Max(0, resource.Tier.Value);
        }

        if (resource.Enabled.HasValue)
        {
            target.Enabled = resource.Enabled.Value;
        }

        _trackerEntryService.Update(target);
        _broadcastTrackersCache.TryRemove(torrentId, out _);
        _eventLogService.Info(torrentId, "Tracker", $"Updated tracker {target.Url}: Tier={target.Tier}, Enabled={target.Enabled}");

        var remaining = _trackerEntryService.GetByTorrentId(torrentId);
        var primaryTracker = remaining.Where(t => t.Enabled).OrderBy(t => t.Tier).FirstOrDefault()?.Url
            ?? remaining.OrderBy(t => t.Tier).FirstOrDefault()?.Url;

        if (torrent.TrackerUrl != primaryTracker)
        {
            torrent.TrackerUrl = primaryTracker;
            _torrentService.Update(torrent);
        }

        return Ok(TorrentResourceMapper.ToTrackerResource(target));
    }

    [HttpDelete("{torrentId:int}/trackers/{trackerId:int}")]
    public ActionResult DeleteTracker(int torrentId, int trackerId)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return NotFound();
        }

        var trackers = _trackerEntryService.GetByTorrentId(torrentId);
        var target = trackers.FirstOrDefault(t => t.Id == trackerId);
        if (target == null)
        {
            return NotFound();
        }

        _trackerEntryService.Delete(trackerId);
        _broadcastTrackersCache.TryRemove(torrentId, out _);
        _eventLogService.Info(torrentId, "Tracker", $"Removed tracker {target.Url}");

        var remaining = _trackerEntryService.GetByTorrentId(torrentId);
        torrent.TrackerUrl = remaining.OrderBy(t => t.Tier).FirstOrDefault()?.Url;
        _torrentService.Update(torrent);

        TriggerAnnounceInternal(torrent);

        return NoContent();
    }

    [HttpGet("{torrentId:int}/peers")]
    public ActionResult<List<PeerResource>> GetPeers(int torrentId)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return NotFound();
        }

        if (string.IsNullOrEmpty(torrent.InfoHash))
        {
            return new List<PeerResource>();
        }

        var connections = _connectionManager.GetConnections(torrent.InfoHash);
        var id = 1;
        return connections.Select(c =>
        {
            var geo = _geoIpService?.Lookup(c.RemoteIp);
            return TorrentResourceMapper.ToPeerResource(c, id++, geo);
        }).ToList();
    }

    [HttpPost]
    public ActionResult<TorrentResource> Create([FromBody] TorrentResource resource)
    {
        if (!string.IsNullOrEmpty(resource.MagnetLink))
        {
            try
            {
                var imported = _torrentImportService.ImportFromMagnet(resource.MagnetLink);
                var shouldUpdate = false;

                if (!string.IsNullOrWhiteSpace(resource.Category))
                {
                    imported.Category = resource.Category;
                    if (_categoryService != null)
                    {
                        var cat = _categoryService.GetByName(resource.Category);
                        ApplyCategoryLimits(imported, cat);
                    }

                    shouldUpdate = true;
                }

                if (!string.IsNullOrWhiteSpace(resource.SavePath))
                {
                    imported.SavePath = resource.SavePath;
                    shouldUpdate = true;
                }

                if (resource.SequentialDownload)
                {
                    imported.SequentialDownload = true;
                    shouldUpdate = true;
                }

                if (string.Equals(resource.Status, "Paused", StringComparison.OrdinalIgnoreCase))
                {
                    imported.Status = TorrentStatus.Paused;
                    shouldUpdate = true;
                }

                if (shouldUpdate)
                {
                    _torrentService.Update(imported);
                }

                return Created($"/api/v1/torrent/{imported.Id}", TorrentResourceMapper.ToResource(imported));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (DuplicateTorrentException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        var validationResult = SharedValidator.Validate(resource);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        if (string.IsNullOrWhiteSpace(resource.InfoHash) || !global::System.Text.RegularExpressions.Regex.IsMatch(resource.InfoHash, "^[a-fA-F0-9]{40}$"))
        {
            return BadRequest(new { message = "'InfoHash' must be a 40-character hexadecimal string." });
        }

        var existing = _torrentService.GetByInfoHash(resource.InfoHash);
        if (existing != null)
        {
            if (resource.Trackers != null && resource.Trackers.Count > 0)
            {
                var existingTrackers = _trackerEntryService.GetByTorrentId(existing.Id);
                var existingUrls = new HashSet<string>(existingTrackers.Select(t => t.Url), StringComparer.OrdinalIgnoreCase);

                var tier = 0;
                foreach (var url in resource.Trackers)
                {
                    if (!string.IsNullOrWhiteSpace(url) && existingUrls.Add(url))
                    {
                        _trackerEntryService.Add(new TrackerEntry
                        {
                            TorrentId = existing.Id,
                            Url = url,
                            Tier = tier,
                            Enabled = true
                        });
                    }

                    tier++;
                }
            }
            else if (!string.IsNullOrWhiteSpace(resource.TrackerUrl))
            {
                var existingTrackers = _trackerEntryService.GetByTorrentId(existing.Id);
                var existingUrls = new HashSet<string>(existingTrackers.Select(t => t.Url), StringComparer.OrdinalIgnoreCase);

                if (existingUrls.Add(resource.TrackerUrl))
                {
                    _trackerEntryService.Add(new TrackerEntry
                    {
                        TorrentId = existing.Id,
                        Url = resource.TrackerUrl,
                        Tier = 0,
                        Enabled = true
                    });
                }
            }

            return Ok(TorrentResourceMapper.ToResource(existing));
        }

        var torrent = TorrentResourceMapper.ToModel(resource);
        torrent.DateAdded = DateTime.UtcNow;

        try
        {
            var added = _torrentService.Add(torrent);
            return Created($"/api/v1/torrent/{added.Id}", TorrentResourceMapper.ToResource(added));
        }
        catch (DuplicateTorrentException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public IActionResult Upload(
        [FromForm(Name = "file")] List<IFormFile> files,
        [FromForm] string category = null,
        [FromForm] string savePath = null,
        [FromForm] bool? sequentialDownload = null,
        [FromForm] bool? paused = null)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest("No torrent file provided");
        }

        var added = new List<TorrentResource>();
        var failed = new List<TorrentUploadFailure>();

        foreach (var file in files)
        {
            if (file == null || file.Length == 0)
            {
                continue;
            }

            try
            {
                using var stream = file.OpenReadStream();
                var torrent = _torrentImportService.ImportFromFile(stream, file.FileName);
                var shouldUpdate = false;

                if (!string.IsNullOrWhiteSpace(category))
                {
                    torrent.Category = category;
                    if (_categoryService != null)
                    {
                        var cat = _categoryService.GetByName(category);
                        ApplyCategoryLimits(torrent, cat);
                    }

                    shouldUpdate = true;
                }

                if (!string.IsNullOrWhiteSpace(savePath))
                {
                    torrent.SavePath = savePath;
                    shouldUpdate = true;
                }

                if (sequentialDownload == true)
                {
                    torrent.SequentialDownload = true;
                    shouldUpdate = true;
                }

                if (paused == true)
                {
                    torrent.Status = TorrentStatus.Paused;
                    shouldUpdate = true;
                }

                if (shouldUpdate)
                {
                    _torrentService.Update(torrent);
                }

                added.Add(TorrentResourceMapper.ToResource(torrent));
            }
            catch (Exception ex)
            {
                failed.Add(new TorrentUploadFailure(file.FileName, ex.Message));
            }
        }

        return Ok(new TorrentUploadResult(added, failed));
    }

    [HttpPut("{id:int}")]
    public ActionResult<TorrentResource> Update(int id, [FromBody] TorrentResource resource)
    {
        var validationResult = SharedValidator.Validate(resource);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        // Detect Force Complete: when progress is set to 1.0 via PUT, mark ForceCompleted
        if (resource.Progress >= 1.0)
        {
            resource.ForceCompleted = true;
        }

        var existing = _torrentService.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        LogUpdateTransitions(existing, resource);

        var updates = TorrentResourceMapper.ToModel(resource);
        var updated = _torrentService.UpdateUserFields(id, updates);
        return MapTorrentToResource(updated);
    }

    private List<TrackerAnnounceResult> TriggerAnnounceInternal(Torrent torrent, TrackerEntry specificTracker = null)
    {
        if (torrent == null)
        {
            return new List<TrackerAnnounceResult>();
        }

        List<TrackerAnnounceResult> results = null;

        if (_trackerAnnounceService != null)
        {
            if (specificTracker != null)
            {
                var result = _trackerAnnounceService.AnnounceTracker(torrent, specificTracker, force: true);
                results = new List<TrackerAnnounceResult> { result };
            }
            else
            {
                results = _trackerAnnounceService.AnnounceTorrent(torrent, force: true);
            }
        }
        else
        {
            _logger.Warn("TrackerAnnounceService is not available; skipping announce for torrent {0}", torrent.Id);
            results = new List<TrackerAnnounceResult>();
        }

        torrent.LastActive = DateTime.UtcNow;
        _torrentService.Update(torrent);

        if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            _trackerBoostService?.ReannounceDownloadClients(torrent.InfoHash);
        }

        return results ?? new List<TrackerAnnounceResult>();
    }

    [HttpPost("{id:int}/announce")]
    public ActionResult Announce(int id)
    {
        var torrent = _torrentService.Get(id);
        if (torrent == null)
        {
            return NotFound();
        }

        var results = TriggerAnnounceInternal(torrent);

        var trackers = _trackerEntryService.GetByTorrentId(id);
        var successfulCount = results.Count(r => r.Success);
        var failedCount = results.Count(r => !r.Success);
        return Ok(new
        {
            success = results.Count == 0 || successfulCount > 0,
            torrentId = id,
            torrentName = torrent.Name,
            trackersCount = trackers.Count(t => t.Enabled),
            successfulAnnounces = successfulCount,
            failedAnnounces = failedCount,
            results,
            message = $"Announce completed for {results.Count} tracker(s): {successfulCount} succeeded, {failedCount} failed"
        });
    }

    [HttpPost("{id:int}/recheck")]
    public ActionResult<TorrentResource> Recheck(int id)
    {
        var torrent = _torrentService.Recheck(id);
        if (torrent == null)
        {
            return NotFound();
        }

        _eventLogService.Info(id, "Recheck", $"Recheck complete: progress {torrent.Progress:P0}");
        return TorrentResourceMapper.ToResource(torrent);
    }

    [HttpPut("{id:int}/queue")]
    public ActionResult MoveQueue(int id, [FromBody] QueuePositionResource resource)
    {
        var torrent = _torrentService.Get(id);
        if (torrent == null)
        {
            return NotFound();
        }

        _eventLogService.Info(id, "Queue", $"Queue position moved: {resource.Position}");
        _torrentService.MoveQueue(id, resource.Position);
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id, [FromQuery] bool deleteFiles = false)
    {
        var torrent = _torrentService.Get(id);
        _torrentService.Delete(id, deleteFiles);
        InvalidateBroadcastCache(id, torrent?.InfoHash);
        return Ok();
    }

    [HttpPost("bulk")]
    public ActionResult<BulkActionResult> BulkAction([FromBody] BulkTorrentActionResource resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Action))
        {
            return BadRequest(new { message = "Action is required." });
        }

        var result = new BulkActionResult();
        if (resource.TorrentIds == null || resource.TorrentIds.Count == 0)
        {
            return Ok(result);
        }

        var action = resource.Action.Trim().ToLowerInvariant();
        string resolvedCategoryName = null;
        Category resolvedCategory = null;
        if (action == "setcategory" && resource.CategoryId.HasValue && resource.CategoryId.Value > 0)
        {
            var cat = _categoryService?.Get(resource.CategoryId.Value);
            if (cat == null)
            {
                return BadRequest($"Category with ID {resource.CategoryId.Value} not found.");
            }

            resolvedCategoryName = cat.Name;
            resolvedCategory = cat;
        }

        foreach (var id in resource.TorrentIds)
        {
            try
            {
                ExecuteActionForTorrent(id, resource, resolvedCategoryName, resolvedCategory);
                result.SucceededIds.Add(id);
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                result.FailedIds[id] = ex.Message;
                result.FailedCount++;
                result.Errors.Add($"Torrent {id}: {ex.Message}");
                _logger.Error(ex, "Failed to execute bulk action '{0}' for torrent {1}", resource.Action, id);
            }
        }

        return Ok(result);
    }

    private void ExecuteActionForTorrent(int id, BulkTorrentActionResource resource, string resolvedCategoryName = null, Category resolvedCategory = null)
    {
        var action = resource.Action.Trim().ToLowerInvariant();
        switch (action)
        {
            case "start":
            case "resume":
            {
                var torrent = _torrentService.Get(id);
                if (torrent == null)
                {
                    throw new KeyNotFoundException($"Torrent {id} not found");
                }

                _torrentService.Start(id);
                _eventLogService?.Info(id, "Bulk", "Started torrent");
                break;
            }

            case "stop":
            case "pause":
            {
                var torrent = _torrentService.Get(id);
                if (torrent == null)
                {
                    throw new KeyNotFoundException($"Torrent {id} not found");
                }

                _torrentService.Pause(id);
                _eventLogService?.Info(id, "Bulk", "Stopped torrent");
                break;
            }

            case "delete":
            case "remove":
            {
                var torrent = _torrentService.Get(id);
                if (torrent == null)
                {
                    throw new KeyNotFoundException($"Torrent {id} not found");
                }

                _torrentService.Delete(id, resource.DeleteFiles);
                _eventLogService?.Info(id, "Bulk", $"Deleted torrent (deleteFiles={resource.DeleteFiles})");
                break;
            }

            case "recheck":
            case "forcerecheck":
            {
                var torrent = _torrentService.Recheck(id);
                if (torrent == null)
                {
                    throw new KeyNotFoundException($"Torrent {id} not found");
                }

                _eventLogService?.Info(id, "Recheck", $"Recheck complete: progress {torrent.Progress:P0}");
                break;
            }

            case "announce":
            case "forceannounce":
            {
                var torrent = _torrentService.Get(id);
                if (torrent == null)
                {
                    throw new KeyNotFoundException($"Torrent {id} not found");
                }

                TriggerAnnounceInternal(torrent);
                _eventLogService?.Info(id, "Announce", "Triggered tracker announce");
                break;
            }

            case "setcategory":
            {
                var torrent = _torrentService.Get(id);
                if (torrent == null)
                {
                    throw new KeyNotFoundException($"Torrent {id} not found");
                }

                var category = resolvedCategory;
                var categoryName = resolvedCategoryName;
                if (category == null && resource.CategoryId.HasValue && resource.CategoryId.Value > 0 && _categoryService != null)
                {
                    category = _categoryService.Get(resource.CategoryId.Value);
                    categoryName = category?.Name;
                }
                else if (category == null && !string.IsNullOrWhiteSpace(categoryName) && _categoryService != null)
                {
                    category = _categoryService.GetByName(categoryName);
                }

                torrent.Category = category?.Name ?? categoryName;

                if (category != null)
                {
                    ApplyCategoryLimits(torrent, category);
                }

                _torrentService.Update(torrent);
                _eventLogService?.Info(id, "Category", $"Category set to '{torrent.Category ?? "None"}'");
                break;
            }

            case "addtags":
            {
                var torrent = _torrentService.Get(id);
                if (torrent == null)
                {
                    throw new KeyNotFoundException($"Torrent {id} not found");
                }

                if (resource.TagIds != null && resource.TagIds.Count > 0)
                {
                    torrent.TagIds ??= new List<int>();
                    torrent.TagIds = torrent.TagIds.Union(resource.TagIds).Distinct().ToList();
                    _torrentService.Update(torrent);
                    _eventLogService?.Info(id, "Tags", $"Added tags: {string.Join(", ", resource.TagIds)}");
                }

                break;
            }

            case "removetags":
            {
                var torrent = _torrentService.Get(id);
                if (torrent == null)
                {
                    throw new KeyNotFoundException($"Torrent {id} not found");
                }

                if (resource.TagIds != null && resource.TagIds.Count > 0 && torrent.TagIds != null)
                {
                    torrent.TagIds = torrent.TagIds.Except(resource.TagIds).ToList();
                    _torrentService.Update(torrent);
                    _eventLogService?.Info(id, "Tags", $"Removed tags: {string.Join(", ", resource.TagIds)}");
                }

                break;
            }

            case "setpriority":
            {
                var torrent = _torrentService.Get(id);
                if (torrent == null)
                {
                    throw new KeyNotFoundException($"Torrent {id} not found");
                }

                if (resource.Priority.HasValue)
                {
                    torrent.Priority = resource.Priority.Value;
                    _torrentService.Update(torrent);
                    _eventLogService?.Info(id, "Priority", $"Priority set to {resource.Priority.Value}");
                }

                break;
            }

            case "setspeedlimits":
            {
                var torrent = _torrentService.Get(id);
                if (torrent == null)
                {
                    throw new KeyNotFoundException($"Torrent {id} not found");
                }

                if (resource.UploadLimit.HasValue)
                {
                    torrent.UploadLimit = resource.UploadLimit.Value;
                }

                if (resource.DownloadLimit.HasValue)
                {
                    torrent.DownloadLimit = resource.DownloadLimit.Value;
                }

                _torrentService.Update(torrent);
                _eventLogService?.Info(id, "Limits", $"Limits updated: Upload={torrent.UploadLimit} KB/s, Download={torrent.DownloadLimit} KB/s");
                break;
            }

            default:
                throw new ArgumentException($"Unknown action: {resource.Action}");
        }
    }

    private void LogUpdateTransitions(Torrent existing, TorrentResource resource)
    {
        if (resource.ForceCompleted && !existing.ForceCompleted)
        {
            _eventLogService.Info(existing.Id, "Edit", "Marked as force-completed (100%)");
        }

        if (resource.ForceStart != existing.ForceStart)
        {
            _eventLogService.Info(existing.Id, "Edit", resource.ForceStart ? "Force start enabled" : "Force start disabled");
        }

        if (resource.SuperSeeding != existing.SuperSeeding)
        {
            _eventLogService.Info(existing.Id, "Edit", resource.SuperSeeding ? "Super seeding enabled" : "Super seeding disabled");
        }

        if (!string.IsNullOrEmpty(resource.Status) &&
            !string.Equals(resource.Status, existing.Status.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            _eventLogService.Info(existing.Id, "Status", $"Status changed: {existing.Status} -> {resource.Status}");
        }
    }

    [HttpPost("{torrentId:int}/trackers/{trackerId:int}/announce")]
    public ActionResult AnnounceTracker(int torrentId, int trackerId)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return NotFound();
        }

        var trackers = _trackerEntryService.GetByTorrentId(torrentId);
        var target = trackers.FirstOrDefault(t => t.Id == trackerId);
        if (target == null)
        {
            return NotFound();
        }

        var results = TriggerAnnounceInternal(torrent, target);
        var res = results.FirstOrDefault();
        return Ok(new
        {
            success = res?.Success ?? false,
            message = res != null
                ? (res.Success ? $"Announced to {target.Url} successfully ({res.Seeders} seeders, {res.Leechers} leechers, {res.PeersDiscovered} peers)" : $"Announce failed for {target.Url}: {res.FailureReason}")
                : $"Announce skipped for {target.Url}",
            result = res
        });
    }

    [HttpGet("{id:int}/logs")]
    public ActionResult<List<TorrentEventLogResource>> GetLogs(
        int id,
        [FromQuery] string level = null,
        [FromQuery] int count = 100)
    {
        var torrent = _torrentService.Get(id);
        if (torrent == null)
        {
            return NotFound();
        }

        if (count < 1)
        {
            count = 1;
        }

        if (count > 1000)
        {
            count = 1000;
        }

        var minimumRank = ParseLevelRank(level) ?? LevelRank.Debug;
        var entries = _eventLogService.GetByTorrentId(id, count);

        var resources = entries
            .Where(e => ParseLevelRank(e.Level) >= minimumRank)
            .Select(ToResource)
            .ToList();

        return Ok(resources);
    }

    private static int? ParseLevelRank(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return null;
        }

        return level.Trim().ToLowerInvariant() switch
        {
            "trace" => LevelRank.Trace,
            "debug" => LevelRank.Debug,
            "info" => LevelRank.Info,
            "warn" or "warning" => LevelRank.Warn,
            "error" => LevelRank.Error,
            "fatal" => LevelRank.Fatal,
            _ => null
        };
    }

    private static TorrentEventLogResource ToResource(TorrentEventLog log)
    {
        return new TorrentEventLogResource
        {
            Id = log.Id,
            TorrentId = log.TorrentId,
            TimeStamp = log.TimeStamp,
            Level = log.Level,
            Source = log.Source,
            Message = log.Message,
        };
    }

    private static void ApplyCategoryLimits(Torrent torrent, Category category)
    {
        if (torrent == null || category == null)
        {
            return;
        }

        if (category.DefaultDownloadLimit > 0 && torrent.DownloadLimit <= 0)
        {
            torrent.DownloadLimit = category.DefaultDownloadLimit;
        }

        if (category.DefaultUploadLimit > 0 && torrent.UploadLimit <= 0)
        {
            torrent.UploadLimit = category.DefaultUploadLimit;
        }
    }
}

public record TorrentUploadFailure(string FileName, string Reason);

public record TorrentUploadResult(List<TorrentResource> Added, List<TorrentUploadFailure> Failed);

public class AddTorrentTrackerResource
{
    public string Url { get; set; } = string.Empty;
    public int Tier { get; set; } = 1;
}

public class UpdateTorrentTrackerResource
{
    public int? Tier { get; set; }
    public bool? Enabled { get; set; }
}
