using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
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
        return ToResource(model);
    }

    [HttpGet]
    public List<TorrentResource> GetAll()
    {
        return _torrentService.GetAll().Select(ToResource).ToList();
    }

    [HttpGet("{id:int}")]
    public ActionResult<TorrentResource> GetById(int id)
    {
        var torrent = _torrentService.Get(id);
        if (torrent == null)
        {
            return NotFound();
        }

        return ToResource(torrent);
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
        return files.Select(ToFileResource).ToList();
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
        return trackers.Select(ToTrackerResource).ToList();
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
        return connections.Select(c => ToPeerResource(c, id++)).ToList();
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

        var torrent = ToModel(resource);
        torrent.DateAdded = DateTime.UtcNow;
        var added = _torrentService.Add(torrent);
        return Created($"/api/v1/torrent/{added.Id}", ToResource(added));
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

        return Created($"/api/v1/torrent/{added.Id}", ToResource(added));
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

        var torrent = ToModel(resource);
        torrent.Id = id;
        var updated = _torrentService.Update(torrent);
        return ToResource(updated);
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

        return ToResource(torrent);
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

    private static TorrentResource ToResource(Torrent model)
    {
        return new TorrentResource
        {
            Id = model.Id,
            Name = model.Name,
            InfoHash = model.InfoHash,
            TotalSize = model.TotalSize,
            PieceCount = model.PieceCount,
            PieceLength = model.PieceLength,
            Comment = model.Comment,
            CreatedBy = model.CreatedBy,
            CreationDate = model.CreationDate,
            IsPrivate = model.IsPrivate,
            Status = model.Status.ToString(),
            Uploaded = model.Uploaded,
            Downloaded = model.Downloaded,
            Ratio = model.Ratio,
            Seeders = model.Seeders,
            Leechers = model.Leechers,
            TrackerUrl = model.TrackerUrl,
            DateAdded = model.DateAdded,
            LastActive = model.LastActive,
            Priority = model.Priority,
            UploadLimit = model.UploadLimit,
            DownloadLimit = model.DownloadLimit,
            SuperSeeding = model.SuperSeeding,
            ForceStart = model.ForceStart,
            Label = model.Label,
            Progress = model.Progress,
            SequentialDownload = model.SequentialDownload,
            AnnounceInterval = model.AnnounceInterval,
            NextUpdate = model.NextUpdate,
            SessionUploaded = model.SessionUploaded,
            SessionDownloaded = model.SessionDownloaded,
            SmallTorrentLimit = model.SmallTorrentLimit,
            Threshold = model.Threshold,
            UploadSpeed = model.UploadSpeed,
            DownloadSpeed = model.DownloadSpeed,
            Active = model.Active,
            Availability = model.Availability,
            Eta = model.Eta,
            SortOrder = model.SortOrder,
            ForceCompleted = model.ForceCompleted
        };
    }

    private static Torrent ToModel(TorrentResource resource)
    {
        return new Torrent
        {
            Id = resource.Id,
            Name = resource.Name,
            InfoHash = resource.InfoHash,
            TotalSize = resource.TotalSize,
            PieceCount = resource.PieceCount,
            PieceLength = resource.PieceLength,
            Comment = resource.Comment,
            CreatedBy = resource.CreatedBy,
            CreationDate = resource.CreationDate,
            IsPrivate = resource.IsPrivate,
            Status = Enum.TryParse<TorrentStatus>(resource.Status, true, out var status) ? status : TorrentStatus.Stopped,
            Uploaded = resource.Uploaded,
            Downloaded = resource.Downloaded,
            Ratio = resource.Ratio,
            Seeders = resource.Seeders,
            Leechers = resource.Leechers,
            TrackerUrl = resource.TrackerUrl,
            DateAdded = resource.DateAdded,
            LastActive = resource.LastActive,
            Priority = resource.Priority,
            UploadLimit = resource.UploadLimit,
            DownloadLimit = resource.DownloadLimit,
            SuperSeeding = resource.SuperSeeding,
            ForceStart = resource.ForceStart,
            Label = resource.Label,
            Progress = resource.Progress,
            SequentialDownload = resource.SequentialDownload,
            AnnounceInterval = resource.AnnounceInterval,
            NextUpdate = resource.NextUpdate,
            SessionUploaded = resource.SessionUploaded,
            SessionDownloaded = resource.SessionDownloaded,
            SmallTorrentLimit = resource.SmallTorrentLimit,
            Threshold = resource.Threshold,
            UploadSpeed = resource.UploadSpeed,
            DownloadSpeed = resource.DownloadSpeed,
            Active = resource.Active,
            Availability = resource.Availability,
            Eta = resource.Eta,
            SortOrder = resource.SortOrder,
            ForceCompleted = resource.ForceCompleted
        };
    }

    private static TorrentFileResource ToFileResource(TorrentFile model)
    {
        return new TorrentFileResource
        {
            Id = model.Id,
            TorrentId = model.TorrentId,
            Path = model.Path,
            Size = model.Size,
            PieceOffset = model.PieceOffset,
            PieceCount = model.PieceCount
        };
    }

    private static TrackerEntryResource ToTrackerResource(TrackerEntry model)
    {
        return new TrackerEntryResource
        {
            Id = model.Id,
            TorrentId = model.TorrentId,
            Url = model.Url,
            Tier = model.Tier,
            Status = model.Status.ToString(),
            Enabled = model.Enabled,
            Seeders = model.Seeders,
            Leechers = model.Leechers,
            Downloaded = model.Downloaded,
            TotalAnnounces = model.TotalAnnounces,
            SuccessfulAnnounces = model.SuccessfulAnnounces,
            ConsecutiveFailures = model.ConsecutiveFailures,
            LastResponseTime = model.LastResponseTime,
            AverageResponseTime = model.AverageResponseTime,
            AnnounceInterval = model.AnnounceInterval,
            MinAnnounceInterval = model.MinAnnounceInterval,
            LastAnnounce = model.LastAnnounce,
            LastScrape = model.LastScrape,
            NextAnnounce = model.NextAnnounce,
            ErrorMessage = model.ErrorMessage,
            LastErrorTime = model.LastErrorTime,
            WarningMessage = model.WarningMessage
        };
    }

    private static PeerResource ToPeerResource(PeerConnection connection, int id)
    {
        var flags = string.Empty;
        if (connection.IsEncrypted)
        {
            flags += "E";
        }

        if (connection.PeerInterested)
        {
            flags += "I";
        }

        if (!connection.AmChoking)
        {
            flags += "U";
        }

        return new PeerResource
        {
            Id = id,
            Ip = connection.RemoteIp,
            Port = connection.RemotePort,
            Client = connection.PeerId ?? string.Empty,
            UploadSpeed = 0,
            DownloadSpeed = 0,
            Uploaded = 0,
            Downloaded = 0,
            Progress = 0,
            Flags = flags
        };
    }

    private ActionResult<TorrentResource> CreateFromMagnet(TorrentResource resource)
    {
        var magnetUri = resource.MagnetLink;
        var queryStart = magnetUri.IndexOf('?');
        if (queryStart < 0)
        {
            return BadRequest("Invalid magnet link: no parameters found");
        }

        var queryString = magnetUri[(queryStart + 1)..];
        var parameters = HttpUtility.ParseQueryString(queryString);

        var xt = parameters["xt"];
        if (string.IsNullOrEmpty(xt) || !xt.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Invalid magnet link: missing urn:btih: parameter");
        }

        var infoHash = xt["urn:btih:".Length..];

        if (infoHash.Length == 32)
        {
            var bytes = Base32Decode(infoHash);
            if (bytes == null || bytes.Length != 20)
            {
                return BadRequest("Invalid magnet link: could not decode base32 info hash");
            }

            infoHash = Convert.ToHexString(bytes).ToLowerInvariant();
        }
        else
        {
            infoHash = infoHash.ToLowerInvariant();
        }

        if (infoHash.Length != 40)
        {
            return BadRequest("Invalid magnet link: info hash must be 40 hex characters");
        }

        if (_torrentService.ExistsByInfoHash(infoHash))
        {
            return Conflict(new { message = "Torrent with this info hash already exists" });
        }

        var displayName = parameters["dn"];
        if (!string.IsNullOrEmpty(displayName))
        {
            displayName = HttpUtility.UrlDecode(displayName);
        }
        else
        {
            displayName = infoHash;
        }

        var trackerUrls = parameters.GetValues("tr");
        var primaryTracker = trackerUrls?.FirstOrDefault();

        var torrent = new Torrent
        {
            Name = displayName,
            InfoHash = infoHash,
            TrackerUrl = primaryTracker != null ? HttpUtility.UrlDecode(primaryTracker) : null,
            Status = TorrentStatus.Queued,
            DateAdded = DateTime.UtcNow
        };

        var added = _torrentService.Add(torrent);

        if (trackerUrls != null)
        {
            var tier = 0;
            foreach (var url in trackerUrls)
            {
                var decodedUrl = HttpUtility.UrlDecode(url);
                _trackerEntryService.Add(new TrackerEntry
                {
                    TorrentId = added.Id,
                    Url = decodedUrl,
                    Tier = tier++,
                    Enabled = true
                });
            }
        }

        return Created($"/api/v1/torrent/{added.Id}", ToResource(added));
    }

    private static byte[] Base32Decode(string input)
    {
        input = input.ToUpperInvariant();
        var output = new byte[input.Length * 5 / 8];
        var bitIndex = 0;
        var inputIndex = 0;
        var outputBits = 0;
        var outputIndex = 0;

        while (inputIndex < input.Length)
        {
            var byteIndex = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(input[inputIndex]);
            if (byteIndex < 0)
            {
                return null;
            }

            var bits = Math.Min(5, 8 - bitIndex);
            if (bitIndex == 0)
            {
                outputBits = byteIndex << 3;
            }
            else if (bits < 5)
            {
                outputBits |= byteIndex >> (5 - bits);
                output[outputIndex++] = (byte)outputBits;
                outputBits = (byteIndex << (3 + bits)) & 0xFF;
            }
            else
            {
                outputBits |= byteIndex << (8 - bitIndex - 5);
            }

            bitIndex += 5;
            if (bitIndex >= 8)
            {
                bitIndex -= 8;
                if (bitIndex == 0)
                {
                    output[outputIndex++] = (byte)outputBits;
                    outputBits = 0;
                }
            }

            inputIndex++;
        }

        return output;
    }
}
