using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.DownloadClients.Deluge;
using NzbDrone.Core.DownloadClients.QBitTorrent;
using NzbDrone.Core.DownloadClients.Transmission;
using NzbDrone.Core.Torrents;
using Seedarr.Http;

namespace Seedarr.Api.V1.DownloadClients;

[V1ApiController("downloadclients")]
public class DownloadClientController : Controller
{
    private const string PasswordMask = "********";

    private readonly IDownloadClientFactory _downloadClientFactory;
    private readonly NzbDrone.Core.DownloadClients.Sync.IDownloadClientSyncService _syncService;

    public DownloadClientController(
        IDownloadClientFactory downloadClientFactory,
        NzbDrone.Core.DownloadClients.Sync.IDownloadClientSyncService syncService)
    {
        _downloadClientFactory = downloadClientFactory;
        _syncService = syncService;
    }

    [HttpGet]
    public ActionResult<List<DownloadClientDefinition>> GetAll()
    {
        var definitions = _downloadClientFactory.All();
        return Ok(definitions.Select(MaskPassword).ToList());
    }

    [HttpGet("{id}")]
    public ActionResult<DownloadClientDefinition> Get(int id)
    {
        var definition = _downloadClientFactory.Get(id);
        if (definition == null)
        {
            return NotFound();
        }

        return Ok(MaskPassword(definition));
    }

    [HttpPost]
    public ActionResult<DownloadClientDefinition> Create([FromBody] DownloadClientDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Implementation))
        {
            definition.Implementation = $"{definition.ClientType}Client";
        }

        if (string.IsNullOrWhiteSpace(definition.ConfigContract))
        {
            definition.ConfigContract = $"{definition.ClientType}Settings";
        }

        try
        {
            CreateClient(definition);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var created = _downloadClientFactory.Create(definition);
        return Ok(MaskPassword(created));
    }

    [HttpPut("{id}")]
    public ActionResult Update(int id, [FromBody] DownloadClientDefinition definition)
    {
        definition.Id = id;

        if (string.IsNullOrWhiteSpace(definition.Implementation))
        {
            definition.Implementation = $"{definition.ClientType}Client";
        }

        if (string.IsNullOrWhiteSpace(definition.ConfigContract))
        {
            definition.ConfigContract = $"{definition.ClientType}Settings";
        }

        // If the masked password was sent back, preserve the existing value
        if (definition.Password == PasswordMask)
        {
            var existing = _downloadClientFactory.Get(id);
            if (existing == null)
            {
                return NotFound();
            }

            definition.Password = existing.Password;
        }

        _downloadClientFactory.Update(definition);
        return Ok(MaskPassword(definition));
    }

    [HttpDelete("{id}")]
    public ActionResult Delete(int id)
    {
        _downloadClientFactory.Delete(id);
        return Ok();
    }

    [HttpPost("{id}/test")]
    public ActionResult<DownloadClientTestResult> TestConnection(int id)
    {
        var definition = _downloadClientFactory.Get(id);
        if (definition == null)
        {
            return NotFound();
        }

        IDownloadClient client;
        try
        {
            client = CreateClient(definition);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var result = client.TestConnectionDetailed();
        return Ok(result);
    }

    [HttpPost("test")]
    public ActionResult<DownloadClientTestResult> TestDirect([FromBody] DownloadClientDefinition definition)
    {
        if (definition.Id > 0 && definition.Password == PasswordMask)
        {
            var existing = _downloadClientFactory.Get(definition.Id);
            if (existing != null)
            {
                definition.Password = existing.Password;
            }
        }

        IDownloadClient client;
        try
        {
            client = CreateClient(definition);
        }
        catch (ArgumentException ex)
        {
            return Ok(DownloadClientTestResult.Fail($"Invalid configuration: {ex.Message}"));
        }

        var result = client.TestConnectionDetailed();
        return Ok(result);
    }

    [HttpGet("{id}/items")]
    public ActionResult<List<DownloadClientRemoteItem>> GetItems(int id)
    {
        try
        {
            var items = _syncService.GetClientItems(id);
            return Ok(items);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Failed to fetch items from download client: {ex.Message}" });
        }
    }

    [HttpPost("{id}/import/{infoHash}")]
    public ActionResult<Torrent> ImportTorrent(int id, string infoHash)
    {
        try
        {
            var torrent = _syncService.ImportTorrent(id, infoHash);
            return Ok(torrent);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Failed to import torrent: {ex.Message}" });
        }
    }

    [HttpPost("{id}/import")]
    public ActionResult<SyncResult> ImportTorrents(int id, [FromBody] DownloadClientImportRequest request)
    {
        try
        {
            var result = _syncService.ImportTorrents(id, request?.InfoHashes ?? new List<string>());
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Failed to import torrents: {ex.Message}" });
        }
    }

    private static DownloadClientDefinition MaskPassword(DownloadClientDefinition definition)
    {
        var clone = definition.Clone();
        clone.Password = string.IsNullOrEmpty(clone.Password) ? "" : PasswordMask;
        return clone;
    }

    private static IDownloadClient CreateClient(DownloadClientDefinition definition)
    {
        return definition.ClientType switch
        {
            "QBitTorrent" => new QBitTorrentClient
            {
                Host = definition.Host,
                Port = definition.Port,
                UseSsl = definition.UseSsl,
                Username = definition.Username,
                Password = definition.Password,
                Category = definition.Category,
            },
            "Transmission" => new TransmissionClient
            {
                Host = definition.Host,
                Port = definition.Port,
                UseSsl = definition.UseSsl,
                Username = definition.Username,
                Password = definition.Password,
                Category = definition.Category,
            },
            "Deluge" => new DelugeClient
            {
                Host = definition.Host,
                Port = definition.Port,
                UseSsl = definition.UseSsl,
                Username = definition.Username,
                Password = definition.Password,
                Category = definition.Category,
            },
            _ => throw new ArgumentException($"Unknown client type: {definition.ClientType}"),
        };
    }
}
