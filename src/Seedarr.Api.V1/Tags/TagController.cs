using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Tags;
using Seedarr.Http;

namespace Seedarr.Api.V1.Tags;

[V1ApiController("tag")]
public class TagController : Controller
{
    private readonly ITagService _tagService;

    public TagController(ITagService tagService)
    {
        _tagService = tagService;
    }

    [HttpGet]
    public ActionResult<List<Tag>> GetAll() => _tagService.GetAll();

    [HttpGet("{id:int}")]
    public ActionResult<Tag> Get(int id) => _tagService.Get(id);

    [HttpPost]
    public ActionResult<Tag> Create([FromBody] Tag tag) => _tagService.Add(tag);

    [HttpPut]
    public ActionResult<Tag> Update([FromBody] Tag tag) => _tagService.Update(tag);

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _tagService.Delete(id);
        return Ok();
    }
}
