using MarbookApi.Models;

namespace MarbookApi.DTOs;

public class UserResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public string? CoverUrl { get; set; }
    public bool IsEmailVerified { get; set; }
    public DateOnly? Birthdate { get; set; }
    public bool IsBirthdatePrivate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Maps a User entity to a DTO with owner email masking or full redaction for other viewers.
    /// </summary>
    public static UserResponseDto FromUser(User user, bool isOwner) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Username = user.Username,
        Email = isOwner ? MaskEmail(user.Email) : null,
        Bio = user.Bio,
        AvatarUrl = user.AvatarUrl,
        CoverUrl = user.CoverUrl,
        IsEmailVerified = user.IsEmailVerified,
        Birthdate = (user.IsBirthdatePrivate && !isOwner) ? null : user.Birthdate,
        IsBirthdatePrivate = user.IsBirthdatePrivate,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt
    };

    /// <summary>
    /// Returns a viewer-scoped copy of an already-cached owner DTO.
    /// </summary>
    public UserResponseDto ForViewer(bool isOwner)
    {
        if (isOwner) return this;

        return new UserResponseDto
        {
            Id = Id,
            Name = Name,
            Username = Username,
            Email = null,
            Bio = Bio,
            AvatarUrl = AvatarUrl,
            CoverUrl = CoverUrl,
            IsEmailVerified = IsEmailVerified,
            Birthdate = IsBirthdatePrivate ? null : Birthdate,
            IsBirthdatePrivate = IsBirthdatePrivate,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }

    /// <summary>
    /// Masks an email address (e.g. "maroo@gmail.com" -> "ma***@g***.com") so owners recognize
    /// their inbox without exposing the raw address in API payloads or browser storage.
    /// </summary>
    public static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0 || atIndex == email.Length - 1) return "***";

        var local = email[..atIndex];
        var domain = email[(atIndex + 1)..];
        var maskedLocal = local.Length switch
        {
            1 => "*",
            2 => $"{local[0]}*",
            _ => $"{local[..2]}***"
        };

        var dotIndex = domain.LastIndexOf('.');
        if (dotIndex <= 0)
        {
            return $"{maskedLocal}@{domain[0]}***";
        }

        var domainName = domain[..dotIndex];
        var tld = domain[dotIndex..];
        var maskedDomain = domainName.Length == 1 ? "*" : $"{domainName[0]}***";

        return $"{maskedLocal}@{maskedDomain}{tld}";
    }
}
