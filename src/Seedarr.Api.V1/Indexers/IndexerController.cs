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
        return Ok(MaskApiKey(_indexerFactory.Get(id)));
    }

    [HttpPost]
    public ActionResult<IndexerDefinition> Create([FromBody] IndexerDefinition definition)
    {
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
        var indexer = CreateIndexer(definition);
        var success = indexer.TestConnection(definition);
        return Ok(new { success });
    }

    private static IndexerDefinition MaskApiKey(IndexerDefinition definition)
    {
        definition.ApiKey = definition.ApiKey?.Length > 4
            ? new string('*', definition.ApiKey.Length - 4) + definition.ApiKey[^4..]
            : new string('*', definition.ApiKey?.Length ?? 0);
        return definition;
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
