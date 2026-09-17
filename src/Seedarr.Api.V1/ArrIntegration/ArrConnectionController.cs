using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.ArrIntegration.Webhook;
using Seedarr.Http;

namespace Seedarr.Api.V1.ArrIntegration;

[V1ApiController("arrconnections")]
public class ArrConnectionController : Controller
{
    private readonly IArrConnectionFactory _connectionFactory;
    private readonly IArrSyncService _arrSyncService;
    private readonly IArrWebhookRegistration _webhookRegistration;
    private readonly HttpClient _explicitHttpClient;
    private readonly Logger _logger;

    public ArrConnectionController(
        IArrConnectionFactory connectionFactory,
        IArrSyncService arrSyncService,
        IArrWebhookRegistration webhookRegistration,
        HttpClient httpClient = null)
    {
        _connectionFactory = connectionFactory;
        _arrSyncService = arrSyncService;
        _webhookRegistration = webhookRegistration;
        _explicitHttpClient = httpClient;
        _logger = LogManager.GetCurrentClassLogger();
    }

    [HttpGet]
    public ActionResult<List<ArrConnectionDefinition>> GetAll()
    {
        var definitions = _connectionFactory.All();
        return Ok(definitions.Select(MaskApiKey).ToList());
    }

    [HttpGet("{id}")]
    public ActionResult<ArrConnectionDefinition> Get(int id)
    {
        var definition = _connectionFactory.Get(id);
        if (definition == null)
        {
            return NotFound();
        }

        return Ok(MaskApiKey(definition));
    }

    [HttpPost]
    public ActionResult<ArrConnectionDefinition> Create([FromBody] ArrConnectionDefinition definition)
    {
        if (definition == null)
        {
            return BadRequest("Request body cannot be null");
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            definition.Name = definition.ArrType ?? "ArrConnection";
        }

        if (!ArrConnectionResources.TryNormalizeUrl(definition.Url, out var normalizedUrl, out var urlError))
        {
            return BadRequest(urlError);
        }

        definition.Url = normalizedUrl;
        definition.SyncIntervalMinutes = Math.Max(1, definition.SyncIntervalMinutes);

        // Connectivity problems are not fatal on create: the arr instance may be
        // temporarily offline. Use the test-connection endpoint to validate.
        if (!_arrSyncService.TestConnectionDirect(definition))
        {
            _logger.Warn("Connection test failed for '{0}' at {1}; creating connection anyway", definition.Name, definition.Url);
        }

        var created = _connectionFactory.Create(definition);

        if (!_webhookRegistration.RegisterWebhook(created))
        {
            _logger.Warn("Failed to register webhook in {0} at {1} during connection creation", created.ArrType, created.Url);
        }

        return Ok(MaskApiKey(created));
    }

    [HttpPut("{id}")]
    public ActionResult Update(int id, [FromBody] ArrConnectionDefinition definition)
    {
        if (definition == null)
        {
            return BadRequest("Request body cannot be null");
        }

        definition.Id = id;

        var existing = _connectionFactory.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            definition.Name = existing.Name ?? definition.ArrType ?? "ArrConnection";
        }

        if (!ArrConnectionResources.TryNormalizeUrl(definition.Url, out var updateNormalizedUrl, out var updateUrlError))
        {
            return BadRequest(updateUrlError);
        }

        definition.Url = updateNormalizedUrl;
        definition.SyncIntervalMinutes = Math.Max(1, definition.SyncIntervalMinutes);

        if (string.IsNullOrWhiteSpace(definition.ApiKey) || definition.ApiKey.Contains('*'))
        {
            definition.ApiKey = existing.ApiKey;
        }

        var shouldUnregister = (existing.WebhookEnabled && !definition.WebhookEnabled) ||
                               (existing.Enable && !definition.Enable) ||
                               (!string.Equals(existing.Url?.TrimEnd('/'), definition.Url?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));

        if (shouldUnregister)
        {
            _webhookRegistration.UnregisterWebhook(existing);
        }

        _connectionFactory.Update(definition);

        if (definition.Enable && definition.WebhookEnabled)
        {
            if (!_webhookRegistration.RegisterWebhook(definition))
            {
                _logger.Warn("Failed to register webhook in {0} at {1} during connection update", definition.ArrType, definition.Url);
            }
        }

        return Ok(MaskApiKey(definition));
    }

    [HttpDelete("{id}")]
    public ActionResult Delete(int id)
    {
        var definition = _connectionFactory.Get(id);
        if (definition == null)
        {
            return NotFound();
        }

        _webhookRegistration.UnregisterWebhook(definition);
        _connectionFactory.Delete(id);
        return Ok();
    }

    [HttpPost("{id}/test")]
    public ActionResult<ArrTestResult> TestConnection(int id)
    {
        var result = _arrSyncService.TestConnectionDetailed(id);
        return Ok(result);
    }

    [HttpPost("test")]
    public ActionResult<ArrTestResult> TestDirect([FromBody] ArrConnectionDefinition definition)
    {
        if (definition == null)
        {
            return Ok(ArrTestResult.Fail("Request body cannot be null"));
        }

        if (definition.Id > 0 && definition.ApiKey != null && definition.ApiKey.Contains('*'))
        {
            var existing = _connectionFactory.Get(definition.Id);
            if (existing != null)
            {
                definition.ApiKey = existing.ApiKey;
            }
        }

        if (!ArrConnectionResources.TryNormalizeUrl(definition.Url, out var testNormalizedUrl, out var testUrlError))
        {
            return Ok(ArrTestResult.Fail(testUrlError));
        }

        definition.Url = testNormalizedUrl;

        var result = _arrSyncService.TestConnectionDetailedDirect(definition);
        return Ok(result);
    }

    [HttpPost("sync")]
    public ActionResult<SyncResult> Sync()
    {
        var result = _arrSyncService.Sync();
        return Ok(result);
    }

    [HttpGet("image-proxy")]
    [HttpGet("/api/v1/arr/image-proxy")]
    public async Task<IActionResult> GetImageProxy([FromQuery] int connectionId, [FromQuery] string path, CancellationToken cancellationToken = default)
    {
        if (connectionId <= 0 || string.IsNullOrWhiteSpace(path))
        {
            return BadRequest("connectionId and path are required");
        }

        var definition = _connectionFactory.Get(connectionId);
        if (definition == null || string.IsNullOrWhiteSpace(definition.Url))
        {
            return NotFound("Arr connection not found");
        }

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        var targetUrl = $"{definition.Url.TrimEnd('/')}{path}";
        var client = _explicitHttpClient ?? ArrConnectionResources.GetClient(definition.AcceptInvalidCertificates);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
            if (!string.IsNullOrEmpty(definition.ApiKey))
            {
                request.Headers.Add("X-Api-Key", definition.ApiKey);
            }

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode);
            }

            var contentType = response.Content.Headers.ContentType?.ToString() ?? "image/jpeg";
            if (Response != null)
            {
                Response.Headers["Cache-Control"] = "public, max-age=86400";
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return File(stream, contentType);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error proxying image from Arr connection {0} for path {1}", connectionId, path);
            return StatusCode(502, "Failed to proxy image from Arr instance");
        }
    }

    private static ArrConnectionDefinition MaskApiKey(ArrConnectionDefinition definition)
    {
        var clone = definition.Clone();
        clone.ApiKey = clone.ApiKey?.Length > 4
            ? new string('*', clone.ApiKey.Length - 4) + clone.ApiKey[^4..]
            : new string('*', clone.ApiKey?.Length ?? 0);
        return clone;
    }
}
