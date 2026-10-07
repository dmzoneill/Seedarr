using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Validation;
using Seedarr.Http;

namespace Seedarr.Api.V1.DownloadClients;

[V1ApiController("downloadclients")]
public class DownloadClientController : Controller
{
    private const string PasswordMask = "********"; // NOSONAR

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
        return Ok(definitions.Select(d => MaskPassword(EnrichWithStatus(d))).ToList());
    }

    [HttpGet("{id}")]
    public ActionResult<DownloadClientDefinition> Get(int id)
    {
        var definition = _downloadClientFactory.Get(id);
        if (definition == null)
        {
            return NotFound(new { message = $"Download client {id} not found" });
        }

        return Ok(MaskPassword(EnrichWithStatus(definition)));
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
    public ActionResult Update(int id, [FromBody] JsonElement body)
    {
        if (body.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return BadRequest("Request body cannot be null");
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            return BadRequest("Request body must be a JSON object");
        }

        var presentPropertyKeys = body.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var incoming = JsonSerializer.Deserialize<DownloadClientDefinition>(body, STJson.GetSerializerSettings());
        if (incoming == null)
        {
            return BadRequest("Request body cannot be null");
        }

        return UpdateDefinition(id, incoming, presentPropertyKeys);
    }

    [NonAction]
    public ActionResult Update(int id, DownloadClientDefinition definition) => UpdateDefinition(id, definition, null);

    private ActionResult UpdateDefinition(int id, DownloadClientDefinition incoming, IReadOnlySet<string> presentPropertyKeys)
    {
        if (incoming == null)
        {
            return BadRequest("Request body cannot be null");
        }

        var existing = _downloadClientFactory.Get(id);
        if (existing == null)
        {
            return NotFound(new { message = $"Download client {id} not found" });
        }

        var definition = presentPropertyKeys == null
            ? incoming
            : DownloadClientUpdateMerger.Merge(existing, incoming, presentPropertyKeys);

        definition.Id = id;

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

        if (presentPropertyKeys == null &&
            (string.IsNullOrWhiteSpace(definition.Password) || definition.Password == PasswordMask))
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
        _syncService.ResetClientStatus(id);
        return Ok();
    }

    [HttpPost("{id}/test")]
    public ActionResult<DownloadClientTestResult> TestConnection(int id, [FromQuery] bool force = false)
    {
        var definition = _downloadClientFactory.Get(id);
        if (definition == null)
        {
            return NotFound(new { message = $"Download client {id} not found" });
        }

        var backoffResult = TryRejectClientBackoff(id, force);
        if (backoffResult != null)
        {
            return backoffResult;
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
    public ActionResult<DownloadClientTestResult> TestDirect([FromBody] DownloadClientDefinition definition, [FromQuery] bool force = false)
    {
        if (definition == null)
        {
            return BadRequest("Request body cannot be null");
        }

        if (definition.Id > 0)
        {
            var backoffResult = TryRejectClientBackoff(definition.Id, force);
            if (backoffResult != null)
            {
                return backoffResult;
            }
        }

        if (!UrlValidator.IsSafeUrl($"http://{definition.Host}:{definition.Port}", allowLoopback: true, allowInternal: true))
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

    [HttpGet("items")]
    public ActionResult<DownloadClientAllItemsResult> GetAllItems([FromQuery] bool strict = false)
    {
        try
        {
            var result = _syncService.GetAllClientItems();
            if (strict && result.ClientErrors.Count > 0)
            {
                var firstError = result.ClientErrors[0];
                var statusCode = firstError.Code switch
                {
                    "authentication" => Microsoft.AspNetCore.Http.StatusCodes.Status401Unauthorized,
                    "unavailable" => Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable,
                    "backoff" => Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable,
                    "configuration" => Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest,
                    _ => Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError
                };

                return StatusCode(statusCode, new
                {
                    message = firstError.Message,
                    clientId = firstError.ClientId,
                    code = firstError.Code
                });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError, new { message = $"Failed to fetch aggregated items: {ex.Message}" });
        }
    }

    [HttpPost("{id:int}/torrents/{infoHash}/pause")]
    public ActionResult PauseTorrent(int id, string infoHash, [FromQuery] bool force = false)
    {
        var definition = _downloadClientFactory.Get(id);
        if (definition == null)
        {
            return NotFound(new { message = $"Download client {id} not found" });
        }

        var backoffResult = TryRejectClientBackoff(id, force);
        if (backoffResult != null)
        {
            return backoffResult;
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

        var success = client.PauseTorrent(infoHash);
        if (!success)
        {
            return BadRequest(new { message = $"Failed to pause torrent {infoHash} on client {definition.Name}" });
        }

        return Ok(new { success = true });
    }

    [HttpPost("{id:int}/torrents/{infoHash}/resume")]
    public ActionResult ResumeTorrent(int id, string infoHash, [FromQuery] bool force = false)
    {
        var definition = _downloadClientFactory.Get(id);
        if (definition == null)
        {
            return NotFound(new { message = $"Download client {id} not found" });
        }

        var backoffResult = TryRejectClientBackoff(id, force);
        if (backoffResult != null)
        {
            return backoffResult;
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

        var success = client.ResumeTorrent(infoHash);
        if (!success)
        {
            return BadRequest(new { message = $"Failed to resume torrent {infoHash} on client {definition.Name}" });
        }

        return Ok(new { success = true });
    }

    [HttpDelete("{id:int}/torrents/{infoHash}")]
    public ActionResult DeleteTorrent(int id, string infoHash, [FromQuery] bool deleteData = false, [FromQuery] bool force = false)
    {
        var definition = _downloadClientFactory.Get(id);
        if (definition == null)
        {
            return NotFound(new { message = $"Download client {id} not found" });
        }

        var backoffResult = TryRejectClientBackoff(id, force);
        if (backoffResult != null)
        {
            return backoffResult;
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

        var success = client.DeleteTorrent(infoHash, deleteData);
        if (!success)
        {
            return BadRequest(new { message = $"Failed to delete torrent {infoHash} on client {definition.Name}" });
        }

        return Ok(new { success = true });
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
            if (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound(new { message = ex.Message });
            }

            return BadRequest(new { message = ex.Message });
        }
        catch (DownloadClientAuthenticationException ex)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status401Unauthorized, new { message = ex.Message });
        }
        catch (DownloadClientUnavailableException ex)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
        catch (global::System.Net.Http.HttpRequestException ex)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError, new { message = $"Failed to fetch items from download client: {ex.Message}" });
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

    private ActionResult TryRejectClientBackoff(int clientId, bool force)
    {
        if (force)
        {
            return null;
        }

        var status = _syncService.GetClientStatus(clientId);
        if (status?.IsInBackoff != true)
        {
            return null;
        }

        return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable, new
        {
            message = $"Download client is in backoff until {status.BackoffUntil:O}.",
            code = "backoff",
            clientId,
            backoffUntil = status.BackoffUntil,
        });
    }

    private DownloadClientDefinition EnrichWithStatus(DownloadClientDefinition definition)
    {
        if (definition == null)
        {
            return null;
        }

        var status = _syncService.GetClientStatus(definition.Id);
        if (status != null)
        {
            definition.IsOnline = status.IsOnline;
            definition.Version = status.Version;
            definition.LastSyncTime = status.LastSyncTime;
            definition.LastErrorMessage = status.LastErrorMessage;
            definition.ConsecutiveFailures = status.ConsecutiveFailures;
            definition.BackoffUntil = status.BackoffUntil;
        }

        return definition;
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
