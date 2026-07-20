using System;
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
        if (string.IsNullOrEmpty(definition.WebhookSecret))
        {
            definition.WebhookSecret = Guid.NewGuid().ToString("N");
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

        if (definition.ApiKey != null)
        {
            var existing = _connectionFactory.Get(id);
            if (existing == null)
            {
                return NotFound();
            }

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
    public ActionResult<object> TestConnection(int id)
    {
        var success = _arrSyncService.TestConnection(id);
        return Ok(new { success });
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
