using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Categories;
using NzbDrone.SignalR;
using Seedarr.Http;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Categories;

[V1ApiController("categories")]
[Route("api/v1/category")]
public class CategoryController : RestControllerWithSignalR<CategoryResource, Category>
{
    private readonly ICategoryService _categoryService;

    public CategoryController(
        ICategoryService categoryService,
        IBroadcastSignalRMessage signalRBroadcaster)
        : base(signalRBroadcaster)
    {
        _categoryService = categoryService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<CategoryResource>), StatusCodes.Status200OK)]
    public ActionResult<List<CategoryResource>> GetAll()
    {
        var categories = _categoryService.GetAll();
        var resources = categories.Select(CategoryResourceMapper.ToResource).ToList();
        return Ok(resources);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CategoryResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<CategoryResource> GetById(int id)
    {
        var category = _categoryService.Get(id);
        if (category == null)
        {
            return NotFound();
        }

        return Ok(CategoryResourceMapper.ToResource(category));
    }

    [HttpPost]
    [ProducesResponseType(typeof(CategoryResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<CategoryResource> Add([FromBody] CategoryResource resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Name))
        {
            return BadRequest("Category name is required.");
        }

        if (!IsValidSavePath(resource.SavePath, out var pathError))
        {
            return BadRequest(pathError);
        }

        var trimmedName = resource.Name.Trim();
        var existing = _categoryService.GetByName(trimmedName);
        if (existing != null)
        {
            return BadRequest($"A category with name '{trimmedName}' already exists.");
        }

        resource.Name = trimmedName;
        if (!string.IsNullOrWhiteSpace(resource.SavePath))
        {
            resource.SavePath = NormalizeSavePath(resource.SavePath);
        }

        var model = CategoryResourceMapper.ToModel(resource);
        try
        {
            var inserted = _categoryService.Add(model);
            return Ok(CategoryResourceMapper.ToResource(inserted));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id:int}")]
    [HttpPut]
    [ProducesResponseType(typeof(CategoryResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<CategoryResource> Update(int? id, [FromBody] CategoryResource resource)
    {
        if (resource == null || string.IsNullOrWhiteSpace(resource.Name))
        {
            return BadRequest("Category name is required.");
        }

        if (!IsValidSavePath(resource.SavePath, out var pathError))
        {
            return BadRequest(pathError);
        }

        var targetId = id ?? resource.Id;
        if (targetId <= 0)
        {
            return BadRequest("Valid category ID is required.");
        }

        var current = _categoryService.Get(targetId);
        if (current == null)
        {
            return NotFound();
        }

        var trimmedName = resource.Name.Trim();
        var existingWithName = _categoryService.GetByName(trimmedName);
        if (existingWithName != null && existingWithName.Id != targetId)
        {
            return BadRequest($"A category with name '{trimmedName}' already exists.");
        }

        resource.Name = trimmedName;
        if (!string.IsNullOrWhiteSpace(resource.SavePath))
        {
            resource.SavePath = NormalizeSavePath(resource.SavePath);
        }

        var model = CategoryResourceMapper.ToModel(resource);
        model.Id = targetId;
        try
        {
            var updated = _categoryService.Update(model);
            return Ok(CategoryResourceMapper.ToResource(updated));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult Delete(int id)
    {
        try
        {
            _categoryService.Delete(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private static bool IsValidSavePath(string savePath, out string errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(savePath))
        {
            return true;
        }

        if (savePath.Contains('\0'))
        {
            errorMessage = "Save path cannot contain null bytes.";
            return false;
        }

        if (savePath.Contains(".."))
        {
            errorMessage = "Save path cannot contain directory traversal sequences ('..').";
            return false;
        }

        if (savePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            errorMessage = "Save path contains invalid path characters.";
            return false;
        }

        if (!IsPathRooted(savePath))
        {
            errorMessage = "Save path must be an absolute path (e.g. '/downloads/movies' or 'D:\\Downloads\\Movies').";
            return false;
        }

        try
        {
            _ = Path.GetFullPath(savePath);
        }
        catch (Exception ex)
        {
            errorMessage = $"Invalid save path format: {ex.Message}";
            return false;
        }

        return true;
    }

    private static bool IsPathRooted(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return true;
        }

        // Support Windows drive roots (e.g. D:\path or C:/path) on non-Windows platforms
        if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
        {
            return true;
        }

        // Support UNC paths (\\server\share) on non-Windows platforms
        if (path.Length >= 2 && path[0] == '\\' && path[1] == '\\')
        {
            return true;
        }

        return false;
    }

    public static string NormalizeSavePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim();
        var root = Path.GetPathRoot(trimmed);
        if (!string.IsNullOrEmpty(root) && string.Equals(trimmed, root, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.Length == 3 && char.IsLetter(trimmed[0]) && trimmed[1] == ':' && (trimmed[2] == '\\' || trimmed[2] == '/'))
        {
            return trimmed;
        }

        return trimmed.TrimEnd('/', '\\');
    }

    protected override CategoryResource GetResourceById(Category model)
    {
        return CategoryResourceMapper.ToResource(model);
    }
}
