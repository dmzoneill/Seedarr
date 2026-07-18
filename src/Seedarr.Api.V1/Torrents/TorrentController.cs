using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

    public TorrentController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITrackerEntryService trackerEntryService,
        ITorrentFileParser torrentFileParser,
        IConnectionManager connectionManager,
        IBroadcastSignalRMessage signalRBroadcaster,
        TorrentResourceValidator torrentResourceValidator)
        : base(signalRBroadcaster)
    {
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _trackerEntryService = trackerEntryService;
        _torrentFileParser = torrentFileParser;
        _connectionManager = connectionManager;

        SharedValidator = torrentResourceValidator;
    }

    protected override TorrentResource GetResourceById(Torrent model)
    {
        return TorrentResourceMapper.ToResource(model);
    }

    [HttpGet]
    public List<TorrentResource> GetAll()
    {
        return _torrentService.GetAll().Select(TorrentResourceMapper.ToResource).ToList();
    }

    [HttpGet("{id:int}")]
    public ActionResult<TorrentResource> GetById(int id)
    {
        var torrent = _torrentService.Get(id);
        if (torrent == null)
        {
            return NotFound();
        }

        return TorrentResourceMapper.ToResource(torrent);
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
    public ActionResult<TorrentResource> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No torrent file provided");
        }

        ParsedTorrent parsed;
        using (var stream = file.OpenReadStream())
        {
            parsed = _torrentFileParser.Parse(stream);
        }

        if (_torrentService.ExistsByInfoHash(parsed.InfoHash))
        {
            return Conflict(new { message = "Torrent with this info hash already exists" });
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

        var added = _torrentService.Add(torrent);

        if (parsed.Files != null)
        {
            foreach (var f in parsed.Files)
            {
                _torrentFileService.Add(new TorrentFile { TorrentId = added.Id, Path = f.Path, Size = f.Size });
            }
        }

        if (parsed.AnnounceList != null)
        {
            var tier = 0;
            foreach (var tierUrls in parsed.AnnounceList)
            {
                foreach (var url in tierUrls)
                {
                    _trackerEntryService.Add(new TrackerEntry { TorrentId = added.Id, Url = url, Tier = tier, Enabled = true });
                }

                tier++;
            }
        }
        else if (!string.IsNullOrEmpty(parsed.AnnounceUrl))
        {
            _trackerEntryService.Add(new TrackerEntry { TorrentId = added.Id, Url = parsed.AnnounceUrl, Tier = 0, Enabled = true });
        }

        return Created($"/api/v1/torrent/{added.Id}", TorrentResourceMapper.ToResource(added));
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
        return TorrentResourceMapper.ToResource(updated);
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

        _torrentService.MoveQueue(id, resource.Position);
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id, [FromQuery] bool deleteFiles = false)
    {
        _torrentService.Delete(id, deleteFiles);
        return Ok();
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
