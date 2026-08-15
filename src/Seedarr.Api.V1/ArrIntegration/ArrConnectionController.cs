using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
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

    public ArrConnectionController(
        IArrConnectionFactory connectionFactory,
        IArrSyncService arrSyncService,
        IArrWebhookRegistration webhookRegistration)
    {
        _connectionFactory = connectionFactory;
        _arrSyncService = arrSyncService;
        _webhookRegistration = webhookRegistration;
    }

    [HttpGet]
    public ActionResult<List<ArrConnectionDefinition>> GetAll()
    {
        return Ok(_connectionFactory.All());
    }

    [HttpGet("{id}")]
    public ActionResult<ArrConnectionDefinition> Get(int id)
    {
        return Ok(_connectionFactory.Get(id));
    }

    [HttpPost]
    public ActionResult<ArrConnectionDefinition> Create([FromBody] ArrConnectionDefinition definition)
    {
        var created = _connectionFactory.Create(definition);
        _webhookRegistration.RegisterWebhook(created);
        return Ok(created);
    }

    [HttpPut("{id}")]
    public ActionResult Update(int id, [FromBody] ArrConnectionDefinition definition)
    {
        definition.Id = id;
        _connectionFactory.Update(definition);
        _webhookRegistration.RegisterWebhook(definition);
        return Ok(definition);
    }

    [HttpDelete("{id}")]
    public ActionResult Delete(int id)
    {
        var definition = _connectionFactory.Get(id);
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
}
