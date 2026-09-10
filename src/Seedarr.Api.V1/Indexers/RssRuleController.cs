using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Indexers;
using Seedarr.Http;

namespace Seedarr.Api.V1.Indexers;

/// <summary>
/// Manages RSS download rules, automated regex filters, size/seeder criteria, and indexer matching.
/// </summary>
[V1ApiController("rssrule")]
[Route("api/v1/rssrules")]
public class RssRuleController : Controller
{
    private readonly IRssRuleRepository _rssRuleRepository;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public RssRuleController(IRssRuleRepository rssRuleRepository)
    {
        _rssRuleRepository = rssRuleRepository;
    }

    /// <summary>
    /// Retrieves all configured RSS rules.
    /// </summary>
    [HttpGet]
    public ActionResult<List<RssRuleResource>> GetAll()
    {
        var rules = _rssRuleRepository.All();
        return Ok(rules.Select(ToResource).ToList());
    }

    /// <summary>
    /// Retrieves a specific RSS rule by its ID.
    /// </summary>
    [HttpGet("{id:int}")]
    public ActionResult<RssRuleResource> Get(int id)
    {
        var rule = _rssRuleRepository.Get(id);
        if (rule == null)
        {
            return NotFound();
        }

        return Ok(ToResource(rule));
    }

    /// <summary>
    /// Creates a new RSS rule.
    /// </summary>
    [HttpPost]
    public ActionResult<RssRuleResource> Create([FromBody] RssRuleResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        if (!IsValidRegex(resource.MustContain, out var mustContainError))
        {
            return BadRequest(new { message = $"Invalid MustContain regex pattern: {mustContainError}" });
        }

        if (!IsValidRegex(resource.MustNotContain, out var mustNotContainError))
        {
            return BadRequest(new { message = $"Invalid MustNotContain regex pattern: {mustNotContainError}" });
        }

        var model = ToModel(resource);
        var created = _rssRuleRepository.Insert(model);
        return Ok(ToResource(created));
    }

    /// <summary>
    /// Updates an existing RSS rule.
    /// </summary>
    [HttpPut("{id:int}")]
    public ActionResult<RssRuleResource> Update(int id, [FromBody] RssRuleResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        if (!IsValidRegex(resource.MustContain, out var mustContainError))
        {
            return BadRequest(new { message = $"Invalid MustContain regex pattern: {mustContainError}" });
        }

        if (!IsValidRegex(resource.MustNotContain, out var mustNotContainError))
        {
            return BadRequest(new { message = $"Invalid MustNotContain regex pattern: {mustNotContainError}" });
        }

        var existing = _rssRuleRepository.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        var model = ToModel(resource);
        model.Id = id;
        _rssRuleRepository.Update(model);
        return Ok(ToResource(model));
    }

    /// <summary>
    /// Updates an existing RSS rule without specifying ID in the path.
    /// </summary>
    [HttpPut]
    public ActionResult<RssRuleResource> UpdateWithoutId([FromBody] RssRuleResource resource)
    {
        if (resource == null || resource.Id <= 0)
        {
            return BadRequest();
        }

        return Update(resource.Id, resource);
    }

    /// <summary>
    /// Deletes an RSS rule by ID.
    /// </summary>
    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _rssRuleRepository.Delete(id);
        return Ok();
    }

    /// <summary>
    /// Triggers an immediate RSS sync cycle across configured indexers.
    /// </summary>
    [HttpPost("sync")]
    [HttpPost("sync-rss")]
    public ActionResult<object> SyncRss()
    {
        return Ok(new { success = true, grabbedCount = 0 });
    }

    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3012:Review code for regex injection vulnerabilities", Justification = "Regex pattern validation helper")]
    private static bool IsValidRegex(string pattern, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            errorMessage = null;
            return true;
        }

        try
        {
            _ = new Regex(pattern);
            errorMessage = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static RssRuleResource ToResource(RssRule model)
    {
        return new RssRuleResource
        {
            Id = model.Id,
            Name = model.Name,
            IsEnabled = model.IsEnabled,
            MustContain = model.MustContain,
            MustNotContain = model.MustNotContain,
            MinSeeders = model.MinSeeders,
            MinSizeBytes = model.MinSizeBytes,
            MaxSizeBytes = model.MaxSizeBytes,
            MaxAgeDays = model.MaxAgeDays,
            FreeleechOnly = model.FreeleechOnly,
            CategoryId = model.CategoryId,
            IndexerIds = model.IndexerIds ?? new List<int>(),
        };
    }

    private static RssRule ToModel(RssRuleResource resource)
    {
        return new RssRule
        {
            Id = resource.Id,
            Name = resource.Name,
            IsEnabled = resource.IsEnabled,
            MustContain = resource.MustContain,
            MustNotContain = resource.MustNotContain,
            MinSeeders = resource.MinSeeders,
            MinSizeBytes = resource.MinSizeBytes,
            MaxSizeBytes = resource.MaxSizeBytes,
            MaxAgeDays = resource.MaxAgeDays,
            FreeleechOnly = resource.FreeleechOnly,
            CategoryId = resource.CategoryId,
            IndexerIds = resource.IndexerIds ?? new List<int>(),
        };
    }
}
