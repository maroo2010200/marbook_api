using MarbookApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarbookApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MediaController : ControllerBase
{
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<MediaController> _logger;

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "posts",
        "avatars",
        "covers",
        "comments"
    };

    public MediaController(IFileStorageService fileStorageService, ILogger<MediaController> logger)
    {
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(IFormFile? file, [FromQuery] string type = "posts")
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No file was uploaded." });
        }

        var folder = AllowedTypes.Contains(type) ? type.ToLowerInvariant() : "posts";

        try
        {
            var url = await _fileStorageService.SaveFileAsync(file, folder);
            return Ok(new { url });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error uploading media");
            return StatusCode(500, new { message = "An error occurred while uploading the file." });
        }
    }

    [HttpPost("upload-multiple")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadMultiple(List<IFormFile>? files, [FromQuery] string type = "posts")
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest(new { message = "No files were uploaded." });
        }

        if (files.Count > 10)
        {
            return BadRequest(new { message = "Maximum 10 files can be uploaded at a time." });
        }

        var folder = AllowedTypes.Contains(type) ? type.ToLowerInvariant() : "posts";
        var urls = new List<string>();

        try
        {
            foreach (var file in files)
            {
                if (file.Length > 0)
                {
                    var url = await _fileStorageService.SaveFileAsync(file, folder);
                    urls.Add(url);
                }
            }

            return Ok(new { urls });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message, uploadedSoFar = urls });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message, uploadedSoFar = urls });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading multiple files");
            return StatusCode(500, new { message = "An error occurred while uploading files.", uploadedSoFar = urls });
        }
    }
}
