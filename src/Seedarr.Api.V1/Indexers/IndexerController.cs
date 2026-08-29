using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Newznab;
using NzbDrone.Core.Indexers.Prowlarr;
using NzbDrone.Core.Indexers.Torznab;
using NzbDrone.Core.Torrents;
using Seedarr.Api.V1.Torrents;
using Seedarr.Http;

namespace Seedarr.Api.V1.Indexers;

[V1ApiController("indexers")]
public class IndexerController : Controller
{
    private static readonly HttpClient HttpClient = new();
    private readonly IIndexerFactory _indexerFactory;
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly IDownloadHistoryService _downloadHistoryService;

    public IndexerController(
        IIndexerFactory indexerFactory,
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITrackerEntryService trackerEntryService,
        ITorrentFileParser torrentFileParser,
        IDownloadHistoryService downloadHistoryService)
    {
        _indexerFactory = indexerFactory;
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _trackerEntryService = trackerEntryService;
        _torrentFileParser = torrentFileParser;
        _downloadHistoryService = downloadHistoryService;
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
        if (string.IsNullOrWhiteSpace(definition.Implementation))
        {
            definition.Implementation = $"{definition.IndexerType}Indexer";
        }

        if (string.IsNullOrWhiteSpace(definition.ConfigContract))
        {
            definition.ConfigContract = "IndexerDefinition";
        }

        try
        {
            CreateIndexer(definition);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var created = _indexerFactory.Create(definition);
        return Ok(MaskApiKey(created));
    }

    [HttpPut("{id}")]
    public ActionResult Update(int id, [FromBody] IndexerDefinition definition)
    {
        definition.Id = id;

        if (string.IsNullOrWhiteSpace(definition.Implementation))
        {
            definition.Implementation = $"{definition.IndexerType}Indexer";
        }

        if (string.IsNullOrWhiteSpace(definition.ConfigContract))
        {
            definition.ConfigContract = "IndexerDefinition";
        }

        // If the masked API key was sent back, preserve the existing value
        if (definition.ApiKey != null && definition.ApiKey.Contains('*'))
        {
            var existing = _indexerFactory.Get(id);
            if (existing == null)
            {
                return NotFound();
            }

            definition.ApiKey = existing.ApiKey;
        }

        _indexerFactory.Update(definition);
        return Ok(MaskApiKey(definition));
    }

    [HttpDelete("{id}")]
    public ActionResult Delete(int id)
    {
        _indexerFactory.Delete(id);
        return Ok();
    }

    [HttpPost("test")]
    public ActionResult<IndexerTestResult> TestDirect([FromBody] IndexerDefinition definition)
    {
        IIndexer indexer;
        try
        {
            indexer = CreateIndexer(definition);
        }
        catch (ArgumentException ex)
        {
            return Ok(new IndexerTestResult { Success = false, Message = ex.Message });
        }

        var result = indexer.TestConnectionDetailed(definition);
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

        IIndexer indexer;
        try
        {
            indexer = CreateIndexer(definition);
        }
        catch (ArgumentException ex)
        {
            return Ok(new IndexerTestResult { Success = false, Message = ex.Message });
        }

        var result = indexer.TestConnectionDetailed(definition);
        return Ok(result);
    }

    [HttpGet("search")]
    public ActionResult<List<ReleaseInfo>> Search([FromQuery] string query, [FromQuery] string category = null, [FromQuery] int? indexerId = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Ok(new List<ReleaseInfo>());
        }

        var definitions = _indexerFactory.All().Where(d => d.Enable && d.EnableSearch).ToList();
        if (indexerId.HasValue && indexerId.Value > 0)
        {
            definitions = definitions.Where(d => d.Id == indexerId.Value).ToList();
        }

        var allResults = new List<ReleaseInfo>();
        foreach (var def in definitions)
        {
            try
            {
                var indexer = CreateIndexer(def);
                var results = indexer.Search(def, query, category);
                if (results != null && results.Count > 0)
                {
                    allResults.AddRange(results);
                }
            }
            catch
            {
                // Continue to next indexer
            }
        }

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

        if (!string.IsNullOrWhiteSpace(request.MagnetUrl))
        {
            ParsedMagnetLink parsed;
            try
            {
                parsed = MagnetLinkParser.Parse(request.MagnetUrl);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }

            if (_torrentService.ExistsByInfoHash(parsed.InfoHash))
            {
                return Conflict(new { message = "Torrent with this info hash already exists in active library" });
            }

            var torrent = new Torrent
            {
                Name = !string.IsNullOrWhiteSpace(request.Title) ? request.Title : parsed.Name,
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

            _downloadHistoryService.RecordTorrentAdded(
                added,
                source: !string.IsNullOrWhiteSpace(request.IndexerName) ? $"Prowlarr ({request.IndexerName})" : "Prowlarr",
                magnetUrl: request.MagnetUrl,
                indexerName: request.IndexerName);

            return Ok(TorrentResourceMapper.ToResource(added));
        }

        if (!string.IsNullOrWhiteSpace(request.DownloadUrl))
        {
            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.DownloadUrl);

                if (request.IndexerId.HasValue && request.IndexerId.Value > 0)
                {
                    var def = _indexerFactory.Get(request.IndexerId.Value);
                    if (def != null && !string.IsNullOrWhiteSpace(def.ApiKey))
                    {
                        httpRequest.Headers.Add("X-Api-Key", def.ApiKey);
                    }
                }

                using var httpResponse = HttpClient.Send(httpRequest);
                if (!httpResponse.IsSuccessStatusCode)
                {
                    return BadRequest($"Failed to download torrent file from indexer (HTTP {(int)httpResponse.StatusCode})");
                }

                var bytes = httpResponse.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                using var stream = new MemoryStream(bytes);
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
                    DateAdded = DateTime.UtcNow
                };

                var addedTorrent = _torrentService.Add(torrent);

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

                _downloadHistoryService.RecordTorrentAdded(
                    addedTorrent,
                    source: !string.IsNullOrWhiteSpace(request.IndexerName) ? $"Prowlarr ({request.IndexerName})" : "Prowlarr",
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
        return clone;
    }

    private static IIndexer CreateIndexer(IndexerDefinition definition)
    {
        return definition.IndexerType switch
        {
            "Prowlarr" => new ProwlarrIndexer(),
            "Torznab" => new TorznabIndexer(),
            "Newznab" => new NewznabIndexer(),
            _ => throw new ArgumentException($"Unknown indexer type: {definition.IndexerType}"),
        };
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
}
