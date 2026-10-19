using Microsoft.AspNetCore.Http;

namespace MarbookApi.Services;

public interface IFileStorageService
{
    /// <summary>
    /// Validates and saves an uploaded file to storage.
    /// </summary>
    /// <param name="file">The uploaded file.</param>
    /// <param name="subFolder">Target sub-folder (e.g. "posts", "avatars", "covers").</param>
    /// <returns>The public relative URL of the saved file (e.g. "/uploads/posts/guid.jpg").</returns>
    Task<string> SaveFileAsync(IFormFile file, string subFolder = "uploads");

    /// <summary>
    /// Deletes a file from storage if it exists.
    /// </summary>
    /// <param name="fileUrl">The URL or relative path of the file to delete.</param>
    Task DeleteFileAsync(string fileUrl);
}
