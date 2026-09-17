using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Tags;
using Seedarr.Http;

namespace Seedarr.Api.V1.Tags;

[V1ApiController("autotag")]
public class AutoTaggerController : ControllerBase
{
    private readonly IAutoTaggerService _autoTaggerService;
    private readonly ITagService _tagService;

    public AutoTaggerController(IAutoTaggerService autoTaggerService, ITagService tagService = null)
    {
        _autoTaggerService = autoTaggerService;
        _tagService = tagService;
    }

    [HttpGet("rules")]
    public ActionResult<List<AutoTaggerRuleResource>> GetRules()
    {
        var rules = _autoTaggerService.GetAllRules();
        var allTags = _tagService?.GetAll()?.ToDictionary(t => t.Id, t => t.Label) ?? new Dictionary<int, string>();

        var resources = rules.Select(r =>
        {
            allTags.TryGetValue(r.TagId, out var label);
            return AutoTaggerRuleResourceMapper.ToResource(r, label);
        }).ToList();

        return Ok(resources);
    }

    [HttpPost("rules")]
    public ActionResult<AutoTaggerRuleResource> SaveRule([FromBody] AutoTaggerRuleResource resource)
    {
        if (resource == null)
        {
            return BadRequest("Request body cannot be null");
        }

        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return BadRequest("Rule Name is required");
        }

        if (string.IsNullOrWhiteSpace(resource.Pattern))
        {
            return BadRequest("Rule Pattern is required");
        }

        var model = AutoTaggerRuleResourceMapper.ToModel(resource);
        AutoTaggerRule saved;

        if (model.Id > 0)
        {
            saved = _autoTaggerService.UpdateRule(model);
        }
        else
        {
            saved = _autoTaggerService.AddRule(model);
        }

        var tagLabel = _tagService?.Get(saved.TagId)?.Label;
        return Ok(AutoTaggerRuleResourceMapper.ToResource(saved, tagLabel));
    }

    [HttpDelete("rules/{id:int}")]
    public ActionResult DeleteRule(int id)
    {
        _autoTaggerService.DeleteRule(id);
        return Ok();
    }

    [HttpPost("evaluate")]
    public ActionResult Evaluate()
    {
        _autoTaggerService.EvaluateAll();
        return Ok();
    }
}
