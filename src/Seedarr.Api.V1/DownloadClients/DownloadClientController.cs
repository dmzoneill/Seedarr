using System;
using System.Collections.Generic;
using System.Linq;
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
    private const string PasswordMask = "********";

    private readonly IDownloadClientFactory _downloadClientFactory;

    public DownloadClientController(IDownloadClientFactory downloadClientFactory)
    {
        _downloadClientFactory = downloadClientFactory;
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

        // If the masked password was sent back, preserve the existing value
        if (definition.Password == PasswordMask)
        {
            var existing = _downloadClientFactory.Get(id);
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
    public ActionResult<object> TestConnection(int id)
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

        var success = client.TestConnection();
        return Ok(new { success });
    }

    private static DownloadClientDefinition MaskPassword(DownloadClientDefinition definition)
    {
        definition.Password = string.IsNullOrEmpty(definition.Password) ? "" : PasswordMask;
        return definition;
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
