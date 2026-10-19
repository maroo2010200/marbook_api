using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MarbookApi.Services;

public class LocalFileStorageService : IFileStorageService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<LocalFileStorageService> _logger;

    // Allowed extensions and their expected MIME types
    private static readonly Dictionary<string, string[]> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpg", new[] { "image/jpeg", "image/pjpeg" } },
        { ".jpeg", new[] { "image/jpeg", "image/pjpeg" } },
        { ".png", new[] { "image/png" } },
        { ".webp", new[] { "image/webp" } },
        { ".gif", new[] { "image/gif" } }
    };

    // Magic bytes (file signatures)
    private static readonly Dictionary<string, List<byte[]>> FileSignatures = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpeg", new List<byte[]> { new byte[] { 0xFF, 0xD8, 0xFF } } },
        { ".jpg", new List<byte[]> { new byte[] { 0xFF, 0xD8, 0xFF } } },
        { ".png", new List<byte[]> { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } } },
        { ".gif", new List<byte[]> { new byte[] { 0x47, 0x49, 0x46, 0x38 } } }
    };

    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    public LocalFileStorageService(IWebHostEnvironment env, ILogger<LocalFileStorageService> logger)
    {
        _env = env;
        _logger = logger;
    }

    public async Task<string> SaveFileAsync(IFormFile file, string subFolder = "uploads")
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("No file was uploaded or file is empty.", nameof(file));
        }

        if (file.Length > MaxFileSize)
        {
            throw new InvalidOperationException($"File size exceeds the maximum allowed size of {MaxFileSize / (1024 * 1024)}MB.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.ContainsKey(extension))
        {
            throw new InvalidOperationException($"File extension '{extension}' is not allowed. Allowed formats: JPG, PNG, WebP, GIF.");
        }

        // Validate magic bytes (file signature)
        using (var reader = new BinaryReader(file.OpenReadStream()))
        {
            var headerBytes = reader.ReadBytes(12);
            if (!ValidateFileSignature(extension, headerBytes))
            {
                _logger.LogWarning("File signature validation failed for {FileName} with extension {Extension}", file.FileName, extension);
                throw new InvalidOperationException("The uploaded file content does not match a valid image signature.");
            }
        }

        // Sanitize subFolder name (only alphanumeric and hyphens/underscores)
        var sanitizedFolder = string.Join("", subFolder.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(sanitizedFolder))
        {
            sanitizedFolder = "uploads";
        }

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var targetDirectory = Path.Combine(webRoot, "uploads", sanitizedFolder);
        Directory.CreateDirectory(targetDirectory);

        var uniqueFileName = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(targetDirectory, uniqueFileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        _logger.LogInformation("File saved successfully to {FilePath}", filePath);

        // Return relative public URL path
        return $"/uploads/{sanitizedFolder}/{uniqueFileName}";
    }

    public Task DeleteFileAsync(string fileUrl)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
        {
            return Task.CompletedTask;
        }

        try
        {
            var uri = Uri.TryCreate(fileUrl, UriKind.Absolute, out var parsedUri)
                ? parsedUri.AbsolutePath
                : fileUrl;

            if (!uri.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Attempted to delete a file outside of /uploads/: {FileUrl}", fileUrl);
                return Task.CompletedTask;
            }

            var relativePath = uri.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var fullPath = Path.Combine(webRoot, relativePath);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("Deleted file at {FullPath}", fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {FileUrl}", fileUrl);
        }

        return Task.CompletedTask;
    }

    private static bool ValidateFileSignature(string extension, byte[] headerBytes)
    {
        if (headerBytes.Length < 4) return false;

        if (extension == ".webp")
        {
            // RIFF....WEBP: bytes 0..3 are "RIFF", bytes 8..11 are "WEBP"
            if (headerBytes.Length >= 12)
            {
                bool isRiff = headerBytes[0] == 0x52 && headerBytes[1] == 0x49 && headerBytes[2] == 0x46 && headerBytes[3] == 0x46;
                bool isWebp = headerBytes[8] == 0x57 && headerBytes[9] == 0x45 && headerBytes[10] == 0x42 && headerBytes[11] == 0x50;
                return isRiff && isWebp;
            }
            return false;
        }

        if (FileSignatures.TryGetValue(extension, out var signatures))
        {
            return signatures.Any(sig => headerBytes.Take(sig.Length).SequenceEqual(sig));
        }

        return false;
    }
}
