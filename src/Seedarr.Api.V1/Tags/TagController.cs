using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Tags;
using NzbDrone.SignalR;
using Seedarr.Http;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Tags;

[V1ApiController("tag")]
public class TagController : RestControllerWithSignalR<TagResource, Tag>
{
    private readonly ITagService _tagService;
    private readonly TagResourceValidator _validator;

    public TagController(ITagService tagService, TagResourceValidator validator, IBroadcastSignalRMessage signalRBroadcaster)
        : base(signalRBroadcaster)
    {
        _tagService = tagService;
        _validator = validator;
    }

    protected override TagResource GetResourceById(Tag model)
    {
        return ToResource(model);
    }

    [HttpGet]
    public ActionResult<List<TagResource>> GetAll()
    {
        return _tagService.GetAll().Select(ToResource).ToList();
    }

    [HttpGet("{id:int}")]
    public ActionResult<TagResource> Get(int id)
    {
        var tag = _tagService.Get(id);
        return ToResource(tag);
    }

    [HttpPost]
    public ActionResult<TagResource> Create([FromBody] TagResource resource)
    {
        var result = _validator.Validate(resource);
        if (!result.IsValid)
        {
            return BadRequest(result.Errors);
        }

        var tag = ToModel(resource);
        var added = _tagService.Add(tag);
        return ToResource(added);
    }

    [HttpPut]
    public ActionResult<TagResource> Update([FromBody] TagResource resource)
    {
        var result = _validator.Validate(resource);
        if (!result.IsValid)
        {
            return BadRequest(result.Errors);
        }

        var tag = ToModel(resource);
        var updated = _tagService.Update(tag);
        return ToResource(updated);
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _tagService.Delete(id);
        return Ok();
    }

    private static TagResource ToResource(Tag model)
    {
        return new TagResource
        {
            Id = model.Id,
            Label = model.Label
        };
    }

    private static Tag ToModel(TagResource resource)
    {
        return new Tag
        {
            Id = resource.Id,
            Label = resource.Label
        };
    }
}
