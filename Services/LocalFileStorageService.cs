using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MarbookApi.Services;

public class LocalFileStorageService : IFileStorageService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<LocalFileStorageService> _logger;

    private const long MaxImageFileSize = 10 * 1024 * 1024; // 10 MB
    private const long MaxVideoFileSize = 100 * 1024 * 1024; // 100 MB
    private const long MaxDocumentFileSize = 25 * 1024 * 1024; // 25 MB

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".mkv", ".ogg", ".m4v"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".zip", ".rar", ".7z"
    };

    // Allowed extensions and their expected MIME types
    private static readonly Dictionary<string, string[]> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpg", new[] { "image/jpeg", "image/pjpeg" } },
        { ".jpeg", new[] { "image/jpeg", "image/pjpeg" } },
        { ".png", new[] { "image/png" } },
        { ".webp", new[] { "image/webp" } },
        { ".gif", new[] { "image/gif" } },
        { ".mp4", new[] { "video/mp4" } },
        { ".webm", new[] { "video/webm" } },
        { ".mov", new[] { "video/quicktime" } },
        { ".mkv", new[] { "video/x-matroska" } },
        { ".ogg", new[] { "video/ogg", "audio/ogg" } },
        { ".m4v", new[] { "video/x-m4v", "video/mp4" } },
        { ".pdf", new[] { "application/pdf" } },
        { ".doc", new[] { "application/msword" } },
        { ".docx", new[] { "application/vnd.openxmlformats-officedocument.wordprocessingml.document" } },
        { ".xls", new[] { "application/vnd.ms-excel" } },
        { ".xlsx", new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } },
        { ".ppt", new[] { "application/vnd.ms-powerpoint" } },
        { ".pptx", new[] { "application/vnd.openxmlformats-officedocument.presentationml.presentation" } },
        { ".txt", new[] { "text/plain" } },
        { ".csv", new[] { "text/csv", "text/plain" } },
        { ".zip", new[] { "application/zip", "application/x-zip-compressed" } },
        { ".rar", new[] { "application/vnd.rar", "application/x-rar-compressed" } },
        { ".7z", new[] { "application/x-7z-compressed" } }
    };

    // Magic bytes (file signatures)
    private static readonly Dictionary<string, List<byte[]>> FileSignatures = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpeg", new List<byte[]> { new byte[] { 0xFF, 0xD8, 0xFF } } },
        { ".jpg", new List<byte[]> { new byte[] { 0xFF, 0xD8, 0xFF } } },
        { ".png", new List<byte[]> { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } } },
        { ".gif", new List<byte[]> { new byte[] { 0x47, 0x49, 0x46, 0x38 } } },
        { ".pdf", new List<byte[]> { new byte[] { 0x25, 0x50, 0x44, 0x46 } } }, // %PDF
        { ".zip", new List<byte[]> { new byte[] { 0x50, 0x4B, 0x03, 0x04 } } }, // PK..
        { ".docx", new List<byte[]> { new byte[] { 0x50, 0x4B, 0x03, 0x04 } } },
        { ".xlsx", new List<byte[]> { new byte[] { 0x50, 0x4B, 0x03, 0x04 } } },
        { ".pptx", new List<byte[]> { new byte[] { 0x50, 0x4B, 0x03, 0x04 } } },
        { ".doc", new List<byte[]> { new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 } } },
        { ".xls", new List<byte[]> { new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 } } },
        { ".ppt", new List<byte[]> { new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 } } },
        { ".rar", new List<byte[]> { new byte[] { 0x52, 0x61, 0x72, 0x21 } } }, // Rar!
        { ".7z", new List<byte[]> { new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C } } }
    };

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

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.ContainsKey(extension))
        {
            throw new InvalidOperationException($"File extension '{extension}' is not allowed. Allowed formats: JPG, PNG, WebP, GIF, MP4, WebM, MOV, MKV, OGG.");
        }

        var isVideo = VideoExtensions.Contains(extension);
        var isDocument = DocumentExtensions.Contains(extension);

        if ((isVideo || isDocument) && (subFolder.Equals("avatars", StringComparison.OrdinalIgnoreCase) ||
                        subFolder.Equals("covers", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Videos and document files cannot be used as avatars or profile covers.");
        }

        var maxAllowedSize = isVideo ? MaxVideoFileSize : isDocument ? MaxDocumentFileSize : MaxImageFileSize;
        if (file.Length > maxAllowedSize)
        {
            var limitMb = maxAllowedSize / (1024 * 1024);
            throw new InvalidOperationException($"File size exceeds the maximum allowed size of {limitMb}MB.");
        }

        // Validate magic bytes (file signature)
        using (var reader = new BinaryReader(file.OpenReadStream()))
        {
            var headerBytes = reader.ReadBytes(12);
            if (!ValidateFileSignature(extension, headerBytes))
            {
                _logger.LogWarning("File signature validation failed for {FileName} with extension {Extension}", file.FileName, extension);
                throw new InvalidOperationException("The uploaded file content does not match a valid media signature.");
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

        if (extension == ".mp4" || extension == ".m4v" || extension == ".mov")
        {
            // ISO base media file format (MP4/MOV) typically has 'ftyp' at offset 4
            if (headerBytes.Length >= 8)
            {
                bool hasFtyp = headerBytes[4] == 0x66 && headerBytes[5] == 0x74 && headerBytes[6] == 0x79 && headerBytes[7] == 0x70;
                if (hasFtyp) return true;

                // QuickTime MOV common boxes (moov, mdat, wide)
                bool isQtBox = (headerBytes[4] == 0x6D && headerBytes[5] == 0x6F && headerBytes[6] == 0x6F && headerBytes[7] == 0x76) ||
                               (headerBytes[4] == 0x6D && headerBytes[5] == 0x64 && headerBytes[6] == 0x61 && headerBytes[7] == 0x74) ||
                               (headerBytes[4] == 0x77 && headerBytes[5] == 0x69 && headerBytes[6] == 0x64 && headerBytes[7] == 0x65);
                if (isQtBox) return true;
            }
            return false;
        }

        if (extension == ".webm" || extension == ".mkv")
        {
            // EBML ID: 1A 45 DF A3
            return headerBytes[0] == 0x1A && headerBytes[1] == 0x45 && headerBytes[2] == 0xDF && headerBytes[3] == 0xA3;
        }

        if (extension == ".ogg")
        {
            // OggS: 4F 67 67 53
            return headerBytes[0] == 0x4F && headerBytes[1] == 0x67 && headerBytes[2] == 0x67 && headerBytes[3] == 0x53;
        }

        if (extension == ".txt" || extension == ".csv")
        {
            // Plain text files must not contain null bytes in their leading bytes
            return !headerBytes.Contains((byte)0x00);
        }

        if (FileSignatures.TryGetValue(extension, out var signatures))
        {
            return signatures.Any(sig => headerBytes.Take(sig.Length).SequenceEqual(sig));
        }

        return false;
    }
}
