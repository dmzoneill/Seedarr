using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.SignalR;
using Seedarr.Http;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Tags;

[V1ApiController("tag")]
public class TagController : RestControllerWithSignalR<TagResource, Tag>
{
    private readonly ITagService _tagService;
    private readonly TagResourceValidator _validator;
    private readonly ITorrentService _torrentService;

    public TagController(
        ITagService tagService,
        TagResourceValidator validator,
        IBroadcastSignalRMessage signalRBroadcaster,
        ITorrentService torrentService = null)
        : base(signalRBroadcaster)
    {
        _tagService = tagService;
        _validator = validator;
        _torrentService = torrentService;
    }

    protected override TagResource GetResourceById(Tag model)
    {
        var count = GetTorrentCountForTag(model.Id);
        return TagResourceMapper.ToResource(model, count);
    }

    [HttpGet]
    public ActionResult<List<TagResource>> GetAll()
    {
        var tags = _tagService.GetAll();
        var torrents = _torrentService?.GetAll() ?? new List<Torrent>();
        var counts = torrents
            .Where(t => t.TagIds != null)
            .SelectMany(t => t.TagIds)
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        return tags.Select(t => TagResourceMapper.ToResource(t, counts.GetValueOrDefault(t.Id, 0))).ToList();
    }

    [HttpGet("{id:int}")]
    public ActionResult<TagResource> Get(int id)
    {
        var tag = _tagService.Get(id);
        if (tag == null)
        {
            return NotFound();
        }

        var count = GetTorrentCountForTag(id);
        return TagResourceMapper.ToResource(tag, count);
    }

    [HttpPost]
    public ActionResult<TagResource> Create([FromBody] TagResource resource)
    {
        if (resource == null)
        {
            return BadRequest("Request body cannot be null");
        }

        var result = _validator.Validate(resource);
        if (!result.IsValid)
        {
            return BadRequest(result.Errors);
        }

        try
        {
            var tag = ToModel(resource);
            var added = _tagService.Add(tag);
            return TagResourceMapper.ToResource(added, 0);
        }
        catch (DuplicateTagException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    [HttpPut]
    public ActionResult<TagResource> Update([FromBody] TagResource resource, int? id = null)
    {
        if (resource == null)
        {
            return BadRequest("Request body cannot be null");
        }

        if (id.HasValue && id.Value > 0)
        {
            resource.Id = id.Value;
        }

        var result = _validator.Validate(resource);
        if (!result.IsValid)
        {
            return BadRequest(result.Errors);
        }

        if (resource.Id <= 0 || _tagService.Get(resource.Id) == null)
        {
            return NotFound();
        }

        try
        {
            var tag = ToModel(resource);
            var updated = _tagService.Update(tag);
            var count = GetTorrentCountForTag(updated.Id);
            return TagResourceMapper.ToResource(updated, count);
        }
        catch (DuplicateTagException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [NonAction]
    public ActionResult<TagResource> Update(int id, TagResource resource) => Update(resource, id);

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _tagService.Delete(id);
        return Ok();
    }

    [HttpPost("bulk-assign")]
    public ActionResult BulkAssign([FromBody] BulkTagRequest request)
    {
        if (request == null)
        {
            return BadRequest("Request body cannot be null");
        }

        if (request.TagIds == null || request.TorrentIds == null)
        {
            return BadRequest("TagIds and TorrentIds are required");
        }

        if (_torrentService == null)
        {
            return Ok();
        }

        foreach (var torrentId in request.TorrentIds)
        {
            var torrent = _torrentService.Get(torrentId);
            if (torrent == null)
            {
                continue;
            }

            torrent.TagIds ??= new List<int>();
            var modified = false;
            foreach (var tagId in request.TagIds)
            {
                if (!torrent.TagIds.Contains(tagId))
                {
                    torrent.TagIds.Add(tagId);
                    modified = true;
                }
            }

            if (modified)
            {
                _torrentService.UpdateUserFields(torrent);
            }
        }

        return Ok();
    }

    [HttpPost("bulk-remove")]
    public ActionResult BulkRemove([FromBody] BulkTagRequest request)
    {
        if (request == null)
        {
            return BadRequest("Request body cannot be null");
        }

        if (request.TagIds == null || request.TorrentIds == null)
        {
            return BadRequest("TagIds and TorrentIds are required");
        }

        if (_torrentService == null)
        {
            return Ok();
        }

        var tagSet = new HashSet<int>(request.TagIds);
        foreach (var torrentId in request.TorrentIds)
        {
            var torrent = _torrentService.Get(torrentId);
            if (torrent == null || torrent.TagIds == null || torrent.TagIds.Count == 0)
            {
                continue;
            }

            var removed = torrent.TagIds.RemoveAll(id => tagSet.Contains(id));
            if (removed > 0)
            {
                _torrentService.UpdateUserFields(torrent);
            }
        }

        return Ok();
    }

    private int GetTorrentCountForTag(int tagId)
    {
        if (_torrentService == null)
        {
            return 0;
        }

        var torrents = _torrentService.GetAll();
        return torrents?.Count(t => t.TagIds != null && t.TagIds.Contains(tagId)) ?? 0;
    }

    private static TagResource ToResource(Tag model) => TagResourceMapper.ToResource(model);

    private static Tag ToModel(TagResource resource) => TagResourceMapper.ToModel(resource);
}
