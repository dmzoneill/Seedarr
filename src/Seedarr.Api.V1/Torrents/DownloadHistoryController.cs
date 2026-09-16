using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Torrents;
using Seedarr.Http;

namespace Seedarr.Api.V1.Torrents;

[V1ApiController("downloadhistory")]
public class DownloadHistoryController : Controller
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
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
        [FromQuery] int limit = 500,
        [FromQuery] int offset = 0)
    {
        var records = _historyService.GetAll(query, status, limit, offset);
        return Ok(records.Select(ToResource).ToList());
    }

    [HttpGet("export")]
    public ActionResult Export(
        [FromQuery] string query = null,
        [FromQuery] string status = null,
        [FromQuery] string format = "json")
    {
        var records = _historyService.GetAll(query, status, limit: -1, offset: 0);
        var resources = records.Select(ToResource).ToList();

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = GenerateCsv(resources);
            var bytes = Encoding.UTF8.GetBytes(csv);
            return File(bytes, "text/csv; charset=utf-8", $"seedarr-history-{DateTime.UtcNow:yyyy-MM-dd}.csv");
        }

        return Ok(resources);
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
            Task.Run(() =>
            {
                try
                {
                    _metadataEnricherService.EnrichAll();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Background history enrichment failed");
                }
            });
        }

        return Ok(new { message = "Enrichment started" });
    }

    [HttpPost("reconcile")]
    public ActionResult Reconcile()
    {
        var count = 0;
        if (_metadataEnricherService != null)
        {
            count = _metadataEnricherService.ReconcileAndEnrichAll();
        }
        else
        {
            count = _historyService.ReconcileAllTorrents();
        }

        return Ok(new { success = true, processedCount = count });
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

        var savePath = model.SavePath;
        var category = model.Category;
        var downloadClientId = model.DownloadClientId;
        var sourcePath = model.SourcePath;

        if (!string.IsNullOrEmpty(model.DataJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(model.DataJson);
                var root = doc.RootElement;
                if (string.IsNullOrEmpty(savePath) && root.TryGetProperty("savePath", out var spProp))
                {
                    savePath = spProp.GetString();
                }

                if (string.IsNullOrEmpty(category) && root.TryGetProperty("category", out var catProp))
                {
                    category = catProp.GetString();
                }

                if (!downloadClientId.HasValue && root.TryGetProperty("downloadClientId", out var dcProp) && dcProp.TryGetInt32(out var dcId))
                {
                    downloadClientId = dcId;
                }

                if (string.IsNullOrEmpty(sourcePath) && root.TryGetProperty("sourcePath", out var srcProp))
                {
                    sourcePath = srcProp.GetString();
                }
            }
            catch
            {
                // Ignore parse errors
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
            Metadata = metadata,
            SavePath = savePath,
            Category = category,
            DownloadClientId = downloadClientId,
            SourcePath = sourcePath,
            IsPrivate = model.IsPrivate
        };
    }

    public static string EscapeCsvField(object value)
    {
        if (value == null)
        {
            return "\"\"";
        }

        var str = value.ToString() ?? string.Empty;
        var formulaChars = new[] { '=', '+', '-', '@', '\t', '\r' };
        if (formulaChars.Any(c => str.StartsWith(c)))
        {
            str = "'" + str;
        }

        return $"\"{str.Replace("\"", "\"\"")}\"";
    }

    public static string GenerateCsv(IEnumerable<DownloadHistoryResource> records)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ID,Title,InfoHash,Source,Status,TotalSize,Uploaded,Ratio,SeedingTimeSeconds,DateAdded,DateCompleted");
        foreach (var h in records)
        {
            sb.AppendLine(string.Join(
                ",",
                h.Id,
                EscapeCsvField(h.Title),
                EscapeCsvField(h.InfoHash),
                EscapeCsvField(h.Source),
                EscapeCsvField(h.Status),
                h.TotalSize,
                h.Uploaded,
                h.Ratio,
                h.SeedingTime,
                EscapeCsvField(h.DateAdded.ToString("o")),
                EscapeCsvField(h.DateCompleted?.ToString("o") ?? "")));
        }

        return sb.ToString();
    }
}
