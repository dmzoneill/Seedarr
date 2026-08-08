using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Torrents;
using Seedarr.Http;

namespace Seedarr.Api.V1.Torrents;

[V1ApiController("downloadhistory")]
public class DownloadHistoryController : Controller
{
    private readonly IDownloadHistoryService _historyService;
    private readonly IArrMetadataEnricherService _metadataEnricherService;

    public DownloadHistoryController(
        IDownloadHistoryService historyService,
        IArrMetadataEnricherService metadataEnricherService = null)
    {
        _historyService = historyService;
        _metadataEnricherService = metadataEnricherService;
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

    [HttpPost("{id:int}/enrich")]
    public ActionResult<DownloadHistoryResource> Enrich(int id)
    {
        if (_metadataEnricherService == null)
        {
            return BadRequest(new { message = "Metadata enricher service not available" });
        }

        var metadata = _metadataEnricherService.EnrichHistoryEntry(id);
        var record = _historyService.Get(id);
        if (record == null)
        {
            return NotFound();
        }

        var resource = ToResource(record);
        resource.Metadata = metadata;
        return Ok(resource);
    }

    [HttpPost("enrich-all")]
    public ActionResult EnrichAll()
    {
        if (_metadataEnricherService != null)
        {
            _metadataEnricherService.EnrichAll();
        }

        return Ok(new { message = "Enrichment started" });
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
        MediaMetadata metadata = null;
        if (!string.IsNullOrEmpty(model.DataJson))
        {
            try
            {
                metadata = JsonSerializer.Deserialize<MediaMetadata>(
                    model.DataJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                metadata = null;
            }
        }

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
            DataJson = model.DataJson,
            Metadata = metadata
        };
    }
}
