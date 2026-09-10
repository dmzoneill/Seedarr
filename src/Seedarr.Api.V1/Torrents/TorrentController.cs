using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Configuration;
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
        IMediaEnrichmentService mediaEnrichmentService = null)
        : base(signalRBroadcaster)
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

    protected override TorrentResource GetResourceById(Torrent model)
    {
        return MapTorrentToResource(model);
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
    public ActionResult<List<TorrentFileResource>> GetFiles(int torrentId)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return NotFound();
        }

        var files = _torrentFileService.GetByTorrentId(torrentId);
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

        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return NotFound();
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

        var updatedEntry = _trackerEntryService.GetByTorrentId(torrentId).FirstOrDefault(t => t.Id == entry.Id) ?? entry;

        return Ok(TorrentResourceMapper.ToTrackerResource(updatedEntry));
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
        return connections.Select(c => TorrentResourceMapper.ToPeerResource(c, id++)).ToList();
    }

    [HttpPost]
    public ActionResult<TorrentResource> Create([FromBody] TorrentResource resource)
    {
        if (!string.IsNullOrEmpty(resource.MagnetLink))
        {
            try
            {
                var imported = _torrentImportService.ImportFromMagnet(resource.MagnetLink);
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
        }

        var validationResult = SharedValidator.Validate(resource);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var torrent = TorrentResourceMapper.ToModel(resource);
        torrent.DateAdded = DateTime.UtcNow;
        var added = _torrentService.Add(torrent);
        return Created($"/api/v1/torrent/{added.Id}", TorrentResourceMapper.ToResource(added));
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public IActionResult Upload([FromForm(Name = "file")] List<IFormFile> files)
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
        _torrentService.Delete(id, deleteFiles);
        return Ok();
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
            Message = log.Message
        };
    }
}

public record TorrentUploadFailure(string FileName, string Reason);

public record TorrentUploadResult(List<TorrentResource> Added, List<TorrentUploadFailure> Failed);

public class AddTorrentTrackerResource
{
    public string Url { get; set; } = string.Empty;
    public int Tier { get; set; } = 1;
}
