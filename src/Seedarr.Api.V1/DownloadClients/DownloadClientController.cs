using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.DownloadClients.Deluge;
using NzbDrone.Core.DownloadClients.QBitTorrent;
using NzbDrone.Core.DownloadClients.Transmission;
using Seedarr.Http;

namespace Seedarr.Api.V1.DownloadClients;

[V1ApiController("downloadclients")]
public class DownloadClientController : Controller
{
    private readonly IDownloadClientFactory _downloadClientFactory;

    public DownloadClientController(IDownloadClientFactory downloadClientFactory)
    {
        _downloadClientFactory = downloadClientFactory;
    }

    [HttpGet]
    public ActionResult<List<DownloadClientDefinition>> GetAll()
    {
        return Ok(_downloadClientFactory.All());
    }

    [HttpGet("{id}")]
    public ActionResult<DownloadClientDefinition> Get(int id)
    {
        return Ok(_downloadClientFactory.Get(id));
    }

    [HttpPost]
    public ActionResult<DownloadClientDefinition> Create([FromBody] DownloadClientDefinition definition)
    {
        var created = _downloadClientFactory.Create(definition);
        return Ok(created);
    }

    [HttpPut("{id}")]
    public ActionResult Update(int id, [FromBody] DownloadClientDefinition definition)
    {
        definition.Id = id;
        _downloadClientFactory.Update(definition);
        return Ok(definition);
    }

    [HttpDelete("{id}")]
    public ActionResult Delete(int id)
    {
        _downloadClientFactory.Delete(id);
        return Ok();
    }

    [HttpPost("{id}/test")]
    public ActionResult<object> TestConnection(int id)
    {
        var definition = _downloadClientFactory.Get(id);
        var client = CreateClient(definition);
        var success = client.TestConnection();
        return Ok(new { success });
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
