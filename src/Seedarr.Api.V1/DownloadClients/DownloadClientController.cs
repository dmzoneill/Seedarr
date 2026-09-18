using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Validation;
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
        if (definition == null)
        {
            return BadRequest("Request body cannot be null");
        }

        var validationError = ValidateDefinition(definition);
        if (validationError != null)
        {
            return BadRequest(validationError);
        }

        if (!string.IsNullOrWhiteSpace(definition.ClientType))
        {
            definition.ClientType = NormalizeClientType(definition.ClientType);
        }

        if (string.IsNullOrWhiteSpace(definition.Implementation))
        {
            definition.Implementation = $"{definition.ClientType}Client";
        }

        if (string.IsNullOrWhiteSpace(definition.ConfigContract))
        {
            definition.ConfigContract = $"{definition.ClientType}Settings";
        }

        var created = _downloadClientFactory.Create(definition);
        return Ok(MaskPassword(created));
    }

    [HttpPut("{id}")]
    public ActionResult Update(int id, [FromBody] DownloadClientDefinition definition)
    {
        if (definition == null)
        {
            return BadRequest("Request body cannot be null");
        }

        var validationError = ValidateDefinition(definition);
        if (validationError != null)
        {
            return BadRequest(validationError);
        }

        var existing = _downloadClientFactory.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        definition.Id = id;

        if (!string.IsNullOrWhiteSpace(definition.ClientType))
        {
            definition.ClientType = NormalizeClientType(definition.ClientType);
        }

        if (string.IsNullOrWhiteSpace(definition.Implementation))
        {
            definition.Implementation = $"{definition.ClientType}Client";
        }

        if (string.IsNullOrWhiteSpace(definition.ConfigContract))
        {
            definition.ConfigContract = $"{definition.ClientType}Settings";
        }

        // If password is omitted, empty, or masked, preserve the existing value
        if (string.IsNullOrWhiteSpace(definition.Password) || definition.Password == PasswordMask)
        {
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

        if (!UrlValidator.IsSafeUrl($"http://{definition.Host}:{definition.Port}", allowLoopback: true, allowInternal: true))
        {
            return BadRequest("Target host/URL is not permitted.");
        }

        IDownloadClient client;
        try
        {
            client = _downloadClientFactory.CreateClient(definition);
            if (client == null)
            {
                return BadRequest(new { message = $"Unknown client type: {definition.ClientType}" });
            }
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
        if (definition == null)
        {
            return BadRequest("Request body cannot be null");
        }

        if (!UrlValidator.IsSafeUrl($"http://{definition.Host}:{definition.Port}"))
        {
            return BadRequest("Target host/URL is not permitted.");
        }

        if (definition.Id > 0 && (string.IsNullOrWhiteSpace(definition.Password) || definition.Password == PasswordMask))
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
            client = _downloadClientFactory.CreateClient(definition);
            if (client == null)
            {
                return Ok(DownloadClientTestResult.Fail($"Invalid configuration: Unknown client type: {definition.ClientType}"));
            }
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
    [HttpPost("{id}/import-torrents")]
    public ActionResult<BatchImportResponse> ImportTorrents(int id, [FromBody] DownloadClientImportRequest request)
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

    private static string NormalizeClientType(string clientType)
    {
        return clientType?.Trim().ToLowerInvariant() switch
        {
            "qbittorrent" => "QBitTorrent",
            "transmission" => "Transmission",
            "deluge" => "Deluge",
            _ => clientType?.Trim(),
        };
    }

    private static string ValidateDefinition(DownloadClientDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            return "Name is required";
        }

        if (string.IsNullOrWhiteSpace(definition.Host))
        {
            return "Host is required";
        }

        if (definition.Port < 1 || definition.Port > 65535)
        {
            return "Port must be between 1 and 65535";
        }

        return null;
    }
}
