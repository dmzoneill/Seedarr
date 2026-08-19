using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Newznab;
using NzbDrone.Core.Indexers.Prowlarr;
using NzbDrone.Core.Indexers.Torznab;
using Seedarr.Http;

namespace Seedarr.Api.V1.Indexers;

[V1ApiController("indexers")]
public class IndexerController : Controller
{
    private readonly IIndexerFactory _indexerFactory;

    public IndexerController(IIndexerFactory indexerFactory)
    {
        _indexerFactory = indexerFactory;
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

    [HttpPost("{id}/test")]
    public ActionResult<object> TestConnection(int id)
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
            return BadRequest(new { message = ex.Message });
        }

        var success = indexer.TestConnection(definition);
        return Ok(new { success });
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
