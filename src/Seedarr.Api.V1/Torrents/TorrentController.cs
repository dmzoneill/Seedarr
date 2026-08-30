using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.SignalR;
using Seedarr.Http;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Torrents;

[V1ApiController("torrent")]
public class TorrentController : RestControllerWithSignalR<TorrentResource, Torrent>
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly IConnectionManager _connectionManager;
    private readonly ITorrentEventLogService _eventLogService;
    private readonly IConfigService _configService;
    private readonly IDownloadHistoryRepository _downloadHistoryRepository;

    public TorrentController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITrackerEntryService trackerEntryService,
        ITorrentFileParser torrentFileParser,
        IConnectionManager connectionManager,
        ITorrentEventLogService eventLogService,
        IConfigService configService,
        IBroadcastSignalRMessage signalRBroadcaster,
        TorrentResourceValidator torrentResourceValidator,
        IDownloadHistoryRepository downloadHistoryRepository = null)
        : base(signalRBroadcaster)
    {
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _trackerEntryService = trackerEntryService;
        _torrentFileParser = torrentFileParser;
        _connectionManager = connectionManager;
        _eventLogService = eventLogService;
        _configService = configService;
        _downloadHistoryRepository = downloadHistoryRepository;

        SharedValidator = torrentResourceValidator;
    }

    private TorrentResource MapTorrentToResource(Torrent torrent)
    {
        var trackers = _trackerEntryService.GetByTorrentId(torrent.Id);
        return MapTorrentToResource(torrent, trackers);
    }

    private TorrentResource MapTorrentToResource(Torrent torrent, List<TrackerEntry> trackers)
    {
        var resource = TorrentResourceMapper.ToResource(torrent);
        var torrentTrackers = trackers.Where(t => t.TorrentId == torrent.Id).ToList();

        if (torrentTrackers.Any())
        {
            var mainTracker = torrentTrackers.OrderBy(tr => tr.Tier).First();
            resource.AnnounceInterval = mainTracker.AnnounceInterval;

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

        if (_downloadHistoryRepository != null && !string.IsNullOrEmpty(torrent.InfoHash))
        {
            try
            {
                var history = _downloadHistoryRepository.FindByInfoHash(torrent.InfoHash);
                if (history != null)
                {
                    resource.Source = history.Source;
                    if (!string.IsNullOrEmpty(history.DataJson))
                    {
                        var metadata = JsonSerializer.Deserialize<MediaMetadata>(
                            history.DataJson,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (metadata != null)
                        {
                            resource.PosterUrl = metadata.PosterUrl;
                            resource.FanartUrl = metadata.FanartUrl;
                            resource.BannerUrl = metadata.BannerUrl;
                            resource.MediaTitle = metadata.Title;
                            resource.Year = metadata.Year;
                            resource.Overview = metadata.Overview;
                            resource.Rating = metadata.Rating;
                            resource.Genres = metadata.Genres ?? new();
                        }
                    }
                }
            }
            catch
            {
                // Fallback gracefully
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
        return torrents.Select(t => MapTorrentToResource(t, trackers)).ToList();
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
            return CreateFromMagnet(resource);
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
                added.Add(AddTorrentFromFile(file));
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

        var torrent = TorrentResourceMapper.ToModel(resource);
        torrent.Id = id;

        // Preserve internal statistics fields — not settable via API
        torrent.Uploaded = existing.Uploaded;
        torrent.Downloaded = existing.Downloaded;
        torrent.Ratio = existing.Ratio;
        torrent.Seeders = existing.Seeders;
        torrent.Leechers = existing.Leechers;
        torrent.SessionUploaded = existing.SessionUploaded;
        torrent.SessionDownloaded = existing.SessionDownloaded;
        torrent.UploadSpeed = existing.UploadSpeed;
        torrent.DownloadSpeed = existing.DownloadSpeed;

        var updated = _torrentService.Update(torrent);
        return MapTorrentToResource(updated);
    }

    [HttpPost("{id:int}/announce")]
    public ActionResult Announce(int id)
    {
        var torrent = _torrentService.Get(id);
        if (torrent == null)
        {
            return NotFound();
        }

        var trackers = _trackerEntryService.GetByTorrentId(id);
        foreach (var tracker in trackers)
        {
            if (!tracker.Enabled)
            {
                continue;
            }

            tracker.NextAnnounce = DateTime.UtcNow;
            tracker.LastAnnounce = DateTime.UtcNow;
            tracker.TotalAnnounces++;
            tracker.SuccessfulAnnounces++;
            tracker.Status = TrackerStatus.Working;
            tracker.ConsecutiveFailures = 0;
            _trackerEntryService.Update(tracker);
        }

        torrent.LastActive = DateTime.UtcNow;
        _torrentService.Update(torrent);
        _eventLogService.Info(id, "Tracker", $"Manual announce to {trackers.Count(t => t.Enabled)} enabled tracker(s)");

        return Ok();
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

    private TorrentResource AddTorrentFromFile(IFormFile file)
    {
        ParsedTorrent parsed;
        using (var stream = file.OpenReadStream())
        {
            parsed = _torrentFileParser.Parse(stream);
        }

        if (_torrentService.ExistsByInfoHash(parsed.InfoHash))
        {
            throw new InvalidOperationException("Torrent with this info hash already exists");
        }

        var torrent = new Torrent
        {
            Name = parsed.Name,
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
            DateAdded = DateTime.UtcNow
        };

        var addedTorrent = _torrentService.Add(torrent);

        _eventLogService.Info(addedTorrent.Id, "Add", $"Torrent '{parsed.Name}' added from file '{file.FileName}' ({file.Length} bytes)");

        if (parsed.Files != null)
        {
            foreach (var f in parsed.Files)
            {
                _torrentFileService.Add(new TorrentFile { TorrentId = addedTorrent.Id, Path = f.Path, Size = f.Size });
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

        return TorrentResourceMapper.ToResource(addedTorrent);
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

    [HttpGet("{id:int}/logs")]
    public ActionResult<List<TorrentEventLogResource>> GetLogs(
        int id,
        [FromQuery] string level = null,
        [FromQuery] int count = 200)
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

        var minimumRank = ParseLevelRank(level) ?? ParseLevelRank(_configService.FileLogLevel) ?? LevelRank.Info;
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

    private ActionResult<TorrentResource> CreateFromMagnet(TorrentResource resource)
    {
        ParsedMagnetLink parsed;
        try
        {
            parsed = MagnetLinkParser.Parse(resource.MagnetLink);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        if (_torrentService.ExistsByInfoHash(parsed.InfoHash))
        {
            return Conflict(new { message = "Torrent with this info hash already exists" });
        }

        var torrent = new Torrent
        {
            Name = parsed.Name,
            InfoHash = parsed.InfoHash,
            TrackerUrl = parsed.Trackers.Length > 0 ? parsed.Trackers[0] : null,
            Status = TorrentStatus.Queued,
            DateAdded = DateTime.UtcNow
        };

        var added = _torrentService.Add(torrent);
        _eventLogService.Info(added.Id, "Add", $"Torrent '{parsed.Name}' added from magnet link");

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

        return Created($"/api/v1/torrent/{added.Id}", TorrentResourceMapper.ToResource(added));
    }
}

public record TorrentUploadFailure(string FileName, string Reason);

public record TorrentUploadResult(List<TorrentResource> Added, List<TorrentUploadFailure> Failed);
