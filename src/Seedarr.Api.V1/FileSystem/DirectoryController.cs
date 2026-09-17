using System.Net.Mime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.FileSystem;
using Seedarr.Http;

namespace Seedarr.Api.V1.FileSystem;

[V1ApiController("directory")]
[Route("api/v1/filesystem/directory")]
public class DirectoryController : Controller
{
    private readonly IFileSystemValidationService _fileSystemValidationService;

    public DirectoryController(IFileSystemValidationService fileSystemValidationService)
    {
        _fileSystemValidationService = fileSystemValidationService;
    }

    [HttpPost("validate")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(FileSystemValidationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<FileSystemValidationResult> Validate([FromBody] DirectoryValidationRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Path))
        {
            return BadRequest(new FileSystemValidationResult
            {
                IsValid = false,
                ErrorMessage = "Path cannot be empty or whitespace.",
            });
        }

        var result = _fileSystemValidationService.ValidateDirectory(request.Path, request.TestWrite);
        if (!result.IsValid)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpGet("validate")]
    [ProducesResponseType(typeof(FileSystemValidationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<FileSystemValidationResult> ValidateGet([FromQuery] string path, [FromQuery] bool testWrite = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest(new FileSystemValidationResult
            {
                IsValid = false,
                ErrorMessage = "Path cannot be empty or whitespace.",
            });
        }

        var result = _fileSystemValidationService.ValidateDirectory(path, testWrite);
        if (!result.IsValid)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }
}
