using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarbookApi.Services;

public class CloudinarySettings
{
    public string CloudName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
}

public class CloudinaryFileStorageService : IFileStorageService
{
    private readonly Cloudinary _cloudinary;
    private readonly ILogger<CloudinaryFileStorageService> _logger;

    private const long MaxImageFileSize = 10 * 1024 * 1024; // 10 MB
    private const long MaxVideoFileSize = 100 * 1024 * 1024; // 100 MB

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".mkv", ".ogg", ".m4v"
    };

    private static readonly HashSet<string> AllowedExtensions = new(
        ImageExtensions.Concat(VideoExtensions),
        StringComparer.OrdinalIgnoreCase
    );

    private static readonly Dictionary<string, List<byte[]>> FileSignatures = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpeg", new List<byte[]> { new byte[] { 0xFF, 0xD8, 0xFF } } },
        { ".jpg", new List<byte[]> { new byte[] { 0xFF, 0xD8, 0xFF } } },
        { ".png", new List<byte[]> { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } } },
        { ".gif", new List<byte[]> { new byte[] { 0x47, 0x49, 0x46, 0x38 } } }
    };

    public CloudinaryFileStorageService(
        IOptions<CloudinarySettings> config,
        ILogger<CloudinaryFileStorageService> logger)
    {
        _logger = logger;
        var account = new Account(
            config.Value.CloudName,
            config.Value.ApiKey,
            config.Value.ApiSecret
        );
        _cloudinary = new Cloudinary(account);
        _cloudinary.Api.Secure = true; // Force HTTPS CDN delivery
    }

    public async Task<string> SaveFileAsync(IFormFile file, string subFolder = "uploads")
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("No file was uploaded or file is empty.", nameof(file));
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException($"File extension '{extension}' is not allowed. Allowed formats: JPG, PNG, WebP, GIF, MP4, WebM, MOV, MKV, OGG.");
        }

        var isVideo = VideoExtensions.Contains(extension);

        if (isVideo && (subFolder.Equals("avatars", StringComparison.OrdinalIgnoreCase) ||
                        subFolder.Equals("covers", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Videos cannot be used as avatars or profile covers.");
        }

        var maxAllowedSize = isVideo ? MaxVideoFileSize : MaxImageFileSize;
        if (file.Length > maxAllowedSize)
        {
            var limitMb = maxAllowedSize / (1024 * 1024);
            throw new InvalidOperationException($"File size exceeds the maximum allowed size of {limitMb}MB.");
        }

        await using var stream = file.OpenReadStream();

        // Validate magic bytes (file signature) before uploading to Cloudinary
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            var headerBytes = reader.ReadBytes(12);
            if (!ValidateFileSignature(extension, headerBytes))
            {
                _logger.LogWarning("File signature validation failed for {FileName} with extension {Extension}", file.FileName, extension);
                throw new InvalidOperationException("The uploaded file content does not match a valid media signature.");
            }
        }

        // CRITICAL: Reset stream position back to beginning for Cloudinary upload
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        // Sanitize subFolder name (only alphanumeric and hyphens/underscores)
        var sanitizedFolder = string.Join("", subFolder.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(sanitizedFolder))
        {
            sanitizedFolder = "uploads";
        }

        var publicId = $"{Guid.NewGuid():N}";
        var folderPath = $"marshmallow/{sanitizedFolder}";

        if (isVideo)
        {
            var videoParams = new VideoUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = folderPath,
                PublicId = publicId,
                Overwrite = false
            };

            var videoResult = await _cloudinary.UploadLargeAsync(videoParams);
            if (videoResult.Error != null)
            {
                _logger.LogError("Cloudinary video upload failed: {Error}", videoResult.Error.Message);
                throw new InvalidOperationException($"Cloudinary upload failed: {videoResult.Error.Message}");
            }

            _logger.LogInformation("Successfully uploaded video {FileName} to Cloudinary: {Url}", file.FileName, videoResult.SecureUrl);
            return videoResult.SecureUrl.ToString();
        }

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            Folder = folderPath,
            PublicId = publicId,
            Overwrite = false,
            // Automatic optimization: serves optimal format (WebP/AVIF) and quality to modern browsers
            Transformation = new Transformation().Quality("auto").FetchFormat("auto")
        };

        var uploadResult = await _cloudinary.UploadAsync(uploadParams);

        if (uploadResult.Error != null)
        {
            _logger.LogError("Cloudinary upload failed: {Error}", uploadResult.Error.Message);
            throw new InvalidOperationException($"Cloudinary upload failed: {uploadResult.Error.Message}");
        }

        _logger.LogInformation("Successfully uploaded {FileName} to Cloudinary: {Url}", file.FileName, uploadResult.SecureUrl);

        // Return full HTTPS CDN URL
        return uploadResult.SecureUrl.ToString();
    }

    public async Task DeleteFileAsync(string fileUrl)
    {
        if (string.IsNullOrWhiteSpace(fileUrl)) return;

        try
        {
            var uri = new Uri(fileUrl);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            var uploadIndex = Array.IndexOf(segments, "upload");
            if (uploadIndex >= 0 && uploadIndex < segments.Length - 1)
            {
                var relativeSegments = segments.Skip(uploadIndex + 1).ToList();

                // Skip version segment (e.g. v171928392)
                if (relativeSegments.Count > 0 &&
                    relativeSegments[0].StartsWith("v", StringComparison.OrdinalIgnoreCase) &&
                    long.TryParse(relativeSegments[0].AsSpan(1), out _))
                {
                    relativeSegments = relativeSegments.Skip(1).ToList();
                }

                var fullPath = string.Join("/", relativeSegments);
                var publicId = Path.ChangeExtension(fullPath, null);

                if (!string.IsNullOrEmpty(publicId))
                {
                    bool isVideo = fileUrl.Contains("/video/upload/", StringComparison.OrdinalIgnoreCase);
                    var deleteParams = new DeletionParams(publicId)
                    {
                        ResourceType = isVideo ? ResourceType.Video : ResourceType.Image
                    };
                    var result = await _cloudinary.DestroyAsync(deleteParams);
                    _logger.LogInformation("Deleted asset from Cloudinary: {PublicId}, Result: {Result}", publicId, result.Result);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete asset from Cloudinary: {Url}", fileUrl);
        }
    }

    private static bool ValidateFileSignature(string extension, byte[] headerBytes)
    {
        if (headerBytes.Length < 4) return false;

        if (extension == ".webp")
        {
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

        if (FileSignatures.TryGetValue(extension, out var signatures))
        {
            return signatures.Any(sig => headerBytes.Take(sig.Length).SequenceEqual(sig));
        }

        return false;
    }
}
