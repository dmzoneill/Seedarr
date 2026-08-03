using System.Collections.Generic;
using System.Linq;
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
    private readonly Logger _logger;

    public ArrConnectionController(
        IArrConnectionFactory connectionFactory,
        IArrSyncService arrSyncService,
        IArrWebhookRegistration webhookRegistration)
    {
        _connectionFactory = connectionFactory;
        _arrSyncService = arrSyncService;
        _webhookRegistration = webhookRegistration;
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
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            definition.Name = definition.ArrType ?? "ArrConnection";
        }

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

        if (definition.ApiKey != null)
        {
            var maskedKey = existing.ApiKey?.Length > 4
                ? new string('*', existing.ApiKey.Length - 4) + existing.ApiKey[^4..]
                : new string('*', existing.ApiKey?.Length ?? 0);
            if (definition.ApiKey == maskedKey)
            {
                definition.ApiKey = existing.ApiKey;
            }
        }

        _connectionFactory.Update(definition);

        if (!_webhookRegistration.RegisterWebhook(definition))
        {
            _logger.Warn("Failed to register webhook in {0} at {1} during connection update", definition.ArrType, definition.Url);
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
        if (definition.Id > 0 && definition.ApiKey != null && definition.ApiKey.Contains('*'))
        {
            var existing = _connectionFactory.Get(definition.Id);
            if (existing != null)
            {
                definition.ApiKey = existing.ApiKey;
            }
        }

        var result = _arrSyncService.TestConnectionDetailedDirect(definition);
        return Ok(result);
    }

    [HttpPost("sync")]
    public ActionResult<SyncResult> Sync()
    {
        var result = _arrSyncService.Sync();
        return Ok(result);
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
