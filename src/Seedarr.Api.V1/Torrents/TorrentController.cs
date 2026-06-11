using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Torrents;
using NzbDrone.SignalR;
using Seedarr.Http;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Torrents;

[V1ApiController("torrent")]
public class TorrentController : RestControllerWithSignalR<TorrentResource, Torrent>
{
    private readonly ITorrentService _torrentService;

    public TorrentController(ITorrentService torrentService, IBroadcastSignalRMessage signalRBroadcaster)
        : base(signalRBroadcaster)
    {
        _torrentService = torrentService;
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

    [HttpPost]
    public ActionResult<TorrentResource> Create([FromBody] TorrentResource resource)
    {
        var torrent = ToModel(resource);
        torrent.DateAdded = DateTime.UtcNow;
        var added = _torrentService.Add(torrent);
        return Created($"/api/v1/torrent/{added.Id}", ToResource(added));
    }

    [HttpPut("{id:int}")]
    public ActionResult<TorrentResource> Update(int id, [FromBody] TorrentResource resource)
    {
        var torrent = ToModel(resource);
        torrent.Id = id;
        var updated = _torrentService.Update(torrent);
        return ToResource(updated);
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _torrentService.Delete(id);
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
            LastActive = model.LastActive
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
            LastActive = resource.LastActive
        };
    }
}
