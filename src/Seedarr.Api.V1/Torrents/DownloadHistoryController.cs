using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Torrents;
using Seedarr.Http;

namespace Seedarr.Api.V1.Torrents;

[V1ApiController("downloadhistory")]
public class DownloadHistoryController : Controller
{
    private readonly IDownloadHistoryService _historyService;

    public DownloadHistoryController(IDownloadHistoryService historyService)
    {
        _historyService = historyService;
    }

    [HttpGet]
    public ActionResult<List<DownloadHistoryResource>> GetAll(
        [FromQuery] string query = null,
        [FromQuery] string status = null,
        [FromQuery] int limit = 500)
    {
        var records = _historyService.GetAll(query, status, limit);
        return Ok(records.Select(ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<DownloadHistoryResource> Get(int id)
    {
        var record = _historyService.Get(id);
        if (record == null)
        {
            return NotFound();
        }

        return Ok(ToResource(record));
    }

    [HttpPost("{id:int}/readd")]
    public ActionResult<TorrentResource> ReAdd(int id)
    {
        try
        {
            var added = _historyService.ReAdd(id);
            return Ok(TorrentResourceMapper.ToResource(added));
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _historyService.Delete(id);
        return Ok();
    }

    [HttpDelete]
    public ActionResult ClearAll()
    {
        _historyService.ClearAll();
        return Ok();
    }

    private static DownloadHistoryResource ToResource(DownloadHistory model)
    {
        return new DownloadHistoryResource
        {
            Id = model.Id,
            TorrentId = model.TorrentId,
            Title = model.Title,
            InfoHash = model.InfoHash,
            TotalSize = model.TotalSize,
            DateAdded = model.DateAdded,
            DateCompleted = model.DateCompleted,
            DateRemoved = model.DateRemoved,
            Uploaded = model.Uploaded,
            Downloaded = model.Downloaded,
            Ratio = model.Ratio,
            SeedingTime = model.SeedingTime,
            PrimaryTracker = model.PrimaryTracker,
            IndexerName = model.IndexerName,
            Source = model.Source,
            MagnetUrl = model.MagnetUrl,
            DownloadUrl = model.DownloadUrl,
            Status = model.Status,
            RemovalReason = model.RemovalReason,
            DataJson = model.DataJson
        };
    }
}
